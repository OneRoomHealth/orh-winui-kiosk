# Machine Types Reference

Configuration guide for all three OneRoom Health kiosk machine types.

**Last Updated:** March 2026

---

## Overview

The `kiosk.machineType` field controls the hardware profile, UI behavior, and display target of the kiosk. All three types run the same app binary — the value changes what gets enabled at startup and how the WebView2 window behaves.

> **Note:** Machine type is applied at startup. Changing it via the debug panel saves to config immediately, but a restart is required for full effect (hardware modules, camera auto-selection, and touch settings take effect on next launch). Only the tab bar visibility changes immediately.

| Feature | `carewall` | `providerhub` | `techtablet` |
|---------|-----------|---------------|--------------|
| Default monitor | Secondary (`2`) | Primary (`1`) | Primary (`1`) |
| Tab bar | No | No | **Yes** |
| Touch gestures (pinch, swipe, zoom) | No | No | **Yes** |
| Browser accelerator keys (F5, Ctrl+R) | Disabled | Disabled | **Enabled** |
| Context menu | Debug mode only | Debug mode only | Always on |
| Popup / new window | Blocked | Blocked | Opens as new tab |
| Camera auto-select hint | Huddly | NVIDIA Broadcast | None (skipped) |
| Video mode | **Yes** | No | No |
| LED display (Novastar) | **Yes** | No | No |
| DMX lighting | **Yes** | No | No |
| Biamp | **Yes** | No | No |
| All hardware modules | **Full** | Camera + audio only | None needed |

---

## carewall

Full AV kiosk with LED wall, Huddly cameras, DMX lighting, Biamp audio, and video mode. Runs on the secondary display while the provider's workstation uses the primary.

### Behavior Notes

- Targets `targetMonitorIndex: 2` — always a secondary display
- Camera auto-selection looks for a camera whose label contains `"Huddly"`
- Video mode (`kiosk.videoMode`) is supported and typically enabled
- All hardware modules should be configured and enabled
- WebView2 popups are blocked; the frontend controls all navigation
- Touch/pinch/zoom are disabled — this is a mouse/keyboard wall display, not a touch screen

### Hardware Modules

| Module | Recommended | Notes |
|--------|-------------|-------|
| `displays` | **enabled** | Novastar KU20 LED wall via HTTP API |
| `cameras` | **enabled** | Huddly L1 via SDK — one entry per physical camera |
| `lighting` | **enabled** | DMX512 via FTDI USB adapter |
| `systemAudio` | **enabled** | Controls Windows default audio device |
| `microphones` | situational | Enable if Biamp USB audio is routed through Windows |
| `speakers` | situational | Enable if Biamp USB audio is routed through Windows |
| `biamp` | **enabled** | Biamp Parlé VBC 2800 via Telnet (port 23) |
| `media` | **enabled** | Serves local video files to the frontend at port 8081 |

### Complete Example Config

```json
{
  "kiosk": {
    "defaultUrl": "https://your-frontend-url.com/wall/default",
    "machineType": "carewall",
    "targetMonitorIndex": 2,
    "videoMode": {
      "enabled": true,
      "carescapeVideoPath": "C:\\Users\\CareWall\\Videos\\carescape.mp4",
      "demoVideoPath1": "C:\\Users\\CareWall\\Videos\\demo1.mp4",
      "demoVideoPath2": "C:\\Users\\CareWall\\Videos\\demo2.mp4",
      "videoPaths": [
        "C:\\Users\\CareWall\\Videos\\carescape.mp4",
        "C:\\Users\\CareWall\\Videos\\demo1.mp4",
        "C:\\Users\\CareWall\\Videos\\demo2.mp4"
      ],
      "mpvPath": null,
      "flicButtonEnabled": true
    }
  },
  "debug": {
    "enabled": true,
    "hotkey": "Ctrl+Shift+I"
  },
  "exit": {
    "enabled": true,
    "hotkey": "Ctrl+Shift+Q",
    "passwordHash": ""
  },
  "logging": {
    "path": "%LocalAppData%\\OneRoomHealthKiosk\\logs",
    "maxSizeKb": 10240,
    "maxFiles": 5
  },
  "hardware": {
    "displays": {
      "enabled": true,
      "monitorInterval": 5.0,
      "devices": [
        {
          "id": "display-main",
          "name": "Main LED Wall",
          "model": "Novastar KU20",
          "ipAddresses": ["10.64.0.162", "10.64.0.187"],
          "port": 8001
        }
      ]
    },
    "cameras": {
      "enabled": true,
      "monitorInterval": 5.0,
      "useUsbDiscovery": true,
      "useIpDiscovery": false,
      "devices": [
        {
          "id": "camera-0",
          "name": "Huddly L1 (Main)",
          "model": "L1",
          "deviceId": "YOUR-SERIAL-HERE"
        }
      ]
    },
    "lighting": {
      "enabled": true,
      "monitorInterval": 5.0,
      "fps": 25,
      "devices": [
        {
          "id": "light-main",
          "name": "Room Lights",
          "model": "DMX Decoder",
          "channelMapping": { "red": 1, "green": 2, "blue": 3, "white": 4 }
        }
      ]
    },
    "systemAudio": {
      "enabled": true,
      "monitorInterval": 1.0
    },
    "microphones": {
      "enabled": false,
      "monitorInterval": 1.0,
      "devices": []
    },
    "speakers": {
      "enabled": false,
      "monitorInterval": 1.0,
      "devices": []
    },
    "biamp": {
      "enabled": true,
      "monitorInterval": 5.0,
      "devices": [
        {
          "id": "biamp-main",
          "name": "Biamp Parlé VBC 2800",
          "model": "Parlé VBC 2800",
          "ipAddress": "10.64.0.201",
          "port": 23,
          "username": "control",
          "password": ""
        }
      ]
    },
    "media": {
      "enabled": true,
      "baseDirectory": "C:\\Users\\CareWall\\Videos",
      "additionalDirectories": [],
      "allowedExtensions": ["mp4", "webm", "ogg", "mp3", "wav", "m4a"]
    }
  }
}
```

### carewall-Specific Notes

- **Camera serials:** Each physical Huddly camera needs its own device entry with a single serial number. Do not use hyphenated `"serial1-serial2"` format — that is a legacy format from the old CameraController approach and will cause the second serial to be silently ignored.
- **Camera serial format:** Find serials in Device Manager → Cameras → right-click camera → Properties → Details → Hardware Ids.
- **Display IPs:** Provide multiple IPs in `ipAddresses` if the Novastar has redundant controllers. The module tries each in order.
- **Biamp password:** The Biamp Parlé VBC 2800 default Telnet password is blank. If the device has been secured, enter the password in `"password"`.
- **Video mode:** `mpvPath: null` lets the app auto-detect MPV from standard install locations (`C:\mpv\mpv.exe`, system PATH). Set an explicit path if MPV is installed somewhere non-standard.
- **Video hotkeys:** `Ctrl+Alt+R` (carescape loop), `Ctrl+Alt+D` (demo toggle), `Ctrl+Alt+1/2/3` (indexed from `videoPaths`), `Ctrl+Alt+E` (stop video).
- **Flic button:** If a Flic button is not present, set `flicButtonEnabled: false` to avoid unnecessary connection attempts.
- **`microphones`/`speakers` disabled by default:** Biamp audio is controlled through the `biamp` Telnet module. Only enable the Windows audio mic/speaker modules if the Biamp's USB audio interface is also connected to and routed through Windows audio.

---

## providerhub

Provider workstation kiosk. A browser-locked, always-on-top window on the primary display. Typically used at a provider's desk with a connected webcam (NVIDIA Broadcast virtual camera). No AV hardware — no DMX, no LED wall, no Biamp.

### Behavior Notes

- Targets `targetMonitorIndex: 1` — primary display
- Camera auto-selection looks for a camera whose label contains `"NVIDIA Broadcast"`
- Video mode is not used — leave `videoMode.enabled: false`
- Hardware modules should be limited to camera and audio only
- WebView2 popups are blocked; the frontend controls all navigation
- Touch/pinch/zoom are disabled — this is a mouse/keyboard workstation

### Hardware Modules

| Module | Recommended | Notes |
|--------|-------------|-------|
| `displays` | **disabled** | No Novastar LED wall at a provider hub |
| `cameras` | **enabled** | Typically a webcam or NVIDIA Broadcast virtual camera |
| `lighting` | **disabled** | No DMX hardware |
| `systemAudio` | **enabled** | Controls Windows default audio device |
| `microphones` | situational | Enable if a named USB microphone needs direct control |
| `speakers` | situational | Enable if named USB speakers need direct control |
| `biamp` | **disabled** | No Biamp at a provider hub |
| `media` | **disabled** | No local video serving needed |

### Complete Example Config

```json
{
  "kiosk": {
    "defaultUrl": "https://your-frontend-url.com/provider/dashboard",
    "machineType": "providerhub",
    "targetMonitorIndex": 1,
    "videoMode": {
      "enabled": false,
      "carescapeVideoPath": "C:\\Videos\\carescape.mp4",
      "demoVideoPath1": "C:\\Videos\\demo1.mp4",
      "demoVideoPath2": "C:\\Videos\\demo2.mp4",
      "videoPaths": [],
      "mpvPath": null,
      "flicButtonEnabled": false
    }
  },
  "debug": {
    "enabled": true,
    "hotkey": "Ctrl+Shift+I"
  },
  "exit": {
    "enabled": true,
    "hotkey": "Ctrl+Shift+Q",
    "passwordHash": ""
  },
  "logging": {
    "path": "%LocalAppData%\\OneRoomHealthKiosk\\logs",
    "maxSizeKb": 10240,
    "maxFiles": 5
  },
  "hardware": {
    "displays": {
      "enabled": false,
      "monitorInterval": 5.0,
      "devices": []
    },
    "cameras": {
      "enabled": true,
      "monitorInterval": 5.0,
      "useUsbDiscovery": true,
      "useIpDiscovery": false,
      "devices": [
        {
          "id": "camera-0",
          "name": "Provider Webcam",
          "model": "Webcam",
          "deviceId": "YOUR-SERIAL-HERE"
        }
      ]
    },
    "lighting": {
      "enabled": false,
      "monitorInterval": 5.0,
      "devices": []
    },
    "systemAudio": {
      "enabled": true,
      "monitorInterval": 1.0
    },
    "microphones": {
      "enabled": false,
      "monitorInterval": 1.0,
      "devices": []
    },
    "speakers": {
      "enabled": false,
      "monitorInterval": 1.0,
      "devices": []
    },
    "biamp": {
      "enabled": false,
      "monitorInterval": 5.0,
      "devices": []
    },
    "media": {
      "enabled": false,
      "baseDirectory": "",
      "additionalDirectories": [],
      "allowedExtensions": ["mp4", "webm", "ogg", "mp3", "wav", "m4a"]
    }
  }
}
```

### providerhub-Specific Notes

- **NVIDIA Broadcast:** Camera auto-selection on providerhub searches for `"NVIDIA Broadcast"` in the camera label. If NVIDIA Broadcast is not installed, the app will fall back to whichever camera is first enumerated.
- **Hardware API mode:** The full hardware API (port 8081) is available in debug mode but rarely needed at a provider hub. The Navigate API (port 8787) is typically sufficient for remote URL control.
- **No video mode fields required:** The `videoMode` block can be omitted entirely since it defaults to `enabled: false`. Including it with explicit `false` values is fine and makes the config self-documenting.
- **Minimal hardware config:** Only `systemAudio` and `cameras` are typically active. Disabled modules can be omitted from the config entirely (they default to disabled with no devices), but including them explicitly makes the config easier to audit.

---

## techtablet

Touch-optimized browser kiosk for a tablet or touch-screen device. No hardware integration at all — this type is pure WebView2 with a tab bar and touch gestures enabled.

### Behavior Notes

- Targets `targetMonitorIndex: 1` — primary display (the tablet screen)
- **Tab bar is enabled** — users can open multiple tabs with the `+` button
- **New window requests open as a new tab** instead of being blocked
- **Touch optimized:** pinch-to-zoom, swipe navigation, zoom controls, and status bar are all enabled
- **Browser accelerator keys are enabled** (F5 to reload, Ctrl+R, etc.)
- **Context menus are always on** (right-click works without entering debug mode)
- Camera auto-selection is skipped — the frontend manages camera selection
- No video mode, no hardware modules needed

### Hardware Modules

| Module | Recommended | Notes |
|--------|-------------|-------|
| `displays` | **disabled** | No Novastar LED wall |
| `cameras` | **disabled** | WebRTC camera managed by the browser/frontend directly |
| `lighting` | **disabled** | No DMX hardware |
| `systemAudio` | situational | Enable if system volume control from the frontend is needed |
| `microphones` | **disabled** | Managed by the browser via WebRTC |
| `speakers` | **disabled** | Managed by the browser via WebRTC |
| `biamp` | **disabled** | No Biamp hardware |
| `media` | **disabled** | No local video serving needed |

### Complete Example Config

```json
{
  "kiosk": {
    "defaultUrl": "https://your-frontend-url.com/tablet/home",
    "machineType": "techtablet",
    "targetMonitorIndex": 1,
    "videoMode": {
      "enabled": false,
      "carescapeVideoPath": "C:\\Videos\\carescape.mp4",
      "demoVideoPath1": "C:\\Videos\\demo1.mp4",
      "demoVideoPath2": "C:\\Videos\\demo2.mp4",
      "videoPaths": [],
      "mpvPath": null,
      "flicButtonEnabled": false
    }
  },
  "debug": {
    "enabled": true,
    "hotkey": "Ctrl+Shift+I"
  },
  "exit": {
    "enabled": true,
    "hotkey": "Ctrl+Shift+Q",
    "passwordHash": ""
  },
  "logging": {
    "path": "%LocalAppData%\\OneRoomHealthKiosk\\logs",
    "maxSizeKb": 10240,
    "maxFiles": 5
  },
  "hardware": {
    "displays": {
      "enabled": false,
      "monitorInterval": 5.0,
      "devices": []
    },
    "cameras": {
      "enabled": false,
      "monitorInterval": 5.0,
      "useUsbDiscovery": false,
      "useIpDiscovery": false,
      "devices": []
    },
    "lighting": {
      "enabled": false,
      "monitorInterval": 5.0,
      "devices": []
    },
    "systemAudio": {
      "enabled": true,
      "monitorInterval": 1.0
    },
    "microphones": {
      "enabled": false,
      "monitorInterval": 1.0,
      "devices": []
    },
    "speakers": {
      "enabled": false,
      "monitorInterval": 1.0,
      "devices": []
    },
    "biamp": {
      "enabled": false,
      "monitorInterval": 5.0,
      "devices": []
    },
    "media": {
      "enabled": false,
      "baseDirectory": "",
      "additionalDirectories": [],
      "allowedExtensions": ["mp4", "webm", "ogg", "mp3", "wav", "m4a"]
    }
  }
}
```

### techtablet-Specific Notes

- **Hardware API mode not needed:** Since all hardware is disabled, the Navigate API (port 8787) is the only relevant API. You generally do not need to switch to Hardware API mode on a techtablet.
- **Tab bar is the main differentiator:** The tab bar initializes when machineType is `techtablet` at startup. If you switch to a different type in debug mode, the tab bar is hidden immediately, but tab state is preserved in memory until restart.
- **Camera in WebRTC vs hardware module:** The techtablet camera is exposed to the frontend via the browser's standard WebRTC APIs. The `cameras` hardware module controls the Huddly SDK — not relevant on a tablet with a standard webcam.
- **`systemAudio` is optional:** Enable it if the frontend needs to control system volume via the hardware API. If the user controls volume through Windows directly, it can be disabled.
- **Minimal config is fine:** The entire `hardware` block can be omitted from a techtablet config and all modules will default to disabled. Including it explicitly is recommended so the config is complete and easy to audit.

---

## Switching Machine Types

Machine type can be changed two ways:

**Via config file (recommended):** Edit `%ProgramData%\OneRoomHealth\Kiosk\config.json`, change `kiosk.machineType`, and restart the app.

**Via debug mode:** Open debug mode (`Ctrl+Shift+I`), use the Machine Type dropdown. The change is saved to config immediately. Tab bar visibility changes instantly; all other effects (hardware, touch settings, camera auto-selection) require a restart.

> **Important:** The app logs a security event when machine type is changed via debug mode: `"MachineTypeChanged from X to Y via debug mode"`.

---

## Common Configuration Mistakes

| Mistake | Effect | Fix |
|---------|--------|-----|
| Missing `machineType` field | Defaults to `"carewall"` | Always set explicitly |
| `targetMonitorIndex: 1` on a carewall | Kiosk window appears on primary display, not the LED wall | Set to `2` for carewall |
| Hyphenated camera `deviceId` (e.g. `"AAAA-BBBB"`) | Only one of the two cameras can ever connect; the other is silently ignored | Split into two separate device entries |
| `videoMode.enabled: true` on providerhub or techtablet | Video hotkeys are active but there's no appropriate display setup | Set `enabled: false` |
| `lighting.enabled: true` on providerhub/techtablet | Module starts but finds no FTDI USB adapter; logs errors every monitor interval | Set `enabled: false` |
| `displays.enabled: true` on providerhub/techtablet with no devices | Module starts with 0 devices and polls for nothing | Set `enabled: false` |

---

## See Also

- [Configuration Reference](configuration.md) - Complete field-by-field schema
- [Hardware Config Example](hardware-config-example.md) - Full production carewall config
- [Video Mode Guide](VIDEO_MODE_GUIDE.md) - Video mode hotkeys and MPV setup
- [Debug Mode](DEBUG_MODE.md) - Debug panel features including machine type switching
