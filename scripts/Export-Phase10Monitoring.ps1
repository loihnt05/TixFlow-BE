param([string]$PrometheusUrl,[string]$GrafanaUrl,[string]$Report,[DateTimeOffset]$Start,[DateTimeOffset]$End)
$ErrorActionPreference = 'Stop'
$headers = @{ Authorization='Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes('load:disposable_load_only')) }
$dashboard = (Invoke-RestMethod "$GrafanaUrl/api/dashboards/uid/tixflow-load" -Headers $headers -TimeoutSec 10).dashboard
$dashboard.time = @{from=$Start.UtcDateTime.ToString('o');to=$End.UtcDateTime.ToString('o')}
$dashboard.title = "TixFlow load — $(Split-Path $Report -Leaf)"
$dashboard | Add-Member -NotePropertyName snapshot -NotePropertyValue @{originalUrl="/d/tixflow-load";timestamp=$End.UtcDateTime.ToString('o')} -Force
$seriesCount = 0
$export = @()
foreach ($panel in $dashboard.panels) {
    $frames = @()
    foreach ($target in $panel.targets) {
        $query = [Uri]::EscapeDataString($target.expr)
        $url = "$PrometheusUrl/api/v1/query_range?query=$query&start=$($Start.ToUnixTimeSeconds())&end=$($End.ToUnixTimeSeconds())&step=5"
        $result = Invoke-RestMethod $url -TimeoutSec 30
        if ($result.status -ne 'success') { throw "Prometheus query failed: $($target.expr)" }
        $export += @{panel=$panel.title;expression=$target.expr;response=$result}
        foreach ($series in $result.data.result) {
            $label = ($series.metric | ConvertTo-Json -Compress)
            $frames += @{name=$label;refId=$target.refId;fields=@(
                @{name='Time';type='time';values=@($series.values | ForEach-Object { [long]($_[0]*1000) })},
                @{name=$label;type='number';config=@{unit=$panel.fieldConfig.defaults.unit};values=@($series.values | ForEach-Object { [double]::Parse($_[1],[Globalization.CultureInfo]::InvariantCulture) })}
            )}
            $seriesCount++
        }
    }
    $panel | Add-Member -NotePropertyName snapshotData -NotePropertyValue $frames -Force
}
if ($seriesCount -eq 0) { throw 'Refusing an empty Grafana snapshot' }
if (@($dashboard.panels | Where-Object { $_.snapshotData.Count -eq 0 }).Count) {
    throw "Missing dashboard series: $(($dashboard.panels | Where-Object { $_.snapshotData.Count -eq 0 }).title -join ', ')"
}
$export | ConvertTo-Json -Depth 40 | Set-Content (Join-Path $Report 'prometheus-range.json') -Encoding utf8
# This payload includes actual points, allowing later restoration without Prometheus.
$payload = @{dashboard=$dashboard;name=$dashboard.title;expires=0;external=$false}
$json = $payload | ConvertTo-Json -Depth 60
$json | Set-Content (Join-Path $Report 'grafana-snapshot.json') -Encoding utf8
$receipt = Invoke-RestMethod "$GrafanaUrl/api/snapshots" -Method Post -Headers $headers -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec 30
$stored = Invoke-RestMethod "$GrafanaUrl/api/snapshots/$($receipt.key)" -Headers $headers -TimeoutSec 20
if (@($stored.dashboard.panels | Where-Object { $_.snapshotData.Count -gt 0 }).Count -eq 0) { throw 'Grafana stored an empty snapshot' }
@{series=$seriesCount;panels=$stored.dashboard.panels.Count;key=$receipt.key;start=$Start;end=$End;verified=$true} |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Report 'monitoring-result.json') -Encoding utf8
