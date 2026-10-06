namespace RtspClientSharp.Web.Models;

public static class SourceValidationIssueCodes
{
    public const string DirectIpv4Required = "sourceDirectIpv4Required";
    public const string MulticastIpv4Required = "sourceMulticastIpv4Required";
    public const string HostInvalid = "sourceHostInvalid";
    public const string PortInvalid = "sourcePortInvalid";
    public const string PathControlCharacters = "sourcePathControlCharacters";
    public const string PathSchemeInvalid = "sourcePathSchemeInvalid";
    public const string H264ParameterTooLarge = "sourceH264ParameterTooLarge";
    public const string H264ParameterInvalid = "sourceH264ParameterInvalid";
    public const string H265ParameterTooLarge = "sourceH265ParameterTooLarge";
    public const string H265ParameterInvalid = "sourceH265ParameterInvalid";
    public const string HostUnresolved = "sourceHostUnresolved";
    public const string HostNoAddress = "sourceHostNoAddress";
    public const string LoopbackBlocked = "sourceLoopbackBlocked";
    public const string LinkLocalBlocked = "sourceLinkLocalBlocked";
    public const string NetworkBlocked = "sourceNetworkBlocked";
}

public sealed record SourceValidationIssue(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string>? Values = null);
