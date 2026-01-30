# Configuration Reference

Complete reference for all configuration options in the OneRoom Health Kiosk App.

**Last Updated:** January 2026

---

## Configuration File Location

```
%ProgramData%\OneRoomHealth\Kiosk\config.json
```

The app automatically creates a default configuration file if it doesn't exist.

---

## Complete Configuration Schema

```json
{
  "kiosk": {
    "defaultUrl": "https://example.com/wall/default",
    "targetMonitorIndex": 1,
    "videoMode": {
      "enabled": false,
      "carescapeVideoPath": "C:\\Videos\\carescape.mp4",
      "demoVideoPath": "C:\\Videos\\demo.mp4",
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
      "devices": []
    },
    "cameras": {
      "enabled": true,
      "monitorInterval": 5.0,
      "useUsbDiscovery": true,
      "useIpDiscovery": false,
      "devices": []
    },
    "lighting": {
      "enabled": true,
      "monitorInterval": 5.0,
      "fps": 25,
      "devices": []
    },
    "systemAudio": {
      "enabled": true,
      "monitorInterval": 1.0
    },
    "microphones": {
      "enabled": true,
      "monitorInterval": 1.0,
      "devices": []
    },
    "speakers": {
      "enabled": true,
      "monitorInterval": 1.0,
      "devices": []
    }
  }
}
```

---

## Kiosk Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `defaultUrl` | String | OneRoom Frontend URL | URL to load when app starts |
| `targetMonitorIndex` | Integer | `1` | Monitor index (1-based). `1` = primary, `2` = secondary, etc. |

**Note:** The kiosk always runs fullscreen and always-on-top. These behaviors are not configurable.

---

## Video Mode Settings

Located at `kiosk.videoMode`:

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `enabled` | Boolean | `false` | Enable video mode (Flic button control) |
| `carescapeVideoPath` | String | `C:\Videos\carescape.mp4` | Path to Carescape video file |
| `demoVideoPath` | String | `C:\Videos\demo.mp4` | Path to demo video file |
| `mpvPath` | String | `null` | Custom path to MPV executable (auto-detected if null) |
| `flicButtonEnabled` | Boolean | `true` | Enable Flic button integration |

**Note:** Volume is controlled via the Windows Volume Mixer. The app does not override system volume settings.

---

## Debug Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `enabled` | Boolean | `true` | Enable debug mode hotkey |
| `hotkey` | String | `Ctrl+Shift+I` | Hotkey to toggle debug mode (informational only) |

**Note:** The hotkey string is logged but the actual hotkey is hardcoded to Ctrl+Shift+I.

---

## Exit Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `enabled` | Boolean | `true` | Enable exit hotkey |
| `hotkey` | String | `Ctrl+Shift+Q` | Hotkey to trigger exit (informational only) |
| `passwordHash` | String | `""` | Password (plain text or SHA256 hash) |

**Note:** The hotkey string is logged but the actual hotkey is hardcoded to Ctrl+Shift+Q.

**Password Modes:**
- Empty/null: Uses default password `admin123`
- Plain text: Any string < 64 characters
- SHA256 hash: 64-character hex string

---

## Logging Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `path` | String | `%LocalAppData%\OneRoomHealthKiosk\logs` | Log directory |
| `maxSizeKb` | Integer | `10240` | Max log file size in KB |
| `maxFiles` | Integer | `5` | Max number of log files to keep |

---

## HTTP API Modes

The kiosk supports two API modes, **controlled via a toggle in debug mode** (not config):

| Mode | Port | Description |
|------|------|-------------|
| **Navigate (Default)** | 8787 | Lightweight server for remote URL navigation |
| **Hardware API** | 8081 | Full hardware control with all module endpoints |

### Navigate Mode (Port 8787) - Default
Starts automatically. Provides:
- `POST /navigate` - Navigate WebView: `{"url": "https://..."}`
- `GET /health` - Server status check

### Hardware API Mode (Port 8081)
Enable via debug mode toggle. Provides:
- Full hardware control API
- All module endpoints (display, camera, lighting, audio, etc.)
- Swagger UI at `http://localhost:8081/swagger`

**Note:** Ports are fixed. API mode is toggled in debug UI, not via config.

---

## Hardware Settings

Hardware modules can be toggled ON/OFF in the debug mode Hardware Health panel. The `hardware` section provides device-specific configuration.

### Common Module Settings

All hardware modules inherit these settings:

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `enabled` | Boolean | `true` | Whether the module is enabled at startup |
| `monitorInterval` | Double | `5.0` (or `1.0` for audio) | Health check interval in seconds |

### Display Module
```json
"displays": {
  "enabled": true,
  "monitorInterval": 5.0,
  "devices": [
    {
      "id": "0",
      "name": "Main LED Display",
      "model": "Novastar KU20",
      "ipAddresses": ["10.1.1.40"],
      "port": 8001
    }
  ]
}
```

### Camera Module

Controls Huddly cameras via direct SDK integration. Supports PTZ control, auto-tracking (Genius Framing), and auto-framing.

```json
"cameras": {
  "enabled": true,
  "monitorInterval": 5.0,
  "useUsbDiscovery": true,
  "useIpDiscovery": false,
  "devices": [
    {
      "id": "0",
      "name": "Main Camera",
      "model": "Huddly L1",
      "deviceId": "52446F0141"
    }
  ]
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `useUsbDiscovery` | Boolean | `true` | Enable USB device discovery for Huddly cameras |
| `useIpDiscovery` | Boolean | `false` | Enable IP device discovery (for Huddly L1 over network) |
| `deviceId` | String | - | Camera serial number for matching (find in Device Manager) |

### Lighting Module
```json
"lighting": {
  "enabled": true,
  "monitorInterval": 5.0,
  "fps": 25,
  "devices": [
    {
      "id": "0",
      "name": "Room Lights",
      "channelMapping": {
        "red": 17,
        "green": 18,
        "blue": 19,
        "white": 20
      }
    }
  ]
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `fps` | Integer | `25` | DMX frame rate |

### Audio Modules
```json
"systemAudio": {
  "enabled": true,
  "monitorInterval": 1.0
},
"microphones": {
  "enabled": true,
  "monitorInterval": 1.0,
  "devices": [
    {
      "id": "0",
      "name": "Jabra Speak 750",
      "deviceId": "optional-device-id"
    }
  ]
},
"speakers": {
  "enabled": true,
  "monitorInterval": 1.0,
  "devices": [
    {
      "id": "0",
      "name": "Main Speakers",
      "deviceId": "optional-device-id"
    }
  ]
}
```

---

## Environment Variables

Supported in path settings:
- `%ProgramData%` - Program data directory
- `%LocalAppData%` - Local application data
- `%AppData%` - Roaming application data
- `%TEMP%` - Temporary files directory
- `%USERPROFILE%` - User profile directory

---

## Example Configurations

### Minimal Development Config
```json
{
  "kiosk": {
    "defaultUrl": "https://your-frontend-url.com/wall/default",
    "targetMonitorIndex": 1
  },
  "debug": {
    "enabled": true
  },
  "exit": {
    "enabled": true,
    "passwordHash": ""
  }
}
```

### Production Config
```json
{
  "kiosk": {
    "defaultUrl": "https://production-url.com/wall/default",
    "targetMonitorIndex": 2
  },
  "debug": {
    "enabled": false
  },
  "exit": {
    "enabled": true,
    "passwordHash": "your-sha256-hash-here"
  },
  "logging": {
    "maxSizeKb": 5120,
    "maxFiles": 3
  }
}
```

### Video Mode Config
```json
{
  "kiosk": {
    "defaultUrl": "https://your-url.com/wall/default",
    "targetMonitorIndex": 2,
    "videoMode": {
      "enabled": true,
      "carescapeVideoPath": "C:\\Users\\CareWall\\Videos\\carescape.mp4",
      "demoVideoPath": "C:\\Users\\CareWall\\Videos\\demo.mp4",
      "flicButtonEnabled": true
    }
  }
}
```

### With Hardware Device Config
```json
{
  "kiosk": {
    "defaultUrl": "https://your-url.com/wall/default",
    "targetMonitorIndex": 2
  },
  "hardware": {
    "displays": {
      "enabled": true,
      "devices": [
        {
          "id": "0",
          "name": "LED Wall",
          "ipAddresses": ["10.1.1.40"],
          "port": 8001
        }
      ]
    },
    "cameras": {
      "enabled": true,
      "useUsbDiscovery": true,
      "devices": [
        {
          "id": "0",
          "name": "Huddly L1",
          "deviceId": "YOUR-SERIAL-NUMBER"
        }
      ]
    },
    "lighting": {
      "enabled": true,
      "fps": 30,
      "devices": [
        {
          "id": "0",
          "name": "Room Lights",
          "channelMapping": {
            "red": 1,
            "green": 2,
            "blue": 3,
            "white": 4
          }
        }
      ]
    }
  }
}
```

---

## See Also

- [Debug Mode](DEBUG_MODE.md) - Debug mode features and API toggle
- [Video Mode Guide](VIDEO_MODE_GUIDE.md) - Video mode setup
- [Troubleshooting](TROUBLESHOOTING.md) - Common issues
