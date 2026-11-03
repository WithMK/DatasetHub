param(
    [Parameter(Mandatory)][int]$CollectorProcessId,
    [int]$StaleSeconds = 30,
    [switch]$RestartOnCrash,
    [string]$Executable,
    [string]$ConfigPath,
    [int]$MaxRestarts = 3
)
$ErrorActionPreference = 'Stop'
$healthRoot = Join-Path $env:LOCALAPPDATA 'DatasetHub/health'
$log = Join-Path $healthRoot 'watchdog.log'
New-Item -ItemType Directory -Force $healthRoot | Out-Null
$restarts = 0
$lastAlert = ''
while ($true) {
    $healthPath = Join-Path $healthRoot "$CollectorProcessId.json"
    try { $health = Get-Content -Raw $healthPath | ConvertFrom-Json } catch { $health = $null }
    $process = Get-Process -Id $CollectorProcessId -ErrorAction SilentlyContinue
    $message = ''
    if (!$health) { $message = 'Heartbeat unavailable; verify collector and diagnostic permissions. No automatic restart.' }
    elseif (!$process) {
        if ($health.State -eq 'Stopped') { Add-Content $log "$(Get-Date -Format o) Normal exit; watchdog stops."; break }
        $message = "Process exited unexpectedly: $CollectorProcessId"
        if ($RestartOnCrash -and $restarts -lt $MaxRestarts) {
            if (!(Test-Path -LiteralPath $Executable -PathType Leaf) -or !(Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { throw 'Executable and ConfigPath must exist.' }
            Start-Sleep -Seconds 60
            # Never start a replacement while the original PID is still alive.
            if (!(Get-Process -Id $CollectorProcessId -ErrorAction SilentlyContinue)) {
                $next = Start-Process -FilePath $Executable -ArgumentList @('--collect', ('"' + $ConfigPath + '"')) -PassThru
                $CollectorProcessId = $next.Id; $restarts++
                Add-Content $log "$(Get-Date -Format o) Restarted PID $CollectorProcessId ($restarts/$MaxRestarts)."
            }
        }
    }
    elseif ([Math]::Abs(($process.StartTime.ToUniversalTime() - [datetime]$health.StartedUtc).TotalSeconds) -gt 2) {
        $message = 'PID reused; identity mismatch. No automatic restart.'
    }
    elseif (((Get-Date).ToUniversalTime() - [datetime]$health.HeartbeatUtc).TotalSeconds -gt $StaleSeconds) { $message = 'Process heartbeat stale. Inspect; do not launch a duplicate writer.' }
    elseif (((Get-Date).ToUniversalTime() - [datetime]$health.UiHeartbeatUtc).TotalSeconds -gt $StaleSeconds) { $message = 'UI heartbeat stale. Background collection may still run; inspect without killing it.' }
    if ($message -ne $lastAlert) { Add-Content $log "$(Get-Date -Format o) $message"; Write-Host $message; $lastAlert = $message }
    Start-Sleep -Seconds 5
}
