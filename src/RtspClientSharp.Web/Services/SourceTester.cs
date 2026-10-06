using System.Diagnostics;
using System.Threading.Channels;
using RtspClientSharp.Web.Models;

namespace RtspClientSharp.Web.Services;

public sealed class SourceTester
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private readonly SourceValidator _validator;

    public SourceTester(SourceValidator validator)
    {
        _validator = validator;
    }

    public async Task<SourceTestResponse> ValidateAsync(VideoTileConfig tile, CancellationToken token)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (!tile.HasSource)
            return Result(false, "Invalid", "Enter a source host and port before testing.",
                stopwatch.ElapsedMilliseconds);

        SourceValidationResult validation = await _validator.ValidateAsync(tile, token).ConfigureAwait(false);
        return validation.IsValid
            ? Result(true, "Ready", "The source is ready to test.", stopwatch.ElapsedMilliseconds)
            : Result(false, "Invalid", string.Join(" ", validation.Errors), stopwatch.ElapsedMilliseconds,
                validation.Issues);
    }

    public async Task<SourceTestResponse> TestSessionAsync(string tileId, StreamSession session,
        CancellationToken token)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TestTimeout);

        await using FrameSubscription subscription = session.Subscribe();
        try
        {
            PublishedFrame frame = await subscription.Reader.ReadAsync(timeout.Token)
                .ConfigureAwait(false);
            TileStatusResponse status = session.GetStatus(tileId);
            return Success(frame, status.DetectedTransport, status.DetectedCodec, stopwatch);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            TileStatusResponse status = session.GetStatus(tileId);
            return new SourceTestResponse
            {
                Success = false,
                State = status.State,
                Message = status.Error is { Length: > 0 } error &&
                          !error.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                    ? error
                    : "The shared stream session did not receive a video frame within 5 seconds.",
                DetectedCodec = string.IsNullOrWhiteSpace(status.DetectedCodec) ? "Auto" : status.DetectedCodec,
                DetectedTransport = string.IsNullOrWhiteSpace(status.DetectedTransport) ? "Auto" : status.DetectedTransport,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
            };
        }
        catch (ChannelClosedException exception)
        {
            return Result(false, "Error",
                exception.InnerException?.Message ?? "The shared stream session closed unexpectedly.",
                stopwatch.ElapsedMilliseconds);
        }
    }

    private static SourceTestResponse Success(PublishedFrame frame, string transport, string codec,
        Stopwatch stopwatch)
    {
        string detectedCodec = frame.Kind switch
        {
            BrowserMediaKind.Mjpeg => "MJPEG",
            BrowserMediaKind.H264 => "H.264",
            BrowserMediaKind.H265 => "H.265",
            _ => string.IsNullOrWhiteSpace(codec) || codec == "Auto" ? "Unknown" : codec
        };
        return new SourceTestResponse
        {
            Success = true,
            State = "Streaming",
            Message = $"Received a {detectedCodec} video frame.",
            DetectedCodec = detectedCodec,
            DetectedTransport = string.IsNullOrWhiteSpace(transport) ? "Auto" : transport,
            FrameReceived = true,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
        };
    }

    private static SourceTestResponse Result(bool success, string state, string message, long elapsed,
        IReadOnlyList<SourceValidationIssue>? issues = null)
    {
        return new SourceTestResponse
        {
            Success = success,
            State = state,
            Message = message,
            ElapsedMilliseconds = elapsed,
            Issues = issues ?? Array.Empty<SourceValidationIssue>()
        };
    }
}
