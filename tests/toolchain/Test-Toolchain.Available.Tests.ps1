$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "ToolchainTestHelpers.ps1")

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$toolchainScript = Join-Path $repositoryRoot "scripts/Test-Toolchain.ps1"

# covers: spatial-circuits/package-baseline :: Reproducible development baseline :: Declared toolchain is available
function Test-DeclaredToolchainIsAvailable {
    $fixtureDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("spatial-wires-toolchain-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $fixtureDirectory | Out-Null

    try {
        $dotnet = New-VersionFixture -Directory $fixtureDirectory -Name "dotnet" -VersionOutput "8.0.410"
        $godot = New-VersionFixture -Directory $fixtureDirectory -Name "godot" -VersionOutput "4.7.2.stable.mono.official.test"

        $result = Invoke-ToolchainFixture `
            -ToolchainScript $toolchainScript `
            -DotnetExecutable $dotnet.Executable `
            -GodotExecutable $godot.Executable

        Assert-Equal -Expected 0 -Actual $result.ExitCode -Message "The declared toolchain should pass."
        Assert-Contains -Expected "8.0.410" -Actual $result.Output -Message "The selected .NET SDK should be reported."
        Assert-Contains -Expected "4.7.2" -Actual $result.Output -Message "The selected Godot version should be reported."
        Assert-Equal -Expected "--version" -Actual (Get-Content -LiteralPath $dotnet.InvocationPath -Raw).Trim() -Message "dotnet should only receive a version probe."
        Assert-Equal -Expected "--version" -Actual (Get-Content -LiteralPath $godot.InvocationPath -Raw).Trim() -Message "Godot should only receive a version probe."
    }
    finally {
        Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force
    }
}

# covers: spatial-circuits/package-baseline :: Reproducible development baseline :: Declared toolchain is available
function Test-DeclaredGodotCanBeSelectedFromProjectEnvironment {
    $fixtureDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("spatial-wires-toolchain-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $fixtureDirectory | Out-Null
    $previousGodotExecutable = [Environment]::GetEnvironmentVariable("SPATIAL_WIRES_GODOT", "Process")

    try {
        $dotnet = New-VersionFixture -Directory $fixtureDirectory -Name "dotnet" -VersionOutput "8.0.410"
        $godot = New-VersionFixture -Directory $fixtureDirectory -Name "godot" -VersionOutput "4.7.2.stable.mono.official.test"
        [Environment]::SetEnvironmentVariable("SPATIAL_WIRES_GODOT", $godot.Executable, "Process")

        $result = Invoke-ToolchainFixture `
            -ToolchainScript $toolchainScript `
            -DotnetExecutable $dotnet.Executable

        Assert-Equal -Expected 0 -Actual $result.ExitCode -Message "The declared Godot editor should resolve from SPATIAL_WIRES_GODOT."
        Assert-Contains -Expected $godot.Executable -Actual $result.Output -Message "The environment-selected Godot executable should be reported."
        Assert-VersionProbeOnly -Fixture $dotnet -Message "The environment-selected toolchain should only be version checked."
        Assert-VersionProbeOnly -Fixture $godot -Message "The environment-selected toolchain should only be version checked."
    }
    finally {
        [Environment]::SetEnvironmentVariable("SPATIAL_WIRES_GODOT", $previousGodotExecutable, "Process")
        Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force
    }
}

Test-DeclaredToolchainIsAvailable
Test-DeclaredGodotCanBeSelectedFromProjectEnvironment
Write-Output "PASS: Declared toolchain is available"
