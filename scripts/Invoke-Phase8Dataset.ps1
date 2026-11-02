[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('seed', 'reset')][string]$Action,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{12,64}$')][string]$ContainerId
)
$ErrorActionPreference = 'Stop'
# The caller must select a live Testcontainers PostgreSQL instance explicitly. No default business DB.
$inspection = docker inspect $ContainerId | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Container does not exist.' }
$labels = $inspection[0].Config.Labels
if (-not $labels.'org.testcontainers.resource-reaper-session') { throw 'Refusing a container not owned by Testcontainers.' }
if ($inspection[0].Config.Image -notlike 'postgres:*') { throw 'Expected PostgreSQL Testcontainer.' }
$sql = Join-Path $PSScriptRoot "../database/baseline/$Action.sql"
Get-Content -LiteralPath $sql -Raw | docker exec -i $ContainerId psql -U tixflow_test -d tixflow_auth_tests -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) { throw 'Dataset operation failed.' }
