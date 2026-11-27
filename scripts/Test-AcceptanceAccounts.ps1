param(
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

$accounts = @(
    @{ label = 'customerA'; role = 'Customer'; variable = 'TIXFLOW_CUSTOMER_A_TOKEN' },
    @{ label = 'customerB'; role = 'Customer'; variable = 'TIXFLOW_CUSTOMER_B_TOKEN' },
    @{ label = 'organizerA'; role = 'Organizer'; variable = 'TIXFLOW_ORGANIZER_A_TOKEN' },
    @{ label = 'organizerB'; role = 'Organizer'; variable = 'TIXFLOW_ORGANIZER_B_TOKEN' },
    @{ label = 'admin'; role = 'Admin'; variable = 'TIXFLOW_ADMIN_TOKEN' }
)

$profiles = @()
foreach ($account in $accounts) {
    $token = [Environment]::GetEnvironmentVariable($account.variable)
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "Missing $($account.variable). Obtain a fresh test access token without saving it in Git."
    }
    try {
        $profile = Invoke-RestMethod -Method Get -Uri "$($BaseUrl.TrimEnd('/'))/api/v1/users/me" `
            -Headers @{ Authorization = "Bearer $token" } -TimeoutSec 15
    }
    catch {
        throw "GET /api/v1/users/me failed for $($account.label). Check the test token and API."
    }
    $id = [Guid]::Empty
    if (-not [Guid]::TryParse([string]$profile.id, [ref]$id) -or $id -eq [Guid]::Empty -or
        [string]::IsNullOrWhiteSpace([string]$profile.sub) -or
        [string]::IsNullOrWhiteSpace([string]$profile.email) -or
        @($profile.roles).Count -ne 1 -or $profile.roles[0] -cne $account.role -or
        $profile.status -cne 'Active') {
        throw "Unexpected identity, role or status for $($account.label)."
    }
    $profiles += [pscustomobject]@{
        label = $account.label
        role = $account.role
        id = $id.ToString()
        sub = [string]$profile.sub
        email = [string]$profile.email
    }
}

foreach ($field in @('id', 'sub', 'email')) {
    if (@($profiles | Select-Object -ExpandProperty $field -Unique).Count -ne $accounts.Count) {
        throw "Test accounts have duplicate $field values."
    }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ',
    [System.Globalization.CultureInfo]::InvariantCulture)
$manifestDir = Join-Path $repositoryRoot "artifacts/acceptance/accounts-$runId"
New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
$manifestPath = Join-Path $manifestDir 'manifest.json'
[ordered]@{
    checkedAtUtc = [DateTime]::UtcNow.ToString('o')
    apiBaseUrl = $BaseUrl.TrimEnd('/')
    accounts = $profiles
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Host "Acceptance account manifest: $manifestPath"
