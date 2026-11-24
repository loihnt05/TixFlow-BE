[CmdletBinding()]
param(
    [ValidateSet('Active','Disabled')][string]$Mode = 'Active',
    [ValidateRange(1,1000)][int]$Arrivals = 100,
    [ValidateRange(1,3600)][int]$ArrivalWindowSeconds = 1,
    [ValidateRange(1,1000)][int]$GrantsPerSecond = 5,
    [ValidateSet(1,2)][int]$ApiReplicas = 2,
    [string]$CandidateImage,
    [switch]$Diagnostics
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ').ToLowerInvariant()
$project = "tixflow-wr-$stamp"
$report = Join-Path $repo "artifacts/waiting-room/$stamp-$($Mode.ToLowerInvariant())"
$private = Join-Path $report 'private'
$image = "tixflow-wr-candidate:$stamp"
$compose = @('compose','--project-name',$project,'-f','docker-compose.load.yml','-f','docker-compose.waiting-room.load.yml')
if ($ApiReplicas -eq 1) { $compose += @('-f','docker-compose.waiting-room.single-api.yml') }
$names = @('IMAGE','PRIVATE','REPORT','RUN_ID','MODE','RATE','ARRIVALS','WINDOW','DIAGNOSTICS')
$saved = @{}
foreach ($name in $names) {
    $key = "TIXFLOW_LOAD_$name"
    $saved[$key] = [Environment]::GetEnvironmentVariable($key)
}
function Invoke-Docker([string[]]$Arguments) {
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}
function Invoke-Sql([string]$Statement) {
    $Statement | & docker @compose exec -T postgres psql -X -qAt -v ON_ERROR_STOP=1 -U load -d tixflow_load_tests
    if ($LASTEXITCODE -ne 0) { throw 'Waiting Room load database SQL failed.' }
}
Push-Location $repo
$k6Exit = -1
try {
    New-Item -ItemType Directory -Path $private -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $private 'redis') -Force | Out-Null
    if ($CandidateImage) { $image = $CandidateImage }
    $env:TIXFLOW_LOAD_IMAGE = $image
    $env:TIXFLOW_LOAD_PRIVATE = $private
    $env:TIXFLOW_LOAD_REPORT = $report
    $env:TIXFLOW_LOAD_RUN_ID = $stamp
    $env:TIXFLOW_LOAD_MODE = $Mode
    $env:TIXFLOW_LOAD_RATE = "$GrantsPerSecond"
    $env:TIXFLOW_LOAD_ARRIVALS = "$Arrivals"
    $env:TIXFLOW_LOAD_WINDOW = "$ArrivalWindowSeconds"
    $env:TIXFLOW_LOAD_DIAGNOSTICS = if ($Diagnostics) { 'true' } else { 'false' }

    $inputs = @('docker-compose.load.yml','docker-compose.waiting-room.load.yml',
        'docker-compose.waiting-room.single-api.yml',
        'infrastructure/load/nginx-waiting-room.conf',
        'infrastructure/load/nginx-waiting-room.single-api.conf','load/waiting-room.js',
        'database/load/seed.sql','database/load/verify.sql','scripts/Invoke-WaitingRoomLoad.ps1')
    $hashes = @($inputs | ForEach-Object {
        @{ path=$_; sha256=(Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant() }
    })
    $sourcePaths = @(git ls-files --cached --others --exclude-standard -- src database infrastructure/baseline |
        Where-Object { $_ -match '\.(cs|csproj|sql|json)$|(^|/)Dockerfile$' })
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate image inputs.' }
    $sourceHashes = @($sourcePaths | Sort-Object -Unique | ForEach-Object {
        @{ path=$_; sha256=(Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant() }
    })
    if (!$CandidateImage) { Invoke-Docker @('build','-f','infrastructure/baseline/Dockerfile','-t',$image,'.') }
    $imageId = (Invoke-Docker @('image','inspect',$image,'--format','{{.Id}}')).Trim()
    $manifest = [ordered]@{
        runId=$stamp; mode=$Mode; offeredArrivals=$Arrivals; arrivalWindowSeconds=$ArrivalWindowSeconds
        grantsPerSecond=$GrantsPerSecond; apiReplicas=$ApiReplicas
        diagnosticsEnabled=[bool]$Diagnostics
        apiCpuEach=(2 / $ApiReplicas); apiMemoryBytesEach=(1073741824 / $ApiReplicas)
        postgresCpu=1; postgresMemoryBytes=536870912; dataset='waiting-room-load-v1'
        image=$imageId; revision=(git rev-parse HEAD).Trim(); dirty=(@(git status --porcelain).Count -gt 0)
        startedUtc=[DateTime]::UtcNow.ToString('o'); harnessInputs=$hashes; imageInputs=$sourceHashes
    }
    $manifest | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $report 'manifest.json') -Encoding utf8
    Invoke-Docker ($compose + @('config')) | Set-Content (Join-Path $report 'compose.yml') -Encoding utf8
    Invoke-Docker ($compose + @('build','issuer'))
    Invoke-Docker ($compose + @('up','-d','--wait','postgres','rabbitmq','redis'))
    Invoke-Sql "CREATE EXTENSION pg_stat_statements; CREATE TABLE load_control(dataset text PRIMARY KEY); INSERT INTO load_control VALUES ('load-v2');" | Out-Null
    Invoke-Sql (Get-Content 'database/init/001_initial_schema.sql' -Raw) | Out-Null
    foreach ($migration in Get-ChildItem 'database/migrations' -Filter '*.sql' | Sort-Object Name) {
        Invoke-Sql (Get-Content -LiteralPath $migration.FullName -Raw) | Out-Null
    }
    Invoke-Sql (Get-Content 'database/load/seed.sql' -Raw) | Out-Null
    Invoke-Sql "INSERT INTO waiting_room_policies(event_session_id,state,max_grants_per_second) VALUES ('80000000-0000-0000-0000-000000000002','$Mode',$GrantsPerSecond);" | Out-Null
    $services = @('issuer','api','worker','nginx')
    if ($ApiReplicas -eq 2) { $services += 'api-replica' }
    Invoke-Docker ($compose + @('up','-d') + $services)
    $deadline = [DateTime]::UtcNow.AddMinutes(2)
    while (!(Test-Path (Join-Path $private 'tokens.json'))) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Issuer did not generate tokens.' }
        Start-Sleep -Seconds 1
    }
    & docker @compose run --rm --no-deps k6 2>&1 | Tee-Object -FilePath (Join-Path $report 'k6.log')
    $k6Exit = $LASTEXITCODE
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $verification = Invoke-Sql (Get-Content 'database/load/verify.sql' -Raw) | ConvertFrom-Json
        if ($verification.missing_paid_tickets -eq 0) { break }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)
    $verification | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $report 'invariants.json') -Encoding utf8
    $statementsSql = @'
SELECT coalesce(json_agg(t), '[]'::json)
FROM (
  SELECT queryid, calls, round(total_exec_time::numeric, 2) AS total_ms,
    round(mean_exec_time::numeric, 2) AS mean_ms,
    round(max_exec_time::numeric, 2) AS max_ms, rows,
    left(regexp_replace(query, '[[:space:]]+', ' ', 'g'), 500) AS query
  FROM pg_stat_statements
  WHERE dbid = (SELECT oid FROM pg_database WHERE datname = 'tixflow_load_tests')
    AND query NOT LIKE '%pg_stat_statements%'
  ORDER BY total_exec_time DESC LIMIT 20
) t;
'@
    $statementsPath = Join-Path $report 'pg-stat-statements.json'
    Invoke-Sql $statementsSql | Out-File -LiteralPath $statementsPath -Encoding utf8
    $null = Get-Content -LiteralPath $statementsPath -Raw | ConvertFrom-Json
    $queueSize = [int](Invoke-Docker ($compose + @('exec','-T','redis','redis-cli','ZCARD','wr:queue:80000000-0000-0000-0000-000000000002')))
    $logServices = @('api','worker','redis')
    if ($ApiReplicas -eq 2) { $logServices += 'api-replica' }
    Invoke-Docker ($compose + @('logs','--no-color') + $logServices) |
        Set-Content (Join-Path $report 'services.log') -Encoding utf8
    $violations = @($verification.PSObject.Properties |
        Where-Object { $_.Name -notin @('inventory','paid_orders','tickets') -and $_.Value -ne 0 })
    $changed = @($hashes | Where-Object { (Get-FileHash -LiteralPath $_.path).Hash.ToLowerInvariant() -ne $_.sha256 })
    $result = [ordered]@{
        passed=($k6Exit -eq 0 -and $violations.Count -eq 0 -and $queueSize -eq 0 -and $changed.Count -eq 0)
        k6Exit=$k6Exit; invariantViolations=@($violations | ForEach-Object Name)
        queueRemaining=$queueSize; changedHarnessInputs=@($changed | ForEach-Object path)
        finishedUtc=[DateTime]::UtcNow.ToString('o')
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $report 'result.json') -Encoding utf8
    if (!$result.passed) { throw "Waiting Room load gate failed. See $report" }
    Write-Host "PASS $stamp $Mode; report: $report"
}
catch {
    $_.Exception.Message | Set-Content (Join-Path $report 'runner-error.txt') -Encoding utf8
    if ($env:TIXFLOW_LOAD_IMAGE) {
        & docker @compose logs --no-color postgres rabbitmq redis 2>&1 |
            Set-Content (Join-Path $report 'startup-failure.log') -Encoding utf8
    }
    if (!(Test-Path (Join-Path $report 'result.json'))) {
        @{passed=$false;error=$_.Exception.Message;finishedUtc=[DateTime]::UtcNow.ToString('o')} |
            ConvertTo-Json | Set-Content (Join-Path $report 'result.json') -Encoding utf8
    }
    throw
}
finally {
    if ($env:TIXFLOW_LOAD_IMAGE) { & docker @compose down --remove-orphans 2>&1 | Out-Null }
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key]) }
    Pop-Location
}
