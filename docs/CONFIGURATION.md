# Configuration Reference

Complete reference for all configuration options in the OneRoom Health Kiosk App.

---

## Configuration File Location

The configuration file is located at:
```
%ProgramData%\OneRoomHealth\Kiosk\config.json
```

The app automatically creates a default configuration file if it doesn't exist.

---

## Configuration Schema

```json
{
  "kiosk": {
    "defaultUrl": "string",
    "targetMonitorIndex": 1,
    "fullscreen": true,
    "alwaysOnTop": true,
    "videoMode": {
      "enabled": false,
      "carescapeVideoPath": "string",
      "demoVideoPath": "string",
      "carescapeVolume": 50,
      "demoVolume": 75,
      "targetMonitor": 1,
      "flicButtonEnabled": true
    }
  },
  "debug": {
    "enabled": true,
    "hotkey": "Ctrl+Shift+F12",
    "autoOpenDevTools": false,
    "windowSizePercent": 80
  },
  "exit": {
    "enabled": true,
    "hotkey": "Ctrl+Shift+Escape",
    "requirePassword": true,
    "passwordHash": "string",
    "timeout": 5000
  },
  "logging": {
    "enabled": true,
    "level": "Info",
    "path": "%LocalAppData%\\OneRoomHealthKiosk\\logs",
    "maxSizeKb": 10240,
    "maxFiles": 5
  },
  "httpApi": {
    "enabled": true,
    "port": 8081,
    "allowRemote": false
  },
  "hardware": {
    "displays": { "enabled": true },
    "cameras": { "enabled": true },
    "lighting": { "enabled": true },
    "systemAudio": { "enabled": true },
    "microphones": { "enabled": true },
    "speakers": { "enabled": true }
  }
}
```

---

## Kiosk Settings

### `defaultUrl`
- **Type:** String
- **Default:** `"https://orh-frontend-dev-container.politebeach-927fe169.westus2.azurecontainerapps.io/wall/default"`
- **Description:** URL to load when app starts (if video mode is disabled)

### `targetMonitorIndex`
- **Type:** Integer
- **Default:** `1`
- **Description:** Monitor index (0-based) where the kiosk window should appear
- **Values:** `0` = first monitor, `1` = second monitor, etc.

### `fullscreen`
- **Type:** Boolean
- **Default:** `true`
- **Description:** Whether the window should be fullscreen

### `alwaysOnTop`
- **Type:** Boolean
- **Default:** `true`
- **Description:** Whether the window should stay on top of other windows

### `videoMode`
See [Video Mode Guide](VIDEO_MODE_GUIDE.md) for details.

---

## Debug Settings

### `enabled`
- **Type:** Boolean
- **Default:** `true` (for development)
- **Description:** Enable debug mode hotkey

### `hotkey`
- **Type:** String
- **Default:** `"Ctrl+Shift+F12"`
- **Description:** Hotkey combination to toggle debug mode

### `autoOpenDevTools`
- **Type:** Boolean
- **Default:** `false`
- **Description:** Automatically open WebView2 developer tools when entering debug mode

### `windowSizePercent`
- **Type:** Integer
- **Default:** `80`
- **Description:** Window size as percentage of screen when in debug mode (1-100)

---

## Exit Settings

### `enabled`
- **Type:** Boolean
- **Default:** `true` (for development)
- **Description:** Enable exit mechanism hotkey

### `hotkey`
- **Type:** String
- **Default:** `"Ctrl+Shift+Escape"`
- **Description:** Hotkey combination to trigger exit

### `requirePassword`
- **Type:** Boolean
- **Default:** `true`
- **Description:** Require password to exit kiosk mode

### `passwordHash`
- **Type:** String
- **Default:** `""` (defaults to "admin123")
- **Description:** Exit password - can be plain text or SHA256 hash
- **Modes:**
  - **Empty/null**: Uses default password "admin123"
  - **Plain text**: Any string less than 64 characters (e.g., "admin123", "mypassword")
  - **SHA256 hash**: 64-character hex string (e.g., "240be518fabd2724ddb6f04eeb1da5967448d7e831c08c8fa822809f74c720a9")
- **Security Note:** Plain text passwords are easier to manage but less secure. For production use, consider using SHA256 hashes.
- **Example:** `"passwordHash": "admin123"` for plain text password

### `timeout`
- **Type:** Integer
- **Default:** `5000`
- **Description:** Timeout in milliseconds for exit dialog

---

## Logging Settings

### `enabled`
- **Type:** Boolean
- **Default:** `true`
- **Description:** Enable file logging

### `level`
- **Type:** String
- **Default:** `"Info"`
- **Values:** `"Debug"`, `"Info"`, `"Warning"`, `"Error"`
- **Description:** Minimum log level to record

### `path`
- **Type:** String
- **Default:** `"%LocalAppData%\\OneRoomHealthKiosk\\logs"`
- **Description:** Directory path for log files
- **Note:** Environment variables are expanded automatically

### `maxSizeKb`
- **Type:** Integer
- **Default:** `10240`
- **Description:** Maximum size of a single log file in KB

### `maxFiles`
- **Type:** Integer
- **Default:** `5`
- **Description:** Maximum number of log files to keep (oldest are deleted)

---

## HTTP API Settings

### `enabled`
- **Type:** Boolean
- **Default:** `true`
- **Description:** Enable local HTTP API server

### `port`
- **Type:** Integer
- **Default:** `8081`
- **Description:** Port number for Hardware Control API
- **Range:** 1024-65535

### `allowRemote`
- **Type:** Boolean
- **Default:** `false`
- **Description:** Allow connections from remote machines (not just localhost)
- **Security:** Keep `false` unless you need remote control

---

## Hardware Settings

The `hardware` section configures all hardware control modules.

### Quick Reference

```json
{
  "hardware": {
    "displays": {
      "enabled": true,
      "monitorInterval": 1.0,
      "devices": [
        {
          "id": "0",
          "name": "Main LED Display",
          "model": "Novastar KU20",
          "ipAddresses": ["10.1.1.40"],
          "port": 8001
        }
      ]
    },
    "cameras": {
      "enabled": true,
      "monitorInterval": 5.0,
      "controllerExePath": "hardware/huddly/CameraController.exe",
      "controllerApiPort": 5000,
      "devices": [
        {
          "id": "0",
          "name": "Main Camera",
          "model": "Huddly L1",
          "deviceId": "52446F0141"
        }
      ]
    },
    "lighting": {
      "enabled": true,
      "monitorInterval": 5.0,
      "dmxFps": 25,
      "devices": [
        {
          "id": "0",
          "name": "Room Lights",
          "channels": { "red": 17, "green": 18, "blue": 19, "white": 20 }
        }
      ]
    },
    "systemAudio": {
      "enabled": true
    },
    "microphones": {
      "enabled": true,
      "devices": [
        {
          "id": "0",
          "name": "Jabra Speak 750",
          "friendlyNameMatch": "Jabra"
        }
      ]
    },
    "speakers": {
      "enabled": true,
      "devices": [
        {
          "id": "0",
          "name": "Main Speakers",
          "friendlyNameMatch": "Speakers"
        }
      ]
    }
  }
}
```

### Module Enable/Disable

Each hardware module can be individually enabled or disabled:

```json
{
  "hardware": {
    "displays": { "enabled": false },
    "cameras": { "enabled": true }
  }
}
```

Disabled modules will not be initialized or monitored.

---

## Video Mode Settings

See [Video Mode Guide](VIDEO_MODE_GUIDE.md) for complete documentation.

### Quick Reference

```json
{
  "videoMode": {
    "enabled": false,
    "carescapeVideoPath": "C:\\Videos\\carescape.mp4",
    "demoVideoPath": "C:\\Videos\\demo.mp4",
    "carescapeVolume": 50,
    "demoVolume": 75,
    "targetMonitor": 1,
    "flicButtonEnabled": true
  }
}
```

---

## Environment Variables

The following environment variables can be used in paths:
- `%ProgramData%` - Program data directory
- `%LocalAppData%` - Local application data
- `%AppData%` - Roaming application data
- `%TEMP%` - Temporary files directory
- `%USERPROFILE%` - User profile directory

Example:
```json
{
  "logging": {
    "path": "%LocalAppData%\\OneRoomHealthKiosk\\logs"
  }
}
```

---

## Configuration Validation

The app validates configuration on startup:
- Invalid values are replaced with defaults
- Missing sections are created with defaults
- Errors are logged but don't prevent app startup

---

## Security Considerations

1. **Password Hash**: Never store plain text passwords
2. **File Permissions**: Config file should be readable only by administrators
3. **Remote API**: Keep `allowRemote: false` unless needed
4. **Video Paths**: Validate video file paths to prevent arbitrary file access

---

## Example Configurations

### Development Configuration
```json
{
  "debug": { "enabled": true },
  "exit": { "enabled": true, "requirePassword": false }
}
```

### Production Configuration
```json
{
  "debug": { "enabled": false },
  "exit": { "enabled": true, "requirePassword": true },
  "httpApi": { "allowRemote": false }
}
```

### Video Mode Configuration
```json
{
  "kiosk": {
    "videoMode": {
      "enabled": true,
      "carescapeVideoPath": "C:\\Videos\\carescape.mp4",
      "demoVideoPath": "C:\\Videos\\demo.mp4"
    }
  }
}
```

---

## See Also

- [Video Mode Guide](VIDEO_MODE_GUIDE.md) - Video mode configuration
- [Deployment Guide](DEPLOYMENT_GUIDE.md) - Deployment instructions
- [Troubleshooting Guide](TROUBLESHOOTING.md) - Common issues

