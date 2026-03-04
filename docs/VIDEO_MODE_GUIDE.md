# Video Mode Guide

Complete guide for using video playback mode with MPV player integration.

---

## Overview

The kiosk app supports video playback mode. This allows switching between multiple videos via keyboard hotkeys:

- **Carescape Video** (`Ctrl+Alt+R`): Loops continuously
- **Demo Videos** (`Ctrl+Alt+D`): Plays Demo 1 → Demo 2 → Demo 1, alternating on each press; auto-advances when a video finishes
- **Indexed Videos** (`Ctrl+Alt+1` / `2` / `3`): Each plays its configured video on a continuous loop

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
    "targetMonitorIndex": 2,
    "videoMode": {
      "enabled": true,
      "carescapeVideoPath": "C:\\Videos\\carescape.mp4",
      "demoVideoPath1": "C:\\Videos\\demo1.mp4",
      "demoVideoPath2": "C:\\Videos\\demo2.mp4",
      "videoPaths": [
        "C:\\Videos\\video1.mp4",
        "C:\\Videos\\video2.mp4",
        "C:\\Videos\\video3.mp4"
      ],
      "flicButtonEnabled": true
    }
  }
}
```

Update all paths to match your actual video file locations. Empty strings in `videoPaths` are silently skipped.

### 3. Controls

| Hotkey | Action |
|--------|--------|
| `Ctrl+Alt+R` | Play carescape video on loop |
| `Ctrl+Alt+D` | Toggle between demo videos (auto-alternates on completion) |
| `Ctrl+Alt+1` | Play `videoPaths[0]` on loop |
| `Ctrl+Alt+2` | Play `videoPaths[1]` on loop |
| `Ctrl+Alt+3` | Play `videoPaths[2]` on loop |
| `Ctrl+Alt+E` | Stop video and return to screensaver/WebView mode |

All video hotkeys enter video mode automatically if not already in it.

---

## Configuration Options

Located at `kiosk.videoMode` in config.json:

| Setting | Description | Default |
|---------|-------------|---------|
| `enabled` | Enable/disable video mode | `false` |
| `carescapeVideoPath` | Path to looping carescape video | `C:\Videos\carescape.mp4` |
| `demoVideoPath1` | Path to first demo video (auto-alternates) | `C:\Videos\demo1.mp4` |
| `demoVideoPath2` | Path to second demo video (auto-alternates) | `C:\Videos\demo2.mp4` |
| `videoPaths` | Ordered list of paths for `Ctrl+Alt+1/2/3`; each plays on loop | 3 default entries |
| `mpvPath` | Custom MPV executable path (auto-detected if null) | `null` |
| `flicButtonEnabled` | Enable Flic button control | `true` |

The target monitor is set via `kiosk.targetMonitorIndex` (1-based). Volume is controlled via the Windows Volume Mixer.

---

## How It Works

1. **Ctrl+Alt+R (Carescape)**: Plays the carescape video in a loop. If pressed again, restarts from the beginning.
2. **Ctrl+Alt+D (Demo Toggle)**: Cycles between demo videos:
   - First press: Plays Demo Video 1
   - Second press: Switches to Demo Video 2
   - Third press: Switches back to Demo Video 1
   - When a demo finishes, it automatically plays the other demo
3. **Ctrl+Alt+1/2/3 (Indexed)**: Plays the corresponding entry from `videoPaths` on an infinite loop. Pressing a different index number switches videos immediately.
4. **Ctrl+Alt+E**: Stops all video playback and returns to WebView/screensaver mode.

---

## Multi-Monitor Setup

Set `targetMonitorIndex` to specify which monitor displays the video:
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
3. Or set `mpvPath` in config.json to the full path of your MPV executable

### Videos Not Playing

**Check:**
1. Video file paths are correct in config.json
2. Video files exist and are readable
3. Video codec is supported (MP4 H.264 recommended)
4. Check logs: `%LocalAppData%\OneRoomHealthKiosk\logs\`

### Ctrl+Alt+1/2/3 Does Nothing

**Check:**
1. `videoPaths` array is populated in config.json
2. The video file at that index exists on disk (check logs for `[WARNING] videoPaths[N] not found`)
3. `videoMode.enabled` is `true`

### Volume Not Changing

- Volume is controlled via Windows Volume Mixer only; the app does not override system volume.

### Wrong Monitor

- Adjust `targetMonitorIndex` in config.json and restart the kiosk app.

---

## Video Format Recommendations

**Best formats:**
- MP4 (H.264 video, AAC audio) — Most compatible
- MP4 (H.265/HEVC) — Better compression
- WebM (VP9) — Good for web delivery

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
  "videoPaths": [
    "https://example.com/video1.mp4"
  ]
}
```

**Note:** Network paths may have latency. Local files are recommended for kiosk use.

---

## Integration Details

The video mode uses:
- **VideoController.cs** — Manages MPV process and state machine
- **MainWindow.Keyboard.cs** — Processes all video hotkeys (triple-layer: hook + PreviewKeyDown + accelerators)
- **MainWindow.Debug.cs** — `SwitchToVideoModeAndPlayByIndex()` and related mode-switching helpers

When video mode is active:
- WebView2 is hidden (not used)
- MPV runs as a separate process
- For demo videos, the app monitors MPV exit state and auto-advances

---

## Security Considerations

- Video files should be in protected directories
- Consider using read-only network shares
- Validate video paths in configuration
- Monitor for unauthorized video file changes

---

## Performance Tips

1. **Use hardware acceleration**: MPV automatically uses GPU when available
2. **Optimize video encoding**: Use H.264 with appropriate bitrate for the display resolution
3. **Local storage**: Prefer local files over network paths
4. **Monitor resources**: Check CPU/GPU usage during playback via debug mode Performance tab

---

## See Also

- [Troubleshooting Guide](TROUBLESHOOTING.md) - Common issues and solutions
- [Configuration Reference](configuration.md) - All configuration options
- [Debug Mode Guide](DEBUG_MODE.md) - Debug UI and performance monitoring
