[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $GodotExecutable,

    [Parameter()]
    [string] $PackageRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$fixtureRoot = Join-Path $repositoryRoot "tests\fixtures\blank-consumer"
$gdUnitRoot = Join-Path $repositoryRoot "addons\gdUnit4"
$packageScript = Join-Path $repositoryRoot "scripts\Package-SpatialCircuitsRelease.ps1"
$toolchainScript = Join-Path $repositoryRoot "scripts\Test-Toolchain.ps1"
$processModule = Join-Path $repositoryRoot "scripts\SpatialWires.Process.psm1"
$solutionPath = Join-Path $repositoryRoot "SpatialWires.sln"
$temporaryBase = [System.IO.Path]::GetTempPath()
$runRoot = Join-Path $temporaryBase ("spatial-wires-blank-consumer-" + [Guid]::NewGuid().ToString("N"))
$generatedPackageRoot = $null
$previousLifecycleReport = [Environment]::GetEnvironmentVariable("SPATIAL_WIRES_EDITOR_LIFECYCLE_REPORT", "Process")

Import-Module $processModule -Force

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable does not exist: '$GodotExecutable'."
}

$resolvedGodot = (Resolve-Path -LiteralPath $GodotExecutable).Path
$powershellExecutable = (Get-Command powershell.exe -CommandType Application).Source
$dotnetExecutable = (Get-Command dotnet.exe -CommandType Application).Source

function Invoke-RequiredProcess {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter()]
        [string[]] $ArgumentList = @(),

        [Parameter(Mandatory)]
        [int] $TimeoutMilliseconds,

        [Parameter(Mandatory)]
        [string] $WorkingDirectory,

        [Parameter()]
        [hashtable] $Environment = @{}
    )

    $result = Invoke-BoundedProcess `
        -FilePath $FilePath `
        -ArgumentList $ArgumentList `
        -TimeoutMilliseconds $TimeoutMilliseconds `
        -WorkingDirectory $WorkingDirectory `
        -Environment $Environment

    [Console]::Out.WriteLine("[$Name] process code $($result.ExitCode), timedOut=$($result.TimedOut), durationMs=$($result.DurationMilliseconds)")
    if (-not [string]::IsNullOrEmpty($result.StandardOutput)) {
        [Console]::Out.Write($result.StandardOutput)
    }
    if (-not [string]::IsNullOrEmpty($result.StandardError)) {
        [Console]::Error.Write($result.StandardError)
    }

    if ($result.TimedOut) {
        throw "$Name exceeded $TimeoutMilliseconds ms. Process code: $($result.ExitCode)."
    }
    if ($result.ExitCode -ne 0) {
        throw "$Name failed with process code $($result.ExitCode) after $($result.DurationMilliseconds) ms."
    }

    return $result
}

function Add-PortableSources {
    param([string] $ProjectPath)

    $projectText = [System.IO.File]::ReadAllText($ProjectPath)
    $projectRoot = Split-Path -Parent $ProjectPath
    $sources = @(
        "src\SpatialCircuits.Core\**\*.cs",
        "src\SpatialCircuits.Cells\**\*.cs",
        "src\SpatialCircuits.Hierarchy\**\*.cs",
        "src\SpatialCircuits.Workbench\**\*.cs",
        "src\SpatialCircuits.Persistence\**\*.cs",
        "src\SpatialCircuits.Runner\**\*.cs"
    ) | ForEach-Object {
        "    <Compile Include=`"$(Join-Path $projectRoot $_)`" />"
    }
    $runnerProgram = Join-Path $projectRoot "src\SpatialCircuits.Runner\Program.cs"
    $items = "  <ItemGroup>`n    <Compile Remove=`"src\**\*.cs`" />`n" + ($sources -join "`n") + "`n    <Compile Remove=`"$runnerProgram`" />`n  </ItemGroup>`n"
    $closingTag = "</Project>"
    $closingIndex = $projectText.LastIndexOf($closingTag, [System.StringComparison]::Ordinal)
    if ($closingIndex -lt 0) {
        throw "Blank-consumer project has no closing Project element."
    }

    $projectText = $projectText.Insert($closingIndex, $items)
    [System.IO.File]::WriteAllText($ProjectPath, $projectText, [System.Text.UTF8Encoding]::new($false))
}

function Get-Sha256 {
    param([string] $Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([System.BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace("-", "").ToLowerInvariant()
        }
        finally {
            $algorithm.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-Package {
    param([string] $Root)

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "Package root does not exist: '$Root'."
    }

    $metadataPath = Join-Path $Root "spatial-circuits.package.json"
    if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
        throw "Package metadata does not exist: '$metadataPath'."
    }

    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    if ($metadata.schemaVersion -ne 1 -or $metadata.godotVersion -ne "4.7.1" -or
        $metadata.addonPath -ne "addons/spatial_circuits" -or $metadata.sourcePath -ne "src") {
        throw "Package metadata does not describe the portable Godot 4.7.1 package."
    }

    $paths = @($metadata.files | ForEach-Object { $_.path })
    if ($paths.Count -eq 0 -or (($paths | Sort-Object) -join "`n") -cne ($paths -join "`n")) {
        throw "Package metadata paths are missing or unsorted."
    }

    foreach ($entry in @($metadata.files)) {
        if ([System.IO.Path]::IsPathRooted($entry.path)) {
            throw "Package metadata contains an absolute path: '$($entry.path)'."
        }

        $filePath = Join-Path $Root $entry.path.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf) -or
            (Get-Sha256 $filePath) -cne $entry.sha256) {
            throw "Package hash validation failed for '$($entry.path)'."
        }
    }

    if (@(Get-ChildItem -LiteralPath $Root -File -Recurse |
            Where-Object { $_.Extension -in @(".dll", ".exe", ".pdb") }).Count -gt 0) {
        throw "Package contains a binary artifact."
    }

    return [pscustomobject]@{
        MetadataSha256 = Get-Sha256 $metadataPath
        FileCount = $paths.Count
    }
}

try {
    Invoke-RequiredProcess `
        -Name "toolchain check" `
        -FilePath $powershellExecutable `
        -ArgumentList @("-NoProfile", "-File", $toolchainScript, "-GodotExecutable", $resolvedGodot) `
        -TimeoutMilliseconds 30000 `
        -WorkingDirectory $repositoryRoot | Out-Null

    New-Item -ItemType Directory -Path $runRoot | Out-Null
    Copy-Item -Path (Join-Path $fixtureRoot "*") -Destination $runRoot -Recurse -Force
    if ([string]::IsNullOrWhiteSpace($PackageRoot)) {
        $generatedPackageRoot = Join-Path $temporaryBase ("spatial-wires-blank-package-" + [Guid]::NewGuid().ToString("N"))
        Invoke-RequiredProcess `
            -Name "release package" `
            -FilePath $powershellExecutable `
            -ArgumentList @("-NoProfile", "-File", $packageScript, "-RepositoryRoot", $repositoryRoot, "-OutputRoot", $generatedPackageRoot) `
            -TimeoutMilliseconds 30000 `
            -WorkingDirectory $repositoryRoot | Out-Null
        $PackageRoot = $generatedPackageRoot
    }

    $resolvedPackageRoot = (Resolve-Path -LiteralPath $PackageRoot).Path
    $package = Assert-Package -Root $resolvedPackageRoot
    Copy-Item -Path (Join-Path $resolvedPackageRoot "*") -Destination $runRoot -Recurse -Force

    $consumerAddons = Join-Path $runRoot "addons"
    New-Item -ItemType Directory -Path $consumerAddons -Force | Out-Null
    Copy-Item -LiteralPath $gdUnitRoot -Destination (Join-Path $consumerAddons "gdUnit4") -Recurse -Force

    $consumerProject = Join-Path $runRoot "SpatialWires.BlankConsumer.csproj"
    $runSettings = Join-Path $runRoot "gdunit4.runsettings"
    $lifecycleReport = Join-Path $runRoot "editor-lifecycle-report.json"
    Add-PortableSources -ProjectPath $consumerProject

    Invoke-RequiredProcess `
        -Name "repository managed build" `
        -FilePath $dotnetExecutable `
        -ArgumentList @("build", $solutionPath, "--nologo") `
        -TimeoutMilliseconds 180000 `
        -WorkingDirectory $repositoryRoot | Out-Null

    Invoke-RequiredProcess `
        -Name "blank-consumer managed build" `
        -FilePath $dotnetExecutable `
        -ArgumentList @("build", $consumerProject, "--nologo") `
        -TimeoutMilliseconds 180000 `
        -WorkingDirectory $runRoot | Out-Null

    Invoke-RequiredProcess `
        -Name "Godot import" `
        -FilePath $resolvedGodot `
        -ArgumentList @("--headless", "--path", $runRoot, "--import", "--quit-after", "300") `
        -TimeoutMilliseconds 60000 `
        -WorkingDirectory $runRoot | Out-Null

    Invoke-RequiredProcess `
        -Name "Godot managed-solution build" `
        -FilePath $resolvedGodot `
        -ArgumentList @("--headless", "--path", $runRoot, "--build-solutions", "--quit-after", "300") `
        -TimeoutMilliseconds 120000 `
        -WorkingDirectory $runRoot | Out-Null

    Invoke-RequiredProcess `
        -Name "headless editor lifecycle probe" `
        -FilePath $resolvedGodot `
        -ArgumentList @("--headless", "--editor", "--path", $runRoot, "--quit-after", "300") `
        -TimeoutMilliseconds 60000 `
        -WorkingDirectory $runRoot `
        -Environment @{ SPATIAL_WIRES_EDITOR_PROBE_OUTPUT = $lifecycleReport } | Out-Null
    if (-not (Test-Path -LiteralPath $lifecycleReport -PathType Leaf)) {
        throw "Headless editor lifecycle probe did not write '$lifecycleReport'."
    }

    Invoke-RequiredProcess `
        -Name "gdUnit4 lifecycle and public-type tests" `
        -FilePath $dotnetExecutable `
        -ArgumentList @("test", $consumerProject, "--settings", $runSettings, "--nologo", "--verbosity", "normal") `
        -TimeoutMilliseconds 240000 `
        -WorkingDirectory $runRoot `
        -Environment @{
            GODOT_BIN = $resolvedGodot
            SPATIAL_WIRES_EDITOR_LIFECYCLE_REPORT = $lifecycleReport
        } | Out-Null

    Write-Output "Blank-consumer gdUnit4 acceptance passed."
    Write-Output "Package root: $resolvedPackageRoot"
    Write-Output "Package files: $($package.FileCount)"
    Write-Output "Package metadata SHA-256: $($package.MetadataSha256)"
}
finally {
    [Environment]::SetEnvironmentVariable("SPATIAL_WIRES_EDITOR_LIFECYCLE_REPORT", $previousLifecycleReport, "Process")
    $normalizedTemporaryBase = [System.IO.Path]::GetFullPath($temporaryBase).TrimEnd("\") + "\"
    $normalizedRunRoot = [System.IO.Path]::GetFullPath($runRoot)
    if ($normalizedRunRoot.StartsWith($normalizedTemporaryBase, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $normalizedRunRoot).StartsWith("spatial-wires-blank-consumer-", [System.StringComparison]::Ordinal)) {
        if (Test-Path -LiteralPath $normalizedRunRoot) {
            Remove-Item -LiteralPath $normalizedRunRoot -Recurse -Force
        }
    }

    if ($null -ne $generatedPackageRoot -and (Test-Path -LiteralPath $generatedPackageRoot)) {
        Remove-Item -LiteralPath $generatedPackageRoot -Recurse -Force
    }
}
