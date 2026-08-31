[CmdletBinding()]
param(
    [Parameter()]
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),

    [Parameter(Mandatory)]
    [string] $OutputRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryPath = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$sourceAddonPath = Join-Path $repositoryPath "addons\spatial_circuits"

if (-not (Test-Path -LiteralPath $sourceAddonPath -PathType Container)) {
    throw "Spatial Circuits addon source does not exist: '$sourceAddonPath'."
}

$outputPath = [System.IO.Path]::GetFullPath($OutputRoot)
$stagedAddonPath = Join-Path $outputPath "addons\spatial_circuits"
$manifestName = "spatial-circuits.manifest.json"
$binaryExtensions = @(".dll", ".exe", ".pdb", ".so", ".dylib", ".a", ".lib")
$metadataExtensions = @(".csproj", ".fsproj", ".vbproj", ".props", ".targets", ".cfg")

$normalizedSourceAddonPath = [System.IO.Path]::GetFullPath($sourceAddonPath).TrimEnd("\")
$normalizedStagedAddonPath = [System.IO.Path]::GetFullPath($stagedAddonPath).TrimEnd("\")
if ($normalizedStagedAddonPath.Equals($normalizedSourceAddonPath, [System.StringComparison]::OrdinalIgnoreCase) -or
    $normalizedStagedAddonPath.StartsWith("$normalizedSourceAddonPath\", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Output root would overwrite addon source: '$normalizedStagedAddonPath'."
}

function Get-PortableRelativePath {
    param(
        [Parameter(Mandatory)]
        [string] $BasePath,

        [Parameter(Mandatory)]
        [string] $Path
    )

    $normalizedBase = [System.IO.Path]::GetFullPath($BasePath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $normalizedPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $normalizedPath.StartsWith($normalizedBase, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$normalizedPath' is outside '$normalizedBase'."
    }

    return $normalizedPath.Substring($normalizedBase.Length).Replace("\", "/")
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

$sourceFiles = @(
    Get-ChildItem -LiteralPath $sourceAddonPath -File -Recurse |
        Where-Object { $_.Name -ne $manifestName -and $_.Extension -ne ".uid" } |
        Sort-Object { Get-PortableRelativePath -BasePath $sourceAddonPath -Path $_.FullName }
)

if ($sourceFiles.Count -eq 0) {
    throw "Spatial Circuits addon source contains no portable files: '$sourceAddonPath'."
}

foreach ($sourceFile in $sourceFiles) {
    $relativePath = Get-PortableRelativePath -BasePath $sourceAddonPath -Path $sourceFile.FullName

    if (($sourceFile.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Addon file '$relativePath' is a linked filesystem entry and is not portable."
    }

    if ($binaryExtensions -contains $sourceFile.Extension.ToLowerInvariant()) {
        throw "Addon file '$relativePath' is a binary dependency and is not portable."
    }

    if ($metadataExtensions -contains $sourceFile.Extension.ToLowerInvariant()) {
        $content = Get-Content -LiteralPath $sourceFile.FullName -Raw
        if ($content -match '(?i)<ProjectReference\b') {
            throw "Addon file '$relativePath' contains a ProjectReference and is not portable."
        }
        if ($content -match '(?i)<Compile\b[^>]*\bLink\s*=|<Link>') {
            throw "Addon file '$relativePath' contains linked source and is not portable."
        }
        if ($content -match '(?i)<HintPath\b|\.\.[\\/]') {
            throw "Addon file '$relativePath' contains a repository-relative dependency and is not portable."
        }
    }
}

if (Test-Path -LiteralPath $stagedAddonPath) {
    Remove-Item -LiteralPath $stagedAddonPath -Recurse -Force
}

New-Item -ItemType Directory -Path $stagedAddonPath -Force | Out-Null

$manifestFiles = foreach ($sourceFile in $sourceFiles) {
    $relativePath = Get-PortableRelativePath -BasePath $sourceAddonPath -Path $sourceFile.FullName
    $destinationPath = Join-Path $stagedAddonPath $relativePath.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
    $destinationDirectory = Split-Path -Parent $destinationPath
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $sourceFile.FullName -Destination $destinationPath

    [ordered]@{
        path = $relativePath
        sha256 = Get-Sha256 -Path $destinationPath
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    addonPath = "addons/spatial_circuits"
    files = @($manifestFiles)
}

$manifestPath = Join-Path $stagedAddonPath $manifestName
$manifestJson = $manifest | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText($manifestPath, "$manifestJson`n", [System.Text.UTF8Encoding]::new($false))

[PSCustomObject]@{
    AddonPath = $stagedAddonPath
    ManifestPath = $manifestPath
    FileCount = $manifestFiles.Count
    ManifestSha256 = Get-Sha256 -Path $manifestPath
}
