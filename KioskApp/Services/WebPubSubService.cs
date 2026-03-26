using System.Text.Json;
using Azure.Messaging.WebPubSub.Clients;

namespace KioskApp.Services;

/// <summary>
/// Always-on Azure Web PubSub listener that serves as a transparent fallback for
/// IoT Hub navigation commands. Connects using a workstationId from config and
/// processes workstation-api messages identically to the IoT Hub path.
///
/// Lifecycle: started in EnableHardwareApiModeAsync, stopped in DisableHardwareApiModeAsync.
/// </summary>
public class WebPubSubService : IAsyncDisposable
{
    private readonly WebPubSubSettings _config;
    private readonly WebViewNavigationService _navigationService;
    private readonly HttpClient _httpClient;

    private WebPubSubClient? _client;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// True when the Web PubSub connection is established and active.
    /// </summary>
    public bool IsConnected { get; private set; }

    public WebPubSubService(WebPubSubSettings config, WebViewNavigationService navigationService)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Validates config, creates the WebPubSubClient with auto-reconnect and a credential
    /// delegate that fetches a fresh token on every (re)connect, wires events, and starts
    /// the connection loop.
    /// </summary>
    public async Task StartAsync()
    {
        if (!_config.Enabled)
        {
            Logger.Log("WebPubSubService: not enabled, skipping start");
            return;
        }

        if (string.IsNullOrEmpty(_config.WorkstationId))
        {
            Logger.Log("WebPubSubService: WorkstationId is empty, cannot start");
            return;
        }

        if (string.IsNullOrEmpty(_config.NegotiateUrl))
        {
            Logger.Log("WebPubSubService: NegotiateUrl is empty, cannot start");
            return;
        }

        _cts = new CancellationTokenSource();

        var clientOptions = new WebPubSubClientOptions
        {
            AutoReconnect = true,
        };

        _client = new WebPubSubClient(
            new WebPubSubClientCredential(ct => NegotiateAsync(ct)),
            clientOptions);

        _client.Connected += OnConnected;
        _client.Disconnected += OnDisconnected;
        _client.ServerMessageReceived += OnServerMessageReceived;

        Logger.Log($"WebPubSubService: starting for workstationId={_config.WorkstationId}");

        try
        {
            await _client.StartAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Log($"WebPubSubService: initial connection failed: {ex.Message}");
            _client.Connected -= OnConnected;
            _client.Disconnected -= OnDisconnected;
            _client.ServerMessageReceived -= OnServerMessageReceived;
            _client = null;
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
            throw;
        }
    }

    /// <summary>
    /// Stops the client and cleans up resources.
    /// </summary>
    public async Task StopAsync()
    {
        Logger.Log("WebPubSubService: stopping...");

        if (_cts != null)
        {
            _cts.Cancel();
        }

        if (_client != null)
        {
            _client.Connected -= OnConnected;
            _client.Disconnected -= OnDisconnected;
            _client.ServerMessageReceived -= OnServerMessageReceived;

            try
            {
                await _client.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Log($"WebPubSubService: error stopping client: {ex.Message}");
            }
        }

        IsConnected = false;
        Logger.Log("WebPubSubService: stopped");
    }

    /// <summary>
    /// POSTs to the negotiate endpoint with the workstationId as userId and returns
    /// the client access URL. Called by the SDK credential delegate on every (re)connect,
    /// ensuring tokens never expire mid-session.
    /// </summary>
    private async ValueTask<Uri> NegotiateAsync(CancellationToken ct)
    {
        Logger.Log($"WebPubSubService: negotiating token from {_config.NegotiateUrl}");

        var body = JsonSerializer.Serialize(new { userId = _config.WorkstationId });
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(_config.NegotiateUrl, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var url = doc.RootElement.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("Negotiate response missing 'url' field");

        Logger.Log("WebPubSubService: token negotiated successfully");
        return new Uri(url);
    }

    private void OnConnected(WebPubSubClient sender, WebPubSubConnectedEventArgs e)
    {
        IsConnected = true;
        Logger.Log($"WebPubSubService: connected (connectionId={e.ConnectionId})");
    }

    private void OnDisconnected(WebPubSubClient sender, WebPubSubDisconnectedEventArgs e)
    {
        IsConnected = false;
        Logger.Log($"WebPubSubService: disconnected — {e.DisconnectedMessage?.Message ?? "(no message)"}");
    }

    private void OnServerMessageReceived(WebPubSubClient sender, WebPubSubServerMessageEventArgs e)
    {
        try
        {
            if (e.Message.Data.IsEmpty)
            {
                Logger.Log("WebPubSubService: received empty message, ignoring");
                return;
            }

            var json = e.Message.Data.ToString();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp)
                || typeProp.GetString() != "workstation-api")
            {
                return;
            }

            if (!root.TryGetProperty("payload", out var payloadProp)
                || !payloadProp.TryGetProperty("url", out var urlProp))
            {
                Logger.Log("WebPubSubService: workstation-api message missing payload.url, ignoring");
                return;
            }

            var navUrl = urlProp.GetString();
            if (string.IsNullOrEmpty(navUrl))
            {
                Logger.Log("WebPubSubService: payload.url is empty, ignoring");
                return;
            }

            Logger.Log($"WebPubSubService: received navigation command — url={navUrl}");
            _ = _navigationService.NavigateAsync(navUrl);
        }
        catch (Exception ex)
        {
            Logger.Log($"WebPubSubService: error handling server message: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        if (_client != null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
        _cts?.Dispose();
        _httpClient.Dispose();
    }
}
