[CmdletBinding()]
param([string]$Image)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repo
try {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ').ToLowerInvariant()
    if (!$Image) { $Image = "tixflow-phase10-candidate:$stamp" }
    if ($Image -in @('tixflow-baseline-runner:latest','tixflow-baseline-v1-preserved:phase8')) {
        throw 'Do not overwrite frozen reference images.'
    }
    $report = Join-Path $repo "artifacts/phase10/build-$stamp"
    New-Item -ItemType Directory -Path $report | Out-Null
    $inputs = @(git ls-files --cached --others --exclude-standard -- src tests database infrastructure/baseline |
        Where-Object { $_ -match '\.(cs|csproj|sql|json)$|(^|/)Dockerfile$' }) +
        @('TixFlow.sln','Directory.Build.props','global.json','.dockerignore')
    if ($LASTEXITCODE) { throw 'Cannot enumerate source inputs' }
    $manifest = [ordered]@{
        revision=(git rev-parse HEAD).Trim(); dirty=(@(git status --porcelain).Count -gt 0); candidate='phase10-checkout-shared-lock'
        inputs=@($inputs | Sort-Object -Unique | ForEach-Object {
            @{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()}
        })
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content "$report/manifest.json" -Encoding utf8
    docker build -f infrastructure/baseline/Dockerfile -t $Image .
    if ($LASTEXITCODE) { throw 'Candidate build failed' }
    foreach ($inputHash in $manifest.inputs) {
        if ((Get-FileHash -LiteralPath $inputHash.path).Hash -ne $inputHash.sha256) { throw "Source changed during build: $($inputHash.path)" }
    }
    docker run --rm --cpus 2 --memory 2g -e DOTNET_PROCESSOR_COUNT=2 -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal `
        --add-host host.docker.internal:host-gateway -v /var/run/docker.sock:/var/run/docker.sock -v "${report}:/reports" `
        $Image dotnet test tests/TixFlow.Api.Tests/TixFlow.Api.Tests.csproj -c Release --no-build --filter 'Category!=Baseline' `
        --logger 'trx;LogFileName=tests.trx' --results-directory /reports
    if ($LASTEXITCODE) { throw "Candidate regression failed: $report" }
    Write-Host "Candidate image: $Image"
    Write-Host "Candidate manifest: $report/manifest.json"
}
finally { Pop-Location }
