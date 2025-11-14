# Video Mode Guide - Flic Button Integration

Complete guide for using the Flic button video control feature with MPV player integration.

---

## Overview

The kiosk app supports video playback mode with Flic button control. This allows switching between two videos:
- **Carescape Video**: Loops continuously (default state)
- **Demo Video**: Plays once, then returns to carescape

The implementation uses **MPV player** for high-quality video playback with hardware acceleration.

---

## Quick Start

### 1. Install MPV

Download and install MPV from: https://sourceforge.net/projects/mpv-player-windows/files/

**Recommended locations:**
- `C:\mpv\mpv.exe` (preferred)
- Or add MPV to your system PATH

**Verify installation:**
```powershell
where mpv
# Should show: C:\mpv\mpv.exe (or similar)
```

### 2. Configure Video Mode

Edit `%ProgramData%\OneRoomHealth\Kiosk\config.json`:

```json
{
  "kiosk": {
    "videoMode": {
      "enabled": true,
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

**Update paths** to match your actual video file locations.

### 3. Controls

- **Ctrl+Alt+D** - Toggle between videos (Flic button)
- **Ctrl+Alt+E** - Stop video playback
- **Ctrl+Alt+R** - Restart carescape video

---

## Configuration Options

| Setting | Description | Default |
|---------|-------------|---------|
| `enabled` | Enable/disable video mode | `false` |
| `carescapeVideoPath` | Path to looping video | `C:\Videos\carescape.mp4` |
| `demoVideoPath` | Path to demo video | `C:\Videos\demo.mp4` |
| `carescapeVolume` | Volume for carescape (0-100) | `50` |
| `demoVolume` | Volume for demo (0-100) | `75` |
| `targetMonitor` | Monitor number (1-based) | `1` |
| `flicButtonEnabled` | Enable Flic button control | `true` |

---

## How It Works

1. **App Startup**: If video mode is enabled, the app launches MPV with the carescape video
2. **Flic Button Press**: Sends Ctrl+Alt+D, which toggles to demo video
3. **Demo Completion**: When demo ends, automatically returns to carescape video
4. **Volume Control**: System volume adjusts based on which video is playing

---

## Multi-Monitor Setup

Set `targetMonitor` to specify which monitor displays the video:
- `1` = Primary monitor
- `2` = Second monitor
- `3` = Third monitor, etc.

The kiosk app window can be on a different monitor than the video.

---

## Troubleshooting

### MPV Not Found

**Error:** "MPV executable not found"

**Solution:**
1. Install MPV to `C:\mpv\mpv.exe`
2. Or add MPV to system PATH
3. Or update `VideoController.cs` with custom path

### Videos Not Playing

**Check:**
1. Video file paths are correct in config.json
2. Video files exist and are readable
3. Video codec is supported (MP4 H.264 recommended)
4. Check logs: `%LocalAppData%\OneRoomHealthKiosk\kiosk.log`

### Volume Not Changing

**Solution:**
- Volume control requires Windows audio API access
- Ensure audio device is not muted
- Check Windows volume mixer

### Wrong Monitor

**Solution:**
- Adjust `targetMonitor` in config.json
- Restart the kiosk app
- Verify monitor count: Check Display Settings

---

## Video Format Recommendations

**Best formats:**
- MP4 (H.264 video, AAC audio) - Most compatible
- MP4 (H.265/HEVC) - Better compression
- WebM (VP9) - Good for web delivery

**Avoid:**
- AVI (older codecs)
- MKV (may have codec issues)
- Uncompressed formats (large file sizes)

**Convert videos if needed:**
```powershell
# Using ffmpeg (if installed)
ffmpeg -i input.avi -c:v libx264 -c:a aac output.mp4
```

---

## Advanced: Network Videos

You can use network paths or URLs if MPV supports them:
```json
{
  "carescapeVideoPath": "\\\\server\\videos\\carescape.mp4",
  "demoVideoPath": "https://example.com/demo.mp4"
}
```

**Note:** Network paths may have latency. Local files are recommended for kiosk use.

---

## Integration Details

The video mode uses:
- **VideoController.cs** - Manages MPV process and state
- **VolumeController** - Controls Windows system volume
- **Hotkey handling** - MainWindow.xaml.cs processes Ctrl+Alt+D

When video mode is enabled:
- WebView2 is hidden (not used)
- MPV runs as separate process
- App monitors MPV state for demo completion

---

## Security Considerations

- Video files should be in protected directories
- Consider using read-only network shares
- Validate video paths in configuration
- Monitor for unauthorized video file changes

---

## Performance Tips

1. **Use hardware acceleration**: MPV automatically uses GPU when available
2. **Optimize video encoding**: Use H.264 with appropriate bitrate
3. **Local storage**: Prefer local files over network paths
4. **Monitor resources**: Check CPU/GPU usage during playback

---

## See Also

- [Deployment Guide](DEPLOYMENT_GUIDE.md) - Full deployment instructions
- [Troubleshooting Guide](TROUBLESHOOTING.md) - Common issues and solutions
- [Configuration Reference](CONFIGURATION.md) - All configuration options

