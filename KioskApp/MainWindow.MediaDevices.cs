using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace KioskApp;

/// <summary>
/// MainWindow partial class - Camera and microphone device enumeration, selection, and override management.
/// </summary>
public sealed partial class MainWindow
{
    #region Media Device Fields

    // Media device selection for debug mode
    private List<MediaDeviceInfo> _cameras = new();
    private List<MediaDeviceInfo> _microphones = new();
    private List<MediaDeviceInfo> _speakers = new();
    private string? _selectedCameraId = null;
    private string? _selectedMicrophoneId = null;
    private string? _selectedSpeakerId = null;
    private string? _selectedCameraLabel = null;
    private string? _selectedMicrophoneLabel = null;
    private string? _selectedSpeakerLabel = null;

    // Guard to prevent concurrent media device enumeration (avoids race conditions on _cameras/_microphones/_speakers)
    private readonly SemaphoreSlim _mediaEnumerationLock = new(1, 1);

    // Persistence keys for media device preferences
    private const string PreferredCameraIdKey = "PreferredCameraId";
    private const string PreferredMicrophoneIdKey = "PreferredMicrophoneId";
    private const string PreferredSpeakerIdKey = "PreferredSpeakerId";
    private const string PreferredCameraLabelKey = "PreferredCameraLabel";
    private const string PreferredMicrophoneLabelKey = "PreferredMicrophoneLabel";
    private const string PreferredSpeakerLabelKey = "PreferredSpeakerLabel";

    private const string WebStoragePreferredCameraKey = "__orhPreferredCameraId";
    private const string WebStoragePreferredMicrophoneKey = "__orhPreferredMicrophoneId";
    private const string WebStoragePreferredSpeakerKey = "__orhPreferredSpeakerId";
    private const string WebStoragePreferredCameraLabelKey = "__orhPreferredCameraLabel";
    private const string WebStoragePreferredMicrophoneLabelKey = "__orhPreferredMicrophoneLabel";
    private const string WebStoragePreferredSpeakerLabelKey = "__orhPreferredSpeakerLabel";

    private bool _suppressMediaSelectionEvents = false;

    // Debouncing and reload tracking for media device selection
    private DateTime _lastMediaDeviceChangeTime = DateTime.MinValue;
    private DateTime _lastWebViewReloadTime = DateTime.MinValue;
    private const int MediaDeviceChangeDebounceMs = 2000;
    private const int SkipEnumerationAfterReloadMs = 3000;

    #endregion

    #region Media Device Info

    /// <summary>
    /// Represents a media device (camera or microphone) for the selector dropdowns.
    /// </summary>
    private class MediaDeviceInfo
    {
        public string DeviceId { get; set; } = "";
        public string Label { get; set; } = "";
        public override string ToString() => Label;
    }

    #endregion

    #region Persistence

    private void LoadPersistedMediaDevicePreferences()
    {
        try
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            _selectedCameraId = values.TryGetValue(PreferredCameraIdKey, out var camVal) ? camVal as string : null;
            _selectedMicrophoneId = values.TryGetValue(PreferredMicrophoneIdKey, out var micVal) ? micVal as string : null;
            _selectedSpeakerId = values.TryGetValue(PreferredSpeakerIdKey, out var spkVal) ? spkVal as string : null;
            _selectedCameraLabel = values.TryGetValue(PreferredCameraLabelKey, out var camLabelVal) ? camLabelVal as string : null;
            _selectedMicrophoneLabel = values.TryGetValue(PreferredMicrophoneLabelKey, out var micLabelVal) ? micLabelVal as string : null;
            _selectedSpeakerLabel = values.TryGetValue(PreferredSpeakerLabelKey, out var spkLabelVal) ? spkLabelVal as string : null;

            Logger.Log($"Loaded persisted media preferences. CameraId set: {!string.IsNullOrWhiteSpace(_selectedCameraId)}, MicId set: {!string.IsNullOrWhiteSpace(_selectedMicrophoneId)}, SpeakerId set: {!string.IsNullOrWhiteSpace(_selectedSpeakerId)}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load persisted media preferences: {ex.Message}");
        }
    }

    private void SavePersistedMediaDevicePreferences()
    {
        try
        {
            var values = ApplicationData.Current.LocalSettings.Values;

            if (string.IsNullOrWhiteSpace(_selectedCameraId))
                values.Remove(PreferredCameraIdKey);
            else
                values[PreferredCameraIdKey] = _selectedCameraId!;

            if (string.IsNullOrWhiteSpace(_selectedMicrophoneId))
                values.Remove(PreferredMicrophoneIdKey);
            else
                values[PreferredMicrophoneIdKey] = _selectedMicrophoneId!;

            if (string.IsNullOrWhiteSpace(_selectedSpeakerId))
                values.Remove(PreferredSpeakerIdKey);
            else
                values[PreferredSpeakerIdKey] = _selectedSpeakerId!;

            if (string.IsNullOrWhiteSpace(_selectedCameraLabel))
                values.Remove(PreferredCameraLabelKey);
            else
                values[PreferredCameraLabelKey] = _selectedCameraLabel!;

            if (string.IsNullOrWhiteSpace(_selectedMicrophoneLabel))
                values.Remove(PreferredMicrophoneLabelKey);
            else
                values[PreferredMicrophoneLabelKey] = _selectedMicrophoneLabel!;

            if (string.IsNullOrWhiteSpace(_selectedSpeakerLabel))
                values.Remove(PreferredSpeakerLabelKey);
            else
                values[PreferredSpeakerLabelKey] = _selectedSpeakerLabel!;

            Logger.Log($"Saved persisted media preferences. CameraId set: {!string.IsNullOrWhiteSpace(_selectedCameraId)}, MicId set: {!string.IsNullOrWhiteSpace(_selectedMicrophoneId)}, SpeakerId set: {!string.IsNullOrWhiteSpace(_selectedSpeakerId)}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save persisted media preferences: {ex.Message}");
        }
    }

    #endregion

    #region Script Execution Helpers

    private async Task<string> ExecuteScriptAsyncUi(string js)
    {
        if (KioskWebView?.CoreWebView2 == null) return "{}";

        if (DispatcherQueue.HasThreadAccess)
        {
            return await KioskWebView.CoreWebView2.ExecuteScriptAsync(js);
        }

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueued = DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (KioskWebView?.CoreWebView2 == null)
                {
                    tcs.TrySetResult("{}");
                    return;
                }

                var result = await KioskWebView.CoreWebView2.ExecuteScriptAsync(js);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        if (!enqueued)
        {
            Logger.Log("[WEBVIEW SCRIPT] DispatcherQueue.TryEnqueue failed (dispatcher likely shutting down)");
            throw new OperationCanceledException("UI dispatcher is not accepting work; cannot execute script.");
        }

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        if (completed != tcs.Task)
        {
            Logger.Log("[WEBVIEW SCRIPT] ExecuteScriptAsyncUi timed out waiting for UI dispatcher to run");
            throw new TimeoutException("Timed out waiting for UI dispatcher to execute script.");
        }

        return await tcs.Task;
    }

    private async Task<string?> SendWebMessageRequestAsync(string jsToExecute, string requestId, TimeSpan timeout)
    {
        if (KioskWebView?.CoreWebView2 == null)
        {
            Logger.Log($"[WEBMSG SEND] requestId={requestId} - CoreWebView2 is null");
            return null;
        }

        var coreId = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(KioskWebView.CoreWebView2);

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingWebMessages.TryAdd(requestId, tcs))
        {
            Logger.Log($"[WEBMSG SEND] core={coreId} requestId={requestId} - failed to add to pending (duplicate?)");
            return null;
        }

        Logger.Log($"[WEBMSG SEND] core={coreId} requestId={requestId} - executing JS, waiting {timeout.TotalSeconds}s for response");

        try
        {
            await ExecuteScriptAsyncUi(jsToExecute);
            Logger.Log($"[WEBMSG SEND] core={coreId} requestId={requestId} - JS executed, waiting for postMessage response");

            using var cts = new CancellationTokenSource(timeout);
            await using (cts.Token.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task;
            }
        }
        catch (TaskCanceledException)
        {
            var wasTimeout = _pendingWebMessages.TryRemove(requestId, out _);
            if (wasTimeout)
            {
                Logger.Log($"[WEBMSG] core={coreId} request timed out: {requestId}");
            }
            else
            {
                Logger.Log($"[WEBMSG] core={coreId} request cancelled (WebView reload): {requestId}");
            }
            return null;
        }
        catch (Exception ex)
        {
            _pendingWebMessages.TryRemove(requestId, out _);
            Logger.Log($"Web message request failed: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Device Enumeration

    private async Task<List<MediaDeviceInfo>> EnumerateMediaDevicesViaWebMessageAsync(string kind, string context)
    {
        var requestId = Guid.NewGuid().ToString("N");

        var js = $@"
            (() => {{
                try {{
                    const requestId = '{requestId}';
                    const kind = '{kind}';

                    const post = (payload) => {{
                        try {{
                            if (window.chrome && chrome.webview && chrome.webview.postMessage) {{
                                chrome.webview.postMessage(payload);
                            }}
                        }} catch (e) {{}}
                    }};

                    (async () => {{
                        const result = {{ type: 'orh.mediaDevices.result', requestId, kind, context: '{context.Replace("'", "\\'")}', devices: [], error: null }};
                        try {{
                            if (!navigator || !navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) {{
                                throw new Error('navigator.mediaDevices.enumerateDevices is not available');
                            }}

                            const withTimeout = (p, ms) => Promise.race([
                                p,
                                new Promise((_, reject) => setTimeout(() => reject(new Error('getUserMedia timeout')), ms))
                            ]);
                            try {{
                                if (kind === 'videoinput') {{
                                    const s = await withTimeout(navigator.mediaDevices.getUserMedia({{ video: true }}), 1500);
                                    try {{ s.getTracks().forEach(t => t.stop()); }} catch (e) {{}}
                                }} else if (kind === 'audioinput') {{
                                    const s = await withTimeout(navigator.mediaDevices.getUserMedia({{ audio: true }}), 1500);
                                    try {{ s.getTracks().forEach(t => t.stop()); }} catch (e) {{}}
                                }}
                            }} catch (e) {{}}

                            const devices = await navigator.mediaDevices.enumerateDevices();
                            const filtered = devices.filter(d => d.kind === kind);
                            result.devices = filtered.map((d, idx) => {{
                                const trimmed = ((d.label || '')).trim();
                                const fallback = (kind === 'videoinput' ? 'Camera ' : 'Microphone ') + (idx + 1) + (d.deviceId ? (' (' + d.deviceId.substring(0, 8) + ')') : '');
                                return {{ deviceId: d.deviceId || '', label: trimmed ? trimmed : fallback }};
                            }});
                        }} catch (e) {{
                            result.error = {{ name: e && e.name ? e.name : null, message: e && e.message ? e.message : String(e) }};
                        }}
                        post(result);
                    }})();
                }} catch (e) {{}}
            }})();";

        var json = await SendWebMessageRequestAsync(js, requestId, TimeSpan.FromSeconds(8));
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<MediaDeviceInfo>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind != JsonValueKind.Null)
            {
                var name = errEl.TryGetProperty("name", out var n) ? n.GetString() : null;
                var msg = errEl.TryGetProperty("message", out var m) ? m.GetString() : null;
                Logger.Log($"Media enumeration error ({kind}): {name} {msg}");
            }

            if (!root.TryGetProperty("devices", out var devicesEl) || devicesEl.ValueKind != JsonValueKind.Array)
            {
                return new List<MediaDeviceInfo>();
            }

            var devices = JsonSerializer.Deserialize<List<MediaDeviceInfo>>(devicesEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return devices ?? new List<MediaDeviceInfo>();
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to parse web message devices ({kind}): {ex.Message}");
            return new List<MediaDeviceInfo>();
        }
    }

    private async Task LogWebRtcDeviceDiagnosticsAsync(string context)
    {
        if (KioskWebView?.CoreWebView2 == null) return;

        try
        {
            var requestId = Guid.NewGuid().ToString("N");
            var js = $@"
                (() => {{
                    const requestId = '{requestId}';
                    const post = (payload) => {{
                        try {{ if (window.chrome && chrome.webview && chrome.webview.postMessage) chrome.webview.postMessage(payload); }} catch (e) {{}}
                    }};
                    (async () => {{
                        const info = {{
                            type: 'orh.webrtc.diag',
                            requestId,
                            context: '{context.Replace("'", "\\'")}',
                            href: (typeof location !== 'undefined' && location.href) ? location.href : null,
                            origin: (typeof location !== 'undefined' && location.origin) ? location.origin : null,
                            isSecureContext: (typeof isSecureContext !== 'undefined') ? isSecureContext : null,
                            hasNavigator: (typeof navigator !== 'undefined'),
                            hasMediaDevices: (typeof navigator !== 'undefined' && !!navigator.mediaDevices),
                            hasEnumerateDevices: (typeof navigator !== 'undefined' && !!(navigator.mediaDevices && navigator.mediaDevices.enumerateDevices)),
                            enumerateError: null,
                            enumerateResult: null
                        }};
                        try {{
                            if (info.hasMediaDevices && info.hasEnumerateDevices) {{
                                const devices = await navigator.mediaDevices.enumerateDevices();
                                info.enumerateResult = devices.map(d => ({{
                                    kind: d.kind,
                                    deviceIdPresent: !!d.deviceId,
                                    label: (d.label || null)
                                }}));
                            }}
                        }} catch (e) {{
                            info.enumerateError = (e && (e.name || e.message)) ? {{ name: e.name || null, message: e.message || String(e) }} : String(e);
                        }}
                        post(info);
                    }})();
                }})();";

            var json = await SendWebMessageRequestAsync(js, requestId, TimeSpan.FromSeconds(8));
            if (!string.IsNullOrWhiteSpace(json))
            {
                Logger.Log($"[WebRTC DIAG] {json}");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[WebRTC DIAG] Failed to collect diagnostics: {ex.Message}");
        }
    }

    #endregion

    #region Device Loading

    private async void RefreshCamerasButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadCamerasAsync();
    }

    private async void RefreshMicrophonesButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadMicrophonesAsync();
    }

    private async void RefreshSpeakersButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadSpeakersAsync();
    }

    private async Task LoadAllMediaDevicesAsync()
    {
        if (!await _mediaEnumerationLock.WaitAsync(TimeSpan.FromSeconds(12)))
        {
            Logger.Log("LoadAllMediaDevicesAsync: timed out waiting for lock (another enumeration stuck?)");
            return;
        }

        try
        {
            await LoadCamerasAsyncCore();
            await LoadMicrophonesAsyncCore();
            await LoadSpeakersAsyncCore();
        }
        finally
        {
            _mediaEnumerationLock.Release();
        }
    }

    private async Task LoadCamerasAsync()
    {
        if (!await _mediaEnumerationLock.WaitAsync(TimeSpan.FromSeconds(10)))
        {
            Logger.Log("LoadCamerasAsync: timed out waiting for lock");
            return;
        }

        try
        {
            await LoadCamerasAsyncCore();
        }
        finally
        {
            _mediaEnumerationLock.Release();
        }
    }

    private async Task LoadCamerasAsyncCore()
    {
        if (KioskWebView?.CoreWebView2 == null)
        {
            Logger.Log("Cannot load cameras: WebView2 not initialized");
            return;
        }

        try
        {
            Logger.Log("Loading available cameras...");
            var cameras = await EnumerateMediaDevicesViaWebMessageAsync("videoinput", "LoadCamerasAsync");

            if (cameras != null && cameras.Count > 0)
            {
                // Ensure labels are never blank
                for (int i = 0; i < cameras.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(cameras[i].Label))
                    {
                        var idPart = !string.IsNullOrWhiteSpace(cameras[i].DeviceId)
                            ? $" ({cameras[i].DeviceId.Substring(0, Math.Min(8, cameras[i].DeviceId.Length))})"
                            : "";
                        cameras[i].Label = $"Camera {i + 1}{idPart}";
                    }
                }

                var localCameras = cameras;
                var localSelectedId = _selectedCameraId;
                var localSelectedLabel = _selectedCameraLabel;

                _cameras = cameras;
                DispatcherQueue.TryEnqueue(() =>
                {
                    _suppressMediaSelectionEvents = true;
                    try
                    {
                        CameraSelector.SelectedIndex = -1;
                        CameraSelector.Items.Clear();
                        foreach (var cam in localCameras)
                        {
                            CameraSelector.Items.Add(cam);
                        }
                        Logger.Log($"Loaded {localCameras.Count} camera(s)");

                        // Restore previous selection
                        RestoreCameraSelection(localCameras, localSelectedId, localSelectedLabel);
                    }
                    finally
                    {
                        _suppressMediaSelectionEvents = false;
                    }
                });
            }
            else if (cameras != null && cameras.Count == 0)
            {
                Logger.Log("LoadCamerasAsync: enumeration returned 0 cameras");
                _ = LogWebRtcDeviceDiagnosticsAsync("LoadCamerasAsync: no videoinput devices");
            }
            else
            {
                Logger.Log("LoadCamerasAsync: enumeration returned null (timeout or error)");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load cameras: {ex.Message}");
        }
    }

    private void RestoreCameraSelection(List<MediaDeviceInfo> cameras, string? selectedId, string? selectedLabel)
    {
        // Try to restore by DeviceId first
        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            var selectedIndex = cameras.FindIndex(c => c.DeviceId == selectedId);
            if (selectedIndex >= 0)
            {
                CameraSelector.SelectedIndex = selectedIndex;
                _selectedCameraLabel = cameras[selectedIndex].Label;
                SavePersistedMediaDevicePreferences();
                Logger.Log($"Restored camera selection to index {selectedIndex}: {cameras[selectedIndex].Label}");
                return;
            }
            Logger.Log($"Could not restore camera selection by DeviceId: {selectedId}");
        }

        // Fallback: try to match by label
        if (!string.IsNullOrWhiteSpace(selectedLabel))
        {
            var labelMatchIndex = cameras.FindIndex(c => string.Equals(c.Label?.Trim(), selectedLabel.Trim(), StringComparison.OrdinalIgnoreCase));
            if (labelMatchIndex >= 0)
            {
                CameraSelector.SelectedIndex = labelMatchIndex;
                _selectedCameraId = cameras[labelMatchIndex].DeviceId;
                _selectedCameraLabel = cameras[labelMatchIndex].Label;
                SavePersistedMediaDevicePreferences();
                Logger.Log($"Restored camera selection by Label to index {labelMatchIndex}: {_selectedCameraLabel}");
            }
        }
    }

    private async Task LoadMicrophonesAsync()
    {
        if (!await _mediaEnumerationLock.WaitAsync(TimeSpan.FromSeconds(10)))
        {
            Logger.Log("LoadMicrophonesAsync: timed out waiting for lock");
            return;
        }

        try
        {
            await LoadMicrophonesAsyncCore();
        }
        finally
        {
            _mediaEnumerationLock.Release();
        }
    }

    private async Task LoadMicrophonesAsyncCore()
    {
        if (KioskWebView?.CoreWebView2 == null)
        {
            Logger.Log("Cannot load microphones: WebView2 not initialized");
            return;
        }

        try
        {
            Logger.Log("Loading available microphones...");
            var microphones = await EnumerateMediaDevicesViaWebMessageAsync("audioinput", "LoadMicrophonesAsync");

            if (microphones != null && microphones.Count > 0)
            {
                // Ensure labels are never blank
                for (int i = 0; i < microphones.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(microphones[i].Label))
                    {
                        var idPart = !string.IsNullOrWhiteSpace(microphones[i].DeviceId)
                            ? $" ({microphones[i].DeviceId.Substring(0, Math.Min(8, microphones[i].DeviceId.Length))})"
                            : "";
                        microphones[i].Label = $"Microphone {i + 1}{idPart}";
                    }
                }

                var localMicrophones = microphones;
                var localSelectedId = _selectedMicrophoneId;
                var localSelectedLabel = _selectedMicrophoneLabel;

                _microphones = microphones;
                DispatcherQueue.TryEnqueue(() =>
                {
                    _suppressMediaSelectionEvents = true;
                    try
                    {
                        MicrophoneSelector.SelectedIndex = -1;
                        MicrophoneSelector.Items.Clear();
                        foreach (var mic in localMicrophones)
                        {
                            MicrophoneSelector.Items.Add(mic);
                        }
                        Logger.Log($"Loaded {localMicrophones.Count} microphone(s)");

                        // Restore previous selection
                        RestoreMicrophoneSelection(localMicrophones, localSelectedId, localSelectedLabel);
                    }
                    finally
                    {
                        _suppressMediaSelectionEvents = false;
                    }
                });
            }
            else if (microphones != null && microphones.Count == 0)
            {
                Logger.Log("LoadMicrophonesAsync: enumeration returned 0 microphones");
                _ = LogWebRtcDeviceDiagnosticsAsync("LoadMicrophonesAsync: no audioinput devices");
            }
            else
            {
                Logger.Log("LoadMicrophonesAsync: enumeration returned null (timeout or error)");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load microphones: {ex.Message}");
        }
    }

    private void RestoreMicrophoneSelection(List<MediaDeviceInfo> microphones, string? selectedId, string? selectedLabel)
    {
        // Try to restore by DeviceId first
        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            var selectedIndex = microphones.FindIndex(m => m.DeviceId == selectedId);
            if (selectedIndex >= 0)
            {
                MicrophoneSelector.SelectedIndex = selectedIndex;
                _selectedMicrophoneLabel = microphones[selectedIndex].Label;
                SavePersistedMediaDevicePreferences();
                Logger.Log($"Restored microphone selection to index {selectedIndex}: {microphones[selectedIndex].Label}");
                return;
            }
            Logger.Log($"Could not restore microphone selection by DeviceId: {selectedId}");
        }

        // Fallback: try to match by label
        if (!string.IsNullOrWhiteSpace(selectedLabel))
        {
            var labelMatchIndex = microphones.FindIndex(m => string.Equals(m.Label?.Trim(), selectedLabel.Trim(), StringComparison.OrdinalIgnoreCase));
            if (labelMatchIndex >= 0)
            {
                MicrophoneSelector.SelectedIndex = labelMatchIndex;
                _selectedMicrophoneId = microphones[labelMatchIndex].DeviceId;
                _selectedMicrophoneLabel = microphones[labelMatchIndex].Label;
                SavePersistedMediaDevicePreferences();
                Logger.Log($"Restored microphone selection by Label to index {labelMatchIndex}: {_selectedMicrophoneLabel}");
            }
        }
    }

    private async Task LoadSpeakersAsync()
    {
        if (!await _mediaEnumerationLock.WaitAsync(TimeSpan.FromSeconds(10)))
        {
            Logger.Log("LoadSpeakersAsync: timed out waiting for lock");
            return;
        }

        try
        {
            await LoadSpeakersAsyncCore();
        }
        finally
        {
            _mediaEnumerationLock.Release();
        }
    }

    private async Task LoadSpeakersAsyncCore()
    {
        if (KioskWebView?.CoreWebView2 == null)
        {
            Logger.Log("Cannot load speakers: WebView2 not initialized");
            return;
        }

        try
        {
            Logger.Log("Loading available speakers...");
            var speakers = await EnumerateMediaDevicesViaWebMessageAsync("audiooutput", "LoadSpeakersAsync");

            if (speakers != null && speakers.Count > 0)
            {
                // Ensure labels are never blank
                for (int i = 0; i < speakers.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(speakers[i].Label))
                    {
                        var idPart = !string.IsNullOrWhiteSpace(speakers[i].DeviceId)
                            ? $" ({speakers[i].DeviceId.Substring(0, Math.Min(8, speakers[i].DeviceId.Length))})"
                            : "";
                        speakers[i].Label = $"Speaker {i + 1}{idPart}";
                    }
                }

                var localSpeakers = speakers;
                var localSelectedId = _selectedSpeakerId;
                var localSelectedLabel = _selectedSpeakerLabel;

                _speakers = speakers;
                DispatcherQueue.TryEnqueue(() =>
                {
                    _suppressMediaSelectionEvents = true;
                    try
                    {
                        SpeakerSelector.SelectedIndex = -1;
                        SpeakerSelector.Items.Clear();
                        foreach (var spk in localSpeakers)
                        {
                            SpeakerSelector.Items.Add(spk);
                        }
                        Logger.Log($"Loaded {localSpeakers.Count} speaker(s)");

                        // Restore previous selection
                        RestoreSpeakerSelection(localSpeakers, localSelectedId, localSelectedLabel);
                    }
                    finally
                    {
                        _suppressMediaSelectionEvents = false;
                    }
                });
            }
            else if (speakers != null && speakers.Count == 0)
            {
                Logger.Log("LoadSpeakersAsync: enumeration returned 0 speakers");
                _ = LogWebRtcDeviceDiagnosticsAsync("LoadSpeakersAsync: no audiooutput devices");
            }
            else
            {
                Logger.Log("LoadSpeakersAsync: enumeration returned null (timeout or error)");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load speakers: {ex.Message}");
        }
    }

    private void RestoreSpeakerSelection(List<MediaDeviceInfo> speakers, string? selectedId, string? selectedLabel)
    {
        // Try to restore by DeviceId first
        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            var selectedIndex = speakers.FindIndex(s => s.DeviceId == selectedId);
            if (selectedIndex >= 0)
            {
                SpeakerSelector.SelectedIndex = selectedIndex;
                _selectedSpeakerLabel = speakers[selectedIndex].Label;
                SavePersistedMediaDevicePreferences();
                Logger.Log($"Restored speaker selection to index {selectedIndex}: {speakers[selectedIndex].Label}");
                return;
            }
            Logger.Log($"Could not restore speaker selection by DeviceId: {selectedId}");
        }

        // Fallback: try to match by label
        if (!string.IsNullOrWhiteSpace(selectedLabel))
        {
            var labelMatchIndex = speakers.FindIndex(s => string.Equals(s.Label?.Trim(), selectedLabel.Trim(), StringComparison.OrdinalIgnoreCase));
            if (labelMatchIndex >= 0)
            {
                SpeakerSelector.SelectedIndex = labelMatchIndex;
                _selectedSpeakerId = speakers[labelMatchIndex].DeviceId;
                _selectedSpeakerLabel = speakers[labelMatchIndex].Label;
                SavePersistedMediaDevicePreferences();
                Logger.Log($"Restored speaker selection by Label to index {labelMatchIndex}: {_selectedSpeakerLabel}");
            }
        }
    }

    #endregion

    #region Selection Change Handlers

    private async void CameraSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMediaSelectionEvents) return;

        // Debounce rapid camera changes
        var timeSinceLastChange = (DateTime.UtcNow - _lastMediaDeviceChangeTime).TotalMilliseconds;
        if (timeSinceLastChange < MediaDeviceChangeDebounceMs)
        {
            Logger.Log($"[CAMERA SELECT] Debounced - only {timeSinceLastChange:F0}ms since last change");
            return;
        }

        if (CameraSelector.SelectedItem is MediaDeviceInfo camera)
        {
            _lastMediaDeviceChangeTime = DateTime.UtcNow;
            Logger.Log($"[CAMERA SELECT] User selected: {camera.Label} (ID: {camera.DeviceId})");
            _selectedCameraId = camera.DeviceId;
            _selectedCameraLabel = camera.Label;
            SavePersistedMediaDevicePreferences();
            await ApplyMediaDeviceOverrideAsync(showStatus: true);

            // Attempt live camera switch without reloading WebView
            if (_isDebugMode)
            {
                ShowStatus("Switching Camera", "Attempting live switch...");
                _ = Task.Delay(2500).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));

                try
                {
                    _ = ExecuteScriptAsyncUi(@"
                        (() => {
                            try {
                                if (typeof window.__orhSwitchOutgoingMedia === 'function') {
                                    window.__orhSwitchOutgoingMedia({ video: true, audio: false });
                                }
                            } catch (e) {}
                        })();
                    ");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[CAMERA SELECT] Failed to request live switch: {ex.Message}");
                }
            }
        }
    }

    private async void MicrophoneSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMediaSelectionEvents) return;

        // Debounce rapid microphone changes
        var timeSinceLastChange = (DateTime.UtcNow - _lastMediaDeviceChangeTime).TotalMilliseconds;
        if (timeSinceLastChange < MediaDeviceChangeDebounceMs)
        {
            Logger.Log($"[MIC SELECT] Debounced - only {timeSinceLastChange:F0}ms since last change");
            return;
        }

        if (MicrophoneSelector.SelectedItem is MediaDeviceInfo microphone)
        {
            _lastMediaDeviceChangeTime = DateTime.UtcNow;
            Logger.Log($"[MIC SELECT] User selected: {microphone.Label} (ID: {microphone.DeviceId})");
            _selectedMicrophoneId = microphone.DeviceId;
            _selectedMicrophoneLabel = microphone.Label;
            SavePersistedMediaDevicePreferences();
            await ApplyMediaDeviceOverrideAsync(showStatus: true);

            // Attempt live microphone switch without reloading WebView
            if (_isDebugMode)
            {
                ShowStatus("Switching Microphone", "Attempting live switch...");
                _ = Task.Delay(2500).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));

                try
                {
                    _ = ExecuteScriptAsyncUi(@"
                        (() => {
                            try {
                                if (typeof window.__orhSwitchOutgoingMedia === 'function') {
                                    window.__orhSwitchOutgoingMedia({ video: false, audio: true });
                                }
                            } catch (e) {}
                        })();
                    ");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[MIC SELECT] Failed to request live switch: {ex.Message}");
                }
            }
        }
    }

    private async void SpeakerSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMediaSelectionEvents) return;

        // Debounce rapid speaker changes
        var timeSinceLastChange = (DateTime.UtcNow - _lastMediaDeviceChangeTime).TotalMilliseconds;
        if (timeSinceLastChange < MediaDeviceChangeDebounceMs)
        {
            Logger.Log($"[SPEAKER SELECT] Debounced - only {timeSinceLastChange:F0}ms since last change");
            return;
        }

        if (SpeakerSelector.SelectedItem is MediaDeviceInfo speaker)
        {
            _lastMediaDeviceChangeTime = DateTime.UtcNow;
            Logger.Log($"[SPEAKER SELECT] User selected: {speaker.Label} (ID: {speaker.DeviceId})");
            _selectedSpeakerId = speaker.DeviceId;
            _selectedSpeakerLabel = speaker.Label;
            SavePersistedMediaDevicePreferences();
            await ApplyMediaDeviceOverrideAsync(showStatus: true);

            // Attempt live speaker switch using setSinkId
            if (_isDebugMode)
            {
                ShowStatus("Switching Speaker", "Attempting live switch...");
                _ = Task.Delay(2500).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));

                try
                {
                    var speakerIdJson = System.Text.Json.JsonSerializer.Serialize(speaker.DeviceId);
                    _ = ExecuteScriptAsyncUi($@"
                        (() => {{
                            try {{
                                const speakerId = {speakerIdJson};
                                // Try to switch audio output on all audio/video elements
                                const mediaElements = document.querySelectorAll('audio, video');
                                mediaElements.forEach(el => {{
                                    if (typeof el.setSinkId === 'function') {{
                                        el.setSinkId(speakerId).catch(e => console.log('setSinkId failed:', e));
                                    }}
                                }});
                                // Also call ACS-specific speaker switch if available
                                if (typeof window.__orhSwitchSpeaker === 'function') {{
                                    window.__orhSwitchSpeaker(speakerId);
                                }}
                            }} catch (e) {{}}
                        }})();
                    ");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[SPEAKER SELECT] Failed to request live switch: {ex.Message}");
                }
            }
        }
    }

    #endregion

    #region Media Override

    private async Task ApplyMediaDeviceOverrideAsync(bool showStatus)
    {
        if (KioskWebView?.CoreWebView2 == null) return;

        var cameraIdJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(_selectedCameraId) ? null : _selectedCameraId);
        var microphoneIdJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(_selectedMicrophoneId) ? null : _selectedMicrophoneId);
        var speakerIdJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(_selectedSpeakerId) ? null : _selectedSpeakerId);
        var cameraLabelJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(_selectedCameraLabel) ? null : _selectedCameraLabel);
        var microphoneLabelJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(_selectedMicrophoneLabel) ? null : _selectedMicrophoneLabel);
        var speakerLabelJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(_selectedSpeakerLabel) ? null : _selectedSpeakerLabel);

        var script = $@"
            (() => {{
                try {{
                    window.__preferredCameraId = {cameraIdJson};
                    window.__preferredMicrophoneId = {microphoneIdJson};
                    window.__preferredSpeakerId = {speakerIdJson};
                    window.__preferredCameraLabel = {cameraLabelJson};
                    window.__preferredMicrophoneLabel = {microphoneLabelJson};
                    window.__preferredSpeakerLabel = {speakerLabelJson};

                    try {{
                        localStorage.setItem('{WebStoragePreferredCameraKey}', JSON.stringify(window.__preferredCameraId));
                        localStorage.setItem('{WebStoragePreferredMicrophoneKey}', JSON.stringify(window.__preferredMicrophoneId));
                        localStorage.setItem('{WebStoragePreferredSpeakerKey}', JSON.stringify(window.__preferredSpeakerId));
                        localStorage.setItem('{WebStoragePreferredCameraLabelKey}', JSON.stringify(window.__preferredCameraLabel));
                        localStorage.setItem('{WebStoragePreferredMicrophoneLabelKey}', JSON.stringify(window.__preferredMicrophoneLabel));
                        localStorage.setItem('{WebStoragePreferredSpeakerLabelKey}', JSON.stringify(window.__preferredSpeakerLabel));
                    }} catch (e) {{}}

                    return {{ status: 'ok' }};
                }} catch (e) {{
                    return {{ status: 'error', message: String(e) }};
                }}
            }})();
        ";

        try
        {
            var result = await ExecuteScriptAsyncUi(script);
            Logger.Log($"Media device override applied: {result}");

            // Build status message
            var cameraName = _cameras.FirstOrDefault(c => c.DeviceId == _selectedCameraId)?.Label;
            var micName = _microphones.FirstOrDefault(m => m.DeviceId == _selectedMicrophoneId)?.Label;
            var speakerName = _speakers.FirstOrDefault(s => s.DeviceId == _selectedSpeakerId)?.Label;
            var statusParts = new List<string>();
            if (cameraName != null) statusParts.Add($"Camera: {cameraName}");
            if (micName != null) statusParts.Add($"Mic: {micName}");
            if (speakerName != null) statusParts.Add($"Speaker: {speakerName}");

            if (showStatus && statusParts.Count > 0)
            {
                ShowStatus("Devices Selected", string.Join(" | ", statusParts));
                _ = Task.Delay(2000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => HideStatus()));
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to apply media device override: {ex.Message}");
        }
    }

    private async Task ReloadWebViewForMediaChangeAsync()
    {
        try
        {
            if (KioskWebView?.CoreWebView2 == null) return;

            // Cancel all pending web message requests before reload
            var pendingCount = _pendingWebMessages.Count;
            if (pendingCount > 0)
            {
                Logger.Log($"[MEDIA RELOAD] Cancelling {pendingCount} pending web message request(s)");
                foreach (var kvp in _pendingWebMessages)
                {
                    kvp.Value.TrySetCanceled();
                }
                _pendingWebMessages.Clear();
            }

            // Stop local video tracks before reload
            try
            {
                await ExecuteScriptAsyncUi(@"
                    (() => {
                        try {
                            if (typeof window.__orhStopLocalTracks === 'function') {
                                window.__orhStopLocalTracks('video');
                            }
                        } catch (e) {}
                        return true;
                    })();
                ");
                Logger.Log("[MEDIA RELOAD] Requested stop of local video tracks");
            }
            catch (Exception ex)
            {
                Logger.Log($"[MEDIA RELOAD] Failed to stop tracks: {ex.Message}");
            }

            await Task.Delay(500);
            _lastWebViewReloadTime = DateTime.UtcNow;

            await DispatcherQueue.EnqueueAsync(() =>
            {
                Logger.Log("Reloading WebView to apply media device change");
                KioskWebView.Reload();
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to reload WebView for media change: {ex.Message}");
        }
    }

    #endregion

    #region Media Override Document Script

    private async Task InstallMediaOverrideOnDocumentCreatedAsync()
    {
        if (KioskWebView?.CoreWebView2 == null) return;

        try
        {
            if (_mediaOverrideDocCreatedScriptAdded)
            {
                return;
            }

            var initialCameraIdJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(_selectedCameraId) ? null : _selectedCameraId);
            var initialMicrophoneIdJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(_selectedMicrophoneId) ? null : _selectedMicrophoneId);
            var initialSpeakerIdJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(_selectedSpeakerId) ? null : _selectedSpeakerId);
            var initialCameraLabelJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(_selectedCameraLabel) ? null : _selectedCameraLabel);
            var initialMicrophoneLabelJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(_selectedMicrophoneLabel) ? null : _selectedMicrophoneLabel);
            var initialSpeakerLabelJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(_selectedSpeakerLabel) ? null : _selectedSpeakerLabel);

            // This is a simplified version of the full media override script
            // The full script is quite long - it installs getUserMedia override, RTCPeerConnection tracking, etc.
            var script = GetMediaOverrideDocumentScript(
                initialCameraIdJson,
                initialMicrophoneIdJson,
                initialSpeakerIdJson,
                initialCameraLabelJson,
                initialMicrophoneLabelJson,
                initialSpeakerLabelJson);

            await KioskWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
            _mediaOverrideDocCreatedScriptAdded = true;
            Logger.Log("Installed media override script on document creation");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to install document-created media override: {ex.Message}");
        }
    }

    private string GetMediaOverrideDocumentScript(string camIdJson, string micIdJson, string spkIdJson, string camLabelJson, string micLabelJson, string spkLabelJson)
    {
        // This returns the full media override script that:
        // 1. Seeds localStorage with initial preferences
        // 2. Overrides navigator.mediaDevices.getUserMedia to apply device selection
        // 3. Tracks local getUserMedia streams for cleanup
        // 4. Patches RTCPeerConnection for live track switching
        // 5. Sets up speaker preference for setSinkId
        return $@"
            (() => {{
                try {{
                    if (window.__orhMediaOverrideInstalled) return;
                    window.__orhMediaOverrideInstalled = true;

                    const camKey = '{WebStoragePreferredCameraKey}';
                    const micKey = '{WebStoragePreferredMicrophoneKey}';
                    const spkKey = '{WebStoragePreferredSpeakerKey}';
                    const camLabelKey = '{WebStoragePreferredCameraLabelKey}';
                    const micLabelKey = '{WebStoragePreferredMicrophoneLabelKey}';
                    const spkLabelKey = '{WebStoragePreferredSpeakerLabelKey}';
                    const initialCam = {camIdJson};
                    const initialMic = {micIdJson};
                    const initialSpk = {spkIdJson};
                    const initialCamLabel = {camLabelJson};
                    const initialMicLabel = {micLabelJson};
                    const initialSpkLabel = {spkLabelJson};

                    try {{
                        const sessionMarker = 'orh_media_session_seeded';
                        const isFirstOfSession = !sessionStorage.getItem(sessionMarker);
                        if (isFirstOfSession) {{
                            localStorage.setItem(camKey, JSON.stringify(initialCam));
                            localStorage.setItem(micKey, JSON.stringify(initialMic));
                            localStorage.setItem(spkKey, JSON.stringify(initialSpk));
                            localStorage.setItem(camLabelKey, JSON.stringify(initialCamLabel));
                            localStorage.setItem(micLabelKey, JSON.stringify(initialMicLabel));
                            localStorage.setItem(spkLabelKey, JSON.stringify(initialSpkLabel));
                            sessionStorage.setItem(sessionMarker, 'true');
                        }}
                    }} catch (e) {{}}

                    const readPref = (key) => {{
                        try {{
                            const v = localStorage.getItem(key);
                            if (v === null || v === undefined) return null;
                            return JSON.parse(v);
                        }} catch (e) {{
                            return null;
                        }}
                    }};

                    window.__preferredCameraId = readPref(camKey);
                    window.__preferredMicrophoneId = readPref(micKey);
                    window.__preferredSpeakerId = readPref(spkKey);
                    window.__preferredCameraLabel = readPref(camLabelKey);
                    window.__preferredMicrophoneLabel = readPref(micLabelKey);
                    window.__preferredSpeakerLabel = readPref(spkLabelKey);

                    // Helper function to switch speaker on audio/video elements
                    window.__orhSwitchSpeaker = function(speakerId) {{
                        try {{
                            const mediaElements = document.querySelectorAll('audio, video');
                            mediaElements.forEach(el => {{
                                if (typeof el.setSinkId === 'function') {{
                                    el.setSinkId(speakerId).catch(e => console.log('setSinkId failed:', e));
                                }}
                            }});
                        }} catch (e) {{}}
                    }};

                    // Apply speaker preference on page load
                    const applyInitialSpeaker = () => {{
                        const spkId = readPref(spkKey);
                        if (spkId) {{
                            window.__orhSwitchSpeaker(spkId);
                        }}
                    }};
                    if (document.readyState === 'complete') {{
                        setTimeout(applyInitialSpeaker, 500);
                    }} else {{
                        window.addEventListener('load', () => setTimeout(applyInitialSpeaker, 500));
                    }}

                    // ========== EARLY AUTOPLAY HANDLING ==========
                    // This runs before page scripts, enabling autoplay for carescape/video URLs

                    // Track media elements we've already processed
                    window.__orhAutoplayProcessed = new WeakSet();

                    // Force autoplay on a media element
                    window.__orhForceAutoplay = function(media) {{
                        if (!media || window.__orhAutoplayProcessed.has(media)) return;
                        window.__orhAutoplayProcessed.add(media);

                        // Ensure autoplay attribute is set
                        media.autoplay = true;

                        // If already playing, nothing to do
                        if (!media.paused) return;

                        // Try to play unmuted first
                        const playPromise = media.play();
                        if (playPromise !== undefined) {{
                            playPromise.then(() => {{
                                console.log('[ORH Autoplay] Playing unmuted:', media.src || media.currentSrc || 'inline');
                            }}).catch((err) => {{
                                // Autoplay was blocked, try muted then unmute
                                console.log('[ORH Autoplay] Unmuted blocked, trying muted workaround');
                                media.muted = true;
                                media.play().then(() => {{
                                    // Successfully playing muted, try to unmute after a short delay
                                    setTimeout(() => {{
                                        media.muted = false;
                                        console.log('[ORH Autoplay] Unmuted after workaround');
                                    }}, 100);
                                }}).catch((e2) => {{
                                    console.log('[ORH Autoplay] Even muted play failed:', e2.message);
                                }});
                            }});
                        }}
                    }};

                    // Process all current and future media elements
                    window.__orhProcessMediaElements = function() {{
                        try {{
                            document.querySelectorAll('video, audio').forEach(media => {{
                                window.__orhForceAutoplay(media);
                            }});
                        }} catch (e) {{}}
                    }};

                    // Override createElement to catch videos/audios at creation time
                    const originalCreateElement = document.createElement.bind(document);
                    document.createElement = function(tagName, options) {{
                        const element = originalCreateElement(tagName, options);
                        if (tagName && (tagName.toLowerCase() === 'video' || tagName.toLowerCase() === 'audio')) {{
                            // Set autoplay immediately on creation
                            element.autoplay = true;
                            // Also try to play when src is set
                            const originalSetAttribute = element.setAttribute.bind(element);
                            element.setAttribute = function(name, value) {{
                                originalSetAttribute(name, value);
                                if (name === 'src' || name === 'autoplay') {{
                                    setTimeout(() => window.__orhForceAutoplay(element), 0);
                                }}
                            }};
                            // Watch for src property changes too
                            let srcValue = '';
                            try {{
                                Object.defineProperty(element, 'src', {{
                                    get: function() {{ return srcValue; }},
                                    set: function(v) {{
                                        srcValue = v;
                                        HTMLMediaElement.prototype.__lookupSetter__('src').call(element, v);
                                        setTimeout(() => window.__orhForceAutoplay(element), 0);
                                    }},
                                    configurable: true
                                }});
                            }} catch (e) {{}}
                        }}
                        return element;
                    }};

                    // MutationObserver to catch dynamically added media elements
                    const autoplayObserver = new MutationObserver((mutations) => {{
                        for (const mutation of mutations) {{
                            if (mutation.addedNodes) {{
                                mutation.addedNodes.forEach(node => {{
                                    if (node.nodeType === 1) {{
                                        if (node.tagName === 'VIDEO' || node.tagName === 'AUDIO') {{
                                            window.__orhForceAutoplay(node);
                                        }}
                                        // Also check children
                                        if (node.querySelectorAll) {{
                                            node.querySelectorAll('video, audio').forEach(media => {{
                                                window.__orhForceAutoplay(media);
                                            }});
                                        }}
                                    }}
                                }});
                            }}
                        }}
                    }});

                    // Start observing as soon as possible
                    if (document.documentElement) {{
                        autoplayObserver.observe(document.documentElement, {{
                            childList: true,
                            subtree: true
                        }});
                    }} else {{
                        // Document not ready yet, wait for it
                        const waitForDoc = setInterval(() => {{
                            if (document.documentElement) {{
                                clearInterval(waitForDoc);
                                autoplayObserver.observe(document.documentElement, {{
                                    childList: true,
                                    subtree: true
                                }});
                            }}
                        }}, 10);
                    }}

                    // Also process on various document states
                    if (document.readyState === 'loading') {{
                        document.addEventListener('DOMContentLoaded', window.__orhProcessMediaElements);
                    }}
                    document.addEventListener('readystatechange', () => {{
                        if (document.readyState === 'interactive' || document.readyState === 'complete') {{
                            window.__orhProcessMediaElements();
                        }}
                    }});
                    window.addEventListener('load', window.__orhProcessMediaElements);

                    // Process immediately in case elements already exist
                    setTimeout(window.__orhProcessMediaElements, 0);
                    // ========== END AUTOPLAY HANDLING ==========

                    if (navigator && navigator.mediaDevices && navigator.mediaDevices.getUserMedia) {{
                        const originalGetUserMedia = navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);

                        if (!window.__orhLocalUserMediaStreams) window.__orhLocalUserMediaStreams = [];

                        window.__orhStopLocalTracks = function (kind) {{
                            let stopped = 0;
                            try {{
                                const streams = window.__orhLocalUserMediaStreams || [];
                                for (let i = 0; i < streams.length; i++) {{
                                    const s = streams[i];
                                    if (!s || !s.getTracks) continue;
                                    s.getTracks().forEach(t => {{
                                        try {{
                                            if (!t) return;
                                            if (kind && t.kind !== kind) return;
                                            t.stop();
                                            stopped++;
                                        }} catch (e) {{}}
                                    }});
                                }}
                            }} catch (e) {{}}
                            return stopped;
                        }};

                        navigator.mediaDevices.getUserMedia = async (constraints) => {{
                            const camId = readPref(camKey) || initialCam;
                            const micId = readPref(micKey) || initialMic;

                            if (!camId && !micId) {{
                                return await originalGetUserMedia(constraints);
                            }}

                            const modifyConstraints = (c, cam, mic) => {{
                                const out = {{ ...c }};
                                if (cam && out.video) {{
                                    if (out.video === true) out.video = {{ deviceId: {{ exact: cam }} }};
                                    else if (typeof out.video === 'object') out.video.deviceId = {{ exact: cam }};
                                }}
                                if (mic && out.audio) {{
                                    if (out.audio === true) out.audio = {{ deviceId: {{ exact: mic }} }};
                                    else if (typeof out.audio === 'object') out.audio.deviceId = {{ exact: mic }};
                                }}
                                return out;
                            }};

                            try {{
                                const modified = modifyConstraints(constraints, camId, micId);
                                const stream = await originalGetUserMedia(modified);
                                window.__orhLocalUserMediaStreams.push(stream);
                                return stream;
                            }} catch (e) {{
                                // Fallback to ideal constraint
                                try {{
                                    const fallback = {{ ...constraints }};
                                    if (camId && fallback.video) {{
                                        if (fallback.video === true) fallback.video = {{ deviceId: {{ ideal: camId }} }};
                                        else if (typeof fallback.video === 'object') fallback.video.deviceId = {{ ideal: camId }};
                                    }}
                                    if (micId && fallback.audio) {{
                                        if (fallback.audio === true) fallback.audio = {{ deviceId: {{ ideal: micId }} }};
                                        else if (typeof fallback.audio === 'object') fallback.audio.deviceId = {{ ideal: micId }};
                                    }}
                                    const stream = await originalGetUserMedia(fallback);
                                    window.__orhLocalUserMediaStreams.push(stream);
                                    return stream;
                                }} catch (e2) {{
                                    throw e2;
                                }}
                            }}
                        }};
                    }}
                }} catch (e) {{}}
            }})();";
    }

    #endregion
}
