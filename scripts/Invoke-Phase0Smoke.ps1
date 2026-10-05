[CmdletBinding()]
param(
    [switch]$StartStack,
    [switch]$SkipBuild,
    [int]$TimeoutSeconds = 180,
    [string]$ApiUrl = "http://localhost:8080",
    [string]$KeycloakDiscoveryUrl = "http://localhost:8180/realms/tixflow/.well-known/openid-configuration"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot

function Assert-LastExitCode([string]$operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "$operation failed with exit code $LASTEXITCODE."
    }
}

function Wait-ForHttp200([string]$uri, [int]$timeout) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($timeout)
    $lastError = "No response received."

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                return $response
            }
            $lastError = "Received HTTP $($response.StatusCode)."
        }
        catch {
            $lastError = $_.Exception.Message
        }
        Start-Sleep -Seconds 2
    }

    throw "Timed out waiting for $uri. Last result: $lastError"
}

function Get-ComposeContainerId([string]$service) {
    $ids = & docker compose ps --all -q $service
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Looking up the $service container failed with exit code $exitCode."
    }
    $id = ([string](@($ids)[0])).Trim()
    if ([string]::IsNullOrWhiteSpace($id)) {
        throw "The $service container was not created. Run this script with -StartStack."
    }
    return $id
}

if (-not (Test-Path -LiteralPath (Join-Path $projectRoot ".env"))) {
    throw "Missing .env. Copy .env.example to .env and set local credentials before running this smoke test."
}

Write-Host "Validating Docker Compose configuration..."
& docker compose config --quiet
Assert-LastExitCode "Docker Compose configuration validation"

if ($StartStack) {
    Write-Host "Starting the TixFlow stack (persistent volumes are preserved)..."
    $upArguments = @("compose", "up", "-d")
    if (-not $SkipBuild) {
        $upArguments += "--build"
    }
    & docker @upArguments
    Assert-LastExitCode "Starting Docker Compose"
}

$migrationId = Get-ComposeContainerId "database-migrate"
$migrationStatus = (& docker inspect --format '{{.State.Status}}' $migrationId).Trim()
Assert-LastExitCode "Inspecting database-migrate"
if ($migrationStatus -ne "exited") {
    throw "database-migrate is $migrationStatus; it must complete before the API is verified."
}
$migrationExitCode = (& docker inspect --format '{{.State.ExitCode}}' $migrationId).Trim()
Assert-LastExitCode "Inspecting the database-migrate exit code"
if ($migrationExitCode -ne "0") {
    throw "database-migrate exited with code $migrationExitCode. Run 'docker compose logs database-migrate'."
}

Write-Host "Waiting for API health..."
$health = Wait-ForHttp200 "$($ApiUrl.TrimEnd('/'))/health" $TimeoutSeconds
if ($health.Content.Trim() -ne "Healthy") {
    throw "Unexpected health response: $($health.Content)"
}

Write-Host "Validating OpenAPI document..."
$openApi = (Wait-ForHttp200 "$($ApiUrl.TrimEnd('/'))/swagger/v1/swagger.json" $TimeoutSeconds).Content | ConvertFrom-Json
if ($openApi.openapi -notmatch '^3\.') {
    throw "Swagger did not return an OpenAPI 3 document."
}
if (-not $openApi.components.securitySchemes.oidc) {
    throw "Swagger is missing the oidc security scheme."
}

Write-Host "Validating Keycloak discovery..."
$discovery = (Wait-ForHttp200 $KeycloakDiscoveryUrl $TimeoutSeconds).Content | ConvertFrom-Json
$expectedIssuer = $KeycloakDiscoveryUrl -replace '/\.well-known/openid-configuration$', ''
if ($discovery.issuer -ne $expectedIssuer) {
    throw "Unexpected Keycloak issuer '$($discovery.issuer)'; expected '$expectedIssuer'."
}

Write-Host "Validating the Phase 0 PostgreSQL baseline..."
$phase0Sql = Get-Content -Raw (Join-Path $projectRoot "database\phase0-smoke.sql")
$phase0Sql | & docker compose exec -T postgres sh -ec 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
Assert-LastExitCode "Phase 0 PostgreSQL verification"

Write-Host "Phase 0 smoke test passed. No containers or volumes were removed."
