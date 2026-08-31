[CmdletBinding()]
param(
    [Parameter()]
    [string] $GodotExecutable
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if (-not [string]::IsNullOrWhiteSpace($GodotExecutable)) {
    if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
        throw "Godot executable does not exist: '$GodotExecutable'."
    }

    $env:GODOT_BIN = (Resolve-Path -LiteralPath $GodotExecutable).Path
}

if ([string]::IsNullOrWhiteSpace($env:GODOT_BIN)) {
    throw "GODOT_BIN is required. Set it or pass -GodotExecutable."
}

if (-not (Test-Path -LiteralPath $env:GODOT_BIN -PathType Leaf)) {
    throw "GODOT_BIN does not identify a file: '$env:GODOT_BIN'."
}

$projectPath = Join-Path $PSScriptRoot "SpatialWires.Godot.Tests.csproj"
$settingsPath = Join-Path $PSScriptRoot "gdunit4.runsettings"

& dotnet test $projectPath --settings $settingsPath --nologo --verbosity normal
exit $LASTEXITCODE
