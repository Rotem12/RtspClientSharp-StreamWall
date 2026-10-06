using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RtspClientSharp.Web.Services;

public sealed class CloudflareQuickTunnel : IAsyncDisposable
{
    private static readonly Regex TunnelUrlPattern = new(
        @"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly Process _process;

    private CloudflareQuickTunnel(Process process, string origin)
    {
        _process = process;
        Origin = origin;
    }

    public string Origin { get; }

    public static async Task<CloudflareQuickTunnel> StartAsync(CancellationToken cancellationToken = default)
    {
        string executable = await GetCloudflaredAsync(cancellationToken).ConfigureAwait(false);
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("tunnel");
        startInfo.ArgumentList.Add("--no-autoupdate");
        startInfo.ArgumentList.Add("--url");
        startInfo.ArgumentList.Add("http://127.0.0.1:5085");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var urlCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var exitCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, eventArgs) => ObserveLine(eventArgs.Data, urlCompletion);
        process.ErrorDataReceived += (_, eventArgs) => ObserveLine(eventArgs.Data, urlCompletion);
        process.Exited += (_, _) => exitCompletion.TrySetResult();

        try
        {
            Console.WriteLine("Starting the public tunnel. No account or port forwarding is needed...");
            if (!process.Start())
                throw new InvalidOperationException("Could not start cloudflared.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Task timeout = Task.Delay(TimeSpan.FromSeconds(90), cancellationToken);
            Task completed = await Task.WhenAny(urlCompletion.Task, exitCompletion.Task, timeout)
                .ConfigureAwait(false);
            if (completed == urlCompletion.Task)
                return new CloudflareQuickTunnel(process, await urlCompletion.Task.ConfigureAwait(false));
            if (cancellationToken.IsCancellationRequested)
                cancellationToken.ThrowIfCancellationRequested();
            if (completed == exitCompletion.Task)
                throw new InvalidOperationException("cloudflared exited before it provided a public link.");
            throw new TimeoutException("Cloudflare did not provide a public link within 90 seconds.");
        }
        catch
        {
            await StopProcessAsync(process).ConfigureAwait(false);
            process.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopProcessAsync(_process).ConfigureAwait(false);
        _process.Dispose();
    }

    private static void ObserveLine(string? line, TaskCompletionSource<string> urlCompletion)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        Console.WriteLine($"cloudflared: {line}");
        Match match = TunnelUrlPattern.Match(line);
        if (match.Success)
            urlCompletion.TrySetResult(match.Value);
    }

    private static async Task<string> GetCloudflaredAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StreamWall", "1.0"));
        using HttpResponseMessage releaseResponse = await client.GetAsync(
            "https://api.github.com/repos/cloudflare/cloudflared/releases/latest", cancellationToken)
            .ConfigureAwait(false);
        releaseResponse.EnsureSuccessStatusCode();
        using JsonDocument release = await JsonDocument.ParseAsync(
            await releaseResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        string tag = release.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!Regex.IsMatch(tag, "^[A-Za-z0-9._-]+$"))
            throw new InvalidOperationException("Cloudflare returned an invalid release tag.");

        JsonElement asset = default;
        foreach (JsonElement candidate in release.RootElement.GetProperty("assets").EnumerateArray())
        {
            if (candidate.GetProperty("name").GetString() == "cloudflared-windows-amd64.exe")
            {
                asset = candidate;
                break;
            }
        }
        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("The official Windows cloudflared download was not listed.");

        string downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps || downloadUri.Host != "github.com" ||
            !downloadUri.AbsolutePath.StartsWith("/cloudflare/cloudflared/releases/download/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The cloudflared download URL was not an official GitHub release URL.");
        }

        string digest = asset.GetProperty("digest").GetString() ?? string.Empty;
        if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$"))
            throw new InvalidOperationException("Cloudflare's release did not provide a SHA-256 digest.");
        string expectedHash = digest[7..].ToLowerInvariant();

        string toolDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamWall", "tools");
        Directory.CreateDirectory(toolDirectory);
        string executable = Path.Combine(toolDirectory, $"cloudflared-{tag}.exe");
        if (File.Exists(executable) && await HasHashAsync(executable, expectedHash, cancellationToken).ConfigureAwait(false))
            return executable;

        string temporaryFile = executable + ".download";
        try
        {
            Console.WriteLine($"Downloading Cloudflare's public-link helper ({tag}) to your user profile...");
            using HttpResponseMessage binaryResponse = await client.GetAsync(downloadUri,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            binaryResponse.EnsureSuccessStatusCode();
            await using (var destination = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (Stream source = await binaryResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            if (!await HasHashAsync(temporaryFile, expectedHash, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("The downloaded cloudflared file failed its SHA-256 check.");

            File.Move(temporaryFile, executable, overwrite: true);
            return executable;
        }
        catch
        {
            try { File.Delete(temporaryFile); } catch (IOException) { }
            throw;
        }
    }

    private static async Task<bool> HasHashAsync(string path, string expectedHash, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(expectedHash));
    }

    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (TimeoutException) { }
    }
}
