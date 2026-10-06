using System.Net;
using System.Net.Sockets;
using RtspClientSharp.Web.Models;

namespace RtspClientSharp.Web.Services;

public sealed class SourceEndpointPolicy
{
    private readonly StreamWallOptions _options;
    private readonly IReadOnlyList<CidrNetwork> _allowedNetworks;

    public SourceEndpointPolicy(StreamWallOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _allowedNetworks = options.AllowedSourceNetworks
            .Select(CidrNetwork.TryParse)
            .Where(network => network != null)
            .Select(network => network!)
            .ToList();
    }

    public async Task<IReadOnlyList<SourceValidationIssue>> ValidateAsync(VideoTileConfig tile,
        CancellationToken token)
    {
        if (!_options.EnforceSourceNetworkPolicy || !tile.HasSource)
            return Array.Empty<SourceValidationIssue>();

        string host = GetSourceHost(tile);
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out IPAddress? literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, token).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                return new[] { Issue(SourceValidationIssueCodes.HostUnresolved,
                    $"The source host '{host}' could not be resolved under the source network policy.",
                    ("host", host)) };
            }
        }

        if (addresses.Length == 0)
            return new[] { Issue(SourceValidationIssueCodes.HostNoAddress,
                $"The source host '{host}' did not resolve to an address.",
                ("host", host)) };

        List<SourceValidationIssue> issues = new();
        foreach (IPAddress address in addresses)
        {
            if (IPAddress.IsLoopback(address) && !_options.AllowLoopbackSources)
                issues.Add(Issue(SourceValidationIssueCodes.LoopbackBlocked,
                    $"The source address '{address}' is loopback and is blocked by the source network policy.",
                    ("address", address.ToString())));

            if (IsLinkLocal(address) && !_options.AllowLinkLocalSources)
                issues.Add(Issue(SourceValidationIssueCodes.LinkLocalBlocked,
                    $"The source address '{address}' is link-local and is blocked by the source network policy.",
                    ("address", address.ToString())));

            if (_allowedNetworks.Count > 0 && !_allowedNetworks.Any(network => network.Contains(address)))
            {
                issues.Add(Issue(SourceValidationIssueCodes.NetworkBlocked,
                    $"The source address '{address}' is outside the configured source networks.",
                    ("address", address.ToString())));
            }
        }

        return issues.GroupBy(issue => issue.Message, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static SourceValidationIssue Issue(string code, string message,
        params (string Key, string Value)[] values)
    {
        return new SourceValidationIssue(
            code,
            message,
            values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal));
    }

    private static string GetSourceHost(VideoTileConfig tile)
    {
        string path = tile.Path?.Trim() ?? string.Empty;
        if (tile.Protocol == SourceProtocol.Rtsp &&
            Uri.TryCreate(path, UriKind.Absolute, out Uri? absolute) &&
            absolute.Scheme is "rtsp" or "http")
        {
            return absolute.DnsSafeHost;
        }

        return tile.Host.Trim();
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        if (address.IsIPv6LinkLocal)
            return true;

        byte[] bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    private sealed class CidrNetwork
    {
        private readonly byte[] _network;
        private readonly int _prefixLength;

        private CidrNetwork(byte[] network, int prefixLength)
        {
            _network = network;
            _prefixLength = prefixLength;
        }

        public static CidrNetwork? TryParse(string value)
        {
            string[] parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
            if (!IPAddress.TryParse(parts[0], out IPAddress? address))
                return null;

            int maximum = address.GetAddressBytes().Length * 8;
            int prefix = parts.Length == 1
                ? maximum
                : int.TryParse(parts[1], out int parsed) ? parsed : -1;
            if (prefix < 0 || prefix > maximum)
                return null;

            byte[] network = address.GetAddressBytes();
            int fullBytes = prefix / 8;
            int remainingBits = prefix % 8;
            if (remainingBits > 0 && fullBytes < network.Length)
                network[fullBytes] &= (byte)(0xff << (8 - remainingBits));

            for (int index = fullBytes + (remainingBits > 0 ? 1 : 0); index < network.Length; index++)
                network[index] = 0;

            return new CidrNetwork(network, prefix);
        }

        public bool Contains(IPAddress address)
        {
            byte[] candidate = address.GetAddressBytes();
            if (candidate.Length != _network.Length)
                return false;

            int fullBytes = _prefixLength / 8;
            for (int index = 0; index < fullBytes; index++)
            {
                if (candidate[index] != _network[index])
                    return false;
            }

            int remainingBits = _prefixLength % 8;
            return remainingBits == 0 ||
                   (candidate[fullBytes] & (byte)(0xff << (8 - remainingBits))) == _network[fullBytes];
        }
    }
}
