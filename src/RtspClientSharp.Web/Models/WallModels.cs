using System.Text.Json.Serialization;

namespace RtspClientSharp.Web.Models;

public enum SourceProtocol
{
    Rtsp,
    Rtp,
    Udp
}

public enum StreamTransport
{
    Auto,
    Tcp,
    Udp,
    Multicast,
    Rtp,
    MpegTs
}

public enum StreamCodec
{
    Auto,
    H264,
    H265,
    Mjpeg
}

public enum AspectMode
{
    RespectSource,
    Contain,
    Cover,
    Stretch
}

public enum TitleBackgroundMode
{
    BorderColor,
    CustomColor,
    Transparent
}

public enum TileSizeUnit
{
    Pixels,
    Percent
}

public sealed class WallConfig
{
    public string Id { get; set; } = "default";
    public string Name { get; set; } = "Warehouse overview";
    public string LayoutPreset { get; set; } = "2x2";
    public int GridColumns { get; set; } = 2;
    public int GridRows { get; set; } = 2;
    public int TileGap { get; set; } = 5;
    public string BackgroundColor { get; set; } = "#0d1114";
    public string ForegroundColor { get; set; } = "#edf3f4";
    public string PanelColor { get; set; } = "#151a1f";
    public string AccentColor { get; set; } = "#e7a346";
    public string Locale { get; set; } = "en";
    // The hash is persisted server-side and is never included in WallResponse.
    public string EditorPinHash { get; set; } = string.Empty;
    public List<VideoTileConfig> Tiles { get; set; } = new();
    public List<LayoutPresetConfig> Presets { get; set; } = new();

    [JsonIgnore]
    public bool HasEditorPin => !string.IsNullOrWhiteSpace(EditorPinHash);

    public static WallConfig CreateDefault()
    {
        return new WallConfig
        {
            Tiles = new List<VideoTileConfig>
            {
                VideoTileConfig.Create("Video 1", 0, 0, "#e7a346"),
                VideoTileConfig.Create("Video 2", 1, 0, "#5dbe91"),
                VideoTileConfig.Create("Video 3", 0, 1, "#7997e3"),
                VideoTileConfig.Create("Video 4", 1, 1, "#dc8068")
            }
        };
    }

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? "default" : Id.Trim();
        Name = string.IsNullOrWhiteSpace(Name) ? "Video wall" : Name.Trim();
        LayoutPreset = string.IsNullOrWhiteSpace(LayoutPreset) ? "2x2" : LayoutPreset.Trim();
        Locale = string.IsNullOrWhiteSpace(Locale) ? "en" : Locale.Trim().ToLowerInvariant();
        GridColumns = Math.Clamp(GridColumns, 1, 12);
        GridRows = Math.Clamp(GridRows, 1, 12);
        TileGap = Math.Clamp(TileGap, 0, 64);
        BackgroundColor = NormalizeColor(BackgroundColor, "#0d1114");
        ForegroundColor = NormalizeColor(ForegroundColor, "#edf3f4");
        PanelColor = NormalizeColor(PanelColor, "#151a1f");
        AccentColor = NormalizeColor(AccentColor, "#e7a346");
        Tiles ??= new List<VideoTileConfig>();
        Presets ??= new List<LayoutPresetConfig>();

        foreach (VideoTileConfig tile in Tiles)
            tile.Normalize(GridColumns, GridRows);

        foreach (LayoutPresetConfig preset in Presets)
            preset.Normalize(Tiles);
    }

    private static string NormalizeColor(string? color, string fallback)
    {
        if (string.IsNullOrWhiteSpace(color))
            return fallback;

        string value = color.Trim();
        return value.StartsWith('#') && (value.Length == 4 || value.Length == 7)
            ? value
            : fallback;
    }
}

public sealed class LayoutPresetConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Layout";
    public int GridColumns { get; set; } = 2;
    public int GridRows { get; set; } = 2;
    public List<LayoutTilePlacement> Placements { get; set; } = new();

    public static LayoutPresetConfig Capture(WallConfig wall, string name)
    {
        var preset = new LayoutPresetConfig
        {
            Name = name,
            GridColumns = wall.GridColumns,
            GridRows = wall.GridRows,
            Placements = wall.Tiles.Select(tile => new LayoutTilePlacement
            {
                TileId = tile.Id,
                Column = tile.Column,
                Row = tile.Row,
                ColumnSpan = tile.ColumnSpan,
                RowSpan = tile.RowSpan
            }).ToList()
        };
        preset.Normalize(wall.Tiles);
        return preset;
    }

    public void Normalize(IReadOnlyCollection<VideoTileConfig>? tiles = null)
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Name = string.IsNullOrWhiteSpace(Name) ? "Layout" : Name.Trim();
        if (Name.Length > 64)
            Name = Name[..64];
        GridColumns = Math.Clamp(GridColumns, 1, 12);
        GridRows = Math.Clamp(GridRows, 1, 12);
        Placements ??= new List<LayoutTilePlacement>();

        foreach (LayoutTilePlacement placement in Placements)
            placement.Normalize(GridColumns, GridRows);
    }
}

public sealed class LayoutTilePlacement
{
    public string TileId { get; set; } = string.Empty;
    public int Column { get; set; }
    public int Row { get; set; }
    public int ColumnSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;

    public void Normalize(int gridColumns, int gridRows)
    {
        TileId = TileId?.Trim() ?? string.Empty;
        Column = Math.Clamp(Column, 0, Math.Max(0, gridColumns - 1));
        Row = Math.Clamp(Row, 0, Math.Max(0, gridRows - 1));
        ColumnSpan = Math.Clamp(ColumnSpan, 1, Math.Max(1, gridColumns - Column));
        RowSpan = Math.Clamp(RowSpan, 1, Math.Max(1, gridRows - Row));
    }
}

public sealed record VideoTileConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;
    public string Title { get; set; } = "Video";
    public bool ShowTitle { get; set; } = true;
    public SourceProtocol Protocol { get; set; } = SourceProtocol.Rtsp;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 554;
    public string Path { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string CredentialReference { get; set; } = string.Empty;
    public StreamTransport Transport { get; set; } = StreamTransport.Auto;
    public StreamCodec Codec { get; set; } = StreamCodec.Auto;
    public string H264SpsPpsBase64 { get; set; } = string.Empty;
    public string H265VpsSpsPpsBase64 { get; set; } = string.Empty;
    public int Column { get; set; }
    public int Row { get; set; }
    public int ColumnSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;
    public TileSizeUnit SizeUnit { get; set; } = TileSizeUnit.Percent;
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public AspectMode Aspect { get; set; } = AspectMode.RespectSource;
    public int BorderWidth { get; set; } = 2;
    public string BorderColor { get; set; } = "#e7a346";
    public TitleBackgroundMode TitleBackground { get; set; } = TitleBackgroundMode.BorderColor;
    public string TitleBackgroundColor { get; set; } = "#e7a346";

    [JsonIgnore]
    public bool HasSource => Enabled && !string.IsNullOrWhiteSpace(Host) && Port > 0;

    public static VideoTileConfig Create(string title, int column, int row, string borderColor)
    {
        return new VideoTileConfig
        {
            Title = title,
            Column = column,
            Row = row,
            BorderColor = borderColor,
            TitleBackgroundColor = borderColor
        };
    }

    public void Normalize(int gridColumns = 12, int gridRows = 12)
    {
        gridColumns = Math.Max(1, gridColumns);
        gridRows = Math.Max(1, gridRows);
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Title = Title?.Trim() ?? string.Empty;
        Host = Host?.Trim() ?? string.Empty;
        Path = Path?.Trim() ?? string.Empty;
        Username = Username?.Trim() ?? string.Empty;
        CredentialReference = CredentialReference?.Trim() ?? string.Empty;
        H264SpsPpsBase64 = H264SpsPpsBase64?.Trim() ?? string.Empty;
        H265VpsSpsPpsBase64 = H265VpsSpsPpsBase64?.Trim() ?? string.Empty;
        Port = Math.Clamp(Port, 1, 65535);
        Column = Math.Clamp(Column, 0, Math.Max(0, gridColumns - 1));
        Row = Math.Clamp(Row, 0, Math.Max(0, gridRows - 1));
        ColumnSpan = Math.Clamp(ColumnSpan, 1, Math.Max(1, gridColumns - Column));
        RowSpan = Math.Clamp(RowSpan, 1, Math.Max(1, gridRows - Row));
        if (!Enum.IsDefined(typeof(TileSizeUnit), SizeUnit))
            SizeUnit = TileSizeUnit.Percent;

        bool percent = SizeUnit == TileSizeUnit.Percent;
        double coordinateMaximum = percent ? 99d : 10000d;
        double sizeMaximum = percent ? 100d : 10000d;
        double defaultX = percent ? 100d * Column / gridColumns : Column * 320d;
        double defaultY = percent ? 100d * Row / gridRows : Row * 180d;
        double defaultWidth = percent ? 100d * ColumnSpan / gridColumns : ColumnSpan * 320d;
        double defaultHeight = percent ? 100d * RowSpan / gridRows : RowSpan * 180d;

        X = NormalizeGeometryValue(X, defaultX, 0d, coordinateMaximum);
        Y = NormalizeGeometryValue(Y, defaultY, 0d, coordinateMaximum);
        Width = NormalizeGeometryValue(Width, defaultWidth, 1d, sizeMaximum);
        Height = NormalizeGeometryValue(Height, defaultHeight, 1d, sizeMaximum);
        if (percent)
        {
            Width = Math.Clamp(Width.Value, 1d, Math.Max(1d, 100d - X.Value));
            Height = Math.Clamp(Height.Value, 1d, Math.Max(1d, 100d - Y.Value));
        }
        BorderWidth = Math.Clamp(BorderWidth, 0, 24);
        BorderColor = NormalizeColor(BorderColor, "#e7a346");
        TitleBackgroundColor = NormalizeColor(TitleBackgroundColor, BorderColor);
    }

    private static string NormalizeColor(string? color, string fallback)
    {
        if (string.IsNullOrWhiteSpace(color))
            return fallback;

        string value = color.Trim();
        return value.StartsWith('#') && (value.Length == 4 || value.Length == 7)
            ? value
            : fallback;
    }

    private static double NormalizeGeometryValue(double? value, double fallback, double minimum, double maximum)
    {
        double candidate = value is { } number && double.IsFinite(number) ? number : fallback;
        return Math.Clamp(candidate, minimum, maximum);
    }
}

public sealed class WallResponse
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string LayoutPreset { get; init; } = string.Empty;
    public int GridColumns { get; init; }
    public int GridRows { get; init; }
    public int TileGap { get; init; }
    public string BackgroundColor { get; init; } = string.Empty;
    public string ForegroundColor { get; init; } = string.Empty;
    public string PanelColor { get; init; } = string.Empty;
    public string AccentColor { get; init; } = string.Empty;
    public string Locale { get; init; } = "en";
    public bool EditorProtectionEnabled { get; init; }
    public List<VideoTileResponse> Tiles { get; init; } = new();
    public List<LayoutPresetResponse> Presets { get; init; } = new();
}

public sealed class LayoutPresetResponse
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int GridColumns { get; init; }
    public int GridRows { get; init; }
    public List<LayoutTilePlacement> Placements { get; init; } = new();
}

public sealed class SavePresetRequest
{
    public string Name { get; init; } = string.Empty;
}

public sealed class PresetImportRequest
{
    public int SchemaVersion { get; init; } = 1;
    public string ConflictMode { get; init; } = "rename";
    public List<LayoutPresetConfig> Presets { get; init; } = new();
}

public sealed class PresetExportResponse
{
    public int SchemaVersion { get; init; } = 1;
    public string WallId { get; init; } = string.Empty;
    public DateTimeOffset ExportedAt { get; init; }
    public List<LayoutPresetResponse> Presets { get; init; } = new();
}

public sealed class EditorLoginRequest
{
    public string Pin { get; init; } = string.Empty;
}

public sealed class AccessUpdateRequest
{
    public string EditorPin { get; init; } = string.Empty;
}

public sealed class EditorSessionResponse
{
    public bool EditorProtectionEnabled { get; init; }
    public bool Editor { get; init; }
}

public sealed class TilePlacementRequest
{
    public int Column { get; init; }
    public int Row { get; init; }
    public int ColumnSpan { get; init; } = 1;
    public int RowSpan { get; init; } = 1;
    // A move can exchange more than one tile placement in the same write.
    // The route tileId identifies the primary tile; these are the additional
    // tiles whose final rectangles should be committed atomically with it.
    public List<TilePlacementChange> AdditionalPlacements { get; init; } = new();
}

public sealed class TileGeometryRequest
{
    public TileSizeUnit SizeUnit { get; init; } = TileSizeUnit.Percent;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    // A freeform move can exchange more than one tile geometry in the same
    // write. The route tileId identifies the primary tile.
    public List<TileGeometryChange> AdditionalGeometries { get; init; } = new();
}

public sealed class TilePlacementChange
{
    public string TileId { get; init; } = string.Empty;
    public int Column { get; init; }
    public int Row { get; init; }
    public int ColumnSpan { get; init; } = 1;
    public int RowSpan { get; init; } = 1;
}

public sealed class TileGeometryChange
{
    public string TileId { get; init; } = string.Empty;
    public TileSizeUnit SizeUnit { get; init; } = TileSizeUnit.Percent;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public sealed class VideoTileResponse
{
    public string Id { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public string Title { get; init; } = string.Empty;
    public bool ShowTitle { get; init; }
    public SourceProtocol Protocol { get; init; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Path { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public bool HasPassword { get; init; }
    public string CredentialReference { get; init; } = string.Empty;
    public StreamTransport Transport { get; init; }
    public StreamCodec Codec { get; init; }
    public int Column { get; init; }
    public int Row { get; init; }
    public int ColumnSpan { get; init; }
    public int RowSpan { get; init; }
    public TileSizeUnit SizeUnit { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public AspectMode Aspect { get; init; }
    public int BorderWidth { get; init; }
    public string BorderColor { get; init; } = string.Empty;
    public TitleBackgroundMode TitleBackground { get; init; }
    public string TitleBackgroundColor { get; init; } = string.Empty;
}

public sealed class TileStatusResponse
{
    public string TileId { get; init; } = string.Empty;
    public string State { get; init; } = "Idle";
    public string? Error { get; init; }
    public int ViewerCount { get; init; }
    public long FrameCount { get; init; }
    public DateTimeOffset? LastFrameAt { get; init; }
    public string DetectedCodec { get; init; } = "Auto";
    public string DetectedTransport { get; init; } = "Auto";
    public string BrowserOutput { get; init; } = "Waiting for source";
}

public sealed class SourceTestRequest
{
    // Optional: an editor can test the unsaved source draft without changing
    // the persisted wall. An empty object keeps the legacy saved-tile behavior.
    public VideoTileConfig? Tile { get; init; }
}

public sealed class SourceTestResponse
{
    public bool Success { get; init; }
    public string State { get; init; } = "Error";
    public string Message { get; init; } = string.Empty;
    public string DetectedCodec { get; init; } = "Auto";
    public string DetectedTransport { get; init; } = "Auto";
    public bool FrameReceived { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public IReadOnlyList<SourceValidationIssue> Issues { get; init; } = Array.Empty<SourceValidationIssue>();
}

public static class WallMapper
{
    public static WallResponse ToResponse(WallConfig wall, bool includeSourceDetails = true)
    {
        return new WallResponse
        {
            Id = wall.Id,
            Name = wall.Name,
            LayoutPreset = wall.LayoutPreset,
            GridColumns = wall.GridColumns,
            GridRows = wall.GridRows,
            TileGap = wall.TileGap,
            BackgroundColor = wall.BackgroundColor,
            ForegroundColor = wall.ForegroundColor,
            PanelColor = wall.PanelColor,
            AccentColor = wall.AccentColor,
            Locale = wall.Locale,
            EditorProtectionEnabled = wall.HasEditorPin,
            Tiles = wall.Tiles.Select(tile => new VideoTileResponse
            {
                Id = tile.Id,
                Enabled = tile.Enabled,
                Title = tile.Title,
                ShowTitle = tile.ShowTitle,
                Protocol = tile.Protocol,
                Host = includeSourceDetails ? tile.Host : string.Empty,
                Port = includeSourceDetails ? tile.Port : 0,
                Path = includeSourceDetails ? tile.Path : string.Empty,
                Username = includeSourceDetails ? tile.Username : string.Empty,
                HasPassword = includeSourceDetails && !string.IsNullOrEmpty(tile.Password),
                CredentialReference = includeSourceDetails ? tile.CredentialReference : string.Empty,
                Transport = tile.Transport,
                Codec = tile.Codec,
                Column = tile.Column,
                Row = tile.Row,
                ColumnSpan = tile.ColumnSpan,
                RowSpan = tile.RowSpan,
                SizeUnit = tile.SizeUnit,
                X = tile.X ?? 0d,
                Y = tile.Y ?? 0d,
                Width = tile.Width ?? 1d,
                Height = tile.Height ?? 1d,
                Aspect = tile.Aspect,
                BorderWidth = tile.BorderWidth,
                BorderColor = tile.BorderColor,
                TitleBackground = tile.TitleBackground,
                TitleBackgroundColor = tile.TitleBackgroundColor
            }).ToList(),
            Presets = wall.Presets.Select(ToResponse).ToList()
        };
    }

    public static LayoutPresetResponse ToResponse(LayoutPresetConfig preset)
    {
        return new LayoutPresetResponse
        {
            Id = preset.Id,
            Name = preset.Name,
            GridColumns = preset.GridColumns,
            GridRows = preset.GridRows,
            Placements = preset.Placements.Select(placement => new LayoutTilePlacement
            {
                TileId = placement.TileId,
                Column = placement.Column,
                Row = placement.Row,
                ColumnSpan = placement.ColumnSpan,
                RowSpan = placement.RowSpan
            }).ToList()
        };
    }
}
