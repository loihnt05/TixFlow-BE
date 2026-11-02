param([string]$Repo, [string[]]$ComposeArgs, [string]$Report, [string]$ProjectName, [string]$RabbitUrl)
$ErrorActionPreference = 'Stop'
Set-Location $Repo
$headers = @{ Authorization='Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes('load:disposable_load_only')) }
function Number($value) { ([double]$value).ToString('R',[Globalization.CultureInfo]::InvariantCulture) }
function Bytes([string]$value) {
    if ($value -notmatch '^([\d.]+)([KMGT]?i?B)$') { throw "Unknown Docker memory unit: $value" }
    $power = @{B=0;kB=1;MB=2;GB=3;TB=4;KiB=1;MiB=2;GiB=3;TiB=4}[$matches[2]]
    $base = if ($matches[2].Contains('i')) { 1024 } else { 1000 }
    [double]::Parse($matches[1],[Globalization.CultureInfo]::InvariantCulture) * [Math]::Pow($base,$power)
}
while ($true) {
    try {
        $sample = Get-Content database/load/sample.sql -Raw | & docker @ComposeArgs exec -T postgres psql -X -qAt -v ON_ERROR_STOP=1 -U load -d tixflow_load_tests
        if ($LASTEXITCODE) { throw 'Database sampler failed' }
        $sample | Add-Content (Join-Path $Report 'database.jsonl') -Encoding utf8
        $db = $sample | ConvertFrom-Json
        $lines = [Collections.Generic.List[string]]::new()
        foreach ($name in @('connections','active','lock_waiters','oldest_transaction_seconds','pending_outbox','oldest_outbox_seconds','consumer_lag_seconds')) {
            $lines.Add("tixflow_$name $(Number $db.$name)")
        }
        $ids = @(& docker ps -q --filter "label=com.docker.compose.project=$ProjectName")
        if ($LASTEXITCODE) { throw 'Container enumeration failed' }
        if ($ids.Count) {
            $rawStats = & docker stats --no-stream --format '{{json .}}' @ids 2>&1
            if ($LASTEXITCODE) {
                $currentIds = @(& docker ps -q --filter "label=com.docker.compose.project=$ProjectName")
                if ($LASTEXITCODE -or !(Compare-Object $ids $currentIds)) { throw 'Resource sampler failed with unchanged containers' }
                # k6 --rm can exit between enumeration and stats. Record this
                # expected lifecycle race, then retry the current set once.
                @{utc=[DateTime]::UtcNow.ToString('o');event='container-set-changed-during-stats';before=$ids;after=$currentIds} |
                    ConvertTo-Json -Compress | Add-Content (Join-Path $Report 'sampler-events.jsonl') -Encoding utf8
                $rawStats = & docker stats --no-stream --format '{{json .}}' @currentIds 2>&1
                if ($LASTEXITCODE) { throw 'Resource sampler retry failed' }
            }
            $stats = @($rawStats | ForEach-Object { $_ | ConvertFrom-Json })
            @{ utc=[DateTime]::UtcNow.ToString('o'); containers=$stats } | ConvertTo-Json -Depth 6 -Compress |
                Add-Content (Join-Path $Report 'resources.jsonl') -Encoding utf8
            foreach ($s in $stats) {
                $name = $s.Name.Replace("$ProjectName-",'') -replace '-\d+$','' -replace '-run-.*$',''
                $lines.Add("tixflow_container_cpu_percent{service=`"$name`"} $($s.CPUPerc.TrimEnd('%'))")
                $lines.Add("tixflow_container_memory_bytes{service=`"$name`"} $(Number (Bytes (($s.MemUsage -split ' / ')[0])))")
            }
        }
        $queues = Invoke-RestMethod "$RabbitUrl/api/queues" -Headers $headers -TimeoutSec 5
        @{utc=[DateTime]::UtcNow.ToString('o');queues=@($queues | Select-Object name,messages_ready,messages_unacknowledged)} |
            ConvertTo-Json -Depth 6 -Compress | Add-Content (Join-Path $Report 'queue-samples.jsonl') -Encoding utf8
        foreach ($q in $queues) {
            if ($null -eq $q.messages_ready -or $null -eq $q.messages_unacknowledged) { continue }
            $name = $q.name.Replace('\','\\').Replace('"','\"')
            $lines.Add("tixflow_queue_ready{queue=`"$name`"} $(Number $q.messages_ready)")
            $lines.Add("tixflow_queue_unacked{queue=`"$name`"} $(Number $q.messages_unacknowledged)")
        }
        $lines.Add("tixflow_sampler_timestamp_seconds $([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())")
        # A single replacement prevents Prometheus reading a partially written sample.
        [IO.File]::WriteAllText((Join-Path $Report 'live.prom.tmp'),($lines -join "`n")+"`n")
        Move-Item -LiteralPath (Join-Path $Report 'live.prom.tmp') -Destination (Join-Path $Report 'live.prom') -Force
    }
    catch {
        @{utc=[DateTime]::UtcNow.ToString('o');error=$_.Exception.Message} | ConvertTo-Json -Compress |
            Add-Content (Join-Path $Report 'sampler-errors.jsonl') -Encoding utf8
    }
    Start-Sleep -Seconds 2
}
