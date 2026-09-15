$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$packageScript = Join-Path $repositoryRoot "scripts\Package-SpatialCircuitsRelease.ps1"
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("spatial-wires-package-test-" + [Guid]::NewGuid().ToString("N"))

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

function Assert-Package {
    param([string] $PackageRoot, $Result)
    $metadataPath = Join-Path $PackageRoot "spatial-circuits.package.json"
    Assert-True (Test-Path -LiteralPath $metadataPath -PathType Leaf) "Package metadata is missing."
    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    Assert-True ($metadata.schemaVersion -eq 1) "Package schema version is not 1."
    Assert-True ($metadata.packageVersion -eq "0.15.0") "Package version is not 0.15.0."
    Assert-True ($metadata.godotVersion -eq "4.7.1") "Package Godot version is not 4.7.1."
    Assert-True ($metadata.addonPath -eq "addons/spatial_circuits") "Package addon path is not portable."
    Assert-True ($metadata.sourcePath -eq "src") "Package source path is not portable."
    $paths = @($metadata.files.path)
    Assert-True (($paths -join "`n") -ceq (($paths | Sort-Object) -join "`n")) "Package paths are not sorted."
    foreach ($entry in $metadata.files) {
        Assert-True (-not [System.IO.Path]::IsPathRooted($entry.path)) "Package path is absolute: '$($entry.path)'."
        $filePath = Join-Path $PackageRoot $entry.path.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
        Assert-True (Test-Path -LiteralPath $filePath -PathType Leaf) "Package file is missing: '$($entry.path)'."
        Assert-True ((Get-Sha256 $filePath) -ceq $entry.sha256) "Package hash does not match '$($entry.path)'."
    }

    Assert-True ($Result.FileCount -eq $metadata.files.Count) "Reported package file count is wrong."
    Assert-True ($Result.MetadataSha256 -ceq (Get-Sha256 $metadataPath)) "Reported metadata hash is wrong."
    Assert-True (@(Get-ChildItem -LiteralPath $PackageRoot -File -Recurse |
        Where-Object { $_.Extension -in @('.dll', '.exe', '.pdb') }).Count -eq 0) "Package contains a binary artifact."
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    $firstRoot = Join-Path $testRoot "first"
    $secondRoot = Join-Path $testRoot "second"
    $first = & $packageScript -RepositoryRoot $repositoryRoot -OutputRoot $firstRoot
    $second = & $packageScript -RepositoryRoot $repositoryRoot -OutputRoot $secondRoot

    Assert-Package -PackageRoot $firstRoot -Result $first
    Assert-Package -PackageRoot $secondRoot -Result $second
    Assert-True ($first.MetadataSha256 -ceq $second.MetadataSha256) "Repeated packaging produced different metadata."
    Assert-True ($first.AddonManifestSha256 -ceq $second.AddonManifestSha256) "Repeated packaging produced different addon manifests."
    Assert-ThrowsLike {
        & $packageScript -RepositoryRoot $repositoryRoot -OutputRoot (Join-Path $repositoryRoot "release-output")
    } "*cannot contain or overwrite the repository*"

    Write-Output "PASS: release package metadata, hashes, and boundaries"
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
