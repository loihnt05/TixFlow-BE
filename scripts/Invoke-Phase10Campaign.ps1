[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OptimizedImage,
    [Parameter(Mandatory)][string]$OptimizedManifest,
    [Parameter(Mandatory)][string]$Python,
    [ValidateSet('smoke','ramp','spike','soak')][string[]]$Profiles = @('smoke','ramp','spike','soak'),
    [ValidateRange(1,10)][int]$Repetitions = 3
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repo
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$campaign = Join-Path $repo "artifacts/phase10/campaign-$stamp"
New-Item -ItemType Directory -Path $campaign -Force | Out-Null
$state = [ordered]@{startedUtc=[DateTime]::UtcNow;status='running';profiles=$Profiles;repetitions=$Repetitions;currentProfile=$null;batches=@()}
function Save-State { $state | ConvertTo-Json -Depth 8 | Set-Content "$campaign/status.json" -Encoding utf8 }
try {
    Save-State
    foreach ($profileName in $Profiles) {
        $state.currentProfile = $profileName
        Save-State
        $before = @(Get-ChildItem artifacts/phase10 -Directory | Select-Object -ExpandProperty Name)
        $vus = @{smoke=10;ramp=20;spike=100;soak=10}[$profileName]
        try {
            & ./scripts/Invoke-Phase10Load.ps1 -Variant both -Profile $profileName -Vus $vus -Repetitions $Repetitions `
                -OptimizedImage $OptimizedImage -OptimizedManifest $OptimizedManifest *>&1 |
                Tee-Object -FilePath "$campaign/$profileName.log"
        }
        finally {
            $new = @(Get-ChildItem artifacts/phase10 -Directory | Where-Object { $_.Name -notin $before -and $_.Name -like "*-$profileName" })
            foreach ($batch in $new) { $state.batches += @{profile=$profileName;vus=$vus;path=$batch.FullName} }
            Save-State
        }
        if ($new.Count -ne 1) { throw 'Cannot identify campaign batch' }
        & $Python scripts/Summarize-Phase10.py $new[0].FullName
        if ($LASTEXITCODE) { throw "Summary failed: $($new[0].FullName)" }
    }
    $state.status = 'measured'
    $state.currentProfile = $null
    $state['finishedUtc'] = [DateTime]::UtcNow
    Save-State
    Write-Host "Measurements finished; review results and proposed SLO before closing phase 10: $campaign"
}
catch {
    $state.status = 'failed'
    $state['error'] = $_.Exception.Message
    $state['finishedUtc'] = [DateTime]::UtcNow
    Save-State
    throw
}
finally { Pop-Location }
