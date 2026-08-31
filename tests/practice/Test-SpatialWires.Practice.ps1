$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$modulePath = Join-Path $repositoryRoot "scripts\SpatialWires.Practice.psm1"
$systemTemporaryBoundary = (Resolve-Path -LiteralPath ([System.IO.Path]::GetTempPath())).Path.TrimEnd("\")
$testIdentity = [Guid]::NewGuid().ToString("N")
$testRoot = Join-Path $systemTemporaryBoundary ("spatial-wires-practice-cleanup-test-" + $testIdentity)
$boundary = Join-Path $testRoot "boundary"
$outside = Join-Path $testRoot "outside"

Import-Module $modulePath -Force

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

function Test-SuccessfulCleanupPreservesUnrelatedSibling {
    $unrelatedSibling = Join-Path $boundary "unrelated-sibling"
    New-Item -ItemType Directory -Path $unrelatedSibling | Out-Null
    Set-Content -LiteralPath (Join-Path $unrelatedSibling "keep.txt") -Value "keep" -NoNewline

    $practice = New-SpatialWiresPracticeDirectory -TemporaryBoundary $boundary
    Set-Content -LiteralPath (Join-Path $practice.Root "payload.txt") -Value "practice" -NoNewline
    $removedPath = Remove-SpatialWiresPracticeDirectory `
        -Path $practice.Root `
        -Identity $practice.Identity `
        -TemporaryBoundary $practice.TemporaryBoundary

    Assert-True ($removedPath -ceq $practice.Root) "Cleanup returned a different resolved path."
    Assert-True (-not (Test-Path -LiteralPath $practice.Root)) "Cleanup left the practice directory behind."
    Assert-True (Test-Path -LiteralPath (Join-Path $unrelatedSibling "keep.txt") -PathType Leaf) "Cleanup removed an unrelated sibling."
}

function Test-CleanupRefusesPathOutsideTemporaryBoundary {
    $practice = New-SpatialWiresPracticeDirectory -TemporaryBoundary $outside
    Assert-ThrowsLike {
        Remove-SpatialWiresPracticeDirectory `
            -Path $practice.Root `
            -Identity $practice.Identity `
            -TemporaryBoundary $boundary
    } "*outside the temporary boundary*"
    Assert-True (Test-Path -LiteralPath $practice.Root -PathType Container) "Refused outside path was removed."
}

function Test-CleanupRefusesWrongPrefixAndIdentity {
    $wrongPrefixPractice = New-SpatialWiresPracticeDirectory -TemporaryBoundary $boundary
    $wrongPrefixPath = Join-Path $boundary ("wrong-prefix-" + $wrongPrefixPractice.Identity)
    Move-Item -LiteralPath $wrongPrefixPractice.Root -Destination $wrongPrefixPath
    Assert-ThrowsLike {
        Remove-SpatialWiresPracticeDirectory `
            -Path $wrongPrefixPath `
            -Identity $wrongPrefixPractice.Identity `
            -TemporaryBoundary $boundary
    } "*unexpected prefix or identity*"
    Assert-True (Test-Path -LiteralPath $wrongPrefixPath -PathType Container) "Wrong-prefix directory was removed."

    $wrongIdentityPractice = New-SpatialWiresPracticeDirectory -TemporaryBoundary $boundary
    $wrongIdentity = [Guid]::NewGuid().ToString("N")
    Assert-ThrowsLike {
        Remove-SpatialWiresPracticeDirectory `
            -Path $wrongIdentityPractice.Root `
            -Identity $wrongIdentity `
            -TemporaryBoundary $boundary
    } "*unexpected prefix or identity*"
    Assert-True (Test-Path -LiteralPath $wrongIdentityPractice.Root -PathType Container) "Wrong-identity directory was removed."

    $identityPath = Join-Path $wrongIdentityPractice.Root ".spatial-wires-practice.json"
    $tamperedIdentity = [ordered]@{
        schemaVersion = 1
        purpose = "SpatialWires independent consumer"
        identity = $wrongIdentity
    } | ConvertTo-Json
    Set-Content -LiteralPath $identityPath -Value $tamperedIdentity
    Assert-ThrowsLike {
        Remove-SpatialWiresPracticeDirectory `
            -Path $wrongIdentityPractice.Root `
            -Identity $wrongIdentityPractice.Identity `
            -TemporaryBoundary $boundary
    } "*identity does not match the current run*"
    Assert-True (Test-Path -LiteralPath $wrongIdentityPractice.Root -PathType Container) "Mismatched-marker directory was removed."
}

try {
    New-Item -ItemType Directory -Path $boundary -Force | Out-Null
    New-Item -ItemType Directory -Path $outside -Force | Out-Null

    Test-SuccessfulCleanupPreservesUnrelatedSibling
    Test-CleanupRefusesPathOutsideTemporaryBoundary
    Test-CleanupRefusesWrongPrefixAndIdentity

    Write-Output "PASS: practice cleanup boundary and identity checks"
}
finally {
    if (Test-Path -LiteralPath $testRoot -PathType Container) {
        $resolvedTestRoot = (Resolve-Path -LiteralPath $testRoot).Path
        $expectedLeaf = "spatial-wires-practice-cleanup-test-" + $testIdentity
        if (-not (Split-Path -Parent $resolvedTestRoot).TrimEnd("\").Equals($systemTemporaryBoundary, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not (Split-Path -Leaf $resolvedTestRoot).Equals($expectedLeaf, [System.StringComparison]::Ordinal)) {
            throw "Refusing test cleanup for unexpected path '$resolvedTestRoot'."
        }
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
