param([Parameter(Mandatory)][string]$Evidence, [ValidateSet('Frontend','Backend','Inventory')][string]$Stage)
$ErrorActionPreference = 'Stop'
$backend = Split-Path $PSScriptRoot
$frontend = Join-Path (Split-Path $backend) 'tixflow-frontend'
New-Item -ItemType Directory -Force $Evidence | Out-Null
function Run([string]$Name, [scriptblock]$Command) {
    if (Test-Path (Join-Path $Evidence "$Name.log")) { throw 'Evidence already exists; choose a new run directory.' }
    $global:LASTEXITCODE = 0
    & $Command 2>&1 | Tee-Object -FilePath (Join-Path $Evidence "$Name.log")
    $code = $LASTEXITCODE
    [ordered]@{name=$Name; exitCode=$code; completedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json -Compress | Add-Content (Join-Path $Evidence "$Stage-results.jsonl")
    if ($code -ne 0) { throw "$Name failed: $code" }
}
if ($Stage -eq 'Frontend') {
    Set-Location $frontend
    Run 'npm-ci' { npm ci --no-audit --no-fund }
    Run 'typegen' { npx --no-install next typegen }
    Run 'lint' { npm run lint }
    Run 'audit-tests' { node --test scripts/audit-production.test.mjs }
    Run 'build' { npm run build }
    npm audit --json | Out-File (Join-Path $Evidence 'npm-audit.json') -Encoding utf8
    if ($LASTEXITCODE -gt 1) { throw 'npm audit registry failure' }
    Run 'npm-audit-gate' { node scripts/audit-production.mjs (Join-Path $Evidence 'npm-audit.json') }
}
if ($Stage -eq 'Backend') {
    Set-Location $backend
    Run 'restore' { dotnet restore TixFlow.sln --locked-mode }
    Run 'regression' { dotnet test TixFlow.sln -c Release --no-restore --filter 'Category!=Baseline' --logger 'trx;LogFileName=regression.trx' --results-directory $Evidence }
    dotnet list TixFlow.sln package --vulnerable --include-transitive --format json | Out-File (Join-Path $Evidence 'dotnet-audit.json') -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw 'dotnet audit failed' }
}
if ($Stage -eq 'Inventory') {
    if (Test-Path (Join-Path $Evidence 'environment.json')) { throw 'Inventory already exists; choose a new run directory.' }
    $inventory = [ordered]@{
        capturedUtc=[DateTime]::UtcNow.ToString('o'); timezone='Asia/Saigon'
        os=Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,OSArchitecture
        cpu=Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors
        memoryBytes=(Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
        disks=Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | Select-Object DeviceID,Size,FreeSpace
        powershell=$PSVersionTable.PSVersion.ToString(); node=(& node --version); npm=(& npm --version)
        dotnetSdks=(& dotnet --list-sdks); docker=(& docker version --format '{{.Client.Version}} / {{.Server.Version}}'); dockerOS=(& docker info --format '{{.OSType}}')
        containers=(& docker ps --format '{{.Names}} | {{.Image}} | {{.Status}} | {{.Ports}}')
    }
    $inventory | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $Evidence 'environment.json')
    foreach ($repo in @($backend,$frontend)) {
        Push-Location $repo
        $files = @(git ls-files --cached --others --exclude-standard) | Sort-Object -Unique
        $manifest = [ordered]@{commit=(& git rev-parse HEAD); branch=(& git branch --show-current); status=@(git status --short); files=@($files | ForEach-Object { if (Test-Path -LiteralPath $_ -PathType Leaf) { @{path=$_; sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash} } })}
        $manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $Evidence "$(Split-Path $repo -Leaf)-source.json")
        Pop-Location
    }
}
