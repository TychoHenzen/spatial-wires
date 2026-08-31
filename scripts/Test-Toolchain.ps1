[CmdletBinding()]
param(
    [Parameter()]
    [string] $GodotExecutable,

    [Parameter()]
    [string] $DotnetExecutable = "dotnet"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-ToolExecutable {
    param(
        [Parameter(Mandatory)]
        [string] $ToolName,

        [Parameter(Mandatory)]
        [string] $Executable
    )

    if ([string]::IsNullOrWhiteSpace($Executable)) {
        throw "${ToolName}: executable was not specified."
    }

    if (Test-Path -LiteralPath $Executable -PathType Leaf) {
        return (Resolve-Path -LiteralPath $Executable).Path
    }

    $command = Get-Command -Name $Executable -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "${ToolName}: executable '$Executable' was not found."
    }

    return $command.Source
}

function Invoke-ToolVersion {
    param(
        [Parameter(Mandatory)]
        [string] $ToolName,

        [Parameter(Mandatory)]
        [string] $Executable
    )

    $standardOutputPath = [System.IO.Path]::GetTempFileName()
    $standardErrorPath = [System.IO.Path]::GetTempFileName()
    try {
        $process = Start-Process `
            -FilePath $Executable `
            -ArgumentList "--version" `
            -Wait `
            -NoNewWindow `
            -PassThru `
            -RedirectStandardOutput $standardOutputPath `
            -RedirectStandardError $standardErrorPath
        $exitCode = $process.ExitCode
        $versionOutput = @(
            Get-Content -LiteralPath $standardOutputPath -Raw
            Get-Content -LiteralPath $standardErrorPath -Raw
        )
    }
    catch {
        throw "${ToolName}: version check could not run '$Executable'. $($_.Exception.Message)"
    }
    finally {
        Remove-Item -LiteralPath $standardOutputPath, $standardErrorPath -Force -ErrorAction SilentlyContinue
    }

    $text = (($versionOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`n").Trim()
    if ($exitCode -ne 0) {
        throw "${ToolName}: version check failed with exit code $exitCode. $text"
    }

    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "${ToolName}: version check returned no version."
    }

    return $text
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repositoryRoot "eng/toolchain.json"
$globalJsonPath = Join-Path $repositoryRoot "global.json"

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$globalJson = Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json

$declaredDotnetVersion = [string] $globalJson.sdk.version
$declaredRollForward = [string] $globalJson.sdk.rollForward
if ($declaredDotnetVersion -ne [string] $manifest.dotnet.sdkVersion) {
    throw "dotnet: global.json SDK '$declaredDotnetVersion' does not match toolchain manifest SDK '$($manifest.dotnet.sdkVersion)'."
}

if ($declaredRollForward -ne "disable" -or [string] $manifest.dotnet.rollForward -ne "disable") {
    throw "dotnet: SDK roll-forward must be disabled in global.json and the toolchain manifest."
}

$resolvedDotnet = Resolve-ToolExecutable -ToolName "dotnet" -Executable $DotnetExecutable
$actualDotnetVersion = Invoke-ToolVersion -ToolName "dotnet" -Executable $resolvedDotnet
if ($actualDotnetVersion -ne $declaredDotnetVersion) {
    throw "dotnet: required SDK '$declaredDotnetVersion', but '$resolvedDotnet' selected '$actualDotnetVersion'."
}

$godotEnvironmentVariable = [string] $manifest.godot.executableEnvironmentVariable
$declaredGodotVariant = [string] $manifest.godot.variant
if ($declaredGodotVariant -ne "dotnet") {
    throw "Godot: toolchain manifest variant must be 'dotnet', but it declares '$declaredGodotVariant'."
}

if ([string]::IsNullOrWhiteSpace($GodotExecutable)) {
    $GodotExecutable = [Environment]::GetEnvironmentVariable($godotEnvironmentVariable)
}

if ([string]::IsNullOrWhiteSpace($GodotExecutable)) {
    throw "Godot: executable path is required. Pass -GodotExecutable or set $godotEnvironmentVariable."
}

$resolvedGodot = Resolve-ToolExecutable -ToolName "Godot" -Executable $GodotExecutable
$actualGodotVersion = Invoke-ToolVersion -ToolName "Godot" -Executable $resolvedGodot
$declaredGodotVersion = [string] $manifest.godot.version

if ($actualGodotVersion -notmatch "^$([Regex]::Escape($declaredGodotVersion))(?=($|[.\-+]))") {
    throw "Godot: required version '$declaredGodotVersion', but '$resolvedGodot' reported '$actualGodotVersion'."
}

if ($actualGodotVersion -notmatch "(^|[.\-])mono([.\-]|$)") {
    throw "Godot: '$resolvedGodot' is not the required .NET editor. It reported '$actualGodotVersion'."
}

[pscustomobject] @{
    DotnetExecutable = $resolvedDotnet
    DotnetVersion = $actualDotnetVersion
    GodotExecutable = $resolvedGodot
    GodotVersion = $declaredGodotVersion
    GodotVariant = $declaredGodotVariant
}
