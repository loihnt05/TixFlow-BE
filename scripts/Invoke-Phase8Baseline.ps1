[CmdletBinding()]
param(
    [ValidateSet('All', 'Correctness', 'Concurrency')][string]$Mode = 'All'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repo
$oldReport = $env:TIXFLOW_BASELINE_REPORT_PATH
$oldRevision = $env:TIXFLOW_SOURCE_REVISION
try {
    $revision = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source revision.' }
    $dirty = @(git status --porcelain).Count -gt 0
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $reportPath = Join-Path $repo "artifacts/phase8/$stamp-$($Mode.ToLowerInvariant())"
    New-Item -ItemType Directory -Path $reportPath -Force | Out-Null
    $env:TIXFLOW_BASELINE_REPORT_PATH = $reportPath.Replace('\', '/')
    $env:TIXFLOW_SOURCE_REVISION = $revision + $(if ($dirty) { '-dirty' } else { '' })

    # Hash exactly the build/config/dataset inputs; do not collect credentials, .git, logs or generated files.
    $inputs = @(git ls-files --cached --others --exclude-standard -- src tests database infrastructure/baseline scripts |
        Where-Object { $_ -match '\.(cs|csproj|sql|json|ps1)$|(^|/)Dockerfile$' })
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate baseline source inputs.' }
    $inputs += @('TixFlow.sln', 'Directory.Build.props', 'global.json', '.dockerignore', 'docker-compose.baseline.yml')
    $hashes = @($inputs | Sort-Object -Unique | ForEach-Object {
        [ordered]@{ path = $_.Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $dockerInfo = docker info --format '{{json .}}' | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Docker must be running with Linux containers.' }
    [ordered]@{
        baseline = 'baseline-v1'; revision = $revision; dirty = $dirty; mode = $Mode; utc = $stamp
        docker = @{ version = $dockerInfo.ServerVersion; os = $dockerInfo.OperatingSystem; cpus = $dockerInfo.NCPU; memoryBytes = $dockerInfo.MemTotal }
        runner = @{ apiInstances = 1; cpus = 2; memoryBytes = 2147483648; transport = 'TestServer'; poolMax = 40; redis = 'absent'; retryCount = 5 }
        dependencies = @{ postgres = @{ cpus = 1; memoryBytes = 536870912 }; rabbitmq = @{ cpus = 1; memoryBytes = 536870912 } }
        inputs = $hashes
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $reportPath 'manifest.json') -Encoding utf8

    docker compose -f docker-compose.baseline.yml build runner
    if ($LASTEXITCODE -ne 0) { throw 'Baseline image build failed.' }
    $runArgs = @('compose', '-f', 'docker-compose.baseline.yml', 'run', '--rm', '--no-deps', 'runner',
        'dotnet', 'test', 'tests/TixFlow.Api.Tests/TixFlow.Api.Tests.csproj', '-c', 'Release', '--no-build',
        '--logger', 'trx;LogFileName=tests.trx', '--results-directory', '/reports')
    if ($Mode -eq 'Correctness') { $runArgs += @('--filter', 'Category!=Baseline') }
    if ($Mode -eq 'Concurrency') { $runArgs += @('--filter', 'Category=Baseline') }
    & docker @runArgs
    if ($LASTEXITCODE -ne 0) { throw "Tests failed. Inspect $reportPath" }
    Write-Host "Baseline reports: $reportPath"
}
finally {
    $env:TIXFLOW_BASELINE_REPORT_PATH = $oldReport
    $env:TIXFLOW_SOURCE_REVISION = $oldRevision
    Pop-Location
}
