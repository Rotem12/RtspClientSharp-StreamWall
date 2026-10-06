# Stream Wall

A browser-based RTSP video wall and manager, packaged to run on Windows without an installer or administrator rights.

## Start it without scripts

1. Download `StreamWall-win-x64.zip` from [Releases](https://github.com/Rotem12/RtspClientSharp-StreamWall/releases).
2. Extract it somewhere your Windows account can write to, such as Documents. Do not run it from inside the ZIP or `Program Files`.
3. Double-click `RtspClientSharp.Web.exe` and choose a mode:
   - **1 — Configure/edit on this PC:** add the RTSP feeds, then press Ctrl+C when finished.
   - **2 — Share on the home network:** the app prints the direct address for other devices on your Wi-Fi. They can view and edit the wall.
   - **3 — Share publicly:** the app creates and prints one direct view link. Send that link; viewers need no password or token.

The executable itself downloads Cloudflare's official `cloudflared` helper into your user profile for public mode and checks its SHA-256 digest. No PowerShell, BAT file, installer, .NET install, or admin rights are needed. Keep the app window open while people are viewing. Press Ctrl+C to stop the server and any public link.

Public sharing is view-only: viewers cannot change your wall and are not sent the saved camera address, path, or username. Home-network sharing allows editing from devices on your Wi-Fi; anyone who can reach that address may change the wall unless you configure an editor PIN in Web Settings. Windows Firewall or workplace policy may still block other devices. If group policy also blocks the app's `.exe`, ask the administrator to allow it; this package does not bypass that policy.

The public URL changes when the tunnel is restarted. Quick Tunnels are intended for testing, have no uptime guarantee, and impose service limits; see [Cloudflare's Quick Tunnel notes](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/). Anyone who gets the link can view the video, so only publish feeds you are allowed to share. Streaming also uses the host PC's internet upload bandwidth.

## Run only on this PC

The **Configure/edit on this PC** choice binds to `127.0.0.1:5085`; it is not reachable from other devices. The app data, including your wall and protected source credentials, is stored in `App_Data`. Keep that folder private and back it up yourself. The BAT launchers remain available for machines that permit scripts.

## Build the portable package from source

Install the .NET 10 SDK on the build machine, then run `Build-Portable.bat`. It produces `artifacts\StreamWall-win-x64.zip`, a self-contained Windows x64 package. The computer that runs the ZIP does not need .NET or administrator rights.

The RTSP client library included under `src/RtspClientSharp` retains its original license in `src/RtspClientSharp/LICENSE.md`.
