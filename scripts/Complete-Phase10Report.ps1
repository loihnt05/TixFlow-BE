[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Campaign,
    [Parameter(Mandatory)][string]$Python,
    [string]$PlotDependencies,
    [string]$Output = 'docs/reports/load-v2'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repo
$oldPythonPath = $env:PYTHONPATH
$resultFile = Join-Path $Campaign 'report-result.json'
try {
    $deadline = [DateTime]::UtcNow.AddHours(6)
    do {
        $state = Get-Content (Join-Path $Campaign 'status.json') -Raw | ConvertFrom-Json
        if ($state.status -ne 'running') { break }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Campaign did not finish within six hours; inspect its process and status.' }
        Start-Sleep -Seconds 15
    } while ($true)
    if ($state.status -ne 'measured') { throw "Campaign stopped before completion: $($state.error)" }
    if ($PlotDependencies) { $env:PYTHONPATH = (Resolve-Path -LiteralPath $PlotDependencies).Path }
    & $Python scripts/Report-Phase10Campaign.py $Campaign $Output
    if ($LASTEXITCODE) { throw 'Report generation failed; raw evidence is preserved.' }
    @{status='generated';finishedUtc=[DateTime]::UtcNow;report=[IO.Path]::GetFullPath((Join-Path $repo "$Output/README.md"));reviewRequired=$true} |
        ConvertTo-Json | Set-Content $resultFile -Encoding utf8
}
catch {
    @{status='failed';finishedUtc=[DateTime]::UtcNow;error=$_.Exception.Message} |
        ConvertTo-Json | Set-Content $resultFile -Encoding utf8
    throw
}
finally { $env:PYTHONPATH=$oldPythonPath; Pop-Location }
