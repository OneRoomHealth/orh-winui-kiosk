# Debug Mode Documentation

**Status:** ✅ Fully Implemented
**Last Updated:** February 18, 2026

---

## Overview

Debug mode provides developer and administrator access to diagnostic tools, hardware health monitoring, logging, and performance metrics within the kiosk application.

---

## Activation

### Enter Debug Mode
**Hotkey:** `Ctrl + Shift + I`

When activated:
- Window transitions from fullscreen to windowed (configurable size)
- Window becomes resizable and movable
- Debug toolbar appears with navigation controls
- Tabbed panel appears with Hardware Health, Logs, Performance, and Device Control tabs
- DevTools button becomes available

### Exit Debug Mode
**Hotkey:** `Ctrl + Shift + I` (same hotkey toggles)

Returns to fullscreen kiosk mode with all debug features hidden.

---

## Exit Kiosk Mode

### Password-Protected Exit
**Hotkey:** `Ctrl + Shift + Q`

1. Password dialog appears
2. Enter administrator password (default: `admin123`)
3. Application exits and launches Explorer.exe (for Shell Launcher users)

**Security:** Change the default password in production via config.json.

---

## Debug Panels

### Hardware Health Panel
Real-time status of all hardware modules:
- **Display Module** - Novastar LED controller status
- **Camera Module** - Huddly camera status (PTZ, auto-tracking)
- **Lighting Module** - DMX512 connection status
- **System Audio** - Windows audio device status
- **Microphone** - Capture device status
- **Speaker** - Playback device status
- **Biamp Module** - Video conferencing codec status

Each module shows:
- Health status (Healthy, Unhealthy, Offline)
- Last seen timestamp
- Device count
- Error messages (if any)

### Device Control Panel
Interactive REST API control for all hardware modules on `localhost:8081/api/v1`:
- **Category Navigation:** System, Cameras, Displays, Lighting, Sys Audio, Mics, Speakers, Biamp, Browser
- **Connection Indicator:** Real-time status of Hardware API server
- **Auto-refresh:** Device list and connection status refresh every 10 seconds
- **Request History:** Tracks recent API requests with status codes and response times

### Log Viewer
Unified logging with filtering:
- **Log Level Filter:** Debug, Info, Warning, Error
- **Module Filter:** Filter by specific module
- Real-time log streaming from Serilog
- Clear and Copy functions

### Performance Panel
System resource monitoring:
- **Memory:** Working Set, GC Memory, Private Memory
- **GC Collections:** Gen 0/1/2 counts
- **Process Stats:** CPU %, Thread Count, Handle Count
- **Uptime:** Application runtime
- **Force GC Button:** Trigger garbage collection with before/after stats

---

## Configuration

Debug mode settings in `%ProgramData%\OneRoomHealth\Kiosk\config.json`:

```json
{
  "debug": {
    "enabled": true,
    "hotkey": "Ctrl+Shift+I",
    "autoOpenDevTools": false,
    "windowSizePercent": 80
  },
  "exit": {
    "enabled": true,
    "hotkey": "Ctrl+Shift+Q",
    "requirePassword": true,
    "passwordHash": ""
  }
}
```

### Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `debug.enabled` | `true` | Enable/disable debug mode hotkey |
| `debug.windowSizePercent` | `80` | Window size as % of screen |
| `debug.autoOpenDevTools` | `false` | Auto-open DevTools when entering debug mode |
| `exit.enabled` | `true` | Enable/disable exit hotkey |
| `exit.requirePassword` | `true` | Require password to exit |
| `exit.passwordHash` | `""` | SHA256 hash or plain text password |

---

## Toolbar Features

### Navigation Controls
- **Back/Forward** - Browser history navigation
- **Refresh** - Reload current page
- **URL Bar** - Direct URL entry
- **Go Button** - Navigate to entered URL

### Device Selection
- **Camera Selector** - Choose WebRTC camera (persisted across sessions)
- **Microphone Selector** - Choose WebRTC microphone (persisted across sessions)
- **Speaker Selector** - Choose audio output device (persisted across sessions)
- **Refresh Buttons** - Re-enumerate devices

Device selections are saved to LocalSettings and applied automatically on page navigations via a `getUserMedia` override injected into the WebView. The override script is reinstalled whenever a device preference changes, ensuring cross-origin navigations (e.g., ACS appointment URLs) use the correct devices immediately.

### Quick Actions
- **DevTools** - Open WebView2 developer tools (F12)
- **Switch Monitor** - Move window to different display

---

## API Mode Toggle

The debug mode title bar includes an API mode toggle switch that controls which HTTP server is active.

### Navigate Mode - Port 8787
- **LocalCommandServer** listens on `http://127.0.0.1:8787`
- Provides `/navigate` endpoint for external URL control
- Provides `/health` endpoint for status check
- Lightweight, minimal resource usage - no hardware modules initialized
- Use for remote kiosk navigation control

### Hardware API Mode (Default) - Port 8081
- **HardwareApiServer** listens on configured port (default 8081)
- Full hardware control API with all module endpoints
- Initializes all hardware modules (Display, Camera, Lighting, Audio, etc.)
- Starts health monitoring and visualization services
- Matches workstation-api functionality
- WebView2 handles navigation internally
- Use when full hardware integration is needed

### Switching Modes
1. Enter debug mode (`Ctrl + Shift + I`)
2. Locate the "Mode:" toggle in the title bar
3. Toggle OFF = Navigate Mode (8787) - lightweight, no hardware
4. Toggle ON = Hardware API Mode (8081) - full hardware integration
5. Status indicator shows green when server is running

**Mode preference persists across debug mode sessions.** Switching to Navigate mode and exiting debug mode will restore Navigate mode the next time debug mode is entered (and vice versa). The preference is stored in `preferences.json` alongside the app data directory.

**Switching to Navigate mode** clears the health cards, closes the detail panel, and updates the status bar. Switching back to Hardware API mode repopulates health data and restarts the health refresh timer.

**Note:** Only one mode's server is active at a time (the Navigate server is stopped when Hardware API mode is enabled, and vice versa). Switching modes shuts down or starts hardware modules accordingly.

---

## Security Considerations

1. **Disable in Production:** Set `debug.enabled: false` and `exit.enabled: false` for production deployments
2. **Strong Password:** Change the default exit password
3. **File Permissions:** Config file should be readable only by administrators
4. **Audit Logging:** All debug/exit actions are logged to Windows Event Log

---

## Files

### Code Files
- `MainWindow.Debug.cs` - Debug mode toggle, tab switching, API mode toggle, exit handling
- `MainWindow.Panels.cs` - Health panel, log viewer, performance panel, module toggles
- `MainWindow.DeviceControl.cs` - Device Control tab with interactive REST API controls
- `MainWindow.MediaDevices.cs` - Camera/mic/speaker enumeration, selection persistence, getUserMedia override
- `MainWindow.WebView.cs` - WebView2 setup, media preference sync timer, DOM injection
- `Helpers/UnifiedLogger.cs` - Serilog bridge for log viewer
- `Helpers/PerformanceMonitor.cs` - GC and performance metrics
- `Helpers/UserPreferences.cs` - Persisted user preferences (API mode, etc.)

### UI Elements (MainWindow.xaml)
- `DebugModeContainer` - Title bar and main debug toolbar
- `TabbedBottomPanel` - Tabbed panel with Health, Logs, Performance, and Device Control tabs
- `ModuleDetailPanel` - Drill-down panel for individual module details
- `DebugStatusBar` - Status bar with module count, health issues, and last refresh time

---

## Troubleshooting

### Debug Mode Not Activating
1. Check `debug.enabled` is `true` in config.json
2. Ensure WebView2 has focus (click on WebView area)
3. Verify hotkey isn't captured by another application

### Exit Password Not Working
1. Check `exit.requirePassword` setting
2. Password can be plain text or SHA256 hash
3. Empty/null passwordHash uses default: `admin123`

### Hardware Panel Shows All Offline
1. Check hardware configuration in config.json
2. Verify network connectivity to hardware devices
3. Enable Hardware API mode via the toggle in debug mode title bar
4. Check Hardware API server is running on port 8081

### API Server Not Responding
1. Check status indicator in debug mode title bar (should be green)
2. Navigate mode uses port 8787, Hardware API uses port 8081
3. Use `netstat -an | findstr "8787 8081"` to check if ports are in use
4. Toggle the mode switch off and on to restart the server

---

**Document Status:** Complete
