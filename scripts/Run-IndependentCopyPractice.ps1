[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $GodotExecutable
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$gdUnitRoot = Join-Path $repositoryRoot "addons\gdUnit4"
$practiceTestSource = Join-Path $repositoryRoot "tests\practice\IndependentConsumerAddonTests.cs"
$stageScript = Join-Path $repositoryRoot "scripts\Stage-SpatialCircuitsAddon.ps1"
$toolchainScript = Join-Path $repositoryRoot "scripts\Test-Toolchain.ps1"
$processModule = Join-Path $repositoryRoot "scripts\SpatialWires.Process.psm1"
$practiceModule = Join-Path $repositoryRoot "scripts\SpatialWires.Practice.psm1"
$practiceContext = $null

Import-Module $processModule -Force
Import-Module $practiceModule -Force

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable does not exist: '$GodotExecutable'."
}
if (-not (Test-Path -LiteralPath $gdUnitRoot -PathType Container)) {
    throw "Pinned gdUnit4 test infrastructure does not exist: '$gdUnitRoot'."
}
if (-not (Test-Path -LiteralPath $practiceTestSource -PathType Leaf)) {
    throw "Independent-consumer gdUnit4 test does not exist: '$practiceTestSource'."
}

$resolvedGodot = (Resolve-Path -LiteralPath $GodotExecutable).Path
$powershellExecutable = (Get-Command powershell.exe -CommandType Application).Source
$dotnetExecutable = (Get-Command dotnet.exe -CommandType Application).Source

function Invoke-RequiredProcess {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter()]
        [string[]] $ArgumentList = @(),

        [Parameter(Mandatory)]
        [int] $TimeoutMilliseconds,

        [Parameter(Mandatory)]
        [string] $WorkingDirectory,

        [Parameter()]
        [hashtable] $Environment = @{}
    )

    $result = Invoke-BoundedProcess `
        -FilePath $FilePath `
        -ArgumentList $ArgumentList `
        -TimeoutMilliseconds $TimeoutMilliseconds `
        -WorkingDirectory $WorkingDirectory `
        -Environment $Environment

    [Console]::Out.WriteLine("[$Name] process code $($result.ExitCode), timedOut=$($result.TimedOut), durationMs=$($result.DurationMilliseconds)")
    if (-not [string]::IsNullOrEmpty($result.StandardOutput)) {
        [Console]::Out.Write($result.StandardOutput)
    }
    if (-not [string]::IsNullOrEmpty($result.StandardError)) {
        [Console]::Error.Write($result.StandardError)
    }

    if ($result.TimedOut) {
        throw "$Name exceeded $TimeoutMilliseconds ms. Process code: $($result.ExitCode)."
    }
    if ($result.ExitCode -ne 0) {
        throw "$Name failed with process code $($result.ExitCode) after $($result.DurationMilliseconds) ms."
    }

    return $result
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string] $Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([System.BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace("-", "").ToLowerInvariant()
        }
        finally {
            $algorithm.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Write-PracticeProject {
    param([Parameter(Mandatory)][string] $ProjectRoot)

    $testsPath = Join-Path $ProjectRoot "tests"
    New-Item -ItemType Directory -Path $testsPath -Force | Out-Null

    [System.IO.File]::WriteAllText(
        (Join-Path $ProjectRoot "global.json"),
        "{`n  `"sdk`": {`n    `"version`": `"8.0.410`",`n    `"rollForward`": `"disable`"`n  }`n}`n",
        [System.Text.UTF8Encoding]::new($false))

    [System.IO.File]::WriteAllText(
        (Join-Path $ProjectRoot "SpatialWires.IndependentConsumer.csproj"),
        @'
<Project Sdk="Godot.NET.Sdk/4.7.1">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
    <PackageReference Include="gdUnit4.api" Version="5.0.0" />
    <PackageReference Include="gdUnit4.test.adapter" Version="3.0.0" />
  </ItemGroup>
</Project>
'@ + "`n",
        [System.Text.UTF8Encoding]::new($false))

    [System.IO.File]::WriteAllText(
        (Join-Path $ProjectRoot "project.godot"),
        @'
; Generated independent Godot .NET consumer for Spatial Circuits practice.

config_version=5

[application]

config/name="SpatialWires Independent Consumer"
config/features=PackedStringArray("4.7", "C#")

[dotnet]

project/assembly_name="SpatialWires.IndependentConsumer"

[editor_plugins]

enabled=PackedStringArray("res://addons/gdUnit4/plugin.cfg", "res://addons/spatial_circuits/plugin.cfg")

[rendering]

renderer/rendering_method="gl_compatibility"
'@ + "`n",
        [System.Text.UTF8Encoding]::new($false))

    [System.IO.File]::WriteAllText(
        (Join-Path $ProjectRoot "gdunit4.runsettings"),
        @'
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <RunConfiguration>
    <MaxCpuCount>1</MaxCpuCount>
    <TestSessionTimeout>180000</TestSessionTimeout>
    <TreatNoTestsAsError>true</TreatNoTestsAsError>
  </RunConfiguration>
  <GdUnit4>
    <Parameters>--headless --quit-after 300</Parameters>
    <DisplayName>FullyQualifiedName</DisplayName>
    <CaptureStdOut>true</CaptureStdOut>
    <CompileProcessTimeout>120000</CompileProcessTimeout>
    <GodotConnectTimeout>60000</GodotConnectTimeout>
  </GdUnit4>
</RunSettings>
'@ + "`n",
        [System.Text.UTF8Encoding]::new($false))

    Copy-Item -LiteralPath $practiceTestSource -Destination (Join-Path $testsPath "IndependentConsumerAddonTests.cs")
}

try {
    Invoke-RequiredProcess `
        -Name "toolchain check" `
        -FilePath $powershellExecutable `
        -ArgumentList @("-NoProfile", "-File", $toolchainScript, "-GodotExecutable", $resolvedGodot) `
        -TimeoutMilliseconds 30000 `
        -WorkingDirectory $repositoryRoot | Out-Null

    $practiceContext = New-SpatialWiresPracticeDirectory
    $resolvedRunRoot = $practiceContext.Root
    $resolvedRepositoryRoot = (Resolve-Path -LiteralPath $repositoryRoot).Path.TrimEnd("\") + "\"
    if ($resolvedRunRoot.StartsWith($resolvedRepositoryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Independent consumer must be outside the repository: '$resolvedRunRoot'."
    }

    Write-PracticeProject -ProjectRoot $resolvedRunRoot

    Invoke-RequiredProcess `
        -Name "addon staging" `
        -FilePath $powershellExecutable `
        -ArgumentList @("-NoProfile", "-File", $stageScript, "-RepositoryRoot", $repositoryRoot, "-OutputRoot", $resolvedRunRoot) `
        -TimeoutMilliseconds 30000 `
        -WorkingDirectory $repositoryRoot | Out-Null

    $productAddon = Join-Path $resolvedRunRoot "addons\spatial_circuits"
    $manifestPath = Join-Path $productAddon "spatial-circuits.manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Staging did not produce the addon manifest: '$manifestPath'."
    }
    if (Test-Path -LiteralPath (Join-Path $productAddon "gdUnit4")) {
        throw "The staged product addon contains gdUnit4 test infrastructure."
    }

    $consumerAddons = Join-Path $resolvedRunRoot "addons"
    Copy-Item -LiteralPath $gdUnitRoot -Destination (Join-Path $consumerAddons "gdUnit4") -Recurse -Force

    $manifestSha256 = Get-Sha256 -Path $manifestPath
    $consumerProject = Join-Path $resolvedRunRoot "SpatialWires.IndependentConsumer.csproj"
    $runSettings = Join-Path $resolvedRunRoot "gdunit4.runsettings"

    Invoke-RequiredProcess `
        -Name "independent-consumer managed build" `
        -FilePath $dotnetExecutable `
        -ArgumentList @("build", $consumerProject, "--nologo") `
        -TimeoutMilliseconds 180000 `
        -WorkingDirectory $resolvedRunRoot | Out-Null

    Invoke-RequiredProcess `
        -Name "Godot import" `
        -FilePath $resolvedGodot `
        -ArgumentList @("--headless", "--path", $resolvedRunRoot, "--import", "--quit-after", "300") `
        -TimeoutMilliseconds 60000 `
        -WorkingDirectory $resolvedRunRoot | Out-Null

    Invoke-RequiredProcess `
        -Name "Godot managed-solution build" `
        -FilePath $resolvedGodot `
        -ArgumentList @("--headless", "--path", $resolvedRunRoot, "--build-solutions", "--quit-after", "300") `
        -TimeoutMilliseconds 120000 `
        -WorkingDirectory $resolvedRunRoot | Out-Null

    Invoke-RequiredProcess `
        -Name "gdUnit4 public Node and Resource tests" `
        -FilePath $dotnetExecutable `
        -ArgumentList @("test", $consumerProject, "--settings", $runSettings, "--nologo", "--verbosity", "normal") `
        -TimeoutMilliseconds 240000 `
        -WorkingDirectory $resolvedRunRoot `
        -Environment @{ GODOT_BIN = $resolvedGodot } | Out-Null

    Write-Output "Independent-copy gdUnit4 practice passed."
    Write-Output "Practice root: $resolvedRunRoot"
    Write-Output "Manifest SHA-256: $manifestSha256"
}
finally {
    if ($null -ne $practiceContext -and (Test-Path -LiteralPath $practiceContext.Root)) {
        $removedPath = Remove-SpatialWiresPracticeDirectory `
            -Path $practiceContext.Root `
            -Identity $practiceContext.Identity `
            -TemporaryBoundary $practiceContext.TemporaryBoundary
        Write-Output "Cleanup removed: $removedPath"
    }
}
