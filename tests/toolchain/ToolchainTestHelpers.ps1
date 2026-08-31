$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Equal {
    param(
        [Parameter(Mandatory)]
        $Expected,

        [Parameter(Mandatory)]
        $Actual,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Assert-Contains {
    param(
        [Parameter(Mandatory)]
        [string] $Expected,

        [Parameter(Mandatory)]
        [string] $Actual,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not $Actual.Contains($Expected)) {
        throw "$Message Expected output to contain '$Expected'. Actual output: $Actual"
    }
}

function Assert-PathDoesNotExist {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (Test-Path -LiteralPath $Path) {
        throw "$Message Unexpected path: $Path"
    }
}

function Assert-VersionProbeOnly {
    param(
        [Parameter(Mandatory)]
        $Fixture,

        [Parameter(Mandatory)]
        [string] $Message
    )

    Assert-PathDoesNotExist -Path $Fixture.CompilationPath -Message $Message
    if (Test-Path -LiteralPath $Fixture.InvocationPath) {
        $invocations = (Get-Content -LiteralPath $Fixture.InvocationPath -Raw).Trim()
        Assert-Equal -Expected "--version" -Actual $invocations -Message $Message
    }
}

function New-VersionFixture {
    param(
        [Parameter(Mandatory)]
        [string] $Directory,

        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $VersionOutput,

        [Parameter()]
        [int] $ExitCode = 0
    )

    $fixturePath = Join-Path $Directory "$Name.cmd"
    $invocationPath = Join-Path $Directory "$Name.invocations.txt"
    $compilationPath = Join-Path $Directory "$Name.compilation-started.txt"
    $escapedInvocationPath = $invocationPath.Replace("%", "%%")
    $escapedCompilationPath = $compilationPath.Replace("%", "%%")
    $escapedOutput = $VersionOutput.Replace("%", "%%")

    $content = @(
        "@echo off"
        "echo %*>>`"$escapedInvocationPath`""
        "if /I `"%~1`"==`"build`" echo compilation-started>`"$escapedCompilationPath`""
        "echo $escapedOutput"
        "exit /b $ExitCode"
    )
    Set-Content -LiteralPath $fixturePath -Value $content -Encoding Ascii

    return [pscustomobject] @{
        Executable = $fixturePath
        InvocationPath = $invocationPath
        CompilationPath = $compilationPath
    }
}

function Invoke-ToolchainFixture {
    param(
        [Parameter(Mandatory)]
        [string] $ToolchainScript,

        [Parameter(Mandatory)]
        [string] $DotnetExecutable,

        [Parameter()]
        [string] $GodotExecutable
    )

    $arguments = @(
        "-NoProfile"
        "-File"
        $ToolchainScript
        "-DotnetExecutable"
        $DotnetExecutable
    )
    if (-not [string]::IsNullOrWhiteSpace($GodotExecutable)) {
        $arguments += @("-GodotExecutable", $GodotExecutable)
    }

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & powershell @arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return [pscustomobject] @{
        ExitCode = $exitCode
        Output = (($output | ForEach-Object { $_.ToString() }) -join "`n")
    }
}
