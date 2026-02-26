# Configuration Reference

Complete reference for all configuration options in the OneRoom Health Kiosk App.

**Last Updated:** February 2026

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
    "machineType": "carewall",
    "targetMonitorIndex": 1,
    "videoMode": {
      "enabled": false,
      "carescapeVideoPath": "C:\\Videos\\carescape.mp4",
      "demoVideoPath1": "C:\\Videos\\demo1.mp4",
      "demoVideoPath2": "C:\\Videos\\demo2.mp4",
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

---

## Kiosk Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `defaultUrl` | String | OneRoom Frontend URL | URL to load when app starts |
| `machineType` | String | `"carewall"` | Hardware profile. `"carewall"` = full AV with secondary display, `"providerhub"` = no DMX, primary display |
| `targetMonitorIndex` | Integer | `1` | Monitor index (1-based). `1` = primary, `2` = secondary, etc. |

**Note:** The kiosk always runs fullscreen and always-on-top. These behaviors are not configurable.

---

## Video Mode Settings

Located at `kiosk.videoMode`:

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `enabled` | Boolean | `false` | Enable video mode (Flic button control) |
| `carescapeVideoPath` | String | `C:\Videos\carescape.mp4` | Path to looping Carescape video file |
| `demoVideoPath1` | String | `C:\Videos\demo1.mp4` | Path to first demo video file |
| `demoVideoPath2` | String | `C:\Videos\demo2.mp4` | Path to second demo video file |
| `mpvPath` | String | `null` | Custom path to MPV executable (auto-detected if null) |
| `flicButtonEnabled` | Boolean | `true` | Enable Flic button integration |

**Note:** Volume is controlled via the Windows Volume Mixer. The app does not override system volume settings. Demo videos alternate automatically: when demo 1 ends, demo 2 plays, and vice versa.

---

## Debug Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `enabled` | Boolean | `true` | Enable debug mode hotkey |
| `hotkey` | String | `Ctrl+Shift+I` | Hotkey to toggle debug mode |

**Note:** The hotkey is hardcoded to Ctrl+Shift+I. The config value is used for reference/logging.

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

The kiosk supports two API modes, **controlled via a toggle in debug mode** (not config). The selected mode **persists across debug mode sessions** via `preferences.json`.

| Mode | Port | Description |
|------|------|-------------|
| **Hardware API (Default)** | 8081 | Full hardware control with all module endpoints |
| **Navigate** | 8787 | Lightweight server for remote URL navigation |

### Hardware API Mode (Port 8081) - Default
Default on first launch (or when `preferences.json` is absent). Provides:
- Full hardware control API
- All module endpoints (display, camera, lighting, audio, biamp, etc.)
- Swagger UI at `http://localhost:8081/swagger`

### Navigate Mode (Port 8787)
Switch via debug mode toggle. Provides:
- `POST /navigate` - Navigate WebView: `{"url": "https://..."}`
- `GET /health` - Server status check

**Note:** Ports are fixed. API mode is toggled in the debug UI, not via config. The preference persists — switching to Navigate mode and exiting debug mode will restore Navigate mode on next entry.

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

### Biamp Module

Controls Biamp Parle VBC 2800 video conferencing codecs via Telnet.

```json
"biamp": {
  "enabled": false,
  "monitorInterval": 5.0,
  "devices": [
    {
      "id": "0",
      "name": "Biamp VBC 2800",
      "model": "Parle VBC 2800",
      "ipAddress": "10.1.1.50",
      "port": 23,
      "username": "control",
      "password": ""
    }
  ]
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `ipAddress` | String | - | Biamp device IP address |
| `port` | Integer | `23` | Telnet port |
| `username` | String | `"control"` | Telnet login username |
| `password` | String | `""` | Telnet login password |

### Media Serving

Enables an HTTP endpoint on the Hardware API server to serve local media files (video, audio) for use by the frontend.

```json
"media": {
  "enabled": false,
  "baseDirectory": "%USERPROFILE%\\Videos",
  "additionalDirectories": [],
  "allowedExtensions": ["mp4", "webm", "ogg", "mp3", "wav", "m4a"]
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `enabled` | Boolean | `false` | Enable media file serving endpoint |
| `baseDirectory` | String | `""` | Base directory for media files (supports environment variables) |
| `additionalDirectories` | String[] | `[]` | Additional directories to search |
| `allowedExtensions` | String[] | See above | Allowed file extensions |

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
    "machineType": "carewall",
    "targetMonitorIndex": 2,
    "videoMode": {
      "enabled": true,
      "carescapeVideoPath": "C:\\Users\\CareWall\\Videos\\carescape.mp4",
      "demoVideoPath1": "C:\\Users\\CareWall\\Videos\\demo1.mp4",
      "demoVideoPath2": "C:\\Users\\CareWall\\Videos\\demo2.mp4",
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
