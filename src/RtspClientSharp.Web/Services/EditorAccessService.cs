using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using RtspClientSharp.Web.Models;

namespace RtspClientSharp.Web.Services;

public sealed class EditorAccessService
{
    private const string CookieName = "stream-wall-editor";
    private static readonly TimeSpan GrantLifetime = TimeSpan.FromHours(8);
    private readonly ConcurrentDictionary<string, EditorGrant> _grants = new(StringComparer.Ordinal);

    public bool IsEditor(HttpContext context, WallConfig wall)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(wall);

        if (!wall.HasEditorPin)
            return true;

        if (!context.Request.Cookies.TryGetValue(CookieName, out string? token) ||
            !_grants.TryGetValue(token, out EditorGrant? grant))
            return false;

        if (grant.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _grants.TryRemove(token, out _);
            return false;
        }

        return string.Equals(grant.WallId, wall.Id, StringComparison.OrdinalIgnoreCase);
    }

    public bool ValidatePin(WallConfig wall, string? pin)
    {
        if (!wall.HasEditorPin || string.IsNullOrEmpty(pin))
            return false;

        try
        {
            byte[] expected = Convert.FromBase64String(wall.EditorPinHash);
            byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(pin));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public void Grant(HttpContext context, string wallId)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.Add(GrantLifetime);
        _grants[token] = new EditorGrant(wallId, expiresAt);
        context.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Path = "/",
            MaxAge = GrantLifetime
        });
    }

    public void Revoke(HttpContext context)
    {
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

    public void RevokeWall(string wallId)
    {
        foreach ((string token, EditorGrant grant) in _grants)
        {
            if (string.Equals(grant.WallId, wallId, StringComparison.OrdinalIgnoreCase))
                _grants.TryRemove(token, out _);
        }
    }

    public static string HashPin(string pin)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(pin)));
    }

    private sealed record EditorGrant(string WallId, DateTimeOffset ExpiresAt);
}
