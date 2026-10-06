using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using RtspClientSharp.Web.Models;
using RtspClientSharp.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var streamWallOptions = new StreamWallOptions();
builder.Configuration.GetSection("StreamWall").Bind(streamWallOptions);
streamWallOptions.Normalize();
var mediaGatewayOptions = new MediaGatewayOptions();
builder.Configuration.GetSection("MediaGateway").Bind(mediaGatewayOptions);
mediaGatewayOptions.Normalize();
string mediaGatewayOrigins = BuildCspMediaOrigins(mediaGatewayOptions);
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = streamWallOptions.MaxRequestBodyBytes);

string keyDirectory = Path.GetFullPath(Path.IsPathRooted(streamWallOptions.KeyDirectory)
    ? streamWallOptions.KeyDirectory
    : Path.Combine(builder.Environment.ContentRootPath, streamWallOptions.KeyDirectory));
Directory.CreateDirectory(keyDirectory);
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("RtspClientSharp.Web")
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
if (OperatingSystem.IsWindows())
    dataProtection.ProtectKeysWithDpapi();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});
builder.Services.AddHttpClient(nameof(MediaGatewayService), client => client.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddSingleton<WallStore>();
builder.Services.AddSingleton<StreamRegistry>();
builder.Services.AddSingleton<EditorAccessService>();
builder.Services.AddSingleton<SourceValidator>();
builder.Services.AddSingleton<SourceEndpointPolicy>();
builder.Services.AddSingleton<SourceCredentialProtector>();
builder.Services.AddSingleton<SourceTester>();
builder.Services.AddSingleton(streamWallOptions);
builder.Services.AddSingleton(mediaGatewayOptions);
builder.Services.AddSingleton<MediaGatewayService>();
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<MediaGatewayService>());
builder.Services.AddSingleton<AccessControlService>();

var app = builder.Build();

if (streamWallOptions.EnableForwardedHeaders)
{
    var forwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };
    forwardedHeaders.KnownIPNetworks.Clear();
    forwardedHeaders.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeaders);
}

if (streamWallOptions.RedirectToHttps)
    app.UseHttpsRedirection();
if (streamWallOptions.EnableHsts)
    app.UseHsts();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' blob: data:; " +
        $"media-src 'self' blob: {mediaGatewayOrigins}; " +
        $"connect-src 'self' ws: wss: {mediaGatewayOrigins}; " +
        "style-src 'self' 'unsafe-inline'; script-src 'self'; base-uri 'self'; frame-ancestors 'self'";
    await next().ConfigureAwait(false);
});

app.Use(async (context, next) =>
{
    AccessControlService access = context.RequestServices.GetRequiredService<AccessControlService>();
    if (access.RequiresAuthentication(context.Request.Path) && !access.IsAuthenticated(context))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers["Cache-Control"] = "no-store";
        await context.Response.WriteAsJsonAsync(new
        {
            code = ApiErrorCodes.AuthenticationRequired,
            error = "Authentication is required."
        },
            cancellationToken: context.RequestAborted).ConfigureAwait(false);
        return;
    }

    await next().ConfigureAwait(false);
});

app.Use(async (context, next) =>
{
    PathString path = context.Request.Path;
    bool wallsApi = path.StartsWithSegments("/api/walls");
    bool backupApi = wallsApi && path.Value?.Contains("/backups", StringComparison.OrdinalIgnoreCase) == true;
    bool writeRequest = context.Request.Method is not ("GET" or "HEAD" or "OPTIONS");
    if (streamWallOptions.ReadOnly && (path.StartsWithSegments("/api/diagnostics") || backupApi || (wallsApi && writeRequest)))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.Headers["Cache-Control"] = "no-store";
        await context.Response.WriteAsJsonAsync(new
        {
            code = "readOnly",
            error = "This public wall is view-only."
        }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
        return;
    }

    await next().ConfigureAwait(false);
});

app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/", () => Results.Redirect("/view/default"));
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "RtspClientSharp.Web" }));
app.MapGet("/api/auth/session", GetAccessSession);
app.MapPost("/api/auth/login", LoginAccess);
app.MapDelete("/api/auth/session", LogoutAccess);

app.MapGet("/api/walls", GetWallsAsync);
app.MapGet("/api/walls/{wallId}", GetWallAsync);
app.MapGet("/api/walls/{wallId}/session", GetEditorSessionAsync);
app.MapPost("/api/walls/{wallId}/session/editor", UnlockEditorAsync);
app.MapDelete("/api/walls/{wallId}/session/editor", LockEditorAsync);
app.MapPut("/api/walls/{wallId}/access", UpdateAccessAsync);
app.MapPut("/api/walls/{wallId}", UpdateWallAsync);
app.MapPost("/api/walls/{wallId}/tiles", AddTileAsync);
app.MapPut("/api/walls/{wallId}/tiles/{tileId}", UpdateTileAsync);
app.MapPut("/api/walls/{wallId}/tiles/{tileId}/placement", UpdateTilePlacementAsync);
app.MapPut("/api/walls/{wallId}/tiles/{tileId}/geometry", UpdateTileGeometryAsync);
app.MapDelete("/api/walls/{wallId}/tiles/{tileId}", DeleteTileAsync);
app.MapGet("/api/walls/{wallId}/status", GetWallStatusAsync);
app.MapPost("/api/walls/{wallId}/presets", SavePresetAsync);
app.MapPost("/api/walls/{wallId}/presets/{presetId}/apply", ApplyPresetAsync);
app.MapDelete("/api/walls/{wallId}/presets/{presetId}", DeletePresetAsync);
app.MapGet("/api/walls/{wallId}/presets/export", ExportPresetsAsync);
app.MapPost("/api/walls/{wallId}/presets/import", ImportPresetsAsync);
app.MapPost("/api/walls/{wallId}/tiles/{tileId}/test", TestTileAsync);
app.MapGet("/api/walls/{wallId}/backups", ListBackupsAsync);
app.MapPost("/api/walls/{wallId}/backups", CreateBackupAsync);
app.MapGet("/api/walls/{wallId}/backups/{fileName}", DownloadBackupAsync);
app.MapGet("/api/diagnostics", GetDiagnosticsAsync);

app.MapGet("/api/streams/{wallId}/{tileId}/capabilities", GetStreamCapabilitiesAsync);
app.MapGet("/api/streams/{wallId}/{tileId}/mjpeg", HandleMjpegAsync);
app.MapGet("/api/streams/{wallId}/{tileId}/ws", HandleWebSocketAsync);

app.MapFallbackToFile("index.html");

app.Run();

static async Task<IResult> GetWallsAsync(WallStore store, StreamWallOptions options, CancellationToken token)
{
    IReadOnlyList<WallConfig> walls = await store.GetAllAsync(token).ConfigureAwait(false);
    return Results.Ok(walls.Select(wall => WallMapper.ToResponse(wall, includeSourceDetails: !options.ReadOnly)));
}

static IResult GetAccessSession(AccessControlService access, StreamWallOptions options, HttpContext context)
{
    return Results.Ok(new AccessSessionResponse
    {
        Enabled = access.Enabled,
        Authenticated = access.IsAuthenticated(context),
        ReadOnly = options.ReadOnly
    });
}

static IResult LoginAccess(AccessLoginRequest request, AccessControlService access, StreamWallOptions options,
    HttpContext context)
{
    if (!access.Enabled)
    {
        return Results.Ok(new AccessSessionResponse
        {
            Enabled = false,
            Authenticated = true,
            ReadOnly = options.ReadOnly
        });
    }

    if (access.TryAuthenticate(context, request.Token, out TimeSpan? retryAfter))
    {
        return Results.Ok(new AccessSessionResponse
        {
            Enabled = true,
            Authenticated = true,
            ReadOnly = options.ReadOnly
        });
    }

    if (retryAfter is { } delay)
    {
        context.Response.Headers["Retry-After"] =
            Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds)).ToString();
        return Results.Json(new
        {
            code = ApiErrorCodes.AccessRateLimited,
            error = "Too many failed access attempts. Try again later."
        },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    return Results.Json(new
    {
        code = ApiErrorCodes.InvalidAccessToken,
        error = "The access token is incorrect."
    },
        statusCode: StatusCodes.Status401Unauthorized);
}

static IResult LogoutAccess(AccessControlService access, StreamWallOptions options, HttpContext context)
{
    access.Revoke(context);
    return Results.Ok(new AccessSessionResponse
    {
        Enabled = access.Enabled,
        Authenticated = !access.Enabled,
        ReadOnly = options.ReadOnly
    });
}

static async Task<IResult> GetWallAsync(string wallId, WallStore store, StreamWallOptions options,
    CancellationToken token)
{
    WallConfig? wall = await store.GetAsync(wallId, token).ConfigureAwait(false);
    return wall == null
        ? Results.NotFound()
        : Results.Ok(WallMapper.ToResponse(wall, includeSourceDetails: !options.ReadOnly));
}

static async Task<IResult> GetEditorSessionAsync(string wallId, WallStore store,
    EditorAccessService access, HttpContext context, CancellationToken token)
{
    WallConfig? wall = await store.GetAsync(wallId, token).ConfigureAwait(false);
    if (wall == null)
        return Results.NotFound();

    return Results.Ok(new EditorSessionResponse
    {
        EditorProtectionEnabled = wall.HasEditorPin,
        Editor = access.IsEditor(context, wall)
    });
}

static async Task<IResult> UnlockEditorAsync(string wallId, EditorLoginRequest request, WallStore store,
    EditorAccessService access, HttpContext context, CancellationToken token)
{
    WallConfig? wall = await store.GetAsync(wallId, token).ConfigureAwait(false);
    if (wall == null)
        return Results.NotFound();

    if (wall.HasEditorPin && !access.ValidatePin(wall, request.Pin))
        return Results.Json(new
        {
            code = ApiErrorCodes.InvalidEditorPin,
            error = "The editor PIN is incorrect."
        }, statusCode: StatusCodes.Status401Unauthorized);

    access.Grant(context, wallId);
    return Results.Ok(new EditorSessionResponse
    {
        EditorProtectionEnabled = wall.HasEditorPin,
        Editor = true
    });
}

static async Task<IResult> LockEditorAsync(string wallId, WallStore store, EditorAccessService access,
    HttpContext context, CancellationToken token)
{
    WallConfig? wall = await store.GetAsync(wallId, token).ConfigureAwait(false);
    if (wall == null)
        return Results.NotFound();

    access.Revoke(context);
    return Results.Ok(new EditorSessionResponse
    {
        EditorProtectionEnabled = wall.HasEditorPin,
        Editor = !wall.HasEditorPin
    });
}

static async Task<IResult> UpdateAccessAsync(string wallId, AccessUpdateRequest request, WallStore store,
    EditorAccessService access, HttpContext context, CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    string pin = request.EditorPin?.Trim() ?? string.Empty;
    if (pin.Any(char.IsControl) || pin.Length is > 0 and < 4 or > 64)
        return Results.BadRequest(new
        {
            code = ApiErrorCodes.InvalidEditorPinFormat,
            error = "The editor PIN must be empty or between 4 and 64 characters."
        });

    WallConfig? wall = await store.SetEditorPinAsync(wallId, pin, token).ConfigureAwait(false);
    if (wall == null)
        return Results.NotFound();

    access.RevokeWall(wallId);
    if (wall.HasEditorPin)
        access.Grant(context, wallId);

    return Results.Ok(new EditorSessionResponse
    {
        EditorProtectionEnabled = wall.HasEditorPin,
        Editor = true
    });
}

static async Task<IResult> UpdateWallAsync(string wallId, WallConfig incoming, WallStore store,
    EditorAccessService access, SourceValidator validator, HttpContext context, CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    incoming.Id = wallId;
    SourceValidationResult validation = await validator.ValidateWallAsync(incoming, token).ConfigureAwait(false);
    if (!validation.IsValid)
        return ValidationFailure(validation);

    incoming.Normalize();
    WallConfig wall = await store.UpdateAsync(incoming, token).ConfigureAwait(false);
    return Results.Ok(WallMapper.ToResponse(wall));
}

static async Task<IResult> AddTileAsync(string wallId, VideoTileConfig tile, WallStore store,
    EditorAccessService access, SourceValidator validator, HttpContext context, CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    SourceValidationResult validation = await validator.ValidateAsync(tile, token).ConfigureAwait(false);
    if (!validation.IsValid)
        return ValidationFailure(validation);

    VideoTileConfig? added = await store.AddTileAsync(wallId, tile, token).ConfigureAwait(false);
    return added == null ? Results.NotFound() : Results.Ok(added with { Password = string.Empty });
}

static async Task<IResult> UpdateTileAsync(string wallId, string tileId, VideoTileConfig tile,
    WallStore store, EditorAccessService access, SourceValidator validator, HttpContext context,
    CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    SourceValidationResult validation = await validator.ValidateAsync(tile, token).ConfigureAwait(false);
    if (!validation.IsValid)
        return ValidationFailure(validation);

    VideoTileConfig? updated = await store.UpdateTileAsync(wallId, tileId, tile, token)
        .ConfigureAwait(false);
    return updated == null
        ? Results.NotFound()
        : Results.Ok(updated with { Password = string.Empty });
}

static async Task<IResult> UpdateTilePlacementAsync(string wallId, string tileId,
    TilePlacementRequest placement, WallStore store, EditorAccessService access, HttpContext context,
    CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    VideoTileConfig? updated = await store.UpdateTilePlacementAsync(wallId, tileId, placement, token)
        .ConfigureAwait(false);
    return updated == null
        ? Results.NotFound()
        : Results.Ok(updated with { Password = string.Empty });
}

static async Task<IResult> UpdateTileGeometryAsync(string wallId, string tileId,
    TileGeometryRequest geometry, WallStore store, EditorAccessService access, HttpContext context,
    CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    VideoTileConfig? updated = await store.UpdateTileGeometryAsync(wallId, tileId, geometry, token)
        .ConfigureAwait(false);
    return updated == null
        ? Results.NotFound()
        : Results.Ok(updated with { Password = string.Empty });
}

static async Task<IResult> DeleteTileAsync(string wallId, string tileId, WallStore store,
    EditorAccessService access, HttpContext context, CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    bool deleted = await store.DeleteTileAsync(wallId, tileId, token).ConfigureAwait(false);
    return deleted ? Results.NoContent() : Results.NotFound();
}

static async Task<IResult> GetWallStatusAsync(string wallId, WallStore store, StreamRegistry registry,
    StreamWallOptions options, CancellationToken token)
{
    WallConfig? wall = await store.GetAsync(wallId, token).ConfigureAwait(false);
    if (wall == null)
        return Results.NotFound();

    IEnumerable<TileStatusResponse> statuses = wall.Tiles.Select(tile => registry.GetStatus(tile.Id, tile));
    return Results.Ok(options.ReadOnly
        ? statuses.Select(status => new TileStatusResponse
        {
            TileId = status.TileId,
            State = status.State,
            ViewerCount = status.ViewerCount,
            FrameCount = status.FrameCount,
            LastFrameAt = status.LastFrameAt,
            DetectedCodec = status.DetectedCodec,
            DetectedTransport = status.DetectedTransport,
            BrowserOutput = status.BrowserOutput
        })
        : statuses);
}

static async Task<IResult> GetStreamCapabilitiesAsync(
    HttpContext context,
    string wallId,
    string tileId,
    WallStore store,
    StreamWallOptions options,
    SourceValidator validator,
    MediaGatewayService mediaGateway,
    ILogger<MediaGatewayService> logger)
{
    CancellationToken token = context.RequestAborted;
    VideoTileConfig? tile = await FindTileAsync(wallId, tileId, store, token).ConfigureAwait(false);
    if (tile == null)
        return Results.NotFound();

    if (!tile.HasSource)
    {
        return Results.BadRequest(new
        {
            code = ApiErrorCodes.SourceNotConfigured,
            error = "The selected tile is not configured."
        });
    }

    SourceValidationResult validation = await validator.ValidateAsync(tile, token).ConfigureAwait(false);
    if (!validation.IsValid)
    {
        if (options.ReadOnly)
        {
            return Results.BadRequest(new
            {
                code = ApiErrorCodes.SourceValidation,
                error = "This stream is unavailable."
            });
        }

        return Results.BadRequest(new
        {
            code = ApiErrorCodes.SourceValidation,
            error = "Source validation failed.",
            errors = validation.Errors,
            issues = validation.Issues
        });
    }

    MediaGatewayTileEndpoints? gateway = null;
    try
    {
        gateway = await mediaGateway.GetTileEndpointsAsync(wallId, tile, token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception exception)
    {
        logger.LogWarning(exception, "Media gateway path setup failed for wall {WallId}, tile {TileId}.",
            wallId, tileId);
    }

    string directPrefix = $"/api/streams/{Uri.EscapeDataString(wallId)}/{Uri.EscapeDataString(tileId)}";
    return Results.Ok(new
    {
        direct = new
        {
            webSocket = directPrefix + "/ws",
            mjpeg = directPrefix + "/mjpeg"
        },
        gateway = gateway is null
            ? null
            : new
            {
                webRtc = gateway.WebRtcUrl,
                hls = gateway.HlsUrl,
                h264WebRtc = gateway.H264WebRtcUrl,
                h264Hls = gateway.H264HlsUrl
            }
    });
}

static async Task<IResult> GetDiagnosticsAsync(StreamRegistry registry)
{
    return Results.Ok(registry.GetDiagnostics());
}

static async Task<IResult> ListBackupsAsync(string wallId, WallStore store,
    EditorAccessService access, HttpContext context, CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    return Results.Ok(await store.ListBackupsAsync(token).ConfigureAwait(false));
}

static async Task<IResult> CreateBackupAsync(string wallId, WallStore store,
    EditorAccessService access, HttpContext context, CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    WallBackupInfo? backup = await store.CreateBackupAsync(token).ConfigureAwait(false);
    return backup == null ? Results.NotFound() : Results.Ok(backup);
}

static async Task<IResult> DownloadBackupAsync(string wallId, string fileName, WallStore store,
    EditorAccessService access, HttpContext context, CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    (string FileName, byte[] Content)? backup = await store.ReadBackupAsync(fileName, token)
        .ConfigureAwait(false);
    return backup == null
        ? Results.NotFound()
        : Results.File(backup.Value.Content, "application/json", backup.Value.FileName);
}

static async Task<IResult> SavePresetAsync(string wallId, SavePresetRequest request, WallStore store,
    EditorAccessService access, HttpContext context, CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new
        {
            code = ApiErrorCodes.PresetNameRequired,
            error = "A preset name is required."
        });

    LayoutPresetConfig? preset = await store.SavePresetAsync(wallId, request.Name, token)
        .ConfigureAwait(false);
    return preset == null
        ? Results.NotFound()
        : Results.Ok(new LayoutPresetResponse
        {
            Id = preset.Id,
            Name = preset.Name,
            GridColumns = preset.GridColumns,
            GridRows = preset.GridRows
        });
}

static async Task<IResult> ApplyPresetAsync(string wallId, string presetId, WallStore store,
    EditorAccessService access, HttpContext context, CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    WallConfig? wall = await store.ApplyPresetAsync(wallId, presetId, token).ConfigureAwait(false);
    return wall == null ? Results.NotFound() : Results.Ok(WallMapper.ToResponse(wall));
}

static async Task<IResult> DeletePresetAsync(string wallId, string presetId, WallStore store,
    EditorAccessService access, HttpContext context, CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    bool deleted = await store.DeletePresetAsync(wallId, presetId, token).ConfigureAwait(false);
    return deleted ? Results.NoContent() : Results.NotFound();
}

static async Task<IResult> ExportPresetsAsync(string wallId, WallStore store, EditorAccessService access,
    HttpContext context, CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    WallConfig? wall = await store.GetAsync(wallId, token).ConfigureAwait(false);
    if (wall == null)
        return Results.NotFound();

    return Results.Ok(new PresetExportResponse
    {
        WallId = wall.Id,
        ExportedAt = DateTimeOffset.UtcNow,
        Presets = wall.Presets.Select(WallMapper.ToResponse).ToList()
    });
}

static async Task<IResult> ImportPresetsAsync(string wallId, PresetImportRequest request, WallStore store,
    EditorAccessService access, HttpContext context, CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    try
    {
        WallConfig? wall = await store.ImportPresetsAsync(wallId, request, token).ConfigureAwait(false);
        return wall == null ? Results.NotFound() : Results.Ok(WallMapper.ToResponse(wall));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new
        {
            code = ApiErrorCodes.PresetImport,
            error = exception.Message
        });
    }
}

static async Task<IResult> TestTileAsync(string wallId, string tileId, SourceTestRequest? request, WallStore store,
    StreamRegistry registry, SourceTester tester, EditorAccessService access, HttpContext context,
    CancellationToken token)
{
    IResult? denied = await RequireEditorAsync(wallId, context, store, access, token).ConfigureAwait(false);
    if (denied != null)
        return denied;

    VideoTileConfig? savedTile = await FindTileAsync(wallId, tileId, store, token).ConfigureAwait(false);
    if (savedTile == null)
        return Results.NotFound();

    VideoTileConfig tile = savedTile;
    if (request?.Tile is { } draftTile)
    {
        draftTile.Id = savedTile.Id;
        if (string.IsNullOrEmpty(draftTile.Password))
            draftTile.Password = savedTile.Password;
        tile = draftTile;
    }

    SourceTestResponse validation = await tester.ValidateAsync(tile, token).ConfigureAwait(false);
    if (!validation.Success)
        return Results.Ok(validation);

    StreamRegistry.StreamLease lease;
    try
    {
        lease = await registry.AcquireAsync(tile).ConfigureAwait(false);
    }
    catch (StreamCapacityException exception)
    {
        return Results.Json(new
        {
            code = ApiErrorCodes.Capacity,
            error = exception.Message
        },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    await using (lease.ConfigureAwait(false))
    {
        SourceTestResponse result = await tester.TestSessionAsync(tileId, lease.Session, token)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }
}

static async Task HandleMjpegAsync(HttpContext context, string wallId, string tileId,
    WallStore store, StreamRegistry registry, SourceValidator validator, StreamWallOptions options)
{
    CancellationToken token = context.RequestAborted;
    VideoTileConfig? tile = await FindTileAsync(wallId, tileId, store, token).ConfigureAwait(false);
    if (tile == null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    if (!tile.HasSource)
    {
        await WriteSourceErrorAsync(context, ApiErrorCodes.SourceNotConfigured,
            "The selected tile is not configured.", token, readOnly: options.ReadOnly)
            .ConfigureAwait(false);
        return;
    }

    SourceValidationResult validation = await validator.ValidateAsync(tile, token).ConfigureAwait(false);
    if (!validation.IsValid)
    {
        await WriteSourceErrorAsync(context, ApiErrorCodes.SourceValidation,
            string.Join(" ", validation.Errors), token, validation.Issues, options.ReadOnly)
            .ConfigureAwait(false);
        return;
    }

    StreamRegistry.StreamLease lease;
    try
    {
        lease = await registry.AcquireAsync(tile).ConfigureAwait(false);
    }
    catch (StreamCapacityException exception)
    {
        await WriteCapacityErrorAsync(context, exception.Message, token).ConfigureAwait(false);
        return;
    }

    await using StreamRegistry.StreamLease activeLease = lease;
    await using FrameSubscription subscription = activeLease.Session.Subscribe();

    context.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
    context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    context.Response.Headers.Pragma = "no-cache";
    await context.Response.StartAsync(token).ConfigureAwait(false);
    await WriteAsciiAsync(context.Response.Body, "# stream-wall\r\n", token).ConfigureAwait(false);
    await context.Response.Body.FlushAsync(token).ConfigureAwait(false);

    try
    {
        await foreach (PublishedFrame frame in subscription.Reader.ReadAllAsync(token))
        {
            if (frame.Kind != BrowserMediaKind.Mjpeg)
                continue;

            await WriteAsciiAsync(context.Response.Body, "--frame\r\n", token).ConfigureAwait(false);
            await WriteAsciiAsync(context.Response.Body, $"Content-Type: image/jpeg\r\nContent-Length: {frame.Data.Length}\r\n\r\n", token)
                .ConfigureAwait(false);
            await context.Response.Body.WriteAsync(frame.Data, token).ConfigureAwait(false);
            await WriteAsciiAsync(context.Response.Body, "\r\n", token).ConfigureAwait(false);
            await context.Response.Body.FlushAsync(token).ConfigureAwait(false);
        }
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
    }
    catch (IOException) when (token.IsCancellationRequested)
    {
    }
}

static async Task HandleWebSocketAsync(HttpContext context, string wallId, string tileId,
    WallStore store, StreamRegistry registry, SourceValidator validator, StreamWallOptions options)
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket upgrade required", context.RequestAborted);
        return;
    }

    CancellationToken token = context.RequestAborted;
    VideoTileConfig? tile = await FindTileAsync(wallId, tileId, store, token).ConfigureAwait(false);
    if (tile == null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    if (!tile.HasSource)
    {
        await WriteSourceErrorAsync(context, ApiErrorCodes.SourceNotConfigured,
            "The selected tile is not configured.", token, readOnly: options.ReadOnly)
            .ConfigureAwait(false);
        return;
    }

    SourceValidationResult validation = await validator.ValidateAsync(tile, token).ConfigureAwait(false);
    if (!validation.IsValid)
    {
        await WriteSourceErrorAsync(context, ApiErrorCodes.SourceValidation,
            string.Join(" ", validation.Errors), token, validation.Issues, options.ReadOnly)
            .ConfigureAwait(false);
        return;
    }

    StreamRegistry.StreamLease lease;
    try
    {
        lease = await registry.AcquireAsync(tile).ConfigureAwait(false);
    }
    catch (StreamCapacityException exception)
    {
        await WriteCapacityErrorAsync(context, exception.Message, token).ConfigureAwait(false);
        return;
    }

    await using StreamRegistry.StreamLease activeLease = lease;
    await using FrameSubscription subscription = activeLease.Session.Subscribe();
    using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);

    BrowserMediaKind? configuredKind = null;
    string? configuredCodec = null;

    try
    {
        await foreach (PublishedFrame frame in subscription.Reader.ReadAllAsync(token))
        {
            if (socket.State != WebSocketState.Open)
                break;

            if (frame.Kind != configuredKind ||
                !string.Equals(frame.Codec, configuredCodec, StringComparison.Ordinal))
            {
                configuredKind = frame.Kind;
                configuredCodec = frame.Codec;
                string configuration = JsonSerializer.Serialize(new
                {
                    type = "config",
                    kind = frame.Kind switch
                    {
                        BrowserMediaKind.Mjpeg => "mjpeg",
                        BrowserMediaKind.H264 => "h264",
                        BrowserMediaKind.H265 => "h265",
                        _ => "unsupported"
                    },
                    codec = frame.Codec,
                    reason = (string?)null
                });
                byte[] configurationBytes = Encoding.UTF8.GetBytes(configuration);
                await socket.SendAsync(configurationBytes, WebSocketMessageType.Text, true, token)
                    .ConfigureAwait(false);
            }

            if (frame.Kind is BrowserMediaKind.H264 or BrowserMediaKind.H265)
            {
                byte[] packet = BrowserPublisher.CreateWebSocketPacket(frame);
                await socket.SendAsync(packet, WebSocketMessageType.Binary, true, token)
                    .ConfigureAwait(false);
            }
            else if (frame.Kind == BrowserMediaKind.Mjpeg)
            {
                await socket.SendAsync(frame.Data, WebSocketMessageType.Binary, true, token)
                    .ConfigureAwait(false);
            }
        }
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
    }
    catch (WebSocketException)
    {
    }
}

static async Task<VideoTileConfig?> FindTileAsync(string wallId, string tileId, WallStore store,
    CancellationToken token)
{
    WallConfig? wall = await store.GetAsync(wallId, token).ConfigureAwait(false);
    return wall?.Tiles.FirstOrDefault(tile =>
        string.Equals(tile.Id, tileId, StringComparison.OrdinalIgnoreCase));
}

static Task WriteAsciiAsync(Stream stream, string value, CancellationToken token)
{
    return stream.WriteAsync(Encoding.ASCII.GetBytes(value), token).AsTask();
}

static async Task WriteSourceErrorAsync(HttpContext context, string code, string message,
    CancellationToken token, IReadOnlyList<SourceValidationIssue>? issues = null, bool readOnly = false)
{
    context.Response.StatusCode = StatusCodes.Status400BadRequest;
    await context.Response.WriteAsJsonAsync(new
    {
        code,
        error = "Source validation failed.",
        errors = readOnly ? Array.Empty<string>() : new[] { message },
        issues = readOnly ? Array.Empty<SourceValidationIssue>() : issues ?? Array.Empty<SourceValidationIssue>()
    }, cancellationToken: token).ConfigureAwait(false);
}

static async Task WriteCapacityErrorAsync(HttpContext context, string message, CancellationToken token)
{
    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
    context.Response.Headers["Retry-After"] = "5";
    await context.Response.WriteAsJsonAsync(new
    {
        code = ApiErrorCodes.Capacity,
        error = "The stream server is at capacity.",
        details = new[] { message }
    }, cancellationToken: token).ConfigureAwait(false);
}

static async Task<IResult?> RequireEditorAsync(string wallId, HttpContext context, WallStore store,
    EditorAccessService access, CancellationToken token)
{
    WallConfig? wall = await store.GetAsync(wallId, token).ConfigureAwait(false);
    if (wall == null)
        return Results.NotFound();

    return access.IsEditor(context, wall)
        ? null
        : Results.Json(new
        {
            code = ApiErrorCodes.EditorAccessRequired,
            error = "Editor access is required for this action."
        },
            statusCode: StatusCodes.Status403Forbidden);
}

static IResult ValidationFailure(SourceValidationResult validation)
{
    return Results.BadRequest(new
    {
        code = ApiErrorCodes.SourceValidation,
        error = "Source validation failed.",
        errors = validation.Errors,
        issues = validation.Issues
    });
}

static string BuildCspMediaOrigins(MediaGatewayOptions options)
{
    var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (string value in new[] { options.WebRtcBaseUrl, options.HlsBaseUrl })
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            continue;

        origins.Add(uri.GetLeftPart(UriPartial.Authority));
    }

    return string.Join(' ', origins);
}
