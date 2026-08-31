[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $GodotExecutable
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$fixtureRoot = Join-Path $repositoryRoot "tests\fixtures\blank-consumer"
$gdUnitRoot = Join-Path $repositoryRoot "addons\gdUnit4"
$stageScript = Join-Path $repositoryRoot "scripts\Stage-SpatialCircuitsAddon.ps1"
$toolchainScript = Join-Path $repositoryRoot "scripts\Test-Toolchain.ps1"
$processModule = Join-Path $repositoryRoot "scripts\SpatialWires.Process.psm1"
$solutionPath = Join-Path $repositoryRoot "SpatialWires.sln"
$temporaryBase = [System.IO.Path]::GetTempPath()
$runRoot = Join-Path $temporaryBase ("spatial-wires-blank-consumer-" + [Guid]::NewGuid().ToString("N"))
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

try {
    Invoke-RequiredProcess `
        -Name "toolchain check" `
        -FilePath $powershellExecutable `
        -ArgumentList @("-NoProfile", "-File", $toolchainScript, "-GodotExecutable", $resolvedGodot) `
        -TimeoutMilliseconds 30000 `
        -WorkingDirectory $repositoryRoot | Out-Null

    New-Item -ItemType Directory -Path $runRoot | Out-Null
    Copy-Item -Path (Join-Path $fixtureRoot "*") -Destination $runRoot -Recurse -Force
    $stageResult = Invoke-RequiredProcess `
        -Name "addon staging" `
        -FilePath $powershellExecutable `
        -ArgumentList @("-NoProfile", "-File", $stageScript, "-RepositoryRoot", $repositoryRoot, "-OutputRoot", $runRoot) `
        -TimeoutMilliseconds 30000 `
        -WorkingDirectory $repositoryRoot

    $consumerAddons = Join-Path $runRoot "addons"
    New-Item -ItemType Directory -Path $consumerAddons -Force | Out-Null
    Copy-Item -LiteralPath $gdUnitRoot -Destination (Join-Path $consumerAddons "gdUnit4") -Recurse -Force

    $consumerProject = Join-Path $runRoot "SpatialWires.BlankConsumer.csproj"
    $runSettings = Join-Path $runRoot "gdunit4.runsettings"
    $lifecycleReport = Join-Path $runRoot "editor-lifecycle-report.json"

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
    Write-Output "Staging process code: $($stageResult.ExitCode)"
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
}
