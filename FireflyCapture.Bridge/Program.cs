using System.Text.Json;
using FireflyCapture.Bridge;
using Serilog;

// -------------------------------------------------------------------------
// FireflyCapture.Bridge — x86 process wrapping the 32-bit SnapDll.dll
// Exposes a minimal HTTP API consumed by FireflyModule in the 64-bit kiosk.
//
// Endpoints:
//   GET  /health        — liveness check
//   GET  /button-state  — current raw button state
//   POST /release       — manually call ReleaseButton (testing / recovery)
//   GET  /events        — SSE stream; yields a JSON object per button press
// -------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

// --- Logging ---------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OneRoomHealthKiosk", "logs", "firefly-bridge.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 5)
    .CreateLogger();

builder.Host.UseSerilog();

// --- Options ---------------------------------------------------------------
var bridgeOptions = builder.Configuration.GetSection("Bridge").Get<BridgeOptions>() ?? new BridgeOptions();
builder.Services.AddSingleton(bridgeOptions);

// --- Core services ---------------------------------------------------------
builder.Services.AddSingleton<SnapDllInterop>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<SnapDllInterop>>();
    // Resolve DLL path relative to the exe directory so deployment is portable
    var exeDir = AppContext.BaseDirectory;
    var dllPath = Path.IsPathRooted(bridgeOptions.SnapDllPath)
        ? bridgeOptions.SnapDllPath
        : Path.Combine(exeDir, bridgeOptions.SnapDllPath);
    return new SnapDllInterop(dllPath, logger);
});

builder.Services.AddSingleton<ButtonEventBroadcaster>();
builder.Services.AddHostedService<FireflyPollingService>();

// --- Kestrel ---------------------------------------------------------------
builder.WebHost.UseKestrel(k => k.ListenLocalhost(bridgeOptions.Port));

var app = builder.Build();

// Ensure SnapDllInterop is created eagerly so load errors appear at startup
_ = app.Services.GetRequiredService<SnapDllInterop>();

var startTime = DateTimeOffset.UtcNow;
var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

// --- GET /health -----------------------------------------------------------
app.MapGet("/health", (SnapDllInterop snap) =>
{
    return Results.Ok(new
    {
        healthy = true,
        snapDllLoaded = snap.IsLoaded,
        uptimeSeconds = (DateTimeOffset.UtcNow - startTime).TotalSeconds,
        timestamp = DateTimeOffset.UtcNow
    });
})
.WithName("GetHealth")
.WithSummary("Bridge liveness check");

// --- GET /button-state -----------------------------------------------------
app.MapGet("/button-state", (SnapDllInterop snap) =>
{
    return Results.Ok(new
    {
        pressed = snap.IsButtonPressed(),
        snapDllLoaded = snap.IsLoaded,
        timestamp = DateTimeOffset.UtcNow
    });
})
.WithName("GetButtonState")
.WithSummary("Read current Firefly button state without releasing");

// --- POST /release ---------------------------------------------------------
app.MapPost("/release", (SnapDllInterop snap) =>
{
    snap.ReleaseButton();
    return Results.Ok(new { released = true, timestamp = DateTimeOffset.UtcNow });
})
.WithName("ReleaseButton")
.WithSummary("Manually call ReleaseButton() — for testing and recovery only");

// --- GET /events  (SSE) ----------------------------------------------------
app.MapGet("/events", async (
    ButtonEventBroadcaster broadcaster,
    HttpContext ctx,
    CancellationToken ct) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    // Heartbeat: send a comment every 15 s to keep the connection alive through
    // proxies and load balancers.
    // Note: do NOT use `using var` here — the timer must stay alive until after
    // heartbeatTask has finished; disposing it early causes ObjectDisposedException
    // inside WaitForNextTickAsync on the background task.
    // writeLock serialises the heartbeat writer and the event writer so they
    // cannot interleave bytes into Kestrel's PipeWriter, which is not thread-safe.
    var writeLock = new SemaphoreSlim(1, 1);
    var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));
    var heartbeatTask = Task.Run(async () =>
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await heartbeatTimer.WaitForNextTickAsync(ct);
                await writeLock.WaitAsync(ct);
                try
                {
                    await ctx.Response.WriteAsync(": heartbeat\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
                finally { writeLock.Release(); }
            }
            catch { break; }
        }
    });

    try
    {
        await foreach (var evt in broadcaster.SubscribeAsync(ct))
        {
            var json = JsonSerializer.Serialize(evt, jsonOpts);
            await writeLock.WaitAsync(ct);
            try
            {
                await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }
            finally { writeLock.Release(); }
        }
    }
    finally
    {
        // Await the heartbeat task before disposing the timer and lock it references.
        // heartbeatTask never throws (all exceptions are caught internally),
        // so this will not mask an exception from the foreach above.
        await heartbeatTask;
        heartbeatTimer.Dispose();
        writeLock.Dispose();
    }
})
.WithName("ButtonEvents")
.WithSummary("SSE stream of button-press events");

app.Logger.LogInformation(
    "FireflyCapture.Bridge starting on http://localhost:{Port}",
    bridgeOptions.Port);

await app.RunAsync();
