namespace RtspClientSharp.Web.Models;

public sealed class AccessLoginRequest
{
    public string Token { get; init; } = string.Empty;
}

public sealed class AccessSessionResponse
{
    public bool Enabled { get; init; }
    public bool Authenticated { get; init; }
    public bool ReadOnly { get; init; }
}

public sealed record StreamDiagnosticsResponse(
    int ActiveSessions,
    int ActiveViewers,
    int MaximumActiveSessions,
    int MaximumViewersPerSession);

public sealed record WallBackupInfo(string FileName, DateTimeOffset CreatedAt, long Size);
