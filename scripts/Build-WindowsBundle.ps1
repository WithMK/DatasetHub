param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/DatasetHub-Windows-x64'))
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$bundle = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path $bundle) { Remove-Item -LiteralPath $bundle -Recurse -Force }
New-Item -ItemType Directory -Force -Path $bundle | Out-Null
& dotnet publish (Join-Path $repo 'src/DatasetHub.Desktop/DatasetHub.Desktop.csproj') -c Release -r win-x64 --self-contained true -o $bundle
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
$console = Join-Path $bundle 'console'
& dotnet publish (Join-Path $repo 'src/DatasetHub.Collector/DatasetHub.Collector.csproj') -c Release -r win-x64 --self-contained true -o $console
if ($LASTEXITCODE -ne 0) { throw 'Collector publish failed.' }
New-Item -ItemType Directory -Force -Path (Join-Path $bundle 'config'), (Join-Path $bundle 'scripts') | Out-Null
Copy-Item (Join-Path $repo 'config/collector.local-example.json') (Join-Path $bundle 'config/collector.local-example.json')
Copy-Item (Join-Path $repo 'scripts/New-MockLogs.ps1') (Join-Path $bundle 'scripts/New-MockLogs.ps1')
Copy-Item (Join-Path $repo 'docs/windows-bundle.md') (Join-Path $bundle 'START-HERE.md')
if (!(Test-Path (Join-Path $bundle 'DatasetHub.Desktop.exe')) -or !(Test-Path (Join-Path $console 'DatasetHub.Collector.exe'))) { throw 'Missing executable.' }
Write-Host "Bundle created: $bundle"
