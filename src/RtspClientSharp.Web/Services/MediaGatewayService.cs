using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using RtspClientSharp.Web.Models;

namespace RtspClientSharp.Web.Services;

public sealed class MediaGatewayService : IHostedService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHostEnvironment _environment;
    private readonly MediaGatewayOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MediaGatewayService> _logger;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _pathGate = new(1, 1);
    private readonly Dictionary<string, string> _configuredPaths = new(StringComparer.Ordinal);
    private Process? _process;
    private string? _configPath;
    private bool _disposed;

    public MediaGatewayService(
        IHostEnvironment environment,
        MediaGatewayOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<MediaGatewayService> logger)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return;

        if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
            _logger.LogWarning("Media gateway is enabled but is not ready; direct browser output remains available.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopProcess();
        return Task.CompletedTask;
    }

    public async Task<MediaGatewayTileEndpoints?> GetTileEndpointsAsync(
        string wallId,
        VideoTileConfig tile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (!_options.Enabled ||
            (string.IsNullOrWhiteSpace(_options.WebRtcBaseUrl) &&
             string.IsNullOrWhiteSpace(_options.HlsBaseUrl)))
            return null;

        if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
            return null;

        string sourcePath = GetPathName(wallId, tile.Id);
        string h264Path = sourcePath + "_h264";
        string source = BuildSource(tile);
        string signature = string.Join("|", source, tile.Transport, tile.Codec,
            _options.H264FallbackEnabled, _options.H264Encoder, _options.H264Preset,
            _options.H264Crf, _options.H264KeyframeInterval, _options.MaxReadersPerPath);

        await _pathGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_configuredPaths.TryGetValue(sourcePath, out string? configuredSignature) ||
                !string.Equals(configuredSignature, signature, StringComparison.Ordinal))
            {
                await UpsertPathAsync(sourcePath, BuildSourcePathConfig(tile, source), cancellationToken)
                    .ConfigureAwait(false);

                if (_options.H264FallbackEnabled)
                {
                    await UpsertPathAsync(h264Path, BuildH264PathConfig(sourcePath), cancellationToken)
                        .ConfigureAwait(false);
                }

                _configuredPaths[sourcePath] = signature;
            }
        }
        finally
        {
            _pathGate.Release();
        }

        return new MediaGatewayTileEndpoints(
            BuildOutputUrl(_options.WebRtcBaseUrl, sourcePath, "/whep"),
            BuildOutputUrl(_options.HlsBaseUrl, sourcePath, "/index.m3u8"),
            _options.H264FallbackEnabled
                ? BuildOutputUrl(_options.WebRtcBaseUrl, h264Path, "/whep")
                : null,
            _options.H264FallbackEnabled
                ? BuildOutputUrl(_options.HlsBaseUrl, h264Path, "/index.m3u8")
                : null);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        StopProcess();
        _startGate.Dispose();
        _pathGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
            return false;

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await ProbeAsync(cancellationToken).ConfigureAwait(false))
                return true;

            if (!_options.StartProcess)
                return false;

            if (_process is null || _process.HasExited)
            {
                StopProcess();
                if (!TryStartProcess())
                    return false;
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                if (await ProbeAsync(cancellationToken).ConfigureAwait(false))
                    return true;

                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not start or reach the media gateway.");
            return false;
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(_options.ApiBaseUrl + "/v3/info", UriKind.Absolute, out Uri? uri))
            return false;

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(nameof(MediaGatewayService));
            using HttpResponseMessage response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private bool TryStartProcess()
    {
        string executable = ResolveExecutablePath();
        if (string.IsNullOrWhiteSpace(executable))
        {
            _logger.LogWarning("Media gateway executable was not found. Configure MediaGateway:ExecutablePath.");
            return false;
        }

        string workingDirectory = ResolveDirectory(_options.WorkingDirectory);
        Directory.CreateDirectory(workingDirectory);
        _configPath = Path.Combine(workingDirectory, "mediamtx.streamwall.yml");
        File.WriteAllText(_configPath, BuildConfiguration(), new UTF8Encoding(false));

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(_configPath);

        try
        {
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) => LogGatewayLine(args.Data, false);
            process.ErrorDataReceived += (_, args) => LogGatewayLine(args.Data, true);
            if (!process.Start())
            {
                process.Dispose();
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            _configuredPaths.Clear();
            _logger.LogInformation("Started media gateway {Executable} using {ConfigPath}.", executable, _configPath);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not start media gateway executable {Executable}.", executable);
            return false;
        }
    }

    private void StopProcess()
    {
        Process? process = _process;
        _process = null;
        _configuredPaths.Clear();
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Media gateway process did not stop cleanly.");
        }
        finally
        {
            process.Dispose();
        }
    }

    private void LogGatewayLine(string? line, bool error)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        if (error)
            _logger.LogDebug("Media gateway: {Line}", line);
        else
            _logger.LogTrace("Media gateway: {Line}", line);
    }

    private async Task UpsertPathAsync(
        string name,
        Dictionary<string, object?> configuration,
        CancellationToken cancellationToken)
    {
        string encodedName = Uri.EscapeDataString(name);
        HttpClient client = _httpClientFactory.CreateClient(nameof(MediaGatewayService));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_options.ApiBaseUrl + "/v3/config/paths/replace/" + encodedName));
        request.Content = JsonContent.Create(configuration, options: JsonOptions);
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            using var addRequest = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(_options.ApiBaseUrl + "/v3/config/paths/add/" + encodedName));
            addRequest.Content = JsonContent.Create(configuration, options: JsonOptions);
            using HttpResponseMessage addResponse = await client.SendAsync(addRequest, cancellationToken)
                .ConfigureAwait(false);
            if (!addResponse.IsSuccessStatusCode)
                await ThrowGatewayErrorAsync(addResponse, name).ConfigureAwait(false);
            return;
        }

        if (!response.IsSuccessStatusCode)
            await ThrowGatewayErrorAsync(response, name).ConfigureAwait(false);
    }

    private async Task ThrowGatewayErrorAsync(HttpResponseMessage response, string path)
    {
        string detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new InvalidOperationException(
            $"Media gateway rejected path '{path}' with {(int)response.StatusCode}: {detail}");
    }

    private Dictionary<string, object?> BuildSourcePathConfig(VideoTileConfig tile, string source)
    {
        var configuration = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["source"] = source,
            ["sourceOnDemand"] = true,
            ["sourceOnDemandStartTimeout"] = "10s",
            ["sourceOnDemandCloseAfter"] = "10s",
            ["maxReaders"] = _options.MaxReadersPerPath
        };

        if (tile.Protocol == SourceProtocol.Rtsp)
            configuration["rtspTransport"] = tile.Transport switch
            {
                StreamTransport.Tcp => "tcp",
                StreamTransport.Udp => "udp",
                StreamTransport.Multicast => "multicast",
                _ => "automatic"
            };

        return configuration;
    }

    private Dictionary<string, object?> BuildH264PathConfig(string sourcePath)
    {
        string localSource = $"rtsp://127.0.0.1:{_options.RtspPort}/{sourcePath}";
        string localDestination = "rtsp://127.0.0.1:$RTSP_PORT/$MTX_PATH";
        string executable = QuoteCommandToken(_options.FfmpegPath);
        string encoderArguments = BuildH264EncoderArguments();
        string command = string.Join(' ',
            executable,
            "-hide_banner -loglevel warning -rtsp_transport tcp -i", QuoteCommandToken(localSource),
            "-map 0:v:0 -an", encoderArguments,
            "-pix_fmt yuv420p -profile:v baseline -level:v 4.2 -bf 0",
            "-g", _options.H264KeyframeInterval,
            "-keyint_min", _options.H264KeyframeInterval,
            "-f rtsp", QuoteCommandToken(localDestination));

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["runOnDemand"] = command,
            ["runOnDemandRestart"] = true,
            ["runOnDemandStartTimeout"] = "15s",
            ["runOnDemandCloseAfter"] = "10s",
            ["maxReaders"] = _options.MaxReadersPerPath
        };
    }

    private string BuildH264EncoderArguments()
    {
        string encoder = QuoteCommandToken(_options.H264Encoder);
        return _options.H264Encoder.Trim().ToLowerInvariant() switch
        {
            "h264_nvenc" => string.Join(' ',
                "-c:v", encoder,
                "-preset p4 -tune ll -zerolatency 1 -rc vbr -cq", _options.H264Crf,
                "-b:v 0"),
            "h264_amf" => string.Join(' ',
                "-c:v", encoder,
                "-rc cqp -qp_i", _options.H264Crf,
                "-qp_p", _options.H264Crf,
                "-qp_b", _options.H264Crf),
            "h264_qsv" => string.Join(' ',
                "-c:v", encoder,
                "-global_quality", _options.H264Crf,
                "-low_delay_brc 1"),
            _ => string.Join(' ',
                "-c:v", encoder,
                "-tune zerolatency -preset", QuoteCommandToken(_options.H264Preset),
                "-crf", _options.H264Crf)
        };
    }

    private string BuildConfiguration()
    {
        if (!Uri.TryCreate(_options.ApiBaseUrl, UriKind.Absolute, out Uri? apiUri))
            throw new InvalidOperationException("MediaGateway:ApiBaseUrl must be an absolute HTTP URL.");

        string listen = _options.ListenAddress;
        string origins = JsonSerializer.Serialize(_options.AllowedOrigins, JsonOptions);
        string additionalHosts = JsonSerializer.Serialize(_options.WebRtcAdditionalHosts, JsonOptions);
        return "# Generated by RtspClientSharp.Web. Do not edit while the app is running.\n" +
               "logLevel: warn\n" +
               "api: true\n" +
               $"apiAddress: 127.0.0.1:{apiUri.Port}\n" +
               "rtsp: true\n" +
               $"rtspAddress: 127.0.0.1:{_options.RtspPort}\n" +
               "rtspTransports: [tcp]\n" +
               "rtmp: false\n" +
               "srt: false\n" +
               "hls: true\n" +
               $"hlsAddress: {listen}:{_options.HlsPort}\n" +
               "hlsVariant: lowLatency\n" +
               $"hlsAllowOrigins: {origins}\n" +
               "webrtc: true\n" +
               $"webrtcAddress: {listen}:{_options.WebRtcPort}\n" +
               $"webrtcAllowOrigins: {origins}\n" +
               $"webrtcLocalUDPAddress: {listen}:{_options.WebRtcUdpPort}\n" +
               "webrtcLocalTCPAddress: ''\n" +
               $"webrtcAdditionalHosts: {additionalHosts}\n" +
               "paths: {}\n";
    }

    private string ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            string configured = Path.IsPathRooted(_options.ExecutablePath)
                ? _options.ExecutablePath
                : Path.Combine(_environment.ContentRootPath, _options.ExecutablePath);
            return File.Exists(configured) ? Path.GetFullPath(configured) : _options.ExecutablePath;
        }

        string bundled = Path.Combine(_environment.ContentRootPath, ".tools", "mediamtx", "mediamtx.exe");
        return File.Exists(bundled) ? bundled : "mediamtx";
    }

    private string ResolveDirectory(string configuredPath)
    {
        return Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(_environment.ContentRootPath, configuredPath));
    }

    private static string GetPathName(string wallId, string tileId)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes($"{wallId}|{tileId}"));
        return "sw_" + Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }

    private static string? BuildOutputUrl(string baseUrl, string path, string suffix)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        return $"{baseUrl.TrimEnd('/')}/{path}{suffix}";
    }

    private static string BuildSource(VideoTileConfig tile)
    {
        if (tile.Protocol == SourceProtocol.Rtsp)
        {
            Uri uri;
            if (Uri.TryCreate(tile.Path, UriKind.Absolute, out Uri? absolute) &&
                absolute.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(absolute)
                {
                    UserName = tile.Username,
                    Password = tile.Password
                };
                uri = builder.Uri;
            }
            else
            {
                string path = string.IsNullOrWhiteSpace(tile.Path) ? "/" : tile.Path.Trim();
                var builder = new UriBuilder("rtsp", tile.Host, tile.Port, path)
                {
                    UserName = tile.Username,
                    Password = tile.Password
                };
                uri = builder.Uri;
            }

            return uri.ToString();
        }

        string scheme = tile.Protocol == SourceProtocol.Udp || tile.Transport == StreamTransport.MpegTs
            ? "udp+mpegts"
            : "udp+rtp";
        return $"{scheme}://{tile.Host}:{tile.Port}";
    }

    private static string QuoteCommandToken(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";
        return '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
    }
}
