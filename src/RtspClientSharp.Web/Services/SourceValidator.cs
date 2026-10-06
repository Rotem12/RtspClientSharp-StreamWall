using System.Net;
using RtspClientSharp.Web.Models;

namespace RtspClientSharp.Web.Services;

public sealed record SourceValidationResult(bool IsValid, IReadOnlyList<SourceValidationIssue> Issues)
{
    public IReadOnlyList<string> Errors => Issues.Select(issue => issue.Message).ToArray();

    public static SourceValidationResult Valid { get; } =
        new(true, Array.Empty<SourceValidationIssue>());
}

public sealed class SourceValidator
{
    private const int MaximumParameterBytes = 1024 * 1024;
    private readonly SourceEndpointPolicy _endpointPolicy;

    public SourceValidator(SourceEndpointPolicy endpointPolicy)
    {
        _endpointPolicy = endpointPolicy;
    }

    public SourceValidationResult Validate(VideoTileConfig tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        // Empty disabled tiles are useful while building a wall and must be
        // allowed to exist before the editor has a source to test.
        if (!tile.Enabled)
            return SourceValidationResult.Valid;

        List<SourceValidationIssue> issues = new();
        string host = tile.Host?.Trim() ?? string.Empty;
        string path = tile.Path?.Trim() ?? string.Empty;

        // A blank host represents an intentionally unconfigured tile. The
        // editor can save it, while the test and stream endpoints still
        // report that no source is ready.
        if (string.IsNullOrWhiteSpace(host))
            return SourceValidationResult.Valid;

        if (tile.Protocol is SourceProtocol.Rtp or SourceProtocol.Udp)
        {
            if (!IPAddress.TryParse(host, out IPAddress? address) ||
                address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                issues.Add(new SourceValidationIssue(
                    SourceValidationIssueCodes.DirectIpv4Required,
                    "Direct RTP and UDP sources require an IPv4 address."));
            }
            else if (tile.Transport == StreamTransport.Multicast && !IsMulticast(address))
            {
                issues.Add(new SourceValidationIssue(
                    SourceValidationIssueCodes.MulticastIpv4Required,
                    "Multicast transport requires a multicast IPv4 address."));
            }
        }
        else if (!IsValidHost(host))
        {
            issues.Add(new SourceValidationIssue(
                SourceValidationIssueCodes.HostInvalid,
                "Enter a valid RTSP host name or IP address."));
        }

        if (tile.Port is < 1 or > 65535)
        {
                issues.Add(new SourceValidationIssue(
                    SourceValidationIssueCodes.PortInvalid,
                    $"The source port '{tile.Port}' must be between 1 and 65535.",
                new Dictionary<string, string> { ["port"] = tile.Port.ToString() }));
        }

        if (tile.Protocol == SourceProtocol.Rtsp)
            ValidateRtspPath(path, issues);

        ValidateBase64(tile.H264SpsPpsBase64, "H.264 parameter data",
            SourceValidationIssueCodes.H264ParameterTooLarge,
            SourceValidationIssueCodes.H264ParameterInvalid, issues);
        ValidateBase64(tile.H265VpsSpsPpsBase64, "H.265 parameter data",
            SourceValidationIssueCodes.H265ParameterTooLarge,
            SourceValidationIssueCodes.H265ParameterInvalid, issues);

        return issues.Count == 0
            ? SourceValidationResult.Valid
            : new SourceValidationResult(false, issues);
    }

    public SourceValidationResult ValidateWall(WallConfig wall)
    {
        ArgumentNullException.ThrowIfNull(wall);

        List<SourceValidationIssue> issues = new();
        foreach (VideoTileConfig tile in wall.Tiles ?? new List<VideoTileConfig>())
        {
            SourceValidationResult result = Validate(tile);
            foreach (SourceValidationIssue issue in result.Issues)
            {
                string label = string.IsNullOrWhiteSpace(tile.Title) ? tile.Id : tile.Title.Trim();
                issues.Add(AddTileContext(issue, label));
            }
        }

        return issues.Count == 0
            ? SourceValidationResult.Valid
            : new SourceValidationResult(false, issues);
    }

    public async Task<SourceValidationResult> ValidateAsync(VideoTileConfig tile, CancellationToken token)
    {
        SourceValidationResult syntax = Validate(tile);
        if (!syntax.IsValid)
            return syntax;

        IReadOnlyList<SourceValidationIssue> endpointIssues = await _endpointPolicy.ValidateAsync(tile, token)
            .ConfigureAwait(false);
        return endpointIssues.Count == 0
            ? SourceValidationResult.Valid
            : new SourceValidationResult(false, endpointIssues);
    }

    public async Task<SourceValidationResult> ValidateWallAsync(WallConfig wall, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(wall);

        List<SourceValidationIssue> issues = new();
        foreach (VideoTileConfig tile in wall.Tiles ?? new List<VideoTileConfig>())
        {
            SourceValidationResult result = await ValidateAsync(tile, token).ConfigureAwait(false);
            foreach (SourceValidationIssue issue in result.Issues)
            {
                string label = string.IsNullOrWhiteSpace(tile.Title) ? tile.Id : tile.Title.Trim();
                issues.Add(AddTileContext(issue, label));
            }
        }

        return issues.Count == 0
            ? SourceValidationResult.Valid
            : new SourceValidationResult(false, issues);
    }

    private static bool IsValidHost(string host)
    {
        if (host.Any(char.IsControl) || host.Contains('/') || host.Contains('\\') || host.Contains('@'))
            return false;

        return Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
    }

    private static void ValidateRtspPath(string path, ICollection<SourceValidationIssue> issues)
    {
        if (path.Any(char.IsControl))
        {
            issues.Add(new SourceValidationIssue(
                SourceValidationIssueCodes.PathControlCharacters,
                "The stream path cannot contain control characters."));
            return;
        }

        if (string.IsNullOrWhiteSpace(path) || !Uri.TryCreate(path, UriKind.Absolute, out Uri? absolute))
            return;

        if (absolute.Scheme is not ("rtsp" or "http"))
        {
            issues.Add(new SourceValidationIssue(
                SourceValidationIssueCodes.PathSchemeInvalid,
                "An absolute stream URL must use rtsp:// or http://.",
                new Dictionary<string, string> { ["scheme"] = absolute.Scheme }));
        }
    }

    private static void ValidateBase64(string? value, string label, string tooLargeCode,
        string invalidCode, ICollection<SourceValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        try
        {
            byte[] bytes = Convert.FromBase64String(value.Trim());
            if (bytes.Length > MaximumParameterBytes)
            {
                issues.Add(new SourceValidationIssue(
                    tooLargeCode,
                    $"{label} is larger than the 1 MB limit.",
                    new Dictionary<string, string> { ["label"] = label }));
            }
        }
        catch (FormatException)
        {
            issues.Add(new SourceValidationIssue(
                invalidCode,
                $"{label} must be valid base64 data.",
                new Dictionary<string, string> { ["label"] = label }));
        }
    }

    private static SourceValidationIssue AddTileContext(SourceValidationIssue issue, string label)
    {
        Dictionary<string, string> values = issue.Values == null
            ? new(StringComparer.Ordinal)
            : new(issue.Values, StringComparer.Ordinal);
        values["tile"] = label;
        return issue with
        {
            Message = $"{label}: {issue.Message}",
            Values = values
        };
    }

    private static bool IsMulticast(IPAddress address)
    {
        byte first = address.GetAddressBytes()[0];
        return first is >= 224 and <= 239;
    }
}
