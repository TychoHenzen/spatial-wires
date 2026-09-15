[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $GodotExecutable,

    [Parameter()]
    [string] $OutputRoot = (Join-Path ([System.IO.Path]::GetTempPath()) ("spatial-wires-release-" + [Guid]::NewGuid().ToString("N")))
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path.TrimEnd("\")
$outputPath = [System.IO.Path]::GetFullPath($OutputRoot).TrimEnd("\")
if ($outputPath.Equals($repositoryRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
    $outputPath.StartsWith("$repositoryRoot\", [System.StringComparison]::OrdinalIgnoreCase) -or
    $repositoryRoot.StartsWith("$outputPath\", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Release output cannot be inside the repository: '$outputPath'."
}

$resolvedGodot = (Resolve-Path -LiteralPath $GodotExecutable).Path
$powershellExecutable = (Get-Command powershell.exe -CommandType Application).Source
$dotnetExecutable = (Get-Command dotnet.exe -CommandType Application).Source
$nodeExecutable = (Get-Command node.exe -CommandType Application | Select-Object -First 1).Source
$processModule = Join-Path $repositoryRoot "scripts\SpatialWires.Process.psm1"
$toolchainScript = Join-Path $repositoryRoot "scripts\Test-Toolchain.ps1"
$packageScript = Join-Path $repositoryRoot "scripts\Package-SpatialCircuitsRelease.ps1"
$benchmarkProject = Join-Path $repositoryRoot "benchmarks\SpatialCircuits.Benchmarks.csproj"
$benchmarkFixture = Join-Path $repositoryRoot "benchmarks\fixtures\temporal-frontier.fixture.json"
$benchmarkIdentityTest = Join-Path $repositoryRoot "tests\benchmarks\Test-BenchmarkFixtureIdentity.ps1"
$godotTests = Join-Path $repositoryRoot "tests\SpatialWires.Godot.Tests\Run-GdUnit.ps1"
$blankAcceptance = Join-Path $repositoryRoot "tests\acceptance\Run-BlankConsumerAcceptance.ps1"
$independentPractice = Join-Path $repositoryRoot "scripts\Run-IndependentCopyPractice.ps1"
$solutionPath = Join-Path $repositoryRoot "SpatialWires.sln"
$exportPath = Join-Path $outputPath "SpatialWires.pck"
$packagePath = Join-Path $outputPath "package"
$benchmarkOutput = Join-Path $outputPath "benchmark.json"

Import-Module $processModule -Force

function Invoke-RequiredProcess {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $FilePath,
        [Parameter()][string[]] $ArgumentList = @(),
        [Parameter(Mandatory)][int] $TimeoutMilliseconds,
        [Parameter(Mandatory)][string] $WorkingDirectory,
        [Parameter()][hashtable] $Environment = @{}
    )

    $result = Invoke-BoundedProcess -FilePath $FilePath -ArgumentList $ArgumentList `
        -TimeoutMilliseconds $TimeoutMilliseconds -WorkingDirectory $WorkingDirectory -Environment $Environment
    [Console]::Out.WriteLine("[$Name] process code $($result.ExitCode), timedOut=$($result.TimedOut), durationMs=$($result.DurationMilliseconds)")
    if (-not [string]::IsNullOrEmpty($result.StandardOutput)) {
        [Console]::Out.Write($result.StandardOutput)
    }
    if (-not [string]::IsNullOrEmpty($result.StandardError)) {
        [Console]::Error.Write($result.StandardError)
    }
    if ($result.TimedOut) {
        throw "$Name exceeded $TimeoutMilliseconds ms."
    }
    if ($result.ExitCode -ne 0) {
        throw "$Name failed with process code $($result.ExitCode)."
    }
    return $result
}

function Invoke-PureTests {
    param([string] $WorkingDirectory)

    $projects = @(
        "tests/SpatialCircuits.Core.Tests/SpatialCircuits.Core.Tests.csproj",
        "tests/SpatialCircuits.Cells.Tests/SpatialCircuits.Cells.Tests.csproj",
        "tests/SpatialCircuits.Hierarchy.Tests/SpatialCircuits.Hierarchy.Tests.csproj",
        "tests/SpatialCircuits.Workbench.Tests/SpatialCircuits.Workbench.Tests.csproj",
        "tests/SpatialCircuits.Persistence.Tests/SpatialCircuits.Persistence.Tests.csproj",
        "tests/SpatialCircuits.Runner.Tests/SpatialCircuits.Runner.Tests.csproj"
    )
    foreach ($project in $projects) {
        Invoke-RequiredProcess -Name "pure tests $project" -FilePath $dotnetExecutable `
            -ArgumentList @("test", $project, "--configuration", "Release", "--no-restore", "--nologo", "--verbosity", "minimal") `
            -TimeoutMilliseconds 180000 -WorkingDirectory $WorkingDirectory | Out-Null
    }
}

if (Test-Path -LiteralPath $outputPath) {
    [System.IO.Directory]::Delete($outputPath, $true)
}
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

Invoke-RequiredProcess -Name "toolchain check" -FilePath $powershellExecutable `
    -ArgumentList @("-NoProfile", "-File", $toolchainScript, "-GodotExecutable", $resolvedGodot) `
    -TimeoutMilliseconds 30000 -WorkingDirectory $repositoryRoot | Out-Null
Invoke-RequiredProcess -Name "solution restore" -FilePath $dotnetExecutable `
    -ArgumentList @("restore", $solutionPath, "--nologo") -TimeoutMilliseconds 180000 `
    -WorkingDirectory $repositoryRoot | Out-Null
Invoke-RequiredProcess -Name "solution build" -FilePath $dotnetExecutable `
    -ArgumentList @("build", $solutionPath, "--configuration", "Release", "--no-restore", "--nologo") `
    -TimeoutMilliseconds 180000 -WorkingDirectory $repositoryRoot | Out-Null
Invoke-PureTests -WorkingDirectory $repositoryRoot
Invoke-RequiredProcess -Name "release benchmark" -FilePath $dotnetExecutable `
    -ArgumentList @("run", "--project", $benchmarkProject, "--configuration", "Release", "--no-restore", "--", $benchmarkFixture, $benchmarkOutput) `
    -TimeoutMilliseconds 180000 -WorkingDirectory $repositoryRoot | Out-Null
if (-not (Test-Path -LiteralPath $benchmarkOutput -PathType Leaf)) {
    throw "Release benchmark did not create '$benchmarkOutput'."
}
Invoke-RequiredProcess -Name "benchmark fixture identity test" -FilePath $powershellExecutable `
    -ArgumentList @("-NoProfile", "-File", $benchmarkIdentityTest) `
    -TimeoutMilliseconds 180000 -WorkingDirectory $repositoryRoot | Out-Null
Invoke-RequiredProcess -Name "Godot managed-solution build" -FilePath $resolvedGodot `
    -ArgumentList @("--headless", "--path", $repositoryRoot, "--build-solutions", "--quit-after", "300") `
    -TimeoutMilliseconds 120000 -WorkingDirectory $repositoryRoot | Out-Null
Invoke-RequiredProcess -Name "Godot adapter tests" -FilePath $powershellExecutable `
    -ArgumentList @("-NoProfile", "-File", $godotTests, "-GodotExecutable", $resolvedGodot) `
    -TimeoutMilliseconds 300000 -WorkingDirectory $repositoryRoot | Out-Null
Invoke-RequiredProcess -Name "Godot acceptance tests" -FilePath $nodeExecutable `
    -ArgumentList @("--test", "--test-concurrency=1", "tests/SpatialWires.Godot.Tests/public-addon-types.test.mjs", "tests/acceptance/blank-consumer-addon.test.mjs", "tests/acceptance/failing-public-type.test.mjs", "tests/acceptance/independent-copy-practice.test.mjs") `
    -TimeoutMilliseconds 600000 -WorkingDirectory $repositoryRoot -Environment @{ GODOT_BIN = $resolvedGodot } | Out-Null
Invoke-RequiredProcess -Name "release package" -FilePath $powershellExecutable `
    -ArgumentList @("-NoProfile", "-File", $packageScript, "-RepositoryRoot", $repositoryRoot, "-OutputRoot", $packagePath) `
    -TimeoutMilliseconds 120000 -WorkingDirectory $repositoryRoot | Out-Null
$templatePath = Join-Path ([Environment]::GetFolderPath("ApplicationData")) "Godot\export_templates\4.7.1.stable.mono\windows_release_x86_64.exe"
if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
    throw "Godot 4.7.1 Windows export template does not exist: '$templatePath'."
}
Invoke-RequiredProcess -Name "Windows export pack" -FilePath $resolvedGodot `
    -ArgumentList @("--headless", "--path", $repositoryRoot, "--export-pack", "Windows Desktop", $exportPath) `
    -TimeoutMilliseconds 300000 -WorkingDirectory $repositoryRoot | Out-Null
if (-not (Test-Path -LiteralPath $exportPath -PathType Leaf)) {
    throw "Windows export did not create '$exportPath'."
}
Invoke-RequiredProcess -Name "blank-consumer package acceptance" -FilePath $powershellExecutable `
    -ArgumentList @("-NoProfile", "-File", $blankAcceptance, "-GodotExecutable", $resolvedGodot, "-PackageRoot", $packagePath) `
    -TimeoutMilliseconds 360000 -WorkingDirectory $repositoryRoot | Out-Null
Invoke-RequiredProcess -Name "independent package practice" -FilePath $powershellExecutable `
    -ArgumentList @("-NoProfile", "-File", $independentPractice, "-GodotExecutable", $resolvedGodot) `
    -TimeoutMilliseconds 360000 -WorkingDirectory $repositoryRoot | Out-Null

Write-Output "Windows release qualification passed."
Write-Output "Package root: $packagePath"
Write-Output "Export pack: $exportPath"
