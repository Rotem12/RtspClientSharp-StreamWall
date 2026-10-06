using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using RtspClientSharp.Codecs.Video;
using RtspClientSharp.RawFrames;
using RtspClientSharp.Rtsp;
using DirectRtpClient = RtspClientSharp.RtpClient.RtpClient;
using RtspTransportProtocol = RtspClientSharp.RtpTransportProtocol;
using RtspClientSharp.Web.Models;

namespace RtspClientSharp.Web.Services;

public sealed class StreamSession : IAsyncDisposable
{
    private readonly VideoTileConfig _tile;
    private readonly ILogger<StreamSession> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Channel<PublishedFrame>> _subscribers = new();
    private CancellationTokenSource? _cancellation;
    private Task _runTask = Task.CompletedTask;
    private string _state = "Idle";
    private string? _error;
    private string _detectedCodec = "Auto";
    private string _detectedTransport = "Auto";
    private string _browserOutput = "Waiting for source";
    private long _viewerCount;
    private long _frameCount;
    private long _lastFrameUnixMilliseconds;
    private string? _browserCodec;
    private PublishedFrame? _latestKeyFrame;

    public StreamSession(VideoTileConfig tile, ILogger<StreamSession> logger)
    {
        _tile = tile ?? throw new ArgumentNullException(nameof(tile));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int ViewerCount => (int)Math.Max(0, Interlocked.Read(ref _viewerCount));

    public static string CreateSourceKey(VideoTileConfig tile)
    {
        string passwordMarker = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(tile.Password ?? string.Empty)));
        return string.Join("|", tile.Protocol, tile.Host.Trim().ToLowerInvariant(), tile.Port,
            tile.Path.Trim(), tile.Username.Trim(), passwordMarker, tile.Transport, tile.Codec,
            tile.H264SpsPpsBase64.Trim(), tile.H265VpsSpsPpsBase64.Trim());
    }

    public bool TryAddViewer(int maximumViewers)
    {
        while (true)
        {
            long current = Interlocked.Read(ref _viewerCount);
            if (current >= maximumViewers)
                return false;

            if (Interlocked.CompareExchange(ref _viewerCount, current + 1, current) == current)
            {
                EnsureStarted();
                return true;
            }
        }
    }

    public void RemoveViewer()
    {
        Interlocked.Decrement(ref _viewerCount);
    }

    public FrameSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<PublishedFrame>(new BoundedChannelOptions(3)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        lock (_sync)
        {
            _subscribers[id] = channel;
            if (_latestKeyFrame is { } keyFrame)
                channel.Writer.TryWrite(keyFrame);
        }

        return new FrameSubscription(this, id, channel);
    }

    public TileStatusResponse GetStatus(string tileId)
    {
        lock (_sync)
        {
            DateTimeOffset? lastFrame = _lastFrameUnixMilliseconds <= 0
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(_lastFrameUnixMilliseconds);

            return new TileStatusResponse
            {
                TileId = tileId,
                State = _state,
                Error = _error,
                ViewerCount = ViewerCount,
                FrameCount = Interlocked.Read(ref _frameCount),
                LastFrameAt = lastFrame,
                DetectedCodec = _detectedCodec,
                DetectedTransport = _detectedTransport,
                BrowserOutput = _browserOutput
            };
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        CompleteSubscribers();
    }

    internal void RemoveSubscription(Guid id)
    {
        Channel<PublishedFrame>? channel = null;
        lock (_sync)
        {
            if (_subscribers.Remove(id, out Channel<PublishedFrame>? removed))
                channel = removed;
        }

        channel?.Writer.TryComplete();
    }

    private void EnsureStarted()
    {
        lock (_sync)
        {
            if (_cancellation is { IsCancellationRequested: false } && !_runTask.IsCompleted)
                return;

            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            _runTask = RunLoopAsync(_cancellation.Token);
        }
    }

    private async Task StopAsync()
    {
        Task runTask;
        CancellationTokenSource? cancellation;

        lock (_sync)
        {
            cancellation = _cancellation;
            runTask = _runTask;
            _cancellation = null;
        }

        if (cancellation == null)
            return;

        cancellation.Cancel();
        try
        {
            await runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (ViewerCount == 0)
                {
                    SetState("Idle", null);
                    await Task.Delay(250, token).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    await ReceiveOnceAsync(token).ConfigureAwait(false);
                    if (!token.IsCancellationRequested)
                        SetState("Reconnecting", null);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (InvalidCredentialException exception)
                {
                    SetState("Error", "Invalid source credentials");
                    _logger.LogWarning(exception, "Stream source rejected credentials for {Host}:{Port}",
                        _tile.Host, _tile.Port);
                }
                catch (Exception exception)
                {
                    SetState("Error", exception.Message);
                    _logger.LogWarning(exception, "Stream source failed for {Host}:{Port}",
                        _tile.Host, _tile.Port);
                }

                if (!token.IsCancellationRequested && ViewerCount > 0)
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            SetState("Idle", null);
        }
    }

    private async Task ReceiveOnceAsync(CancellationToken token)
    {
        ConnectionParameters connectionParameters = CreateConnectionParameters();

        if (_tile.Protocol == SourceProtocol.Rtsp)
        {
            using var client = new RtspClient(connectionParameters)
            {
                UseInlineFrameDelivery = true
            };
            client.FrameReceived += RtspClientOnFrameReceived;
            try
            {
                SetState("Connecting", null);
                await client.ConnectAsync(token).ConfigureAwait(false);
                SetDetected("RTSP", null);
                SetState("Connected", null);
                await client.ReceiveAsync(token).ConfigureAwait(false);
            }
            finally
            {
                client.FrameReceived -= RtspClientOnFrameReceived;
            }

            return;
        }

        using var rtpClient = new DirectRtpClient(connectionParameters)
        {
            VideoCodec = ParseCodec(_tile.Codec),
            H264SpsPpsBytes = DecodeBase64(_tile.H264SpsPpsBase64),
            H265VpsSpsPpsBytes = DecodeBase64(_tile.H265VpsSpsPpsBase64),
            UseInlineFrameDelivery = true,
            Timeout = 3000
        };
        rtpClient.FrameReceived += DirectClientOnFrameReceived;
        try
        {
            SetState("Connecting", null);
            await rtpClient.ConnectAsync(token).ConfigureAwait(false);
            SetDetected(rtpClient.DetectedTransportMode.ToString(), null);
            SetState("Connected", null);
            await rtpClient.ReceiveLoopAsync(token).ConfigureAwait(false);
        }
        finally
        {
            rtpClient.FrameReceived -= DirectClientOnFrameReceived;
        }

        void DirectClientOnFrameReceived(object? sender, RawFrame frame)
        {
            SetDetected(rtpClient.DetectedTransportMode.ToString(), rtpClient.DetectedVideoCodec);
            Publish(frame);
        }
    }

    private void RtspClientOnFrameReceived(object? sender, RawFrame frame)
    {
        Publish(frame);
    }

    private void Publish(RawFrame frame)
    {
        long sequence = Interlocked.Increment(ref _frameCount);
        if (!BrowserPublisher.TryCreate(frame, sequence, out PublishedFrame published))
            return;

        lock (_sync)
        {
            if (published.Codec != null)
                _detectedCodec = published.Kind switch
                {
                    BrowserMediaKind.Mjpeg => "MJPEG",
                    BrowserMediaKind.H264 => "H.264",
                    BrowserMediaKind.H265 => "H.265",
                    _ => published.Codec
                };

            if (published.Kind == BrowserMediaKind.H264)
            {
                _browserOutput = "H.264 WebCodecs";
                _detectedCodec = "H.264";
            }
            else if (published.Kind == BrowserMediaKind.Mjpeg)
            {
                _browserOutput = "MJPEG multipart or WebSocket";
                _detectedCodec = "MJPEG";
            }
            else if (published.Kind == BrowserMediaKind.H265)
            {
                _browserOutput = "H.265 WebCodecs";
                _detectedCodec = "H.265";
            }

            _error = null;
            _state = "Streaming";
            _lastFrameUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (published.Kind is BrowserMediaKind.H264 or BrowserMediaKind.H265)
            {
                string codec = published.Codec ?? _browserCodec ??
                    (published.Kind == BrowserMediaKind.H264 ? "avc1.42E01E" : "hvc1.1.6.L120.B0");
                _browserCodec = codec;
                PublishedFrame normalized = published with { Codec = codec };
                if (normalized.KeyFrame)
                    _latestKeyFrame = normalized;

                foreach (Channel<PublishedFrame> subscriber in _subscribers.Values)
                    subscriber.Writer.TryWrite(normalized);
            }
            else
            {
                foreach (Channel<PublishedFrame> subscriber in _subscribers.Values)
                    subscriber.Writer.TryWrite(published);
            }
        }
    }

    private ConnectionParameters CreateConnectionParameters()
    {
        if (_tile.Protocol == SourceProtocol.Rtsp)
        {
            Uri uri = BuildRtspUri();
            if (string.IsNullOrWhiteSpace(_tile.Username))
            {
                return new ConnectionParameters(uri)
                {
                    RequiredTracks = RequiredTracks.Video,
                    RtpTransport = ParseRtpTransport(_tile.Transport)
                };
            }

            return new ConnectionParameters(uri,
                new NetworkCredential(_tile.Username, _tile.Password ?? string.Empty))
            {
                RequiredTracks = RequiredTracks.Video,
                RtpTransport = ParseRtpTransport(_tile.Transport)
            };
        }

        if (!IPAddress.TryParse(_tile.Host, out _))
            throw new InvalidOperationException("Direct RTP/UDP sources require an IP address.");

        return new ConnectionParameters(new Uri($"rtp://{_tile.Host}:{_tile.Port}"))
        {
            TransportMode = ParseMediaTransport(_tile.Transport),
            MulticastInterfaceAddress = null
        };
    }

    private Uri BuildRtspUri()
    {
        string path = string.IsNullOrWhiteSpace(_tile.Path) ? "/" : _tile.Path.Trim();
        if (Uri.TryCreate(path, UriKind.Absolute, out Uri? absolute) &&
            (absolute.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) ||
             absolute.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)))
        {
            return new UriBuilder(absolute) { UserName = string.Empty, Password = string.Empty }.Uri;
        }

        if (!path.StartsWith('/'))
            path = "/" + path;

        return new UriBuilder("rtsp", _tile.Host.Trim(), _tile.Port, path).Uri;
    }

    private static MediaTransportMode ParseMediaTransport(StreamTransport transport)
    {
        return transport switch
        {
            StreamTransport.Rtp => MediaTransportMode.Rtp,
            StreamTransport.MpegTs => MediaTransportMode.MpegTs,
            _ => MediaTransportMode.Auto
        };
    }

    private static RtspTransportProtocol ParseRtpTransport(StreamTransport transport)
    {
        return transport switch
        {
            StreamTransport.Udp => RtspTransportProtocol.UDP,
            StreamTransport.Multicast => RtspTransportProtocol.MULTICAST,
            _ => RtspTransportProtocol.TCP
        };
    }

    private static CodecInfoType ParseCodec(StreamCodec codec)
    {
        return codec switch
        {
            StreamCodec.H264 => CodecInfoType.H264,
            StreamCodec.H265 => CodecInfoType.H265,
            StreamCodec.Mjpeg => CodecInfoType.MJPEG,
            _ => CodecInfoType.Auto
        };
    }

    private void SetDetected(string transport, CodecInfoType? codec)
    {
        lock (_sync)
        {
            _detectedTransport = string.IsNullOrWhiteSpace(transport) ? "Auto" : transport;
            if (codec is { } detected && detected != CodecInfoType.Auto)
                _detectedCodec = detected.ToString();
        }
    }

    private void SetState(string state, string? error)
    {
        lock (_sync)
        {
            _state = state;
            _error = error;
        }
    }

    private void CompleteSubscribers()
    {
        Channel<PublishedFrame>[] subscribers;
        lock (_sync)
        {
            subscribers = _subscribers.Values.ToArray();
            _subscribers.Clear();
        }

        foreach (Channel<PublishedFrame> subscriber in subscribers)
            subscriber.Writer.TryComplete();
    }

    private static byte[] DecodeBase64(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<byte>();

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("The configured codec parameter bytes are not valid base64.", exception);
        }
    }
}

public sealed class FrameSubscription : IAsyncDisposable
{
    private readonly StreamSession _session;
    private readonly Guid _id;
    private readonly Channel<PublishedFrame> _channel;
    private int _disposed;

    internal FrameSubscription(StreamSession session, Guid id, Channel<PublishedFrame> channel)
    {
        _session = session;
        _id = id;
        _channel = channel;
    }

    public ChannelReader<PublishedFrame> Reader => _channel.Reader;

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _session.RemoveSubscription(_id);

        return ValueTask.CompletedTask;
    }
}
