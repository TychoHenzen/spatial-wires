$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path.TrimEnd("\")
$releaseScript = Join-Path $repositoryRoot "scripts\Test-WindowsRelease.ps1"
$parentOutput = Split-Path -Parent $repositoryRoot

try {
    & $releaseScript -GodotExecutable (Join-Path $parentOutput "missing-godot.exe") -OutputRoot $parentOutput
}
catch {
    if ($_.Exception.Message -notlike "*cannot be inside the repository*") {
        throw "Unexpected boundary error: $($_.Exception.Message)"
    }

    Write-Output "PASS: Windows release output rejects repository parent"
    exit 0
}

throw "Expected the Windows release script to reject a repository parent output."
