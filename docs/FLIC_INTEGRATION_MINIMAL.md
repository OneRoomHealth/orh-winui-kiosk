# Minimal Integration Steps - Flic Video Control

This shows the absolute minimal changes needed to add Flic video control to your kiosk app.

## Quick Implementation (10 minutes)

### 1. Add Video Configuration

Add to `KioskApp/KioskConfiguration.cs` after line 6:

```csharp
public class VideoModeSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;
    
    [JsonPropertyName("carescapeVideoPath")]
    public string CarescapeVideoPath { get; set; } = @"C:\Videos\carescape.mp4";
    
    [JsonPropertyName("demoVideoPath")]
    public string DemoVideoPath { get; set; } = @"C:\Videos\demo.mp4";
    
    [JsonPropertyName("useMpv")]
    public bool UseMpv { get; set; } = true;
}
```

And in the `KioskSettings` class, add:

```csharp
[JsonPropertyName("videoMode")]
public VideoModeSettings VideoMode { get; set; } = new VideoModeSettings();
```

### 2. Add Simple MPV Control

Add to `KioskApp/MainWindow.xaml.cs` after line 28:

```csharp
// Video mode fields
private Process? _mpvProcess;
private bool _isVideoMode = false;
private bool _isDemoPlaying = false;
private string _carescapeVideo = "";
private string _demoVideo = "";
```

### 3. Load Video Configuration

In the constructor after `LoadConfiguration()` (around line 46):

```csharp
// Check if video mode is enabled
if (_configuration.Kiosk.VideoMode?.Enabled == true)
{
    _isVideoMode = true;
    _carescapeVideo = _configuration.Kiosk.VideoMode.CarescapeVideoPath;
    _demoVideo = _configuration.Kiosk.VideoMode.DemoVideoPath;
    Logger.Log($"Video mode enabled - Carescape: {_carescapeVideo}");
}
```

### 4. Add Video Hotkeys

In `OnKeyDown` method, add after the existing hotkey checks (around line 95):

```csharp
// Video control hotkeys (when video mode is enabled)
if (_isVideoMode)
{
    // Ctrl+Alt+D - Toggle video (Flic button)
    if (ctrl.HasFlag(CoreVirtualKeyStates.Down) && 
        alt.HasFlag(CoreVirtualKeyStates.Down) && 
        args.VirtualKey == VirtualKey.D)
    {
        await ToggleVideoAsync();
        args.Handled = true;
    }
}
```

### 5. Add Video Methods

Add these methods to MainWindow.xaml.cs:

```csharp
private async Task ToggleVideoAsync()
{
    if (_isDemoPlaying)
    {
        Logger.Log("Switching back to carescape video");
        await PlayVideoAsync(_carescapeVideo, loop: true);
        _isDemoPlaying = false;
    }
    else
    {
        Logger.Log("Playing demo video");
        await PlayVideoAsync(_demoVideo, loop: false);
        _isDemoPlaying = true;
        
        // Monitor for demo end
        _ = Task.Run(async () =>
        {
            while (_isDemoPlaying && _mpvProcess != null && !_mpvProcess.HasExited)
            {
                await Task.Delay(500);
            }
            
            if (_isDemoPlaying && (_mpvProcess?.HasExited ?? true))
            {
                await DispatcherQueue.TryEnqueue(async () =>
                {
                    Logger.Log("Demo ended, returning to carescape");
                    await PlayVideoAsync(_carescapeVideo, loop: true);
                    _isDemoPlaying = false;
                });
            }
        });
    }
}

private async Task PlayVideoAsync(string videoPath, bool loop)
{
    try
    {
        // Kill existing MPV if running
        if (_mpvProcess != null && !_mpvProcess.HasExited)
        {
            _mpvProcess.Kill();
            await Task.Delay(100);
        }
        
        // Build MPV command
        var args = "--fullscreen --no-osc --no-border --ontop --screen=1";
        if (loop) args += " --loop-file=inf";
        args += $" \"{videoPath}\"";
        
        // Start MPV
        _mpvProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "mpv", // Assumes MPV is in PATH
            Arguments = args,
            UseShellExecute = false
        });
        
        // Hide WebView when playing video
        KioskWebView.Visibility = Visibility.Collapsed;
    }
    catch (Exception ex)
    {
        Logger.Log($"Error playing video: {ex.Message}");
    }
}
```

### 6. Initialize Video Mode

In `InitializeWebViewAsync()`, after WebView2 initialization (around line 160):

```csharp
// Start video mode if enabled
if (_isVideoMode && File.Exists(_carescapeVideo))
{
    await PlayVideoAsync(_carescapeVideo, loop: true);
}
```

### 7. Update Config File

Create `%ProgramData%\OneRoomHealth\Kiosk\config.json`:

```json
{
  "kiosk": {
    "defaultUrl": "https://your-url.com",
    "videoMode": {
      "enabled": true,
      "carescapeVideoPath": "C:\\Users\\CareWall\\Desktop\\Demo\\chehaliscare.mp4",
      "demoVideoPath": "C:\\Users\\CareWall\\Desktop\\Demo\\demo_video.mp4"
    }
  }
}
```

### 8. Install MPV

Download MPV and either:
- Add to PATH, or
- Place in `C:\mpv\mpv.exe` and update the code

## That's It!

With these minimal changes:
- Press `Ctrl+Alt+D` to toggle between videos
- Demo video plays once then returns to carescape
- Videos play fullscreen on second monitor
- WebView is hidden during video playback

## Testing Commands

```powershell
# Test if MPV is accessible
where mpv

# Test video playback
mpv --fullscreen "C:\path\to\video.mp4"

# Launch kiosk with video mode
# (Make sure config.json has videoMode.enabled = true)
```

## Next Steps

For more features like:
- Volume control per video
- Better multi-monitor support  
- Error handling
- Progress monitoring

See the full implementation in `VideoController.cs` and `FLIC_INTEGRATION_PLAN.md`
