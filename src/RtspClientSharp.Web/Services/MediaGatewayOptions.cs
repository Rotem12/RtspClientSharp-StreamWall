namespace RtspClientSharp.Web.Services;

public sealed class MediaGatewayOptions
{
    public bool Enabled { get; set; }
    public bool StartProcess { get; set; } = true;
    public string ExecutablePath { get; set; } = string.Empty;
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string WorkingDirectory { get; set; } = "App_Data/media-gateway";
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:9997";
    public string ListenAddress { get; set; } = "0.0.0.0";
    public int RtspPort { get; set; } = 8554;
    public int HlsPort { get; set; } = 8888;
    public int WebRtcPort { get; set; } = 8889;
    public int WebRtcUdpPort { get; set; } = 8189;
    public string WebRtcBaseUrl { get; set; } = string.Empty;
    public string HlsBaseUrl { get; set; } = string.Empty;
    public bool H264FallbackEnabled { get; set; } = true;
    public string H264Encoder { get; set; } = "libx264";
    public string H264Preset { get; set; } = "veryfast";
    public int H264Crf { get; set; } = 18;
    public int H264KeyframeInterval { get; set; } = 60;
    public int MaxReadersPerPath { get; set; } = 32;
    public List<string> WebRtcAdditionalHosts { get; set; } = new();
    public List<string> AllowedOrigins { get; set; } = new() { "*" };

    public void Normalize()
    {
        ExecutablePath = ExecutablePath?.Trim() ?? string.Empty;
        FfmpegPath = string.IsNullOrWhiteSpace(FfmpegPath) ? "ffmpeg" : FfmpegPath.Trim();
        WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectory)
            ? "App_Data/media-gateway"
            : WorkingDirectory.Trim();
        ApiBaseUrl = string.IsNullOrWhiteSpace(ApiBaseUrl)
            ? "http://127.0.0.1:9997"
            : ApiBaseUrl.Trim().TrimEnd('/');
        ListenAddress = string.IsNullOrWhiteSpace(ListenAddress) ? "0.0.0.0" : ListenAddress.Trim();
        RtspPort = Math.Clamp(RtspPort, 1, 65535);
        HlsPort = Math.Clamp(HlsPort, 1, 65535);
        WebRtcPort = Math.Clamp(WebRtcPort, 1, 65535);
        WebRtcUdpPort = Math.Clamp(WebRtcUdpPort, 1, 65535);
        WebRtcBaseUrl = WebRtcBaseUrl?.Trim().TrimEnd('/') ?? string.Empty;
        HlsBaseUrl = HlsBaseUrl?.Trim().TrimEnd('/') ?? string.Empty;
        H264Encoder = string.IsNullOrWhiteSpace(H264Encoder) ? "libx264" : H264Encoder.Trim();
        H264Preset = string.IsNullOrWhiteSpace(H264Preset) ? "veryfast" : H264Preset.Trim();
        H264Crf = Math.Clamp(H264Crf, 0, 51);
        H264KeyframeInterval = Math.Clamp(H264KeyframeInterval, 1, 600);
        MaxReadersPerPath = Math.Clamp(MaxReadersPerPath, 1, 256);
        WebRtcAdditionalHosts ??= new List<string>();
        WebRtcAdditionalHosts = WebRtcAdditionalHosts
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        AllowedOrigins ??= new List<string>();
        AllowedOrigins = AllowedOrigins
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (AllowedOrigins.Count == 0)
            AllowedOrigins.Add("*");
    }
}

public sealed record MediaGatewayTileEndpoints(
    string? WebRtcUrl,
    string? HlsUrl,
    string? H264WebRtcUrl,
    string? H264HlsUrl);
