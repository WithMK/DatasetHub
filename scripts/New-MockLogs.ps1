param([string]$Root = (Join-Path $PSScriptRoot '../runtime/mock-source'))
$ErrorActionPreference = 'Stop'
$folder = Join-Path $Root '20261026'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$path = Join-Path $folder '2026102610_DEMO001.log'
if (Test-Path $path) { throw "Mock file already exists: $path" }
$fields = @('2026-10-26 10:00:00.000','DEMO001','Operation','LOT_DEMO','Recipe_DEMO','PRODUCT_DEMO','Run','Demo','CollectTest','Event','Synthetic data')
[IO.File]::WriteAllText($path + '.partial', "SEM_LOG_Version=2.0`r`n" + ($fields -join "`t") + "`r`n", [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath ($path + '.partial') -Destination $path
[IO.File]::WriteAllText($path + '.done', '')
Write-Host "Created synthetic log: $path"
