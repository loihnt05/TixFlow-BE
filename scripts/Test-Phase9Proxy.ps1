[CmdletBinding()]
param([string]$BaseUrl = 'http://localhost:8080')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$results = @()
for ($i = 0; $i -lt 12; $i++) {
    $response = Invoke-WebRequest "$BaseUrl/api/v1/events?limit=3" -TimeoutSec 30
    $results += [ordered]@{ status = [int]$response.StatusCode; instance = [string]$response.Headers['X-TixFlow-Instance'][0]; cache = [string]$response.Headers['X-Catalog-Cache'][0] }
}
$instances = @($results.instance | Sort-Object -Unique)
if ($instances.Count -ne 2) { throw "Expected two API replicas, observed $($instances.Count)." }
if (@($results | Where-Object cache -eq 'HIT').Count -lt 2) { throw 'Expected warm cache hits on both replicas.' }
$health = Invoke-WebRequest "$BaseUrl/health" -TimeoutSec 10
$client = [Net.Http.HttpClient]::new()
try {
    $pending = @(1..100 | ForEach-Object { $client.GetAsync("$BaseUrl/api/v1/system/info") })
    $responses = @($pending | ForEach-Object { $_.GetAwaiter().GetResult() })
    $rejected = @($responses | Where-Object { [int]$_.StatusCode -eq 429 })
    if ($rejected.Count -eq 0) { throw 'Expected edge rate limit to reject part of the burst.' }
    foreach ($response in $rejected) {
        if ($null -eq $response.Headers.RetryAfter) { throw '429 response omitted Retry-After.' }
    }
    $report = [ordered]@{ utc = [DateTime]::UtcNow; transport = 'HTTP -> Nginx -> two Kestrel containers';
        catalog = $results; health = [int]$health.StatusCode; edgeBurst = 100; rateLimited = $rejected.Count;
        unexpected = @($responses | Where-Object { [int]$_.StatusCode -notin @(200,429) }).Count }
    if ($report.unexpected -ne 0) { throw 'Unexpected response during rate limit test.' }
    $directory = Join-Path $repo 'artifacts/phase9/proxy'
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $report | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $directory 'proxy-smoke.json') -Encoding utf8
    $report | ConvertTo-Json -Depth 6
    foreach ($response in $responses) { $response.Dispose() }
}
finally { $client.Dispose() }
