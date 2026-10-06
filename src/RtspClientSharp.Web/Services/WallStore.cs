using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RtspClientSharp.Web.Models;

namespace RtspClientSharp.Web.Services;

public sealed class WallStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _filePath;
    private readonly string _backupDirectory;
    private readonly int _backupRetentionCount;
    private readonly ILogger<WallStore> _logger;
    private readonly SourceCredentialProtector _credentialProtector;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _credentialMigrationNeeded;

    public WallStore(IHostEnvironment environment, StreamWallOptions options, ILogger<WallStore> logger,
        SourceCredentialProtector credentialProtector)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _credentialProtector = credentialProtector ?? throw new ArgumentNullException(nameof(credentialProtector));

        string dataDirectory = ResolveDirectory(environment.ContentRootPath, options.DataDirectory);
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "walls.json");
        _backupDirectory = ResolveDirectory(environment.ContentRootPath, options.BackupDirectory);
        Directory.CreateDirectory(_backupDirectory);
        _backupRetentionCount = options.BackupRetentionCount;
    }

    public async Task<WallBackupInfo?> CreateBackupAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await BackupCurrentUnsafeAsync(token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<WallBackupInfo>> ListBackupsAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return ListBackupsUnsafe();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(string FileName, byte[] Content)?> ReadBackupAsync(string fileName,
        CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            string? path = ResolveBackupPath(fileName);
            if (path == null)
                return null;

            byte[] content = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
            return (Path.GetFileName(path), content);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<WallConfig>> GetAllAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<WallConfig> walls = await ReadUnsafeAsync(token).ConfigureAwait(false);
            if (walls.Count == 0)
            {
                walls.Add(WallConfig.CreateDefault());
                await WriteUnsafeAsync(walls, token).ConfigureAwait(false);
            }

            foreach (WallConfig wall in walls)
                wall.Normalize();

            if (_credentialMigrationNeeded)
            {
                await WriteUnsafeAsync(walls, token, createBackup: false).ConfigureAwait(false);
                _credentialMigrationNeeded = false;
            }

            return walls;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WallConfig?> GetAsync(string id, CancellationToken token = default)
    {
        IReadOnlyList<WallConfig> walls = await GetAllAsync(token).ConfigureAwait(false);
        return walls.FirstOrDefault(wall => string.Equals(wall.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<WallConfig> UpdateAsync(WallConfig incoming, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        incoming.Normalize();

        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<WallConfig> walls = await ReadUnsafeAsync(token).ConfigureAwait(false);
            WallConfig? existing = walls.FirstOrDefault(wall =>
                string.Equals(wall.Id, incoming.Id, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                incoming.EditorPinHash = string.Empty;
                walls.Add(incoming);
            }
            else
            {
                existing.Normalize();
                incoming.EditorPinHash = existing.EditorPinHash;
                if (incoming.Presets.Count == 0 && existing.Presets.Count > 0)
                    incoming.Presets = existing.Presets;

                foreach (VideoTileConfig tile in incoming.Tiles)
                {
                    VideoTileConfig? oldTile = existing.Tiles.FirstOrDefault(old =>
                        string.Equals(old.Id, tile.Id, StringComparison.OrdinalIgnoreCase));
                    if (oldTile != null && string.IsNullOrEmpty(tile.Password))
                        tile.Password = oldTile.Password;
                }

                int index = walls.IndexOf(existing);
                walls[index] = incoming;
            }

            await WriteUnsafeAsync(walls, token).ConfigureAwait(false);
            return incoming;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LayoutPresetConfig?> SavePresetAsync(string wallId, string name,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A preset name is required.", nameof(name));

        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<WallConfig> walls = await ReadUnsafeAsync(token).ConfigureAwait(false);
            WallConfig? wall = walls.FirstOrDefault(item =>
                string.Equals(item.Id, wallId, StringComparison.OrdinalIgnoreCase));
            if (wall == null)
                return null;

            wall.Normalize();
            LayoutPresetConfig? existing = wall.Presets.FirstOrDefault(preset =>
                string.Equals(preset.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            LayoutPresetConfig captured = LayoutPresetConfig.Capture(wall, name.Trim());
            if (existing == null)
                wall.Presets.Add(captured);
            else
            {
                captured.Id = existing.Id;
                int index = wall.Presets.IndexOf(existing);
                wall.Presets[index] = captured;
            }

            await WriteUnsafeAsync(walls, token).ConfigureAwait(false);
            return captured;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WallConfig?> ApplyPresetAsync(string wallId, string presetId,
        CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<WallConfig> walls = await ReadUnsafeAsync(token).ConfigureAwait(false);
            WallConfig? wall = walls.FirstOrDefault(item =>
                string.Equals(item.Id, wallId, StringComparison.OrdinalIgnoreCase));
            LayoutPresetConfig? preset = wall?.Presets.FirstOrDefault(item =>
                string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));
            if (wall == null || preset == null)
                return null;

            wall.LayoutPreset = "custom";
            wall.GridColumns = preset.GridColumns;
            wall.GridRows = preset.GridRows;
            foreach (VideoTileConfig tile in wall.Tiles)
            {
                LayoutTilePlacement? placement = preset.Placements.FirstOrDefault(item =>
                    string.Equals(item.TileId, tile.Id, StringComparison.OrdinalIgnoreCase));
                if (placement == null)
                    continue;

                tile.Column = placement.Column;
                tile.Row = placement.Row;
                tile.ColumnSpan = placement.ColumnSpan;
                tile.RowSpan = placement.RowSpan;
            }

            wall.Normalize();
            await WriteUnsafeAsync(walls, token).ConfigureAwait(false);
            return wall;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeletePresetAsync(string wallId, string presetId,
        CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<WallConfig> walls = await ReadUnsafeAsync(token).ConfigureAwait(false);
            WallConfig? wall = walls.FirstOrDefault(item =>
                string.Equals(item.Id, wallId, StringComparison.OrdinalIgnoreCase));
            LayoutPresetConfig? preset = wall?.Presets.FirstOrDefault(item =>
                string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));
            if (wall == null || preset == null)
                return false;

            wall.Presets.Remove(preset);
            await WriteUnsafeAsync(walls, token).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WallConfig?> ImportPresetsAsync(string wallId, PresetImportRequest request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string conflictMode = request.ConflictMode?.Trim().ToLowerInvariant() ?? string.Empty;
        if (conflictMode is not ("rename" or "replace"))
            throw new ArgumentException("Conflict mode must be rename or replace.", nameof(request));
        if (request.Presets is null || request.Presets.Count > 50)
            throw new ArgumentException("Import up to 50 saved layouts at a time.", nameof(request));

        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<WallConfig> walls = await ReadUnsafeAsync(token).ConfigureAwait(false);
            WallConfig? wall = walls.FirstOrDefault(item =>
                string.Equals(item.Id, wallId, StringComparison.OrdinalIgnoreCase));
            if (wall == null)
                return null;

            wall.Normalize();
            foreach (LayoutPresetConfig imported in request.Presets)
            {
                if (string.IsNullOrWhiteSpace(imported.Name))
                    throw new ArgumentException("Every imported layout needs a name.", nameof(request));

                LayoutPresetConfig candidate = new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = imported.Name.Trim(),
                    GridColumns = imported.GridColumns,
                    GridRows = imported.GridRows,
                    Placements = (imported.Placements ?? new List<LayoutTilePlacement>())
                        .Select(placement => new LayoutTilePlacement
                        {
                            TileId = placement.TileId,
                            Column = placement.Column,
                            Row = placement.Row,
                            ColumnSpan = placement.ColumnSpan,
                            RowSpan = placement.RowSpan
                        })
                        .ToList()
                };
                candidate.Normalize(wall.Tiles);
                HashSet<string> tileIds = wall.Tiles
                    .Select(tile => tile.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                candidate.Placements = candidate.Placements
                    .Where(placement => tileIds.Contains(placement.TileId))
                    .ToList();

                LayoutPresetConfig? existing = wall.Presets.FirstOrDefault(preset =>
                    string.Equals(preset.Name, candidate.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null && conflictMode == "replace")
                {
                    candidate.Id = existing.Id;
                    wall.Presets[wall.Presets.IndexOf(existing)] = candidate;
                }
                else
                {
                    candidate.Name = GetUniquePresetName(wall.Presets, candidate.Name);
                    wall.Presets.Add(candidate);
                }
            }

            if (wall.Presets.Count > 50)
                throw new ArgumentException("A wall can contain up to 50 saved layouts.", nameof(request));

            await WriteUnsafeAsync(walls, token).ConfigureAwait(false);
            return wall;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WallConfig?> SetEditorPinAsync(string wallId, string? pin,
        CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<WallConfig> walls = await ReadUnsafeAsync(token).ConfigureAwait(false);
            WallConfig? wall = walls.FirstOrDefault(item =>
                string.Equals(item.Id, wallId, StringComparison.OrdinalIgnoreCase));
            if (wall == null)
                return null;

            wall.Normalize();
            wall.EditorPinHash = string.IsNullOrWhiteSpace(pin)
                ? string.Empty
                : EditorAccessService.HashPin(pin.Trim());
            await WriteUnsafeAsync(walls, token).ConfigureAwait(false);
            return wall;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<VideoTileConfig?> AddTileAsync(string wallId, VideoTileConfig tile,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(tile);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<WallConfig> walls = await ReadUnsafeAsync(token).ConfigureAwait(false);
            WallConfig? wall = walls.FirstOrDefault(item =>
                string.Equals(item.Id, wallId, StringComparison.OrdinalIgnoreCase));
            if (wall == null)
                return null;

            tile.Id = string.IsNullOrWhiteSpace(tile.Id) ? Guid.NewGuid().ToString("N") : tile.Id;
            tile.Normalize(wall.GridColumns, wall.GridRows);
            wall.Tiles.Add(tile);
            await WriteUnsafeAsync(walls, token).ConfigureAwait(false);
            return tile;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<VideoTileConfig?> UpdateTileAsync(string wallId, string tileId,
        VideoTileConfig incoming, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<WallConfig> walls = await ReadUnsafeAsync(token).ConfigureAwait(false);
            WallConfig? wall = walls.FirstOrDefault(item =>
                string.Equals(item.Id, wallId, StringComparison.OrdinalIgnoreCase));
            VideoTileConfig? existing = wall?.Tiles.FirstOrDefault(item =>
                string.Equals(item.Id, tileId, StringComparison.OrdinalIgnoreCase));
            if (wall == null || existing == null)
                return null;

            incoming.Id = existing.Id;
            if (string.IsNullOrEmpty(incoming.Password))
                incoming.Password = existing.Password;
            incoming.Normalize(wall.GridColumns, wall.GridRows);
            int index = wall.Tiles.IndexOf(existing);
            wall.Tiles[index] = incoming;
            await WriteUnsafeAsync(walls, token).ConfigureAwait(false);
            return incoming;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteTileAsync(string wallId, string tileId,
        CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<WallConfig> walls = await ReadUnsafeAsync(token).ConfigureAwait(false);
            WallConfig? wall = walls.FirstOrDefault(item =>
                string.Equals(item.Id, wallId, StringComparison.OrdinalIgnoreCase));
            VideoTileConfig? tile = wall?.Tiles.FirstOrDefault(item =>
                string.Equals(item.Id, tileId, StringComparison.OrdinalIgnoreCase));
            if (wall == null || tile == null)
                return false;

            wall.Tiles.Remove(tile);
            await WriteUnsafeAsync(walls, token).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<VideoTileConfig?> UpdateTilePlacementAsync(string wallId, string tileId,
        TilePlacementRequest placement, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(placement);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<WallConfig> walls = await ReadUnsafeAsync(token).ConfigureAwait(false);
            WallConfig? wall = walls.FirstOrDefault(item =>
                string.Equals(item.Id, wallId, StringComparison.OrdinalIgnoreCase));
            VideoTileConfig? tile = wall?.Tiles.FirstOrDefault(item =>
                string.Equals(item.Id, tileId, StringComparison.OrdinalIgnoreCase));
            if (wall == null || tile == null)
                return null;

            var updates = new List<(VideoTileConfig Tile, int Column, int Row, int ColumnSpan, int RowSpan)>
            {
                (tile, placement.Column, placement.Row, placement.ColumnSpan, placement.RowSpan)
            };
            var updateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tile.Id };
            foreach (TilePlacementChange change in placement.AdditionalPlacements ?? new List<TilePlacementChange>())
            {
                if (string.IsNullOrWhiteSpace(change.TileId) || !updateIds.Add(change.TileId))
                    return null;

                VideoTileConfig? additionalTile = wall.Tiles.FirstOrDefault(item =>
                    string.Equals(item.Id, change.TileId, StringComparison.OrdinalIgnoreCase));
                if (additionalTile == null)
                    return null;

                updates.Add((additionalTile, change.Column, change.Row, change.ColumnSpan, change.RowSpan));
            }

            foreach ((VideoTileConfig updatedTile, int column, int row, int columnSpan, int rowSpan) in updates)
            {
                updatedTile.Column = column;
                updatedTile.Row = row;
                updatedTile.ColumnSpan = columnSpan;
                updatedTile.RowSpan = rowSpan;
                updatedTile.Normalize(wall.GridColumns, wall.GridRows);
            }

            await WriteUnsafeAsync(walls, token).ConfigureAwait(false);
            return tile;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<VideoTileConfig?> UpdateTileGeometryAsync(string wallId, string tileId,
        TileGeometryRequest geometry, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<WallConfig> walls = await ReadUnsafeAsync(token).ConfigureAwait(false);
            WallConfig? wall = walls.FirstOrDefault(item =>
                string.Equals(item.Id, wallId, StringComparison.OrdinalIgnoreCase));
            VideoTileConfig? tile = wall?.Tiles.FirstOrDefault(item =>
                string.Equals(item.Id, tileId, StringComparison.OrdinalIgnoreCase));
            if (wall == null || tile == null)
                return null;

            var updates = new List<(VideoTileConfig Tile, TileSizeUnit SizeUnit, double X, double Y, double Width, double Height)>
            {
                (tile, geometry.SizeUnit, geometry.X, geometry.Y, geometry.Width, geometry.Height)
            };
            var updateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tile.Id };
            foreach (TileGeometryChange change in geometry.AdditionalGeometries ?? new List<TileGeometryChange>())
            {
                if (string.IsNullOrWhiteSpace(change.TileId) || !updateIds.Add(change.TileId))
                    return null;

                VideoTileConfig? additionalTile = wall.Tiles.FirstOrDefault(item =>
                    string.Equals(item.Id, change.TileId, StringComparison.OrdinalIgnoreCase));
                if (additionalTile == null)
                    return null;

                updates.Add((additionalTile, change.SizeUnit, change.X, change.Y, change.Width, change.Height));
            }

            foreach ((VideoTileConfig updatedTile, TileSizeUnit sizeUnit, double x, double y, double width, double height) in updates)
            {
                updatedTile.SizeUnit = sizeUnit;
                updatedTile.X = x;
                updatedTile.Y = y;
                updatedTile.Width = width;
                updatedTile.Height = height;
                updatedTile.Normalize(wall.GridColumns, wall.GridRows);
            }

            await WriteUnsafeAsync(walls, token).ConfigureAwait(false);
            return tile;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<WallConfig>> ReadUnsafeAsync(CancellationToken token)
    {
        if (!File.Exists(_filePath))
            return new List<WallConfig>();

        await using FileStream stream = File.OpenRead(_filePath);
        List<WallConfig> walls = await JsonSerializer.DeserializeAsync<List<WallConfig>>(stream, JsonOptions, token)
                   .ConfigureAwait(false) ?? new List<WallConfig>();
        foreach (WallConfig wall in walls)
        {
            foreach (VideoTileConfig tile in wall.Tiles ?? new List<VideoTileConfig>())
            {
                if (string.IsNullOrEmpty(tile.Password))
                    continue;

                if (_credentialProtector.IsProtected(tile.Password))
                    tile.Password = _credentialProtector.Unprotect(tile.Password);
                else
                    _credentialMigrationNeeded = true;
            }
        }

        return walls;
    }

    private async Task WriteUnsafeAsync(List<WallConfig> walls, CancellationToken token, bool createBackup = true)
    {
        List<(VideoTileConfig Tile, string Password)> plaintextCredentials = new();
        foreach (WallConfig wall in walls)
        {
            foreach (VideoTileConfig tile in wall.Tiles ?? new List<VideoTileConfig>())
            {
                if (string.IsNullOrEmpty(tile.Password))
                    continue;

                plaintextCredentials.Add((tile, tile.Password));
                tile.Password = _credentialProtector.Protect(tile.Password);
            }
        }

        try
        {
            if (createBackup)
                await BackupCurrentUnsafeAsync(token).ConfigureAwait(false);

            string temporaryPath = _filePath + ".tmp";
            await using (FileStream stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, walls, JsonOptions, token).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            foreach ((VideoTileConfig tile, string password) in plaintextCredentials)
                tile.Password = password;
        }
    }

    private async Task<WallBackupInfo?> BackupCurrentUnsafeAsync(CancellationToken token)
    {
        if (!File.Exists(_filePath))
            return null;

        token.ThrowIfCancellationRequested();
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        string fileName = $"walls-{timestamp}.json";
        string backupPath = Path.Combine(_backupDirectory, fileName);
        if (File.Exists(backupPath))
            fileName = $"walls-{timestamp}-{Guid.NewGuid():N}.json";

        try
        {
            File.Copy(_filePath, Path.Combine(_backupDirectory, fileName), false);
            PruneBackupsUnsafe();
            FileInfo info = new(Path.Combine(_backupDirectory, fileName));
            return new WallBackupInfo(fileName, new DateTimeOffset(info.LastWriteTimeUtc), info.Length);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Could not create a wall configuration backup.");
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Could not create a wall configuration backup because access was denied.");
            return null;
        }
    }

    private IReadOnlyList<WallBackupInfo> ListBackupsUnsafe()
    {
        if (!Directory.Exists(_backupDirectory))
            return Array.Empty<WallBackupInfo>();

        return Directory.EnumerateFiles(_backupDirectory, "walls-*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => new WallBackupInfo(
                info.Name,
                new DateTimeOffset(info.LastWriteTimeUtc),
                info.Length))
            .ToList();
    }

    private void PruneBackupsUnsafe()
    {
        foreach (FileInfo info in Directory.EnumerateFiles(_backupDirectory, "walls-*.json", SearchOption.TopDirectoryOnly)
                     .Select(path => new FileInfo(path))
                     .OrderByDescending(item => item.LastWriteTimeUtc)
                     .Skip(_backupRetentionCount))
        {
            try
            {
                info.Delete();
            }
            catch (IOException exception)
            {
                _logger.LogWarning(exception, "Could not prune old wall backup {BackupFile}.", info.Name);
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.LogWarning(exception, "Could not prune old wall backup {BackupFile} because access was denied.", info.Name);
            }
        }
    }

    private string? ResolveBackupPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
            !fileName.StartsWith("walls-", StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return null;

        string path = Path.Combine(_backupDirectory, fileName);
        return File.Exists(path) ? path : null;
    }

    private static string ResolveDirectory(string contentRoot, string configuredPath)
    {
        return Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(contentRoot, configuredPath));
    }

    private static string GetUniquePresetName(IReadOnlyCollection<LayoutPresetConfig> presets, string name)
    {
        string baseName = name.Length > 64 ? name[..64].TrimEnd() : name;
        string candidate = baseName;
        int suffix = 2;
        while (presets.Any(preset => string.Equals(preset.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            string suffixText = $" ({suffix++})";
            int baseLength = Math.Max(1, 64 - suffixText.Length);
            candidate = $"{baseName[..Math.Min(baseName.Length, baseLength)].TrimEnd()}{suffixText}";
        }

        return candidate;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
