# Stream Wall

A browser-based RTSP video wall and manager, packaged to run on Windows without an installer or administrator rights.

## Easiest setup on the host PC

1. Download `StreamWall-win-x64.zip` from [Releases](https://github.com/Rotem12/RtspClientSharp-StreamWall/releases).
2. Extract it somewhere your Windows account can write to, such as Documents. Do not run it from inside the ZIP or `Program Files`.
3. Run `Run-Local.bat`. Add the RTSP sources in the editor; the host PC must be able to reach those cameras.
4. Close the local server, then run `Run-Public.bat`.
5. Send people the one direct `/view/default` link printed by the window. It is copied to the clipboard too.

`Run-Public.bat` starts the app in view-only mode and creates a temporary public HTTPS link. Viewers do not need an account, token, PIN, or password, cannot change your wall, and are not sent the saved camera address, path, or username. The helper downloads Cloudflare's official `cloudflared` executable into your user profile and checks its SHA-256 digest; it does not install a Windows service or require admin access. Keep the window open and the PC online while people are testing. Press Ctrl+C to stop it.

The public URL changes when the tunnel is restarted. Quick Tunnels are intended for testing, have no uptime guarantee, and impose service limits; see [Cloudflare's Quick Tunnel notes](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/). Anyone who gets the link can view the video, so only publish feeds you are allowed to share. Streaming also uses the host PC's internet upload bandwidth.

## Run only on this PC

Run `Run-Local.bat`. This binds to `127.0.0.1:5085`; it is not reachable from the public internet. The app data, including your wall and protected source credentials, is stored in `App_Data`. Keep that folder private and back it up yourself.

## Build the portable package from source

Install the .NET 10 SDK on the build machine, then run `Build-Portable.bat`. It produces `artifacts\StreamWall-win-x64.zip`, a self-contained Windows x64 package. The computer that runs the ZIP does not need .NET or administrator rights.

The RTSP client library included under `src/RtspClientSharp` retains its original license in `src/RtspClientSharp/LICENSE.md`.
