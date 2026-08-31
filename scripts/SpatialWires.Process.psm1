Set-StrictMode -Version Latest

function ConvertTo-WindowsProcessArgument {
    param(
        [AllowEmptyString()]
        [string] $Argument
    )

    if ($Argument.Length -gt 0 -and $Argument -notmatch '[\s"]') {
        return $Argument
    }

    $escaped = [System.Text.RegularExpressions.Regex]::Replace($Argument, '(\\*)"', '$1$1\"')
    $escaped = [System.Text.RegularExpressions.Regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Stop-ChildProcessTree {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process
    )

    if ($Process.HasExited) {
        return
    }

    $taskkillPath = Join-Path $env:SystemRoot "System32\taskkill.exe"
    $killStartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $killStartInfo.FileName = $taskkillPath
    $killStartInfo.Arguments = "/PID $($Process.Id) /T /F"
    $killStartInfo.UseShellExecute = $false
    $killStartInfo.CreateNoWindow = $true
    $killStartInfo.RedirectStandardOutput = $true
    $killStartInfo.RedirectStandardError = $true

    $killer = New-Object System.Diagnostics.Process
    $killer.StartInfo = $killStartInfo
    try {
        if (-not $killer.Start()) {
            throw "Could not start taskkill for child process $($Process.Id)."
        }
        $killStdOut = $killer.StandardOutput.ReadToEndAsync()
        $killStdErr = $killer.StandardError.ReadToEndAsync()
        $killer.WaitForExit()
        [System.Threading.Tasks.Task]::WaitAll(@($killStdOut, $killStdErr))

        if ($killer.ExitCode -ne 0 -and -not $Process.HasExited) {
            throw "Could not terminate child process tree $($Process.Id). taskkill exited $($killer.ExitCode): $($killStdErr.Result)"
        }
    }
    finally {
        $killer.Dispose()
    }
}

function Invoke-BoundedProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter()]
        [AllowEmptyCollection()]
        [string[]] $ArgumentList = @(),

        [Parameter(Mandatory)]
        [ValidateRange(1, [int]::MaxValue)]
        [int] $TimeoutMilliseconds,

        [Parameter()]
        [string] $WorkingDirectory = (Get-Location).Path,

        [Parameter()]
        [hashtable] $Environment = @{}
    )

    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
        throw "Process executable does not exist: '$FilePath'."
    }
    if (-not (Test-Path -LiteralPath $WorkingDirectory -PathType Container)) {
        throw "Process working directory does not exist: '$WorkingDirectory'."
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = (Resolve-Path -LiteralPath $FilePath).Path
    $startInfo.Arguments = (($ArgumentList | ForEach-Object { ConvertTo-WindowsProcessArgument -Argument $_ }) -join " ")
    $startInfo.WorkingDirectory = (Resolve-Path -LiteralPath $WorkingDirectory).Path
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($name in $Environment.Keys) {
        $startInfo.EnvironmentVariables[[string] $name] = [string] $Environment[$name]
    }

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $started = $false

    try {
        if (-not $process.Start()) {
            throw "Could not start process '$FilePath'."
        }
        $started = $true

        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit($TimeoutMilliseconds)
        if ($timedOut) {
            Stop-ChildProcessTree -Process $process
        }

        $process.WaitForExit()
        [System.Threading.Tasks.Task]::WaitAll(@($standardOutput, $standardError))
        $stopwatch.Stop()

        return [pscustomobject] @{
            FilePath = $startInfo.FileName
            ProcessId = $process.Id
            TimedOut = $timedOut
            ExitCode = $process.ExitCode
            StandardOutput = $standardOutput.Result
            StandardError = $standardError.Result
            DurationMilliseconds = $stopwatch.ElapsedMilliseconds
        }
    }
    finally {
        $stopwatch.Stop()
        if ($started -and -not $process.HasExited) {
            Stop-ChildProcessTree -Process $process
            $process.WaitForExit()
        }
        $process.Dispose()
    }
}

Export-ModuleMember -Function Invoke-BoundedProcess
