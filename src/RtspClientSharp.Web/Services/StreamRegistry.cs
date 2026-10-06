using System.Collections.Concurrent;
using RtspClientSharp.Web.Models;

namespace RtspClientSharp.Web.Services;

public sealed class StreamRegistry : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, StreamSession> _sessions = new(StringComparer.Ordinal);
    private readonly ILoggerFactory _loggerFactory;
    private readonly int _maximumActiveSessions;
    private readonly int _maximumViewersPerSession;
    private readonly object _sync = new();

    public StreamRegistry(ILoggerFactory loggerFactory, StreamWallOptions options)
    {
        _loggerFactory = loggerFactory;
        _maximumActiveSessions = options.MaxActiveSessions;
        _maximumViewersPerSession = options.MaxViewersPerSession;
    }

    public ValueTask<StreamLease> AcquireAsync(VideoTileConfig tile)
    {
        string key = StreamSession.CreateSourceKey(tile);
        lock (_sync)
        {
            if (!_sessions.TryGetValue(key, out StreamSession? session))
            {
                if (_sessions.Count >= _maximumActiveSessions)
                    throw new StreamCapacityException(
                        $"The server has reached its limit of {_maximumActiveSessions} active sources.");

                session = new StreamSession(tile, _loggerFactory.CreateLogger<StreamSession>());
                _sessions[key] = session;
            }

            if (!session.TryAddViewer(_maximumViewersPerSession))
            {
                if (session.ViewerCount == 0)
                    _sessions.TryRemove(key, out _);

                throw new StreamCapacityException(
                    $"The source has reached its limit of {_maximumViewersPerSession} viewers.");
            }

            return ValueTask.FromResult(new StreamLease(this, key, session));
        }
    }

    public StreamDiagnosticsResponse GetDiagnostics()
    {
        StreamSession[] sessions = _sessions.Values.ToArray();
        return new StreamDiagnosticsResponse(
            sessions.Length,
            sessions.Sum(session => session.ViewerCount),
            _maximumActiveSessions,
            _maximumViewersPerSession);
    }

    public TileStatusResponse GetStatus(string tileId, VideoTileConfig tile)
    {
        string key = StreamSession.CreateSourceKey(tile);
        return _sessions.TryGetValue(key, out StreamSession? session)
            ? session.GetStatus(tileId)
            : new TileStatusResponse { TileId = tileId };
    }

    public async ValueTask DisposeAsync()
    {
        StreamSession[] sessions;
        lock (_sync)
        {
            sessions = _sessions.Values.ToArray();
            _sessions.Clear();
        }

        foreach (StreamSession session in sessions)
            await session.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask ReleaseAsync(string key, StreamSession session)
    {
        bool dispose;
        lock (_sync)
        {
            session.RemoveViewer();
            dispose = session.ViewerCount == 0 &&
                      _sessions.TryGetValue(key, out StreamSession? current) &&
                      ReferenceEquals(current, session);
            if (dispose)
                _sessions.TryRemove(key, out _);
        }

        if (dispose)
            await session.DisposeAsync().ConfigureAwait(false);
    }

    public sealed class StreamLease : IAsyncDisposable
    {
        private readonly StreamRegistry _registry;
        private readonly string _key;
        private readonly StreamSession _session;
        private int _disposed;

        internal StreamLease(StreamRegistry registry, string key, StreamSession session)
        {
            _registry = registry;
            _key = key;
            _session = session;
        }

        public StreamSession Session => _session;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return ValueTask.CompletedTask;

            return _registry.ReleaseAsync(_key, _session);
        }
    }
}

public sealed class StreamCapacityException : Exception
{
    public StreamCapacityException(string message)
        : base(message)
    {
    }
}
