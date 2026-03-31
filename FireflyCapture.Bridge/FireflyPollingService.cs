namespace FireflyCapture.Bridge;

/// <summary>
/// Background service that polls <see cref="SnapDllInterop.IsButtonPressed"/> at a
/// configurable interval and broadcasts a <see cref="ButtonPressEvent"/> via
/// <see cref="ButtonEventBroadcaster"/> each time a press is detected.
/// ReleaseButton() is called immediately on detection so the hardware can reset.
/// </summary>
public sealed class FireflyPollingService : BackgroundService
{
    private readonly SnapDllInterop _snap;
    private readonly ButtonEventBroadcaster _broadcaster;
    private readonly BridgeOptions _options;
    private readonly ILogger<FireflyPollingService> _logger;
    private long _pressSequence;

    public FireflyPollingService(
        SnapDllInterop snap,
        ButtonEventBroadcaster broadcaster,
        BridgeOptions options,
        ILogger<FireflyPollingService> logger)
    {
        _snap = snap ?? throw new ArgumentNullException(nameof(snap));
        _broadcaster = broadcaster ?? throw new ArgumentNullException(nameof(broadcaster));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_snap.IsLoaded)
        {
            _logger.LogWarning(
                "{Service}: SnapDll not loaded — button polling disabled. " +
                "Bridge will still serve /health and /button-state (always false).",
                nameof(FireflyPollingService));
            return;
        }

        var interval = TimeSpan.FromMilliseconds(_options.PollingIntervalMs);
        _logger.LogInformation(
            "{Service}: Starting button polling at {Interval}ms interval",
            nameof(FireflyPollingService), _options.PollingIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_snap.IsButtonPressed())
                {
                    // Acknowledge the press immediately so hardware can reset
                    _snap.ReleaseButton();

                    var seq = Interlocked.Increment(ref _pressSequence);
                    var evt = new ButtonPressEvent(DateTime.UtcNow, seq);

                    _logger.LogInformation(
                        "{Service}: Button press detected (seq={Seq}), broadcasting to {Count} subscriber(s)",
                        nameof(FireflyPollingService), seq, _broadcaster.SubscriberCount);

                    _broadcaster.Broadcast(evt);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Service}: Unexpected error in polling loop",
                    nameof(FireflyPollingService));
            }

            await Task.Delay(interval, stoppingToken);
        }

        _logger.LogInformation("{Service}: Polling stopped", nameof(FireflyPollingService));
    }
}
