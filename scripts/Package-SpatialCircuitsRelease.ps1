[CmdletBinding()]
param(
    [Parameter()]
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),

    [Parameter(Mandatory)]
    [string] $OutputRoot,

    [Parameter()]
    [string] $PackageVersion = "0.15.0"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryPath = (Resolve-Path -LiteralPath $RepositoryRoot).Path.TrimEnd("\")
$outputPath = [System.IO.Path]::GetFullPath($OutputRoot).TrimEnd("\")
$normalizedOutputRoot = $outputPath + [System.IO.Path]::DirectorySeparatorChar
if ($outputPath.Equals($repositoryPath, [System.StringComparison]::OrdinalIgnoreCase) -or
    $outputPath.StartsWith("$repositoryPath\", [System.StringComparison]::OrdinalIgnoreCase) -or
    $repositoryPath.StartsWith("$outputPath\", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Release output cannot contain or overwrite the repository: '$outputPath'."
}
if ([string]::IsNullOrWhiteSpace($PackageVersion) -or $PackageVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "Package version must be a numeric major.minor.patch value."
}

$stageScript = Join-Path $repositoryPath "scripts\Stage-SpatialCircuitsAddon.ps1"
$toolchainPath = Join-Path $repositoryPath "eng\toolchain.json"
if (Test-Path -LiteralPath $outputPath) {
    [System.IO.Directory]::Delete($outputPath, $true)
}
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

& $stageScript -RepositoryRoot $repositoryPath -OutputRoot $outputPath | Out-Null

$toolchain = Get-Content -LiteralPath $toolchainPath -Raw | ConvertFrom-Json
$manifestPath = Join-Path $outputPath "addons\spatial_circuits\spatial-circuits.manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Addon staging did not create '$manifestPath'."
}

function Get-RelativePath {
    param([string] $Path)

    $normalizedPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $normalizedPath.StartsWith($normalizedOutputRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Package file is outside the package root: '$Path'."
    }

    return $normalizedPath.Substring($normalizedOutputRoot.Length).Replace("\", "/")
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

$files = Get-ChildItem -LiteralPath $outputPath -File -Recurse |
    Sort-Object { Get-RelativePath $_.FullName }
$entries = foreach ($file in $files) {
    if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Package file is a linked filesystem entry: '$($file.FullName)'."
    }

    $relative = Get-RelativePath $file.FullName
    [ordered]@{
        path = $relative
        sha256 = Get-Sha256 $file.FullName
    }
}

$metadata = [ordered]@{
    schemaVersion = 1
    packageVersion = $PackageVersion
    product = "spatial-circuits"
    godotVersion = [string]$toolchain.godot.version
    godotVariant = [string]$toolchain.godot.variant
    dotnetSdkVersion = [string]$toolchain.dotnet.sdkVersion
    addonPath = "addons/spatial_circuits"
    sourcePath = "src"
    files = @($entries)
}
$metadataPath = Join-Path $outputPath "spatial-circuits.package.json"
$metadataJson = $metadata | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText($metadataPath, "$metadataJson`n", [System.Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    PackageRoot = $outputPath
    MetadataPath = $metadataPath
    FileCount = $entries.Count
    MetadataSha256 = Get-Sha256 $metadataPath
    AddonManifestSha256 = Get-Sha256 $manifestPath
}
