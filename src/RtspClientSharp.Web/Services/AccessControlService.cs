using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace RtspClientSharp.Web.Services;

public sealed class AccessControlService
{
    private const string CookieName = "stream-wall-access";
    private static readonly TimeSpan GrantLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    private const int MaximumFailuresPerWindow = 5;

    private readonly string _tokenHash;
    private readonly ConcurrentDictionary<string, LoginFailures> _failures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _grants = new(StringComparer.Ordinal);

    public AccessControlService(StreamWallOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _tokenHash = Hash(options.AccessToken);
        Enabled = !string.IsNullOrWhiteSpace(options.AccessToken);
    }

    public bool Enabled { get; }

    public bool IsAuthenticated(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!Enabled)
            return true;

        if (!context.Request.Cookies.TryGetValue(CookieName, out string? token) ||
            !_grants.TryGetValue(token, out DateTimeOffset expiresAt))
            return false;

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            _grants.TryRemove(token, out _);
            return false;
        }

        return true;
    }

    public bool TryAuthenticate(HttpContext context, string? token, out TimeSpan? retryAfter)
    {
        ArgumentNullException.ThrowIfNull(context);
        retryAfter = null;

        if (!Enabled)
            return true;

        string key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (_failures.TryGetValue(key, out LoginFailures? previous))
        {
            if (DateTimeOffset.UtcNow - previous.WindowStarted < FailureWindow &&
                previous.Count >= MaximumFailuresPerWindow)
            {
                retryAfter = previous.WindowStarted.Add(FailureWindow) - DateTimeOffset.UtcNow;
                return false;
            }

            if (DateTimeOffset.UtcNow - previous.WindowStarted >= FailureWindow)
                _failures.TryRemove(key, out _);
        }

        if (FixedTimeEquals(_tokenHash, token))
        {
            _failures.TryRemove(key, out _);
            Grant(context);
            return true;
        }

        _failures.AddOrUpdate(key,
            _ => new LoginFailures(DateTimeOffset.UtcNow, 1),
            (_, current) => DateTimeOffset.UtcNow - current.WindowStarted >= FailureWindow
                ? new LoginFailures(DateTimeOffset.UtcNow, 1)
                : current with { Count = current.Count + 1 });
        return false;
    }

    public void Revoke(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Request.Cookies.TryGetValue(CookieName, out string? token))
            _grants.TryRemove(token, out _);

        context.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Path = "/"
        });
    }

    public bool RequiresAuthentication(PathString path)
    {
        if (!Enabled)
            return false;

        return path.StartsWithSegments("/api/walls") ||
               path.StartsWithSegments("/api/streams") ||
               path.StartsWithSegments("/api/diagnostics");
    }

    private static string Hash(string? value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));
    }

    private static bool FixedTimeEquals(string expectedHash, string? token)
    {
        byte[] expected = Convert.FromHexString(expectedHash);
        byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private void Grant(HttpContext context)
    {
        string sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _grants[sessionToken] = DateTimeOffset.UtcNow.Add(GrantLifetime);
        context.Response.Cookies.Append(CookieName, sessionToken, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Path = "/",
            MaxAge = GrantLifetime
        });
    }

    private sealed record LoginFailures(DateTimeOffset WindowStarted, int Count);
}
