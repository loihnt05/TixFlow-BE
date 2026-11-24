[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dll = Join-Path $repo 'src/TixFlow.Api/bin/Release/net8.0/TixFlow.Api.dll'
$private = Join-Path $repo 'artifacts/waiting-room/openapi'
$output = Join-Path $repo 'docs/reports/waiting-room-openapi.json'
$provenance = Join-Path $repo 'docs/reports/waiting-room-openapi-provenance.json'
New-Item -ItemType Directory -Path $private -Force | Out-Null
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = ($listener.LocalEndpoint).Port
$listener.Stop()
$variables = @{
    ASPNETCORE_ENVIRONMENT = 'Development'
    ASPNETCORE_URLS = "http://127.0.0.1:$port"
    ConnectionStrings__Postgres = 'Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=fixture-only;Timeout=1'
    ConnectionStrings__Redis = '127.0.0.1:1,abortConnect=false'
    Assistant__Provider = 'Disabled'
    Assistant__ApiKey = ''
    Assistant__KeyDirectory = (Join-Path $private 'assistant-keys')
    Tickets__KeyDirectory = (Join-Path $private 'ticket-keys')
    RabbitMq__Host = '127.0.0.1'
    RabbitMq__Username = 'unused'
    RabbitMq__Password = 'fixture-only'
}
$saved = @{}
$process = $null
try {
    foreach ($key in $variables.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $variables[$key])
    }
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @($dll) -WorkingDirectory (Split-Path $dll) `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $private 'stdout.log') `
        -RedirectStandardError (Join-Path $private 'stderr.log')
    $document = $null
    for ($attempt = 0; $attempt -lt 45; $attempt++) {
        if ($process.HasExited) { throw 'API exited before Swagger was available.' }
        try {
            $document = (Invoke-WebRequest "http://127.0.0.1:$port/swagger/v1/swagger.json" -TimeoutSec 2).Content
            break
        } catch { Start-Sleep -Seconds 1 }
    }
    if (!$document) { throw 'Swagger export timed out.' }
    $parsed = $document | ConvertFrom-Json
    foreach ($route in @('/api/v1/waiting-room/join','/api/v1/waiting-room/status',
            '/api/v1/waiting-room/{queueId}','/api/v1/holds')) {
        if (!$parsed.paths.PSObject.Properties[$route]) { throw "Swagger missing $route" }
    }
    $document | Set-Content -LiteralPath $output -Encoding utf8
    [ordered]@{
        assembly = 'src/TixFlow.Api/bin/Release/net8.0/TixFlow.Api.dll'
        assemblySha256 = (Get-FileHash -LiteralPath $dll).Hash.ToLowerInvariant()
        revision = (git -C $repo rev-parse HEAD).Trim()
        dirty = (@(git -C $repo status --porcelain).Count -gt 0)
        paths = @($parsed.paths.PSObject.Properties).Count
        exportedUtc = [DateTime]::UtcNow.ToString('o')
        environment = 'Development; fixture DB/Redis endpoints unavailable; no business requests'
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $provenance -Encoding utf8
    Write-Host "Exported $(@($parsed.paths.PSObject.Properties).Count) paths to $output"
}
finally {
    if ($process -and !$process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
}
