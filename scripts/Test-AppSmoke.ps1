param([string]$Evidence='artifacts/acceptance/20261103-app01-06')
$ErrorActionPreference='Stop'
if (Test-Path $Evidence) { throw 'Choose a new evidence directory; existing results must be preserved.' }
New-Item -ItemType Directory -Force $Evidence | Out-Null
function Probe([string]$Name,[string]$Url) {
    try {
        $r=Invoke-WebRequest $Url -SkipHttpErrorCheck -TimeoutSec 20
        $body = if ($r.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($r.Content) } else { [string]$r.Content }
        $body | Set-Content (Join-Path $Evidence "$Name.body.txt")
        [pscustomobject]@{name=$Name;url=$Url;status=[int]$r.StatusCode;contentType=[string]$r.Headers['Content-Type']}
    } catch { [pscustomobject]@{name=$Name;url=$Url;error=$_.Exception.Message} }
}
$results=@(
    Probe 'health' 'http://localhost:8080/health'
    Probe 'discovery' 'http://localhost:8180/realms/tixflow/.well-known/openid-configuration'
    Probe 'openapi' 'http://localhost:8080/swagger/v1/swagger.json'
    Probe 'events' 'http://localhost:8080/api/v1/events?page=1&limit=2'
    Probe 'empty' 'http://localhost:8080/api/v1/events?q=acceptance-no-such-event-20261103'
    Probe 'not-found' 'http://localhost:8080/api/v1/events/acceptance-no-such-event-20261103'
    Probe 'validation' 'http://localhost:8080/api/v1/events?dateFrom=2026-12-20&dateTo=2026-12-01'
    Probe 'unauthorized' 'http://localhost:8080/api/v1/orders'
    Probe 'frontend' 'http://localhost:3000/'
)
$results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Evidence 'http-results.json')
$results | Format-Table -AutoSize
docker compose ps --format json | Set-Content (Join-Path $Evidence 'containers.jsonl')
$services=@(
    docker exec tixflow-postgres-1 pg_isready
    docker exec tixflow-redis-1 redis-cli ping
    docker exec tixflow-rabbitmq-1 rabbitmq-diagnostics -q ping
)
$services | Set-Content (Join-Path $Evidence 'services.log')
$services
$r=Invoke-WebRequest 'http://localhost:8080/api/v1/events' -Method Options -Headers @{Origin='http://localhost:3000';'Access-Control-Request-Method'='GET';'Access-Control-Request-Headers'='authorization'}
@{status=[int]$r.StatusCode;origin=[string]$r.Headers['Access-Control-Allow-Origin'];headers=[string]$r.Headers['Access-Control-Allow-Headers']} | ConvertTo-Json | Set-Content (Join-Path $Evidence 'cors.json')
git rev-parse HEAD | Set-Content (Join-Path $Evidence 'backend-sha.txt')
git status --short | Set-Content (Join-Path $Evidence 'backend-status.txt')
git -C ../tixflow-frontend rev-parse HEAD | Set-Content (Join-Path $Evidence 'frontend-sha.txt')
git -C ../tixflow-frontend status --short | Set-Content (Join-Path $Evidence 'frontend-status.txt')
