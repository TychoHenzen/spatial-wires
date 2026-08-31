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
$temporaryBase = [System.IO.Path]::GetTempPath()
$runRoot = Join-Path $temporaryBase ("spatial-wires-blank-consumer-" + [Guid]::NewGuid().ToString("N"))
$previousLifecycleReport = [Environment]::GetEnvironmentVariable("SPATIAL_WIRES_EDITOR_LIFECYCLE_REPORT", "Process")

Import-Module $processModule -Force

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable does not exist: '$GodotExecutable'."
}

try {
    & $toolchainScript -GodotExecutable $GodotExecutable

    New-Item -ItemType Directory -Path $runRoot | Out-Null
    Copy-Item -Path (Join-Path $fixtureRoot "*") -Destination $runRoot -Recurse -Force
    $stageResult = & $stageScript -RepositoryRoot $repositoryRoot -OutputRoot $runRoot

    $consumerAddons = Join-Path $runRoot "addons"
    New-Item -ItemType Directory -Path $consumerAddons -Force | Out-Null
    Copy-Item -LiteralPath $gdUnitRoot -Destination (Join-Path $consumerAddons "gdUnit4") -Recurse -Force

    $env:GODOT_BIN = (Resolve-Path -LiteralPath $GodotExecutable).Path
    $consumerProject = Join-Path $runRoot "SpatialWires.BlankConsumer.csproj"
    $runSettings = Join-Path $runRoot "gdunit4.runsettings"
    $lifecycleReport = Join-Path $runRoot "editor-lifecycle-report.json"

    & dotnet build $consumerProject --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Blank-consumer managed build failed with process code $LASTEXITCODE."
    }

    $editorResult = Invoke-BoundedProcess `
        -FilePath $env:GODOT_BIN `
        -ArgumentList @("--headless", "--editor", "--path", $runRoot, "--quit-after", "300") `
        -TimeoutMilliseconds 60000 `
        -WorkingDirectory $runRoot `
        -Environment @{ SPATIAL_WIRES_EDITOR_PROBE_OUTPUT = $lifecycleReport }
    if ($editorResult.TimedOut) {
        throw "Headless editor lifecycle probe exceeded 60000 ms.`nstdout:`n$($editorResult.StandardOutput)`nstderr:`n$($editorResult.StandardError)"
    }
    if ($editorResult.ExitCode -ne 0) {
        throw "Headless editor lifecycle probe failed with process code $($editorResult.ExitCode).`nstdout:`n$($editorResult.StandardOutput)`nstderr:`n$($editorResult.StandardError)"
    }
    if (-not (Test-Path -LiteralPath $lifecycleReport -PathType Leaf)) {
        throw "Headless editor lifecycle probe did not write '$lifecycleReport'."
    }

    [Environment]::SetEnvironmentVariable("SPATIAL_WIRES_EDITOR_LIFECYCLE_REPORT", $lifecycleReport, "Process")

    & dotnet test $consumerProject --settings $runSettings --nologo --verbosity normal
    if ($LASTEXITCODE -ne 0) {
        throw "Blank-consumer gdUnit4 acceptance failed with process code $LASTEXITCODE."
    }

    Write-Output "Blank-consumer gdUnit4 acceptance passed."
    Write-Output "Manifest SHA-256: $($stageResult.ManifestSha256)"
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
