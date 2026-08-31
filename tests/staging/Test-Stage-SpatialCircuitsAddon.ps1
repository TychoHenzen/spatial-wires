$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$stageScript = Join-Path $repositoryRoot "scripts\Stage-SpatialCircuitsAddon.ps1"
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("spatial-wires-stage-test-" + [Guid]::NewGuid().ToString("N"))

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-ThrowsLike {
    param([scriptblock] $Action, [string] $Pattern)
    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike $Pattern) {
            throw "Expected error like '$Pattern', received '$($_.Exception.Message)'."
        }
        return
    }
    throw "Expected error like '$Pattern', but the command succeeded."
}

function Get-Sha256 {
    param([string] $Path)
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

function New-MinimalRepository {
    param([string] $Name)
    $fixtureRoot = Join-Path $testRoot $Name
    $fixtureAddon = Join-Path $fixtureRoot "addons\spatial_circuits"
    New-Item -ItemType Directory -Path $fixtureAddon -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $fixtureAddon "plugin.cfg") -Value "[plugin]`nscript=`"Plugin.cs`"" -NoNewline
    Set-Content -LiteralPath (Join-Path $fixtureAddon "Plugin.cs") -Value "public sealed class Plugin {}" -NoNewline
    return $fixtureRoot
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null

    $firstOutput = Join-Path $testRoot "first"
    $secondOutput = Join-Path $testRoot "second"
    $first = & $stageScript -RepositoryRoot $repositoryRoot -OutputRoot $firstOutput
    $second = & $stageScript -RepositoryRoot $repositoryRoot -OutputRoot $secondOutput

    Assert-ThrowsLike { & $stageScript -RepositoryRoot $repositoryRoot -OutputRoot $repositoryRoot } "*overwrite addon source*"

    Assert-True (Test-Path -LiteralPath (Join-Path $first.AddonPath "plugin.cfg") -PathType Leaf) "The staged addon is missing plugin.cfg."
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $firstOutput "addons\gdUnit4"))) "The staged product includes gdUnit4."
    Assert-True ($first.ManifestSha256 -eq $second.ManifestSha256) "Repeated staging produced different manifests."

    $manifest = Get-Content -LiteralPath $first.ManifestPath -Raw | ConvertFrom-Json
    $paths = @($manifest.files.path)
    $sortedPaths = @($paths | Sort-Object)
    Assert-True (($paths -join "`n") -ceq ($sortedPaths -join "`n")) "Manifest paths are not sorted."
    Assert-True ($manifest.addonPath -eq "addons/spatial_circuits") "Manifest addon path is not portable."
    foreach ($entry in $manifest.files) {
        $stagedFile = Join-Path $first.AddonPath $entry.path.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
        $actualHash = Get-Sha256 -Path $stagedFile
        Assert-True ($actualHash -ceq $entry.sha256) "Manifest hash does not match '$($entry.path)'."
    }

    $projectReferenceRoot = New-MinimalRepository "project-reference"
    Set-Content -LiteralPath (Join-Path $projectReferenceRoot "addons\spatial_circuits\portable.csproj") -Value '<Project><ItemGroup><ProjectReference Include="..\..\src\Core.csproj" /></ItemGroup></Project>'
    Assert-ThrowsLike { & $stageScript -RepositoryRoot $projectReferenceRoot -OutputRoot (Join-Path $testRoot "bad-project") } "*ProjectReference*"

    $linkedSourceRoot = New-MinimalRepository "linked-source"
    Set-Content -LiteralPath (Join-Path $linkedSourceRoot "addons\spatial_circuits\portable.csproj") -Value '<Project><ItemGroup><Compile Include="..\Shared.cs" Link="Shared.cs" /></ItemGroup></Project>'
    Assert-ThrowsLike { & $stageScript -RepositoryRoot $linkedSourceRoot -OutputRoot (Join-Path $testRoot "bad-link") } "*linked source*"

    $binaryRoot = New-MinimalRepository "binary"
    Set-Content -LiteralPath (Join-Path $binaryRoot "addons\spatial_circuits\dependency.dll") -Value "not-a-real-binary"
    Assert-ThrowsLike { & $stageScript -RepositoryRoot $binaryRoot -OutputRoot (Join-Path $testRoot "bad-binary") } "*binary dependency*"

    $relativeRoot = New-MinimalRepository "relative"
    Set-Content -LiteralPath (Join-Path $relativeRoot "addons\spatial_circuits\portable.props") -Value '<Project><PropertyGroup><HintPath>..\repo.dll</HintPath></PropertyGroup></Project>'
    Assert-ThrowsLike { & $stageScript -RepositoryRoot $relativeRoot -OutputRoot (Join-Path $testRoot "bad-relative") } "*repository-relative dependency*"

    Write-Output "Stage-SpatialCircuitsAddon checks passed."
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
