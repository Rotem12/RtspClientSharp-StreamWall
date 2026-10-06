using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace RtspClientSharp.Web.Services;

public sealed class SourceCredentialProtector
{
    private const string Prefix = "dp1:";
    private readonly IDataProtector _protector;

    public SourceCredentialProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector("RtspClientSharp.Web.SourceCredentials.v1");
    }

    public bool IsProtected(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.StartsWith(Prefix, StringComparison.Ordinal);
    }

    public string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) || IsProtected(value))
            return value ?? string.Empty;

        return Prefix + _protector.Protect(value);
    }

    public string Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !IsProtected(value))
            return value ?? string.Empty;

        try
        {
            return _protector.Unprotect(value[Prefix.Length..]);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            throw new InvalidOperationException(
                "A stored source credential could not be decrypted. Preserve the configured data-protection keys before restoring the wall data.",
                exception);
        }
    }
}
