param(
    [Parameter(Mandatory = $true)][Guid]$OrganizerAEventId,
    [Parameter(Mandatory = $true)][Guid]$OrganizerBEventId,
    [Parameter(Mandatory = $true)][Guid]$CustomerAOrderId,
    [Parameter(Mandatory = $true)][Guid]$CustomerBOrderId,
    [string]$BaseUrl = 'http://localhost:8080'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$uri = $null
if (-not [Uri]::TryCreate($BaseUrl, [UriKind]::Absolute, [ref]$uri) -or
    ($uri.Scheme -ne 'https' -and
        ($uri.Scheme -ne 'http' -or $uri.Host -notin @('localhost', '127.0.0.1', '::1'))) -or
    $uri.UserInfo -or $uri.Query -or $uri.Fragment -or $uri.AbsolutePath -ne '/') {
    throw 'BaseUrl must be HTTPS or loopback HTTP and contain only scheme, host and optional port.'
}
if ($OrganizerAEventId -eq [Guid]::Empty -or $OrganizerBEventId -eq [Guid]::Empty -or
    $CustomerAOrderId -eq [Guid]::Empty -or $CustomerBOrderId -eq [Guid]::Empty -or
    $OrganizerAEventId -eq $OrganizerBEventId -or $CustomerAOrderId -eq $CustomerBOrderId) {
    throw 'Provide four nonempty IDs, with distinct IDs for each owner pair.'
}

$tokens = @{}
foreach ($account in @(
    @{ label = 'customerA'; variable = 'TIXFLOW_CUSTOMER_A_TOKEN' },
    @{ label = 'customerB'; variable = 'TIXFLOW_CUSTOMER_B_TOKEN' },
    @{ label = 'organizerA'; variable = 'TIXFLOW_ORGANIZER_A_TOKEN' },
    @{ label = 'organizerB'; variable = 'TIXFLOW_ORGANIZER_B_TOKEN' },
    @{ label = 'admin'; variable = 'TIXFLOW_ADMIN_TOKEN' }
)) {
    $token = [Environment]::GetEnvironmentVariable($account.variable)
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "Missing $($account.variable). Obtain a fresh test access token without saving it in Git."
    }
    $tokens[$account.label] = $token
}

$checks = @(
    @{ actor = 'organizerA'; resource = 'eventA'; path = "/api/v1/management/events/$OrganizerAEventId"; expected = 200 },
    @{ actor = 'organizerA'; resource = 'eventB'; path = "/api/v1/management/events/$OrganizerBEventId"; expected = 404 },
    @{ actor = 'organizerB'; resource = 'eventB'; path = "/api/v1/management/events/$OrganizerBEventId"; expected = 200 },
    @{ actor = 'organizerB'; resource = 'eventA'; path = "/api/v1/management/events/$OrganizerAEventId"; expected = 404 },
    @{ actor = 'admin'; resource = 'eventA'; path = "/api/v1/management/events/$OrganizerAEventId"; expected = 200 },
    @{ actor = 'admin'; resource = 'eventB'; path = "/api/v1/management/events/$OrganizerBEventId"; expected = 200 },
    @{ actor = 'customerA'; resource = 'orderA'; path = "/api/v1/orders/$CustomerAOrderId"; expected = 200 },
    @{ actor = 'customerA'; resource = 'orderB'; path = "/api/v1/orders/$CustomerBOrderId"; expected = 404 },
    @{ actor = 'customerB'; resource = 'orderB'; path = "/api/v1/orders/$CustomerBOrderId"; expected = 200 },
    @{ actor = 'customerB'; resource = 'orderA'; path = "/api/v1/orders/$CustomerAOrderId"; expected = 404 },
    @{ actor = 'admin'; resource = 'orderA'; path = "/api/v1/orders/$CustomerAOrderId"; expected = 200 },
    @{ actor = 'admin'; resource = 'orderB'; path = "/api/v1/orders/$CustomerBOrderId"; expected = 200 },
    @{ actor = 'customerA'; resource = 'managementEvent'; path = "/api/v1/management/events/$OrganizerAEventId"; expected = 403 },
    @{ actor = 'organizerA'; resource = 'customerOrder'; path = "/api/v1/orders/$CustomerAOrderId"; expected = 403 }
)

$results = @()
foreach ($check in $checks) {
    try {
        $response = Invoke-WebRequest -Method Get -Uri "$($BaseUrl.TrimEnd('/'))$($check.path)" `
            -Headers @{ Authorization = "Bearer $($tokens[$check.actor])" } `
            -SkipHttpErrorCheck -MaximumRedirection 0 -TimeoutSec 15
    }
    catch {
        throw "GET $($check.resource) failed for $($check.actor); check API availability and token expiry."
    }
    $status = [int]$response.StatusCode
    if ($status -ne $check.expected) {
        throw "$($check.actor) / $($check.resource): expected HTTP $($check.expected), got $status."
    }
    $results += [pscustomobject]@{
        actor = $check.actor
        resource = $check.resource
        expectedStatus = $check.expected
        actualStatus = $status
    }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ',
    [System.Globalization.CultureInfo]::InvariantCulture)
$manifestDir = Join-Path $repositoryRoot "artifacts/acceptance/ownership-$runId"
New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
$manifestPath = Join-Path $manifestDir 'manifest.json'
[ordered]@{
    checkedAtUtc = [DateTime]::UtcNow.ToString('o')
    apiBaseUrl = $BaseUrl.TrimEnd('/')
    resourceIds = [ordered]@{
        organizerAEventId = $OrganizerAEventId.ToString()
        organizerBEventId = $OrganizerBEventId.ToString()
        customerAOrderId = $CustomerAOrderId.ToString()
        customerBOrderId = $CustomerBOrderId.ToString()
    }
    checks = $results
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Host "Acceptance ownership manifest: $manifestPath"
