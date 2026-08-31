$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$modulePath = Join-Path $repositoryRoot "scripts\SpatialWires.Process.psm1"
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("spatial-wires-process-test-" + [Guid]::NewGuid().ToString("N"))
$powershellExecutable = (Get-Command powershell.exe -CommandType Application).Source

Import-Module $modulePath -Force

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Wait-ProcessAbsent {
    param([int] $Id)
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        if ($null -eq (Get-Process -Id $Id -ErrorAction SilentlyContinue)) {
            return $true
        }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)
    return $false
}

function Test-SuccessfulExitIsPreserved {
    $result = Invoke-BoundedProcess `
        -FilePath $powershellExecutable `
        -ArgumentList @("-NoProfile", "-Command", "exit 0") `
        -TimeoutMilliseconds 5000 `
        -WorkingDirectory $fixtureRoot

    Assert-True ($result.ExitCode -eq 0) "Successful exit code was not preserved."
    Assert-True (-not $result.TimedOut) "Successful process was reported as timed out."
}

function Test-NonzeroExitIsPreserved {
    $result = Invoke-BoundedProcess `
        -FilePath $powershellExecutable `
        -ArgumentList @("-NoProfile", "-Command", "exit 37") `
        -TimeoutMilliseconds 5000 `
        -WorkingDirectory $fixtureRoot

    Assert-True ($result.ExitCode -eq 37) "Nonzero exit code 37 was changed to $($result.ExitCode)."
    Assert-True (-not $result.TimedOut) "Nonzero process was reported as timed out."
}

function Test-StandardOutputAndErrorAreCapturedSeparately {
    $result = Invoke-BoundedProcess `
        -FilePath $powershellExecutable `
        -ArgumentList @("-NoProfile", "-Command", "[Console]::Out.Write('stdout-value'); [Console]::Error.Write('stderr-value')") `
        -TimeoutMilliseconds 5000 `
        -WorkingDirectory $fixtureRoot

    Assert-True ($result.StandardOutput -eq "stdout-value") "stdout was not captured exactly. Actual: '$($result.StandardOutput)'"
    Assert-True ($result.StandardError -eq "stderr-value") "stderr was not captured exactly. Actual: '$($result.StandardError)'"
}

function Test-TimeoutTerminatesOnlyTheOwnedProcessTree {
    $parentPidPath = Join-Path $fixtureRoot "parent.pid"
    $childPidPath = Join-Path $fixtureRoot "child.pid"
    $fixtureScript = Join-Path $fixtureRoot "timeout fixture.ps1"
    Set-Content -LiteralPath $fixtureScript -Encoding UTF8 -Value @'
param([string] $ParentPidPath, [string] $ChildPidPath)
Set-Content -LiteralPath $ParentPidPath -Value $PID -NoNewline
$child = Start-Process powershell.exe -ArgumentList '-NoProfile', '-Command', 'Start-Sleep -Seconds 30' -PassThru
Set-Content -LiteralPath $ChildPidPath -Value $child.Id -NoNewline
Start-Sleep -Seconds 30
'@

    $unrelated = Start-Process powershell.exe -ArgumentList "-NoProfile", "-Command", "Start-Sleep -Seconds 30" -PassThru
    try {
        $result = Invoke-BoundedProcess `
            -FilePath $powershellExecutable `
            -ArgumentList @("-NoProfile", "-File", $fixtureScript, $parentPidPath, $childPidPath) `
            -TimeoutMilliseconds 1000 `
            -WorkingDirectory $fixtureRoot

        Assert-True $result.TimedOut "Timeout was not reported."
        Assert-True (Test-Path -LiteralPath $parentPidPath) "Timeout fixture did not record its parent PID."
        Assert-True (Test-Path -LiteralPath $childPidPath) "Timeout fixture did not record its child PID."
        $parentPid = [int] (Get-Content -LiteralPath $parentPidPath -Raw)
        $childPid = [int] (Get-Content -LiteralPath $childPidPath -Raw)
        Assert-True (Wait-ProcessAbsent -Id $parentPid) "Timed-out parent process $parentPid is still running."
        Assert-True (Wait-ProcessAbsent -Id $childPid) "Timed-out child process $childPid is still running."
        Assert-True ($null -ne (Get-Process -Id $unrelated.Id -ErrorAction SilentlyContinue)) "Unrelated process $($unrelated.Id) was terminated."
    }
    finally {
        if (-not $unrelated.HasExited) {
            $unrelated.Kill()
            $unrelated.WaitForExit()
        }
        $unrelated.Dispose()
    }
}

try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    Test-SuccessfulExitIsPreserved
    Test-NonzeroExitIsPreserved
    Test-StandardOutputAndErrorAreCapturedSeparately
    Test-TimeoutTerminatesOnlyTheOwnedProcessTree
    Write-Output "PASS: bounded process runner"
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
