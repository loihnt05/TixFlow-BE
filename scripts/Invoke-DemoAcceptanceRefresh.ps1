param(
    [Parameter(Mandatory = $true)]
    [string]$AnchorUtc
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$parsed = [DateTimeOffset]::MinValue
if ($AnchorUtc -notmatch '(Z|z|[+]00:00)$' -or
    -not [DateTimeOffset]::TryParse($AnchorUtc,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::AssumeUniversal,
        [ref]$parsed) -or $parsed.Offset -ne [TimeSpan]::Zero) {
    throw 'AnchorUtc must be an ISO 8601 timestamp with an explicit UTC offset, for example 2026-11-27T00:00:00Z.'
}

$normalizedAnchor = $parsed.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ',
    [System.Globalization.CultureInfo]::InvariantCulture)
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$seedFiles = @(
    (Join-Path $repositoryRoot 'database/demo-acceptance-v1.sql'),
    (Join-Path $repositoryRoot 'database/demo-acceptance-refresh-v2.sql')
)
$verificationFile = Join-Path $repositoryRoot 'database/demo-acceptance-verify-v1.sql'

Push-Location $repositoryRoot
try {
    foreach ($seedFile in $seedFiles) {
        Write-Host "Applying $(Split-Path $seedFile -Leaf) to tixflow_acceptance"
        Get-Content -LiteralPath $seedFile -Raw |
            docker compose exec -T postgres psql -X -v ON_ERROR_STOP=1 `
                -v "anchor_utc=$normalizedAnchor" -U tixflow -d tixflow_acceptance
        if ($LASTEXITCODE -ne 0) {
            throw "Seed failed: $(Split-Path $seedFile -Leaf) (exit $LASTEXITCODE)."
        }
    }

    $verificationOutput = Get-Content -LiteralPath $verificationFile -Raw |
        docker compose exec -T postgres psql -X -qAt -v ON_ERROR_STOP=1 `
            -U tixflow -d tixflow_acceptance
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture verification failed (exit $LASTEXITCODE)."
    }
    $verificationRows = @($verificationOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($verificationRows.Count -ne 1) {
        throw "Fixture verification returned $($verificationRows.Count) rows; expected one JSON row."
    }
    try {
        $verification = $verificationRows[0] | ConvertFrom-Json
    }
    catch {
        throw 'Fixture verification returned invalid JSON.'
    }
    $expectedCounts = [ordered]@{
        events = 2
        sessions = 2
        ticketTypes = 2
        inventories = 2
        seats = 2
    }
    foreach ($name in $expectedCounts.Keys) {
        if ($verification.counts.$name -ne $expectedCounts[$name]) {
            throw "Fixture $name count is $($verification.counts.$name); expected $($expectedCounts[$name])."
        }
    }
    foreach ($name in @('eventWindows', 'sessionWindows', 'links', 'inventory', 'seats')) {
        if ($verification.invariants.$name -ne $true) {
            throw "Fixture invariant failed: $name."
        }
    }
    if ([DateTimeOffset]::Parse($verification.anchorUtc).ToUniversalTime() -ne $parsed.ToUniversalTime()) {
        throw 'Fixture anchor differs from the requested anchor.'
    }

    $runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ',
        [System.Globalization.CultureInfo]::InvariantCulture)
    $manifestDir = Join-Path $repositoryRoot "artifacts/acceptance/seed-$runId"
    New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
    $manifest = [ordered]@{
        seedVersion = 'demo-acceptance-refresh-v2'
        database = 'tixflow_acceptance'
        anchorUtc = $normalizedAnchor
        timeZone = 'Asia/Ho_Chi_Minh'
        randomSeed = 0
        appliedAtUtc = [DateTime]::UtcNow.ToString('o')
        expectedFixtureCounts = $expectedCounts
        actualFixtureCounts = $verification.counts
        fixtureInvariants = $verification.invariants
        inputFiles = @(($seedFiles + $verificationFile) | ForEach-Object {
            [ordered]@{
                path = [System.IO.Path]::GetRelativePath($repositoryRoot, $_).Replace('\', '/')
                sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        })
    }
    $manifestPath = Join-Path $manifestDir 'manifest.json'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Write-Host "Seed manifest: $manifestPath"
}
finally {
    Pop-Location
}
