$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "ToolchainTestHelpers.ps1")

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$toolchainScript = Join-Path $repositoryRoot "scripts/Test-Toolchain.ps1"

function New-TestDirectory {
    $directory = Join-Path ([System.IO.Path]::GetTempPath()) ("spatial-wires-toolchain-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $directory | Out-Null
    return $directory
}

function Assert-ToolchainFailure {
    param(
        [Parameter(Mandatory)]
        $Result,

        [Parameter(Mandatory)]
        [string] $ToolName
    )

    if ($Result.ExitCode -eq 0) {
        throw "$ToolName failure should return a nonzero exit code. Output: $($Result.Output)"
    }

    Assert-Contains -Expected $ToolName -Actual $Result.Output -Message "The diagnostic should identify the offending tool."
}

# covers: spatial-circuits/package-baseline :: Reproducible development baseline :: Declared toolchain is unavailable
function Test-MissingDotnetSdkFailsBeforeCompilation {
    $fixtureDirectory = New-TestDirectory

    try {
        $dotnet = New-VersionFixture `
            -Directory $fixtureDirectory `
            -Name "dotnet" `
            -VersionOutput "A compatible installed .NET SDK for global.json version 8.0.410 was not found." `
            -ExitCode 1
        $godot = New-VersionFixture -Directory $fixtureDirectory -Name "godot" -VersionOutput "4.7.1.stable.mono.official.test"

        $result = Invoke-ToolchainFixture `
            -ToolchainScript $toolchainScript `
            -DotnetExecutable $dotnet.Executable `
            -GodotExecutable $godot.Executable

        Assert-ToolchainFailure -Result $result -ToolName "dotnet"
        Assert-VersionProbeOnly -Fixture $dotnet -Message "A missing .NET SDK must fail before compilation."
        Assert-PathDoesNotExist -Path $godot.InvocationPath -Message "Godot should not run after the dotnet check fails."
        Assert-PathDoesNotExist -Path $godot.CompilationPath -Message "Godot compilation should not start."
    }
    finally {
        Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force
    }
}

# covers: spatial-circuits/package-baseline :: Reproducible development baseline :: Declared toolchain is unavailable
function Test-WrongDotnetSdkFailsBeforeCompilation {
    $fixtureDirectory = New-TestDirectory

    try {
        $dotnet = New-VersionFixture -Directory $fixtureDirectory -Name "dotnet" -VersionOutput "8.0.411"
        $godot = New-VersionFixture -Directory $fixtureDirectory -Name "godot" -VersionOutput "4.7.1.stable.mono.official.test"

        $result = Invoke-ToolchainFixture `
            -ToolchainScript $toolchainScript `
            -DotnetExecutable $dotnet.Executable `
            -GodotExecutable $godot.Executable

        Assert-ToolchainFailure -Result $result -ToolName "dotnet"
        Assert-VersionProbeOnly -Fixture $dotnet -Message "A wrong .NET SDK must fail before compilation."
        Assert-PathDoesNotExist -Path $godot.InvocationPath -Message "Godot should not run after the dotnet check fails."
        Assert-PathDoesNotExist -Path $godot.CompilationPath -Message "Godot compilation should not start."
    }
    finally {
        Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force
    }
}

# covers: spatial-circuits/package-baseline :: Reproducible development baseline :: Declared toolchain is unavailable
function Test-MissingGodotFailsBeforeCompilation {
    $fixtureDirectory = New-TestDirectory

    try {
        $dotnet = New-VersionFixture -Directory $fixtureDirectory -Name "dotnet" -VersionOutput "8.0.410"
        $missingGodot = Join-Path $fixtureDirectory "missing-godot.exe"

        $result = Invoke-ToolchainFixture `
            -ToolchainScript $toolchainScript `
            -DotnetExecutable $dotnet.Executable `
            -GodotExecutable $missingGodot

        Assert-ToolchainFailure -Result $result -ToolName "Godot"
        Assert-VersionProbeOnly -Fixture $dotnet -Message "A missing Godot editor must fail before compilation."
        Assert-PathDoesNotExist -Path (Join-Path $fixtureDirectory "godot.compilation-started.txt") -Message "Godot compilation should not start."
    }
    finally {
        Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force
    }
}

# covers: spatial-circuits/package-baseline :: Reproducible development baseline :: Declared toolchain is unavailable
function Test-WrongGodotVersionFailsBeforeCompilation {
    $fixtureDirectory = New-TestDirectory

    try {
        $dotnet = New-VersionFixture -Directory $fixtureDirectory -Name "dotnet" -VersionOutput "8.0.410"
        $godot = New-VersionFixture -Directory $fixtureDirectory -Name "godot" -VersionOutput "4.7.2.stable.mono.official.test"

        $result = Invoke-ToolchainFixture `
            -ToolchainScript $toolchainScript `
            -DotnetExecutable $dotnet.Executable `
            -GodotExecutable $godot.Executable

        Assert-ToolchainFailure -Result $result -ToolName "Godot"
        Assert-VersionProbeOnly -Fixture $dotnet -Message "A wrong Godot version must fail before compilation."
        Assert-VersionProbeOnly -Fixture $godot -Message "A wrong Godot version must fail before compilation."
    }
    finally {
        Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force
    }
}

# covers: spatial-circuits/package-baseline :: Reproducible development baseline :: Declared toolchain is unavailable
function Test-StandardGodotEditorFailsBeforeCompilation {
    $fixtureDirectory = New-TestDirectory

    try {
        $dotnet = New-VersionFixture -Directory $fixtureDirectory -Name "dotnet" -VersionOutput "8.0.410"
        $godot = New-VersionFixture -Directory $fixtureDirectory -Name "godot" -VersionOutput "4.7.1.stable.official.test"

        $result = Invoke-ToolchainFixture `
            -ToolchainScript $toolchainScript `
            -DotnetExecutable $dotnet.Executable `
            -GodotExecutable $godot.Executable

        Assert-ToolchainFailure -Result $result -ToolName "Godot"
        Assert-Contains -Expected ".NET editor" -Actual $result.Output -Message "The diagnostic should identify the standard non-.NET editor."
        Assert-VersionProbeOnly -Fixture $dotnet -Message "A standard Godot editor must fail before compilation."
        Assert-VersionProbeOnly -Fixture $godot -Message "A standard Godot editor must fail before compilation."
    }
    finally {
        Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force
    }
}

Test-MissingDotnetSdkFailsBeforeCompilation
Test-WrongDotnetSdkFailsBeforeCompilation
Test-MissingGodotFailsBeforeCompilation
Test-WrongGodotVersionFailsBeforeCompilation
Test-StandardGodotEditorFailsBeforeCompilation
Write-Output "PASS: Declared toolchain is unavailable"
