Set-StrictMode -Version Latest

$script:PracticePrefix = "spatial-wires-independent-consumer-"
$script:IdentityFileName = ".spatial-wires-practice.json"
$script:PracticePurpose = "SpatialWires independent consumer"

function Resolve-PracticeDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Description does not exist: '$Path'."
    }

    return (Resolve-Path -LiteralPath $Path).Path.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

function Assert-DirectPracticeChild {
    param(
        [Parameter(Mandatory)]
        [string] $ResolvedPath,

        [Parameter(Mandatory)]
        [string] $ResolvedTemporaryBoundary,

        [Parameter(Mandatory)]
        [string] $Identity
    )

    if ($Identity -notmatch '^[0-9a-f]{32}$') {
        throw "Practice identity is invalid: '$Identity'."
    }

    $expectedLeaf = $script:PracticePrefix + $Identity
    $actualParent = (Split-Path -Parent $ResolvedPath).TrimEnd("\")
    $actualLeaf = Split-Path -Leaf $ResolvedPath
    if (-not $actualParent.Equals($ResolvedTemporaryBoundary, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing practice path outside the temporary boundary: '$ResolvedPath'."
    }
    if (-not $actualLeaf.Equals($expectedLeaf, [System.StringComparison]::Ordinal)) {
        throw "Refusing practice path with unexpected prefix or identity: '$ResolvedPath'."
    }
}

function New-SpatialWiresPracticeDirectory {
    [CmdletBinding()]
    param(
        [Parameter()]
        [string] $TemporaryBoundary = [System.IO.Path]::GetTempPath()
    )

    $resolvedBoundary = Resolve-PracticeDirectory -Path $TemporaryBoundary -Description "Temporary boundary"
    $identity = [Guid]::NewGuid().ToString("N")
    $practicePath = Join-Path $resolvedBoundary ($script:PracticePrefix + $identity)
    New-Item -ItemType Directory -Path $practicePath | Out-Null

    $resolvedPracticePath = Resolve-PracticeDirectory -Path $practicePath -Description "Practice directory"
    Assert-DirectPracticeChild `
        -ResolvedPath $resolvedPracticePath `
        -ResolvedTemporaryBoundary $resolvedBoundary `
        -Identity $identity

    $directoryInfo = Get-Item -LiteralPath $resolvedPracticePath -Force
    if (($directoryInfo.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Practice directory cannot be a linked filesystem entry: '$resolvedPracticePath'."
    }

    $identityDocument = [ordered]@{
        schemaVersion = 1
        purpose = $script:PracticePurpose
        identity = $identity
    } | ConvertTo-Json
    $identityPath = Join-Path $resolvedPracticePath $script:IdentityFileName
    [System.IO.File]::WriteAllText($identityPath, "$identityDocument`n", [System.Text.UTF8Encoding]::new($false))

    return [pscustomobject]@{
        Root = $resolvedPracticePath
        Identity = $identity
        TemporaryBoundary = $resolvedBoundary
        IdentityPath = $identityPath
    }
}

function Remove-SpatialWiresPracticeDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Identity,

        [Parameter(Mandatory)]
        [string] $TemporaryBoundary
    )

    $resolvedBoundary = Resolve-PracticeDirectory -Path $TemporaryBoundary -Description "Temporary boundary"
    $resolvedPracticePath = Resolve-PracticeDirectory -Path $Path -Description "Practice directory"
    Assert-DirectPracticeChild `
        -ResolvedPath $resolvedPracticePath `
        -ResolvedTemporaryBoundary $resolvedBoundary `
        -Identity $Identity

    $directoryInfo = Get-Item -LiteralPath $resolvedPracticePath -Force
    if (($directoryInfo.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to clean linked practice directory '$resolvedPracticePath'."
    }

    $identityPath = Join-Path $resolvedPracticePath $script:IdentityFileName
    if (-not (Test-Path -LiteralPath $identityPath -PathType Leaf)) {
        throw "Refusing to clean practice directory without its identity file: '$resolvedPracticePath'."
    }
    $identityInfo = Get-Item -LiteralPath $identityPath -Force
    if (($identityInfo.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to clean practice directory with a linked identity file: '$resolvedPracticePath'."
    }

    try {
        $identityDocument = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "Refusing to clean practice directory with an invalid identity file: '$resolvedPracticePath'."
    }

    if ([int]$identityDocument.schemaVersion -ne 1 -or
        [string]$identityDocument.purpose -cne $script:PracticePurpose -or
        [string]$identityDocument.identity -cne $Identity) {
        throw "Refusing to clean practice directory whose identity does not match the current run: '$resolvedPracticePath'."
    }

    Remove-Item -LiteralPath $resolvedPracticePath -Recurse -Force
    if (Test-Path -LiteralPath $resolvedPracticePath) {
        throw "Practice cleanup did not remove '$resolvedPracticePath'."
    }

    return $resolvedPracticePath
}

Export-ModuleMember -Function New-SpatialWiresPracticeDirectory, Remove-SpatialWiresPracticeDirectory
