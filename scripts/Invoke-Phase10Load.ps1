[CmdletBinding()]
param(
    [ValidateSet('baseline','optimized','both')][string]$Variant = 'both',
    [Alias('Profile')][ValidateSet('smoke','ramp','spike','soak')][string]$LoadProfile = 'smoke',
    [ValidateRange(1,10)][int]$Repetitions = 3,
    [ValidateRange(1,10000)][int]$Vus = 10,
    [ValidateRange(51,10000)][int]$Iterations = 100,
    [string]$BaselineImage = 'tixflow-baseline-v1-preserved:phase8',
    [string]$OptimizedImage = 'tixflow-baseline-runner:latest'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repo
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$root = Join-Path $repo "artifacts/phase10/$stamp-$LoadProfile"
$project = "tixflow-load-$($stamp.ToLowerInvariant())"
$compose = @('compose','--project-name',$project,'-f','docker-compose.load.yml')
$saved = @{}
foreach ($name in @('IMAGE','PRIVATE','REPORT','PROFILE','RUN_ID','VUS','ITERATIONS','CACHE')) {
    $key = "TIXFLOW_LOAD_$name"; $saved[$key] = [Environment]::GetEnvironmentVariable($key)
}
function Invoke-LoadDocker([string[]]$Arguments) {
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Invoke-LoadDocker failed ($LASTEXITCODE): $($Arguments[0])" }
}
function Sql([string]$Text) {
    $Text | & docker @compose exec -T postgres psql -X -qAt -v ON_ERROR_STOP=1 -U load -d tixflow_load_tests
    if ($LASTEXITCODE -ne 0) { throw 'Load database SQL failed.' }
}
function VerifyImage([string]$Image, [string]$ManifestPath) {
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $paths = @($manifest.inputs | ForEach-Object { '/src/' + $_.path })
    $lines = @(Invoke-LoadDocker (@('run','--rm','--entrypoint','sha256sum',$Image) + $paths))
    if ($lines.Count -ne $paths.Count) { throw 'Source manifest count mismatch.' }
    foreach ($line in $lines) {
        $parts = $line -split '\s+',2; $path = $parts[1].Substring(5)
        if ($parts[0] -ne ($manifest.inputs | Where-Object path -eq $path).sha256) { throw "Image input mismatch: $path" }
    }
    return $manifest
}
$sampler = $null
try {
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $variants = @(if ($Variant -eq 'both') { 'baseline'; 'optimized' } else { $Variant })
    $images = @{ baseline=$BaselineImage; optimized=$OptimizedImage }
    $manifests = @{}
    foreach ($v in $variants) {
        $manifestPath = if ($v -eq 'baseline') { 'docs/reports/baseline-v1/manifest.json' } else { 'docs/reports/optimized-v1/manifest.json' }
        $manifests[$v] = VerifyImage $images[$v] $manifestPath
        # Pin the resolved immutable local ID for the whole experiment.
        $images[$v] = (Invoke-LoadDocker @('image','inspect',$images[$v],'--format','{{.Id}}')).Trim()
    }
    $env:TIXFLOW_LOAD_IMAGE = $images[$variants[0]]
    $env:TIXFLOW_LOAD_PRIVATE = Join-Path $root 'private'
    $env:TIXFLOW_LOAD_REPORT = $root
    $env:TIXFLOW_LOAD_PROFILE = $LoadProfile
    $env:TIXFLOW_LOAD_RUN_ID = $stamp
    $env:TIXFLOW_LOAD_VUS = "$Vus"; $env:TIXFLOW_LOAD_ITERATIONS = "$Iterations"
    New-Item -ItemType Directory -Path $env:TIXFLOW_LOAD_PRIVATE -Force | Out-Null
    Invoke-LoadDocker ($compose + @('build','issuer'))
    Invoke-LoadDocker ($compose + @('pull','k6'))
    # Alternate order to reduce systematic warm-host/order bias.
    for ($repeat=1; $repeat -le $Repetitions; $repeat++) {
        $order = @($variants)
        if ($repeat % 2 -eq 0) { [array]::Reverse($order) }
        foreach ($v in $order) {
            $runId = "$stamp-$v-$repeat"
            $report = Join-Path $root "$v-$repeat"
            New-Item -ItemType Directory -Path $report -Force | Out-Null
            $env:TIXFLOW_LOAD_PRIVATE = Join-Path $root "private/$v-$repeat"
            New-Item -ItemType Directory -Path $env:TIXFLOW_LOAD_PRIVATE -Force | Out-Null
            $env:TIXFLOW_LOAD_IMAGE = $images[$v]; $env:TIXFLOW_LOAD_REPORT = $report
            $env:TIXFLOW_LOAD_RUN_ID = $runId
            $env:TIXFLOW_LOAD_CACHE = if ($v -eq 'optimized') { 'true' } else { 'false' }
            $start = [DateTime]::UtcNow
            Invoke-LoadDocker ($compose + @('up','-d','--wait','postgres','rabbitmq'))
            Sql 'CREATE EXTENSION pg_stat_statements; CREATE TABLE load_control(dataset text PRIMARY KEY); INSERT INTO load_control VALUES (''load-v1'');' | Out-Null
            # Apply the schema shipped with that verified image, not the other version's migrations.
            $files = @('database/init/001_initial_schema.sql') + @(($manifests[$v].inputs | Where-Object { $_.path -like 'database/migrations/*.sql' }).path | Sort-Object)
            foreach ($file in $files) {
                $content = Invoke-LoadDocker @('run','--rm','--entrypoint','cat',$images[$v],"/src/$file")
                Sql ($content -join "`n") | Out-Null
            }
            Sql (Get-Content database/load/seed.sql -Raw) | Out-Null
            Invoke-LoadDocker ($compose + @('up','-d','issuer','api','worker','nginx'))
            $deadline = [DateTime]::UtcNow.AddMinutes(2)
            while (!(Test-Path (Join-Path $env:TIXFLOW_LOAD_PRIVATE 'tokens.json'))) {
                if ([DateTime]::UtcNow -gt $deadline) { throw 'Issuer did not generate tokens.' }
                Start-Sleep -Seconds 1
            }
            $inputs = @(Get-ChildItem load,infrastructure/load,database/load -Recurse -File) + @(Get-Item docker-compose.load.yml,scripts/Invoke-Phase10Load.ps1)
            $inputHashes = @($inputs | ForEach-Object {
                $relative = [IO.Path]::GetRelativePath($repo,$_.FullName).Replace('\','/')
                $snapshot = Join-Path "$report/inputs" $relative
                New-Item -ItemType Directory -Path (Split-Path $snapshot) -Force | Out-Null
                Copy-Item -LiteralPath $_.FullName -Destination $snapshot
                @{ path=$relative; sha256=(Get-FileHash -LiteralPath $snapshot).Hash.ToLowerInvariant() }
            })
            [ordered]@{ runId=$runId; variant=$v; profile=$LoadProfile; repetition=$repeat; utc=$start; revision=$manifests[$v].revision;
                image=$images[$v]; verifiedInputs=$manifests[$v].inputs.Count; dataset='load-v1'; vus=$Vus; iterations=$Iterations;
                apiInstances=1; apiCpus=2; apiMemoryBytes=1073741824; poolMax=40; rateLimit=$false; cache=($v -eq 'optimized');
                loadCpus=2; loadMemoryBytes=1073741824; transport='k6 HTTP -> Nginx -> Kestrel'; identity='isolated RS256 OIDC issuer, no login per iteration';
                host=(Invoke-LoadDocker @('info','--format','{{json .}}') | ConvertFrom-Json | Select-Object ServerVersion,OperatingSystem,NCPU,MemTotal);
                inputs=$inputHashes
            } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $report 'manifest.json') -Encoding utf8
            Invoke-LoadDocker ($compose + @('config')) | Set-Content (Join-Path $report 'compose.yml') -Encoding utf8
            Invoke-LoadDocker ($compose + @('images','--format','json')) | Set-Content (Join-Path $report 'images.json') -Encoding utf8
            # Sampling is outside k6 and keeps raw timestamps; short lock waits may be missed.
            $sampler = Start-Job -ScriptBlock {
                param($Repo,$ComposeArgs,$Report,$ProjectName)
                Set-Location $Repo
                while ($true) {
                    $sample = Get-Content database/load/sample.sql -Raw | & docker @ComposeArgs exec -T postgres psql -X -qAt -U load -d tixflow_load_tests
                    if ($LASTEXITCODE -eq 0) { $sample | Add-Content (Join-Path $Report 'database.jsonl') -Encoding utf8 }
                    # compose ps omits one-off containers; include the k6 generator too.
                    $ids = @(& docker ps -q --filter "label=com.docker.compose.project=$ProjectName")
                    if ($ids.Count) {
                        $stats = & docker stats --no-stream --format '{{json .}}' @ids
                        @{ utc=[DateTime]::UtcNow.ToString('o'); containers=@($stats | ForEach-Object { $_ | ConvertFrom-Json }) } |
                            ConvertTo-Json -Depth 6 -Compress | Add-Content (Join-Path $Report 'resources.jsonl') -Encoding utf8
                    }
                    Start-Sleep -Seconds 2
                }
            } -ArgumentList $repo,$compose,$report,$project
            & docker @compose run --rm --no-deps k6 2>&1 | Tee-Object -FilePath (Join-Path $report 'k6.log')
            $k6Exit = $LASTEXITCODE
            foreach ($inputHash in $inputHashes) {
                if ((Get-FileHash -LiteralPath $inputHash.path).Hash -ne $inputHash.sha256) {
                    $k6Exit = 99
                    Write-Warning "Harness changed during measurement: $($inputHash.path)"
                }
            }
            Stop-Job $sampler; Receive-Job $sampler | Out-Null; Remove-Job $sampler; $sampler=$null
            # Allow bounded asynchronous ticket delivery before the final DB gate.
            $deadline = [DateTime]::UtcNow.AddSeconds(60)
            do {
                $verification = Sql (Get-Content database/load/verify.sql -Raw) | ConvertFrom-Json
                if ($verification.missing_paid_tickets -eq 0) { break }
                Start-Sleep -Seconds 2
            } while ([DateTime]::UtcNow -lt $deadline)
            $verification | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $report 'invariants.json') -Encoding utf8
            Sql "SELECT coalesce(json_agg(x),'[]') FROM (SELECT query,calls,total_exec_time,rows FROM pg_stat_statements ORDER BY total_exec_time DESC LIMIT 20) x;" |
                Set-Content (Join-Path $report 'queries.json') -Encoding utf8
            Invoke-LoadDocker ($compose + @('exec','-T','rabbitmq','rabbitmqctl','list_queues','name','messages_ready','messages_unacknowledged','--formatter','json')) |
                Set-Content (Join-Path $report 'queues.json') -Encoding utf8
            if ($v -eq 'optimized') { Sql "SELECT coalesce(json_agg(x),'[]') FROM processing_metrics x;" | Set-Content (Join-Path $report 'worker-metrics.json') -Encoding utf8 }
            Invoke-LoadDocker ($compose + @('logs','--no-color','api','worker')) | Set-Content (Join-Path $report 'services.log') -Encoding utf8
            $violations = @($verification.PSObject.Properties | Where-Object { $_.Name -notin @('inventory','paid_orders','tickets') -and $_.Value -ne 0 })
            @{ k6Exit=$k6Exit; invariantViolations=$violations.Count; finishedUtc=[DateTime]::UtcNow; passed=($k6Exit -eq 0 -and $violations.Count -eq 0) } |
                ConvertTo-Json | Set-Content (Join-Path $report 'result.json') -Encoding utf8
            Invoke-LoadDocker ($compose + @('down','--remove-orphans'))
            if ($k6Exit -ne 0 -or $violations.Count) { throw "Load gate failed: $report" }
            Write-Host "PASS $runId"
        }
    }
    Write-Host "Reports: $root"
}
finally {
    if ($null -ne $sampler) { Stop-Job $sampler; Remove-Job $sampler -Force }
    if ($env:TIXFLOW_LOAD_IMAGE) { & docker @compose down --remove-orphans }
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key]) }
    Pop-Location
}

