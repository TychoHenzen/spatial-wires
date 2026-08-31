[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $GodotExecutable
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$fixtureRoot = Join-Path $repositoryRoot "tests\fixtures\failing-public-type"
$gdUnitRoot = Join-Path $repositoryRoot "addons\gdUnit4"
$stageScript = Join-Path $repositoryRoot "scripts\Stage-SpatialCircuitsAddon.ps1"
$processModule = Join-Path $repositoryRoot "scripts\SpatialWires.Process.psm1"
$temporaryBase = [System.IO.Path]::GetTempPath()
$runRoot = Join-Path $temporaryBase ("spatial-wires-failing-gdunit-" + [Guid]::NewGuid().ToString("N"))
$runnerExitCode = 126

Import-Module $processModule -Force

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable does not exist: '$GodotExecutable'."
}

$resolvedGodot = (Resolve-Path -LiteralPath $GodotExecutable).Path
$powershellExecutable = (Get-Command powershell.exe -CommandType Application).Source
$dotnetExecutable = (Get-Command dotnet.exe -CommandType Application).Source

try {
    New-Item -ItemType Directory -Path $runRoot | Out-Null
    Copy-Item -Path (Join-Path $fixtureRoot "*") -Destination $runRoot -Recurse -Force

    $stageResult = Invoke-BoundedProcess `
        -FilePath $powershellExecutable `
        -ArgumentList @("-NoProfile", "-File", $stageScript, "-RepositoryRoot", $repositoryRoot, "-OutputRoot", $runRoot) `
        -TimeoutMilliseconds 30000 `
        -WorkingDirectory $repositoryRoot
    [Console]::Out.WriteLine("[negative addon staging] process code $($stageResult.ExitCode), timedOut=$($stageResult.TimedOut), durationMs=$($stageResult.DurationMilliseconds)")
    if (-not [string]::IsNullOrEmpty($stageResult.StandardOutput)) {
        [Console]::Out.Write($stageResult.StandardOutput)
    }
    if (-not [string]::IsNullOrEmpty($stageResult.StandardError)) {
        [Console]::Error.Write($stageResult.StandardError)
    }
    if ($stageResult.TimedOut -or $stageResult.ExitCode -ne 0) {
        throw "Negative-fixture staging failed with process code $($stageResult.ExitCode), timedOut=$($stageResult.TimedOut)."
    }

    $consumerAddons = Join-Path $runRoot "addons"
    New-Item -ItemType Directory -Path $consumerAddons -Force | Out-Null
    Copy-Item -LiteralPath $gdUnitRoot -Destination (Join-Path $consumerAddons "gdUnit4") -Recurse -Force

    $projectPath = Join-Path $runRoot "SpatialWires.FailingPublicType.csproj"
    $settingsPath = Join-Path $runRoot "gdunit4.runsettings"
    $gdUnitResult = Invoke-BoundedProcess `
        -FilePath $dotnetExecutable `
        -ArgumentList @("test", $projectPath, "--settings", $settingsPath, "--nologo", "--verbosity", "normal") `
        -TimeoutMilliseconds 180000 `
        -WorkingDirectory $runRoot `
        -Environment @{ GODOT_BIN = $resolvedGodot }

    [Console]::Out.WriteLine("[expected failing gdUnit4 public-type test] process code $($gdUnitResult.ExitCode), timedOut=$($gdUnitResult.TimedOut), durationMs=$($gdUnitResult.DurationMilliseconds)")
    if (-not [string]::IsNullOrEmpty($gdUnitResult.StandardOutput)) {
        [Console]::Out.Write($gdUnitResult.StandardOutput)
    }
    if (-not [string]::IsNullOrEmpty($gdUnitResult.StandardError)) {
        [Console]::Error.Write($gdUnitResult.StandardError)
    }

    $combinedOutput = $gdUnitResult.StandardOutput + "`n" + $gdUnitResult.StandardError
    if ($gdUnitResult.TimedOut) {
        [Console]::Error.WriteLine("The negative gdUnit4 fixture exceeded 180000 ms.")
        $runnerExitCode = 124
    }
    elseif ($gdUnitResult.ExitCode -eq 0) {
        [Console]::Error.WriteLine("The deliberately failing gdUnit4 assertion was accepted as success.")
        $runnerExitCode = 125
    }
    elseif ($combinedOutput -notmatch "EXPECTED_NEGATIVE_PUBLIC_TYPE_ASSERTION") {
        [Console]::Error.WriteLine("gdUnit4 failed without executing the deliberate public-type assertion.")
        $runnerExitCode = 126
    }
    else {
        [Console]::Out.WriteLine("EXPECTED_GDUNIT_FAILURE exit=$($gdUnitResult.ExitCode) timedOut=$($gdUnitResult.TimedOut) durationMs=$($gdUnitResult.DurationMilliseconds)")
        $runnerExitCode = $gdUnitResult.ExitCode
    }
}
finally {
    $normalizedTemporaryBase = [System.IO.Path]::GetFullPath($temporaryBase).TrimEnd("\") + "\"
    $normalizedRunRoot = [System.IO.Path]::GetFullPath($runRoot)
    if ($normalizedRunRoot.StartsWith($normalizedTemporaryBase, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $normalizedRunRoot).StartsWith("spatial-wires-failing-gdunit-", [System.StringComparison]::Ordinal)) {
        if (Test-Path -LiteralPath $normalizedRunRoot) {
            Remove-Item -LiteralPath $normalizedRunRoot -Recurse -Force
        }
    }
}

exit $runnerExitCode
