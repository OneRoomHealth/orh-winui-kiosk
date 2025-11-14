# Flic Button Video Control Integration Plan
## OneRoom Health WinUI Kiosk Application

**Date:** November 12, 2025  
**Version:** 1.0  
**Status:** Draft

---

## Executive Summary

This document outlines the plan to integrate Flic button video control functionality from the Python `sampleFlick.py` script into the OneRoom Health WinUI Kiosk application. The integration will enable the kiosk to switch between two video sources (carescape and demo videos) using physical button input or hotkeys.

---

## Current Functionality Analysis

### sampleFlick.py Features

1. **Video Playback Control**
   - Uses MPV player for video playback
   - Supports two videos: carescape (looping) and demo (plays once)
   - Targets second monitor for display
   - Full-screen, borderless playback

2. **Button/Hotkey Integration**
   - Ctrl+Alt+D: Toggle between videos (Flic button mapping)
   - Ctrl+Alt+E: Stop playback
   - Ctrl+Alt+R: Restart carescape video
   - Global hotkeys (requires administrator)

3. **Volume Management**
   - Different volume levels for each video
   - Uses Windows audio API (pycaw)
   - Smooth transitions between volumes

4. **State Management**
   - Monitors demo video completion
   - Auto-returns to carescape after demo
   - Thread-based monitoring

---

## Integration Approaches

### Option 1: Web-Based Video Player (Recommended) ⭐

**Description:** Replace MPV with HTML5 video player in WebView2

**Pros:**
- Native integration with existing WebView2
- No external dependencies
- Cross-platform video support
- Easy to maintain

**Cons:**
- May need to convert video formats
- Less control over video rendering

**Implementation:**
1. Create HTML5 video player page
2. Add JavaScript API for video control
3. Use WebView2 messaging for C# ↔ JavaScript communication
4. Implement hotkey handling in MainWindow.xaml.cs

### Option 2: Native Video Control with MediaPlayerElement

**Description:** Use WinUI 3's MediaPlayerElement for video playback

**Pros:**
- Native Windows performance
- Direct C# control
- Good multi-monitor support

**Cons:**
- Requires UI redesign
- May conflict with WebView2 focus

**Implementation:**
1. Add MediaPlayerElement to XAML
2. Create video switching logic
3. Handle display targeting

### Option 3: External MPV Control

**Description:** Launch and control MPV.exe from C# app

**Pros:**
- Reuses existing MPV functionality
- Minimal code changes

**Cons:**
- Requires MPV installation
- Process management complexity
- Security concerns

**Implementation:**
1. Process.Start() for MPV
2. Inter-process communication
3. Window positioning APIs

### Option 4: Hybrid Service Architecture

**Description:** Run Python script as Windows service alongside kiosk

**Pros:**
- Keeps existing Python code
- Clear separation of concerns

**Cons:**
- Complex deployment
- Service management overhead

---

## Recommended Implementation Plan

Based on analysis, **Option 1 (Web-Based Video Player)** is recommended for the following reasons:

1. **Seamless Integration** - Works within existing WebView2 infrastructure
2. **Maintainability** - Single codebase in C#/JavaScript
3. **Security** - No external processes or elevated permissions
4. **Deployment** - No additional dependencies

---

## Detailed Implementation Steps

### Phase 1: Create Video Player Web Component

#### 1.1 Create Video Player HTML Page

```html
<!DOCTYPE html>
<html>
<head>
    <title>Kiosk Video Player</title>
    <style>
        body { margin: 0; background: black; overflow: hidden; }
        video { width: 100vw; height: 100vh; object-fit: fill; }
        #status { position: fixed; top: 10px; right: 10px; color: white; 
                  background: rgba(0,0,0,0.7); padding: 10px; display: none; }
    </style>
</head>
<body>
    <video id="player" autoplay></video>
    <div id="status"></div>
    
    <script>
        class KioskVideoPlayer {
            constructor() {
                this.player = document.getElementById('player');
                this.status = document.getElementById('status');
                this.videos = {
                    carescape: { path: '', volume: 0.5, loop: true },
                    demo: { path: '', volume: 0.75, loop: false }
                };
                this.currentVideo = 'carescape';
                
                this.player.addEventListener('ended', () => {
                    if (this.currentVideo === 'demo') {
                        this.playVideo('carescape');
                    }
                });
                
                // Listen for messages from C#
                window.chrome.webview.addEventListener('message', (e) => {
                    this.handleCommand(e.data);
                });
            }
            
            handleCommand(cmd) {
                switch(cmd.action) {
                    case 'configure':
                        this.videos = cmd.videos;
                        this.playVideo('carescape');
                        break;
                    case 'toggle':
                        this.toggleVideo();
                        break;
                    case 'stop':
                        this.player.pause();
                        break;
                    case 'restart':
                        this.playVideo('carescape');
                        break;
                }
            }
            
            toggleVideo() {
                const nextVideo = this.currentVideo === 'carescape' ? 'demo' : 'carescape';
                this.playVideo(nextVideo);
            }
            
            playVideo(name) {
                const video = this.videos[name];
                if (!video) return;
                
                this.currentVideo = name;
                this.player.src = video.path;
                this.player.volume = video.volume;
                this.player.loop = video.loop;
                this.player.play();
                
                this.showStatus(`Playing: ${name}`);
                
                // Notify C#
                window.chrome.webview.postMessage({
                    event: 'videoChanged',
                    video: name
                });
            }
            
            showStatus(text) {
                this.status.textContent = text;
                this.status.style.display = 'block';
                setTimeout(() => {
                    this.status.style.display = 'none';
                }, 3000);
            }
        }
        
        const player = new KioskVideoPlayer();
    </script>
</body>
</html>
```

#### 1.2 Save as Embedded Resource

Location: `KioskApp/Resources/video-player.html`

### Phase 2: Modify MainWindow.xaml.cs

#### 2.1 Add Video Control Properties

```csharp
// Add to MainWindow class
private bool _videoModeEnabled = false;
private string _carescapeVideoPath;
private string _demoVideoPath;
private double _carescapeVolume = 50;
private double _demoVolume = 75;
```

#### 2.2 Add Video Mode Configuration

```csharp
private void LoadVideoConfiguration()
{
    try
    {
        var config = ConfigurationManager.LoadConfiguration();
        if (config.VideoMode != null && config.VideoMode.Enabled)
        {
            _videoModeEnabled = true;
            _carescapeVideoPath = config.VideoMode.CarescapeVideoPath;
            _demoVideoPath = config.VideoMode.DemoVideoPath;
            _carescapeVolume = config.VideoMode.CarescapeVolume;
            _demoVolume = config.VideoMode.DemoVolume;
            
            Logger.Log($"Video mode enabled - Carescape: {_carescapeVideoPath}, Demo: {_demoVideoPath}");
        }
    }
    catch (Exception ex)
    {
        Logger.Log($"Failed to load video configuration: {ex.Message}");
    }
}
```

#### 2.3 Add Hotkey Handlers

```csharp
private async void OnKeyDown(CoreWindow sender, KeyEventArgs args)
{
    // Existing debug/exit hotkeys...
    
    // Video control hotkeys
    if (_videoModeEnabled)
    {
        var ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control);
        var alt = Window.Current.CoreWindow.GetKeyState(VirtualKey.Menu);
        
        if (ctrl.HasFlag(CoreVirtualKeyStates.Down) && 
            alt.HasFlag(CoreVirtualKeyStates.Down))
        {
            switch (args.VirtualKey)
            {
                case VirtualKey.D: // Flic button press
                    await ToggleVideo();
                    args.Handled = true;
                    break;
                    
                case VirtualKey.E: // Stop
                    await StopVideo();
                    args.Handled = true;
                    break;
                    
                case VirtualKey.R: // Restart
                    await RestartCarescapeVideo();
                    args.Handled = true;
                    break;
            }
        }
    }
}
```

#### 2.4 Implement Video Control Methods

```csharp
private async Task InitializeVideoMode()
{
    if (!_videoModeEnabled) return;
    
    try
    {
        // Load video player HTML from resources
        var html = await LoadVideoPlayerHtml();
        
        // Navigate to data URI
        var dataUri = $"data:text/html;charset=utf-8,{Uri.EscapeDataString(html)}";
        KioskWebView.CoreWebView2.Navigate(dataUri);
        
        // Wait for page load
        await Task.Delay(500);
        
        // Configure videos
        var config = new
        {
            action = "configure",
            videos = new
            {
                carescape = new { 
                    path = ConvertToFileUrl(_carescapeVideoPath), 
                    volume = _carescapeVolume / 100.0, 
                    loop = true 
                },
                demo = new { 
                    path = ConvertToFileUrl(_demoVideoPath), 
                    volume = _demoVolume / 100.0, 
                    loop = false 
                }
            }
        };
        
        await KioskWebView.CoreWebView2.PostWebMessageAsJsonAsync(JsonSerializer.Serialize(config));
        Logger.Log("Video player initialized");
    }
    catch (Exception ex)
    {
        Logger.Log($"Failed to initialize video mode: {ex.Message}");
    }
}

private string ConvertToFileUrl(string filePath)
{
    return "file:///" + filePath.Replace('\\', '/');
}

private async Task ToggleVideo()
{
    await SendVideoCommand("toggle");
    Logger.Log("Video toggled");
}

private async Task StopVideo()
{
    await SendVideoCommand("stop");
    Logger.Log("Video stopped");
}

private async Task RestartCarescapeVideo()
{
    await SendVideoCommand("restart");
    Logger.Log("Carescape video restarted");
}

private async Task SendVideoCommand(string action)
{
    var command = new { action };
    await KioskWebView.CoreWebView2.PostWebMessageAsJsonAsync(JsonSerializer.Serialize(command));
}
```

### Phase 3: Update Configuration System

#### 3.1 Add Video Configuration Model

```csharp
// Add to KioskConfiguration.cs
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
    
    [JsonPropertyName("flicButtonEnabled")]
    public bool FlicButtonEnabled { get; set; } = true;
}

// Add to KioskSettings class
[JsonPropertyName("videoMode")]
public VideoModeSettings VideoMode { get; set; } = new VideoModeSettings();
```

#### 3.2 Update Default Configuration

```json
{
  "kiosk": {
    "defaultUrl": "...",
    "videoMode": {
      "enabled": false,
      "carescapeVideoPath": "C:\\Videos\\carescape.mp4",
      "demoVideoPath": "C:\\Videos\\demo.mp4",
      "carescapeVolume": 50,
      "demoVolume": 75,
      "targetMonitor": 2,
      "flicButtonEnabled": true
    }
  }
}
```

### Phase 4: Multi-Monitor Support

#### 4.1 Add Monitor Detection

```csharp
private DisplayRegion GetTargetMonitor(int index)
{
    var displayRegions = ApplicationView.GetForCurrentView().GetDisplayRegions();
    if (index > 0 && index <= displayRegions.Count)
    {
        return displayRegions[index - 1];
    }
    return displayRegions[0]; // Default to primary
}
```

#### 4.2 Position Window on Target Monitor

```csharp
private void PositionOnTargetMonitor()
{
    if (!_videoModeEnabled) return;
    
    var targetMonitor = GetTargetMonitor(_config.VideoMode.TargetMonitor);
    // Use existing window positioning logic
}
```

### Phase 5: Optional MPV Integration

For users who prefer MPV over HTML5 video:

```csharp
public class MpvController
{
    private Process _mpvProcess;
    
    public async Task PlayVideo(string videoPath, bool loop, int screen)
    {
        var args = new List<string>
        {
            "--fullscreen",
            "--no-osc",
            "--no-border",
            "--ontop",
            $"--screen={screen}",
            $"--fs-screen={screen}",
            "--quiet"
        };
        
        if (loop) args.Add("--loop-file=inf");
        args.Add(videoPath);
        
        _mpvProcess = Process.Start(new ProcessStartInfo
        {
            FileName = FindMpv(),
            Arguments = string.Join(" ", args),
            UseShellExecute = false
        });
    }
    
    private string FindMpv()
    {
        // Implementation similar to Python script
    }
}
```

---

## Testing Plan

### Unit Tests
1. Video player HTML/JavaScript functionality
2. Hotkey registration and handling
3. Configuration loading and validation
4. Multi-monitor detection

### Integration Tests
1. Video transitions (carescape ↔ demo)
2. Volume control
3. Auto-return after demo completion
4. Hotkey functionality
5. Multi-monitor targeting

### User Acceptance Tests
1. Flic button physical testing
2. Video quality and performance
3. Transition smoothness
4. Audio level appropriateness

---

## Deployment Considerations

1. **Video Files**
   - Bundle with MSIX or separate installer
   - Configure paths in config.json
   - Consider video codec compatibility

2. **Permissions**
   - File system access for video files
   - Audio control permissions
   - No admin required (unlike Python version)

3. **Hardware Requirements**
   - Multi-monitor support
   - Adequate GPU for video playback
   - Audio output device

---

## Migration Path

### From Python Script to Kiosk App

1. **Week 1**: Implement basic video player
2. **Week 2**: Add hotkey support
3. **Week 3**: Multi-monitor and volume control
4. **Week 4**: Testing and optimization

### Backwards Compatibility

- Keep HTTP API endpoint for remote video control
- Support both web URLs and local video files
- Configuration toggle for video mode

---

## Security Considerations

1. **File Access**
   - Validate video file paths
   - Sandbox file access to configured directories
   - No arbitrary file execution

2. **Input Validation**
   - Sanitize all configuration values
   - Validate hotkey combinations
   - Prevent command injection

3. **Resource Management**
   - Limit video file sizes
   - Monitor memory usage
   - Graceful error handling

---

## Performance Optimization

1. **Video Loading**
   - Preload videos in background
   - Use hardware acceleration
   - Optimize video encoding

2. **Memory Management**
   - Release video resources when not needed
   - Implement video caching
   - Monitor WebView2 memory usage

3. **CPU Usage**
   - Use efficient video codecs (H.264/H.265)
   - Hardware decoding when available
   - Throttle non-visible videos

---

## Alternative: Companion Service Approach

If web-based video proves insufficient:

```csharp
// Windows Service to run Python script
public class FlicVideoService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var python = Process.Start(new ProcessStartInfo
        {
            FileName = "python",
            Arguments = "sampleFlick.py",
            WorkingDirectory = @"C:\KioskScripts",
            UseShellExecute = false
        });
        
        await python.WaitForExitAsync(stoppingToken);
    }
}
```

---

## Conclusion

The recommended approach is to implement video functionality using WebView2's HTML5 video capabilities. This provides the best balance of:

- **Integration** - Works within existing architecture
- **Maintainability** - Single codebase
- **Performance** - Hardware-accelerated video
- **Security** - No external processes
- **Deployment** - Simple MSIX packaging

The implementation can be completed in 4 weeks with proper testing and documentation.

---

## Next Steps

1. **Approval** - Review and approve implementation approach
2. **Prototype** - Create proof-of-concept video player
3. **Development** - Implement full functionality
4. **Testing** - Comprehensive testing with hardware
5. **Deployment** - Roll out to kiosk devices

---

**Document Status:** Ready for Review  
**Author:** AI Assistant  
**Review Required By:** Development Team Lead
