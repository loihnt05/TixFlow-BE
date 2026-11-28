param([string]$BaseUrl = 'http://localhost:8080')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$uri = $null
if (-not [Uri]::TryCreate($BaseUrl, [UriKind]::Absolute, [ref]$uri) -or
    ($uri.Scheme -ne 'https' -and
        ($uri.Scheme -ne 'http' -or $uri.Host -notin @('localhost', '127.0.0.1', '::1'))) -or
    $uri.UserInfo -or $uri.Query -or $uri.Fragment -or $uri.AbsolutePath -ne '/') {
    throw 'BaseUrl must be HTTPS or loopback HTTP and contain only scheme, host and optional port.'
}

$customerToken = [Environment]::GetEnvironmentVariable('TIXFLOW_CUSTOMER_A_TOKEN')
$organizerToken = [Environment]::GetEnvironmentVariable('TIXFLOW_ORGANIZER_A_TOKEN')
if ([string]::IsNullOrWhiteSpace($customerToken) -or [string]::IsNullOrWhiteSpace($organizerToken)) {
    throw 'Set TIXFLOW_CUSTOMER_A_TOKEN and TIXFLOW_ORGANIZER_A_TOKEN to current test access tokens.'
}

$checks = @(
    @{ actor = 'guest'; path = '/api/v1/users/me'; expected = 401 },
    @{ actor = 'guest'; path = '/api/v1/orders'; expected = 401 },
    @{ actor = 'guest'; path = '/api/v1/management/events'; expected = 401 },
    @{ actor = 'invalid'; path = '/api/v1/users/me'; expected = 401 },
    @{ actor = 'customer'; path = '/api/v1/users/access/organizer'; expected = 403 },
    @{ actor = 'customer'; path = '/api/v1/users/access/admin'; expected = 403 },
    @{ actor = 'customer'; path = '/api/v1/management/events'; expected = 403 },
    @{ actor = 'organizer'; path = '/api/v1/orders'; expected = 403 },
    @{ actor = 'organizer'; path = '/api/v1/holds'; expected = 403 },
    @{ actor = 'organizer'; path = '/api/v1/users/access/admin'; expected = 403 }
)

$results = @()
foreach ($check in $checks) {
    $headers = @{}
    switch ($check.actor) {
        'invalid' { $headers.Authorization = 'Bearer invalid.jwt.token' }
        'customer' { $headers.Authorization = "Bearer $customerToken" }
        'organizer' { $headers.Authorization = "Bearer $organizerToken" }
    }
    try {
        $response = Invoke-WebRequest -Method Get -Uri "$($BaseUrl.TrimEnd('/'))$($check.path)" `
            -Headers $headers -SkipHttpErrorCheck -MaximumRedirection 0 -TimeoutSec 15
    }
    catch {
        throw "GET $($check.path) failed for $($check.actor); check API availability."
    }
    $status = [int]$response.StatusCode
    if ($status -ne $check.expected) {
        throw "$($check.actor) GET $($check.path): expected HTTP $($check.expected), got $status."
    }
    $results += [pscustomobject]@{
        actor = $check.actor
        method = 'GET'
        path = $check.path
        expectedStatus = $check.expected
        actualStatus = $status
    }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ',
    [System.Globalization.CultureInfo]::InvariantCulture)
$manifestDir = Join-Path $repositoryRoot "artifacts/acceptance/auth-access-$runId"
New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
$manifestPath = Join-Path $manifestDir 'manifest.json'
[ordered]@{
    checkedAtUtc = [DateTime]::UtcNow.ToString('o')
    apiBaseUrl = $BaseUrl.TrimEnd('/')
    checks = $results
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Host "Acceptance auth access manifest: $manifestPath"
