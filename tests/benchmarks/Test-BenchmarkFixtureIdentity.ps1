$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$benchmarkProject = Join-Path $repositoryRoot "benchmarks\SpatialCircuits.Benchmarks.csproj"
$fixturePath = Join-Path $repositoryRoot "benchmarks\fixtures\temporal-frontier.fixture.json"
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("spatial-wires-benchmark-test-" + [Guid]::NewGuid().ToString("N"))
$dotnetExecutable = (Get-Command dotnet.exe -CommandType Application | Select-Object -First 1).Source

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

function Invoke-Benchmark {
    param(
        [string] $Fixture,
        [string] $Output
    )

    & $dotnetExecutable run --project $benchmarkProject --configuration Release --no-restore -- $Fixture $Output | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Benchmark failed with process code $LASTEXITCODE."
    }

    return Get-Content -LiteralPath $Output -Raw | ConvertFrom-Json
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    $baseOutput = Join-Path $testRoot "base.json"
    $variantFixture = Join-Path $testRoot "variant.fixture.json"
    $variantOutput = Join-Path $testRoot "variant.json"
    $fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
    $variant = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
    $variant.fixtureId = "benchmarks/temporal-frontier-variant"
    $variant.panelScenario.inputs[0].tick = 1
    [System.IO.File]::WriteAllText(
        $variantFixture,
        ($variant | ConvertTo-Json -Depth 8) + "`n",
        [System.Text.UTF8Encoding]::new($false))

    $baseReport = Invoke-Benchmark -Fixture $fixturePath -Output $baseOutput
    $variantReport = Invoke-Benchmark -Fixture $variantFixture -Output $variantOutput

    if ($baseReport.FixtureId -ne $fixture.fixtureId -or
        $baseReport.FixtureHash -ne (Get-Sha256 $fixturePath) -or
        $variantReport.FixtureId -ne $variant.fixtureId -or
        $variantReport.FixtureHash -eq $baseReport.FixtureHash) {
        throw "Benchmark report did not identify and hash the complete fixture input."
    }

    Write-Output "PASS: benchmark fixture identity and raw hash"
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
