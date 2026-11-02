[CmdletBinding()]
param([string]$Image = 'tixflow-baseline-v1-preserved:phase8')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repo
try {
    # Never compare an image merely on the strength of a mutable Docker tag.
    $manifest = Get-Content docs/reports/baseline-v1/manifest.json -Raw | ConvertFrom-Json
    $paths = @($manifest.inputs | ForEach-Object { '/src/' + $_.path })
    $hashes = @(docker run --rm --entrypoint sha256sum $Image @paths)
    if ($LASTEXITCODE -ne 0 -or $hashes.Count -ne $paths.Count) { throw 'Cannot verify the baseline image inputs.' }
    foreach ($line in $hashes) {
        $parts = $line -split '\s+', 2
        $path = $parts[1].Substring(5)
        if ($parts[0] -ne ($manifest.inputs | Where-Object path -eq $path).sha256) { throw "Baseline hash mismatch: $path" }
    }
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $directory = Join-Path $repo "artifacts/phase9/$stamp-reference"
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $imageId = docker image inspect $Image --format '{{.Id}}'
    [ordered]@{ utc = $stamp; image = $imageId; baselineRevision = $manifest.revision; verifiedInputs = $hashes.Count;
        cpus = 2; memoryBytes = 2147483648; poolMax = 40; cache = 'off'; transport = 'TestServer';
        sourceManifestSha256 = (Get-FileHash docs/reports/baseline-v1/manifest.json).Hash } |
        ConvertTo-Json | Set-Content (Join-Path $directory 'reference-run.json') -Encoding utf8
    docker run --rm --cpus 2 --memory 2g --add-host host.docker.internal:host-gateway `
        -e DOTNET_PROCESSOR_COUNT=2 -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal `
        -e TIXFLOW_REPORT_DIR=/reports -e "TIXFLOW_SOURCE_REVISION=$($manifest.revision)" `
        -v /var/run/docker.sock:/var/run/docker.sock -v "${directory}:/reports" $Image `
        dotnet test tests/TixFlow.Api.Tests/TixFlow.Api.Tests.csproj -c Release --no-build `
        --filter Category=Baseline --logger 'trx;LogFileName=tests.trx' --results-directory /reports
    if ($LASTEXITCODE -ne 0) { throw "Reference run failed: $directory" }
    Write-Host "Reference reports: $directory"
}
finally { Pop-Location }
