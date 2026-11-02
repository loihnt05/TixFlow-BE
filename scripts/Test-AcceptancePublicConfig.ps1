param([Parameter(Mandatory)][string]$Evidence)
$ErrorActionPreference = 'Stop'
$config = @{}
Get-Content (Join-Path $PSScriptRoot '../../tixflow-frontend/.env.local') | ForEach-Object {
    if ($_ -match '^(NEXT_PUBLIC_(API_URL|OIDC_AUTHORITY|OIDC_CLIENT_ID|OIDC_REDIRECT_URI|OIDC_POST_LOGOUT_REDIRECT_URI))=(.*)$') { $config[$Matches[1]]=$Matches[3] }
}
$discovery = Invoke-RestMethod "$($config.NEXT_PUBLIC_OIDC_AUTHORITY)/.well-known/openid-configuration"
$health = Invoke-WebRequest "$($config.NEXT_PUBLIC_API_URL)/health"
$cors = Invoke-WebRequest "$($config.NEXT_PUBLIC_API_URL)/api/v1/events" -Method Options -Headers @{Origin='http://localhost:3000'; 'Access-Control-Request-Method'='GET'; 'Access-Control-Request-Headers'='authorization'}
$realm = Get-Content (Join-Path $PSScriptRoot '../infrastructure/keycloak/tixflow-realm.json') -Raw | ConvertFrom-Json
$client = $realm.clients | Where-Object clientId -eq $config.NEXT_PUBLIC_OIDC_CLIENT_ID
$checks = [ordered]@{
    apiHealth200=($health.StatusCode -eq 200)
    issuerMatches=($discovery.issuer -eq $config.NEXT_PUBLIC_OIDC_AUTHORITY)
    corsOriginMatches=([string]$cors.Headers['Access-Control-Allow-Origin'] -eq 'http://localhost:3000')
    importedCallbackMatches=($config.NEXT_PUBLIC_OIDC_REDIRECT_URI -in $client.redirectUris)
    importedLogoutMatches=($config.NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI -eq $client.attributes.'post.logout.redirect.uris')
    importedPublicClient=$client.publicClient
    importedPkceS256=($client.attributes.'pkce.code.challenge.method' -eq 'S256')
}
@{publicConfig=$config; checks=$checks; scope='Local acceptance. Client settings checked against realm import; live login/logout remains AUTH tests.'} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Evidence 'public-config.json')
if ($checks.Values -contains $false) { throw 'Public configuration check failed' }
