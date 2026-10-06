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

function Get-Cloudflared {
    $toolDirectory = Join-Path $env:LOCALAPPDATA 'StreamWall\tools'
    New-Item -ItemType Directory -Force -Path $toolDirectory | Out-Null

    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/cloudflare/cloudflared/releases/latest' `
        -Headers @{ 'User-Agent' = 'RtspClientSharp-StreamWall' } -TimeoutSec 30
    if ($release.tag_name -notmatch '^[A-Za-z0-9._-]+$') { throw 'Cloudflare returned an invalid release tag.' }

    $asset = $release.assets | Where-Object { $_.name -eq 'cloudflared-windows-amd64.exe' } | Select-Object -First 1
    if (-not $asset -or $asset.digest -notmatch '^sha256:([a-fA-F0-9]{64})$') {
        throw 'Could not find the official Windows cloudflared download and SHA-256 digest.'
    }
    $expectedHash = $Matches[1].ToLowerInvariant()
    $executable = Join-Path $toolDirectory "cloudflared-$($release.tag_name).exe"
    if (Test-Path -LiteralPath $executable -PathType Leaf) {
        $actualHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -eq $expectedHash) { return $executable }
    }

    $downloadPath = "$executable.download"
    Write-Host "Downloading Cloudflare's tunnel helper ($($release.tag_name)); no installer or admin rights needed..."
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $downloadPath -UseBasicParsing -TimeoutSec 180
    $downloadHash = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($downloadHash -ne $expectedHash) {
        Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue
        throw 'The downloaded cloudflared file failed its SHA-256 check; it was not run.'
    }
    Move-Item -LiteralPath $downloadPath -Destination $executable -Force
    return $executable
}

function Get-TunnelUrl {
    param([string[]]$Paths)
    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        $logText = Get-Content -LiteralPath $path -Raw -ErrorAction SilentlyContinue
        if ([string]::IsNullOrWhiteSpace($logText)) { continue }
        $matches = [regex]::Matches($logText, 'https://[a-z0-9-]+\.trycloudflare\.com')
        if ($matches.Count -gt 0) { return $matches[$matches.Count - 1].Value }
    }
    return $null
}

$serverExecutable = Resolve-ServerExecutable
$appDirectory = Split-Path -Parent $serverExecutable
$port = 5085
if (Test-PortInUse -Port $port) {
    throw "Port $port is already in use. Close the other local web server, then retry."
}

$cloudflared = Get-Cloudflared
$logDirectory = Join-Path $appDirectory 'App_Data\logs'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$serverOut = Join-Path $logDirectory "public-$stamp-server.log"
$serverErr = Join-Path $logDirectory "public-$stamp-server-error.log"
$tunnelOut = Join-Path $logDirectory "public-$stamp-tunnel.log"
$tunnelErr = Join-Path $logDirectory "public-$stamp-tunnel-error.log"
$envNames = @('ASPNETCORE_URLS', 'StreamWall__ReadOnly', 'StreamWall__AccessToken')
$oldEnvironment = @{}
foreach ($name in $envNames) {
    $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$server = $null
$tunnel = $null

try {
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$port"
    $env:StreamWall__ReadOnly = 'true'
    $env:StreamWall__AccessToken = ''
    $server = Start-Process -FilePath $serverExecutable -WorkingDirectory $appDirectory `
        -ArgumentList @('--stream-wall-managed') `
        -RedirectStandardOutput $serverOut -RedirectStandardError $serverErr `
        -WindowStyle Hidden -PassThru

    $localHealth = "http://127.0.0.1:$port/api/health"
    $ready = $false
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($server.HasExited) { throw "The web server stopped during startup. See $serverErr" }
        try {
            $health = Invoke-RestMethod -Uri $localHealth -TimeoutSec 3
            if ($health.status -eq 'ok') { $ready = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "The web server did not become ready. See $serverErr" }

    $wall = Invoke-RestMethod -Uri "http://127.0.0.1:$port/api/walls/default" -TimeoutSec 5
    if (@($wall.tiles).Count -eq 0) {
        Write-Warning 'The default wall has no video sources yet. Stop this window, double-click RtspClientSharp.Web.exe, choose 1 to add your RTSP feeds, then choose 3 to share publicly.'
    }

    $tunnel = Start-Process -FilePath $cloudflared `
        -ArgumentList @('tunnel', '--no-autoupdate', '--url', "http://127.0.0.1:$port") `
        -WorkingDirectory (Split-Path -Parent $cloudflared) `
        -RedirectStandardOutput $tunnelOut -RedirectStandardError $tunnelErr `
        -WindowStyle Hidden -PassThru

    $publicOrigin = $null
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    while (-not $publicOrigin -and [DateTimeOffset]::UtcNow -lt $deadline) {
        if ($tunnel.HasExited) { throw "The tunnel stopped during startup. See $tunnelErr" }
        $publicOrigin = Get-TunnelUrl -Paths @($tunnelErr, $tunnelOut)
        if (-not $publicOrigin) { Start-Sleep -Milliseconds 500 }
    }
    if (-not $publicOrigin) { throw "Cloudflare did not issue a public link. See $tunnelErr" }

    $viewUrl = "$publicOrigin/view/default"
    Write-Host ''
    Write-Host "DIRECT PUBLIC VIEW LINK (send this one): $viewUrl" -ForegroundColor Green
    Write-Host 'No viewer account, token, or password is required. Viewers cannot edit the wall.'
    Write-Host 'This PC must stay on and connected. Press Ctrl+C here to stop the public link.'
    try { Set-Clipboard -Value $viewUrl; Write-Host 'The link was copied to the clipboard.' } catch { }
    Start-Process $viewUrl | Out-Null

    while (-not $tunnel.HasExited) {
        if ($server.HasExited) { throw "The web server stopped. See $serverErr" }
        Start-Sleep -Milliseconds 500
    }
    throw "The Cloudflare tunnel stopped. See $tunnelErr"
} finally {
    if ($tunnel -and -not $tunnel.HasExited) {
        Stop-Process -Id $tunnel.Id -Force -ErrorAction SilentlyContinue
    }
    if ($server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
    }
    foreach ($name in $envNames) {
        [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name], 'Process')
    }
}

