$ErrorActionPreference = 'Stop'

function Resolve-ServerExecutable {
    $packaged = Join-Path $PSScriptRoot 'RtspClientSharp.Web.exe'
    if (Test-Path -LiteralPath $packaged -PathType Leaf) { return $packaged }

    $built = Join-Path $PSScriptRoot 'artifacts\portable-win-x64\RtspClientSharp.Web.exe'
    if (Test-Path -LiteralPath $built -PathType Leaf) { return $built }

    throw 'The app is not built here. Download the portable release ZIP, or run Build-Portable.bat first.'
}

function Test-PortInUse {
    param([int]$Port)
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $client.Connect('127.0.0.1', $Port)
        return $true
    } catch {
        return $false
    } finally {
        $client.Dispose()
    }
}

$serverExecutable = Resolve-ServerExecutable
$appDirectory = Split-Path -Parent $serverExecutable
$port = 5085
if (Test-PortInUse -Port $port) {
    throw "Port $port is already in use. Close the other local web server, then retry."
}

$logDirectory = Join-Path $appDirectory 'App_Data\logs'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stdoutLog = Join-Path $logDirectory "local-$stamp.log"
$stderrLog = Join-Path $logDirectory "local-$stamp-error.log"
$oldUrl = [Environment]::GetEnvironmentVariable('ASPNETCORE_URLS', 'Process')
$oldReadOnly = [Environment]::GetEnvironmentVariable('StreamWall__ReadOnly', 'Process')
$server = $null

try {
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$port"
    $env:StreamWall__ReadOnly = 'false'
    $server = Start-Process -FilePath $serverExecutable -WorkingDirectory $appDirectory `
        -ArgumentList @('--stream-wall-managed') `
        -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog `
        -WindowStyle Hidden -PassThru

    $localUrl = "http://127.0.0.1:$port/view/default"
    $ready = $false
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($server.HasExited) { throw "The web server stopped during startup. See $stderrLog" }
        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:$port/api/health" -TimeoutSec 3
            if ($health.status -eq 'ok') { $ready = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "The web server did not become ready. See $stderrLog" }

    Start-Process $localUrl | Out-Null
    Write-Host "Local editor: $localUrl"
    Write-Host 'This address is only reachable from this PC. Close this window or press Ctrl+C to stop.'
    while (-not $server.HasExited) { Start-Sleep -Milliseconds 500 }
    if ($server.ExitCode -ne 0) { throw "The web server exited with code $($server.ExitCode). See $stderrLog" }
} finally {
    if ($server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
    }
    [Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', $oldUrl, 'Process')
    [Environment]::SetEnvironmentVariable('StreamWall__ReadOnly', $oldReadOnly, 'Process')
}

