# Flic Integration Quick Start Guide

This guide shows how to quickly add Flic button video control to the OneRoom Health Kiosk app.

---

## Option 1: Simple MPV Integration (5 Minutes)

### Step 1: Add VideoController.cs
Copy the provided `VideoController.cs` file to the `KioskApp` folder.

### Step 2: Update KioskConfiguration.cs

Add this class to `KioskConfiguration.cs`:

```csharp
public class VideoModeSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;
    
    [JsonPropertyName("carescapeVideoPath")]
    public string CarescapeVideoPath { get; set; } = @"C:\Videos\carescape.mp4";
    
    [JsonPropertyName("demoVideoPath")]  
    public string DemoVideoPath { get; set; } = @"C:\Videos\demo.mp4";
    
    [JsonPropertyName("carescapeVolume")]
    public double CarescapeVolume { get; set; } = 50;
    
    [JsonPropertyName("demoVolume")]
    public double DemoVolume { get; set; } = 75;
    
    [JsonPropertyName("targetMonitor")]
    public int TargetMonitor { get; set; } = 2;
}
```

Add to `KioskSettings` class:
```csharp
[JsonPropertyName("videoMode")]
public VideoModeSettings VideoMode { get; set; } = new VideoModeSettings();
```

### Step 3: Update MainWindow.xaml.cs

Add these members to the MainWindow class:

```csharp
private VideoController? _videoController;
private bool _videoModeEnabled = false;
```

Update the constructor:

```csharp
public MainWindow()
{
    InitializeComponent();
    
    // Existing code...
    
    // Load video configuration
    var config = ConfigurationManager.LoadConfiguration();
    if (config.Kiosk.VideoMode?.Enabled == true)
    {
        _videoModeEnabled = true;
        _videoController = new VideoController(config.Kiosk.VideoMode, Logger);
    }
}
```

Update `OnKeyDown` method to add video hotkeys:

```csharp
private async void OnKeyDown(CoreWindow sender, KeyEventArgs args)
{
    // Existing debug/exit hotkeys...
    
    // Video control hotkeys
    if (_videoModeEnabled && _videoController != null)
    {
        var ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control);
        var alt = Window.Current.CoreWindow.GetKeyState(VirtualKey.Menu);
        
        if (ctrl.HasFlag(CoreVirtualKeyStates.Down) && 
            alt.HasFlag(CoreVirtualKeyStates.Down))
        {
            switch (args.VirtualKey)
            {
                case VirtualKey.D: // Flic button - toggle video
                    await _videoController.HandleFlicButtonPressAsync();
                    args.Handled = true;
                    Logger.Log("Flic button pressed - toggling video");
                    break;
                    
                case VirtualKey.E: // Stop video
                    await _videoController.StopAsync();
                    args.Handled = true;
                    Logger.Log("Video stopped");
                    break;
                    
                case VirtualKey.R: // Restart carescape
                    await _videoController.RestartCarescapeAsync();
                    args.Handled = true;
                    Logger.Log("Restarting carescape video");
                    break;
            }
        }
    }
}
```

Update `InitializeWebViewAsync` to initialize video mode:

```csharp
private async Task InitializeWebViewAsync()
{
    try
    {
        // Existing WebView initialization...
        
        // Initialize video mode if enabled
        if (_videoController != null)
        {
            await _videoController.InitializeAsync();
            
            // Hide WebView if in video mode
            KioskWebView.Visibility = Visibility.Collapsed;
        }
    }
    catch (Exception ex)
    {
        Logger.Log($"WebView initialization error: {ex.Message}");
    }
}
```

### Step 4: Configure Video Mode

Create or update `config.json` in `%ProgramData%\OneRoomHealth\Kiosk\`:

```json
{
  "kiosk": {
    "defaultUrl": "https://your-url.com",
    "videoMode": {
      "enabled": true,
      "carescapeVideoPath": "C:\\Users\\CareWall\\Desktop\\Demo\\chehaliscare.mp4",
      "demoVideoPath": "C:\\Users\\CareWall\\Desktop\\Demo\\demo_video.mp4",
      "carescapeVolume": 50,
      "demoVolume": 75,
      "targetMonitor": 2
    }
  }
}
```

### Step 5: Install MPV

1. Download MPV from: https://sourceforge.net/projects/mpv-player-windows/files/
2. Extract to `C:\mpv\` so you have `C:\mpv\mpv.exe`
3. Or add to system PATH

---

## Option 2: WebView2 Video Player (Recommended)

### Step 1: Create Video Player HTML

Create `KioskApp/Resources/VideoPlayer.html`:

```html
<!DOCTYPE html>
<html>
<head>
    <style>
        body { margin: 0; background: black; overflow: hidden; }
        video { width: 100vw; height: 100vh; object-fit: fill; }
    </style>
</head>
<body>
    <video id="player" autoplay></video>
    <script>
        let currentVideo = 'carescape';
        const videos = {
            carescape: { src: '', volume: 0.5, loop: true },
            demo: { src: '', volume: 0.75, loop: false }
        };
        
        window.chrome.webview.addEventListener('message', (e) => {
            const cmd = e.data;
            switch(cmd.action) {
                case 'init':
                    videos.carescape.src = cmd.carescapeUrl;
                    videos.demo.src = cmd.demoUrl;
                    playVideo('carescape');
                    break;
                case 'toggle':
                    currentVideo = currentVideo === 'carescape' ? 'demo' : 'carescape';
                    playVideo(currentVideo);
                    break;
            }
        });
        
        function playVideo(name) {
            const video = videos[name];
            const player = document.getElementById('player');
            player.src = video.src;
            player.volume = video.volume;
            player.loop = video.loop;
            player.play();
        }
        
        document.getElementById('player').addEventListener('ended', () => {
            if (currentVideo === 'demo') {
                playVideo('carescape');
            }
        });
    </script>
</body>
</html>
```

### Step 2: Update MainWindow to Use HTML Player

```csharp
private async Task InitializeVideoMode()
{
    // Read HTML from resources
    var html = File.ReadAllText("Resources/VideoPlayer.html");
    
    // Navigate to HTML
    var dataUri = $"data:text/html;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(html))}";
    KioskWebView.CoreWebView2.Navigate(dataUri);
    
    // Send video URLs
    await Task.Delay(500); // Wait for page load
    
    var initCmd = new
    {
        action = "init",
        carescapeUrl = new Uri(_config.VideoMode.CarescapeVideoPath).AbsoluteUri,
        demoUrl = new Uri(_config.VideoMode.DemoVideoPath).AbsoluteUri
    };
    
    await KioskWebView.CoreWebView2.PostWebMessageAsJsonAsync(JsonSerializer.Serialize(initCmd));
}
```

---

## Testing

### 1. Manual Test
- Launch the kiosk app
- Press `Ctrl+Alt+D` to toggle between videos
- Verify demo video plays once then returns to carescape
- Test volume changes between videos

### 2. Flic Button Setup
- Configure Flic button to send `Ctrl+Alt+D` 
- Test physical button press
- Verify smooth transitions

### 3. Multi-Monitor Test
- Ensure video appears on correct monitor
- Test with different monitor configurations

---

## Troubleshooting

### MPV Not Found
- Check MPV is installed in one of the expected paths
- Add MPV to system PATH
- Update `FindMpvExecutable()` with custom path

### Videos Not Playing
- Verify video file paths in config.json
- Check video codec compatibility
- Try with different video formats (MP4, MKV)

### Volume Not Changing
- Run app as administrator (one time) to test
- Check Windows audio mixer settings
- Verify audio device is not muted

### Wrong Monitor
- Adjust `targetMonitor` in config.json
- Monitor numbers: 1 = primary, 2 = secondary, etc.

---

## Production Deployment

1. **Package Videos**
   - Include in MSIX package, OR
   - Deploy to fixed location on all kiosks

2. **Configure Paths**
   - Use relative paths if bundled
   - Use network paths for central storage

3. **Test Hardware**
   - Verify Flic button range
   - Test with actual display setup
   - Validate performance

---

## Next Steps

1. Choose implementation approach (MPV vs WebView2)
2. Test with your video files
3. Configure for your hardware setup
4. Deploy to kiosk devices

For the complete implementation plan, see `FLIC_INTEGRATION_PLAN.md`.
