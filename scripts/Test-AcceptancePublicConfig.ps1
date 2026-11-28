param(
    [Parameter(Mandatory)][string]$Evidence,
    [string]$FrontendOrigin = 'http://localhost:3000'
)
$ErrorActionPreference = 'Stop'
$config = @{}
$envPath = Join-Path $PSScriptRoot '../../tixflow-frontend/.env.local'
if (-not (Test-Path -LiteralPath $envPath -PathType Leaf)) { throw 'Frontend .env.local is missing.' }
Get-Content -LiteralPath $envPath | ForEach-Object {
    if ($_ -match '^(NEXT_PUBLIC_(API_URL|OIDC_AUTHORITY|OIDC_CLIENT_ID|OIDC_REDIRECT_URI|OIDC_POST_LOGOUT_REDIRECT_URI))=(.*)$') { $config[$Matches[1]]=$Matches[3] }
}
foreach ($name in @('NEXT_PUBLIC_API_URL', 'NEXT_PUBLIC_OIDC_AUTHORITY', 'NEXT_PUBLIC_OIDC_CLIENT_ID',
    'NEXT_PUBLIC_OIDC_REDIRECT_URI', 'NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI')) {
    if ([string]::IsNullOrWhiteSpace($config[$name])) { throw "Missing public frontend setting: $name" }
}
$frontendUri = $null
if (-not [Uri]::TryCreate($FrontendOrigin, [UriKind]::Absolute, [ref]$frontendUri) -or
    ($frontendUri.Scheme -ne 'https' -and
        ($frontendUri.Scheme -ne 'http' -or $frontendUri.Host -notin @('localhost', '127.0.0.1', '::1'))) -or
    $frontendUri.UserInfo -or $frontendUri.Query -or $frontendUri.Fragment -or
    $frontendUri.AbsolutePath -ne '/') {
    throw 'FrontendOrigin must be HTTPS or loopback HTTP with no path, query or fragment.'
}
$callbackUri = [Uri]$config.NEXT_PUBLIC_OIDC_REDIRECT_URI
$logoutUri = [Uri]$config.NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI
$frontendBase = $frontendUri.GetLeftPart([UriPartial]::Authority)
if ($callbackUri.GetLeftPart([UriPartial]::Authority) -cne $frontendBase -or
    $callbackUri.AbsolutePath -cne '/auth/callback' -or $callbackUri.Query -or $callbackUri.Fragment -or
    $logoutUri.GetLeftPart([UriPartial]::Authority) -cne $frontendBase -or
    $logoutUri.AbsolutePath -cne '/' -or $logoutUri.Query -or $logoutUri.Fragment) {
    throw 'OIDC callback/logout URLs must use the frontend origin and expected paths.'
}
$discovery = Invoke-RestMethod "$($config.NEXT_PUBLIC_OIDC_AUTHORITY)/.well-known/openid-configuration"
$health = Invoke-WebRequest "$($config.NEXT_PUBLIC_API_URL)/health"
$cors = Invoke-WebRequest "$($config.NEXT_PUBLIC_API_URL)/api/v1/events" -Method Options -Headers @{Origin=$frontendBase; 'Access-Control-Request-Method'='GET'; 'Access-Control-Request-Headers'='authorization'}
$realm = Get-Content (Join-Path $PSScriptRoot '../infrastructure/keycloak/tixflow-realm.json') -Raw | ConvertFrom-Json
$client = $realm.clients | Where-Object clientId -eq $config.NEXT_PUBLIC_OIDC_CLIENT_ID
$checks = [ordered]@{
    apiHealth200=($health.StatusCode -eq 200)
    issuerMatches=($discovery.issuer -eq $config.NEXT_PUBLIC_OIDC_AUTHORITY)
    discoveryHasEndpoints=($discovery.authorization_endpoint -and $discovery.token_endpoint -and $discovery.end_session_endpoint)
    corsOriginMatches=([string]$cors.Headers['Access-Control-Allow-Origin'] -eq $frontendBase)
    importedCallbackMatches=($config.NEXT_PUBLIC_OIDC_REDIRECT_URI -in $client.redirectUris)
    importedLogoutMatches=($config.NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI -eq $client.attributes.'post.logout.redirect.uris')
    importedWebOriginMatches=($frontendBase -in $client.webOrigins)
    importedPublicClient=$client.publicClient
    importedPkceS256=($client.attributes.'pkce.code.challenge.method' -eq 'S256')
}
New-Item -ItemType Directory -Path $Evidence -Force | Out-Null
if (Test-Path -LiteralPath (Join-Path $Evidence 'public-config.json')) {
    throw 'Public config evidence already exists; choose a new run directory.'
}
@{publicConfig=$config; frontendOrigin=$frontendBase; checks=$checks; scope='Local acceptance. Client settings checked against realm import; live login/logout remains AUTH tests.'} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Evidence 'public-config.json')
if ($checks.Values -contains $false) { throw 'Public configuration check failed' }
