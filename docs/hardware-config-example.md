# Hardware Configuration Example

Complete example `config.json` for a fully configured OneRoom Health kiosk with all 6 hardware modules.

**Config File Location:** `%ProgramData%\OneRoomHealth\Kiosk\config.json`

---

## Complete Production Config Example

```json
{
  "kiosk": {
    "defaultUrl": "https://orh-frontend-dev-container.politebeach-927fe169.westus2.azurecontainerapps.io/wall/default",
    "targetMonitorIndex": 2,
    "videoMode": {
      "enabled": true,
      "carescapeVideoPath": "C:\\Users\\CareWall\\Videos\\carescape.mp4",
      "demoVideoPath": "C:\\Users\\CareWall\\Videos\\demo.mp4",
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
          "ipAddresses": ["10.1.1.40", "10.1.1.41"],
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
          "id": "camera-main",
          "name": "Huddly L1",
          "model": "L1",
          "deviceId": "52446F0141"
        }
      ]
    },
    "lighting": {
      "enabled": true,
      "monitorInterval": 5.0,
      "fps": 25,
      "devices": [
        {
          "id": "light-room",
          "name": "Room Ambient Lights",
          "model": "RGBW LED Strip",
          "channelMapping": {
            "red": 1,
            "green": 2,
            "blue": 3,
            "white": 4
          }
        },
        {
          "id": "light-accent",
          "name": "Accent Lights",
          "model": "RGBW Par Can",
          "channelMapping": {
            "red": 5,
            "green": 6,
            "blue": 7,
            "white": 8
          }
        }
      ]
    },
    "systemAudio": {
      "enabled": true,
      "monitorInterval": 1.0
    },
    "microphones": {
      "enabled": true,
      "monitorInterval": 1.0,
      "devices": [
        {
          "id": "mic-main",
          "name": "Biamp Parle VBC 2800",
          "deviceId": "28e6:4074"
        }
      ]
    },
    "speakers": {
      "enabled": true,
      "monitorInterval": 1.0,
      "devices": [
        {
          "id": "speaker-main",
          "name": "Biamp Parle VBC 2800",
          "deviceId": "28e6:4074"
        }
      ]
    }
  }
}
```

---

## Module-by-Module Breakdown

### Display Module

Controls Novastar LED displays via HTTP API. Supports multiple IP addresses per display for redundancy.

```json
"displays": {
  "enabled": true,
  "monitorInterval": 5.0,
  "devices": [
    {
      "id": "display-main",
      "name": "Main LED Wall",
      "model": "Novastar KU20",
      "ipAddresses": ["10.1.1.40", "10.1.1.41"],
      "port": 8001
    }
  ]
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `id` | Yes | Unique identifier for the device |
| `name` | Yes | Human-readable display name |
| `model` | No | Display model (for reference) |
| `ipAddresses` | Yes | Array of IP addresses (for redundancy) |
| `port` | No | HTTP API port (default: 8001) |

---

### Camera Module

Controls Huddly cameras via direct SDK integration (Huddly.Sdk NuGet package). Supports PTZ control, auto-tracking (Genius Framing), and auto-framing.

```json
"cameras": {
  "enabled": true,
  "monitorInterval": 5.0,
  "useUsbDiscovery": true,
  "useIpDiscovery": false,
  "devices": [
    {
      "id": "camera-main",
      "name": "Huddly L1",
      "model": "L1",
      "deviceId": "52446F0141"
    }
  ]
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `useUsbDiscovery` | No | Enable USB device discovery (default: true) |
| `useIpDiscovery` | No | Enable IP device discovery for network cameras (default: false) |
| `id` | Yes | Unique identifier for the camera |
| `name` | Yes | Human-readable camera name |
| `model` | No | Camera model (L1, IQ, S1, etc.) |
| `deviceId` | Yes | Camera serial number for matching to config |

**Finding Camera Serial Number:**
1. Open Device Manager
2. Find the Huddly camera under "Cameras"
3. Right-click > Properties > Details > Hardware Ids
4. Look for the serial number (e.g., "52446F0141")
5. Or check camera label/packaging for serial number

**Supported Cameras:**
- Huddly L1 (USB or IP)
- Huddly IQ
- Huddly S1
- Other Huddly cameras with SDK support

---

### Lighting Module

Controls DMX512 lights via FTDI USB adapter. Supports RGBW color mixing.

```json
"lighting": {
  "enabled": true,
  "monitorInterval": 5.0,
  "fps": 25,
  "devices": [
    {
      "id": "light-room",
      "name": "Room Ambient Lights",
      "model": "RGBW LED Strip",
      "channelMapping": {
        "red": 1,
        "green": 2,
        "blue": 3,
        "white": 4
      }
    }
  ]
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `fps` | No | DMX frame rate (default: 25) |
| `id` | Yes | Unique identifier for the light |
| `name` | Yes | Human-readable light name |
| `model` | No | Light fixture model |
| `channelMapping` | Yes | DMX channel assignments (1-512) |

**Channel Mapping:**
- DMX channels are 1-indexed (1-512)
- Each light fixture uses consecutive channels
- Check your fixture's manual for channel order

---

### SystemAudio Module

Controls Windows system audio (default speaker/microphone). No device configuration needed - uses Windows defaults.

```json
"systemAudio": {
  "enabled": true,
  "monitorInterval": 1.0
}
```

This module provides:
- System speaker volume control
- System speaker mute
- System microphone volume control
- System microphone mute

---

### Microphone Module

Controls network microphones via HTTP API.

```json
"microphones": {
  "enabled": true,
  "monitorInterval": 1.0,
  "devices": [
    {
      "id": "mic-main",
      "name": "Biamp Parle VBC 2800",
      "deviceId": "28e6:4074"
    }
  ]
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `id` | Yes | Unique identifier |
| `name` | Yes | Human-readable name |
| `deviceId` | No | USB/Network device identifier |

---

### Speaker Module

Controls network speakers via HTTP API.

```json
"speakers": {
  "enabled": true,
  "monitorInterval": 1.0,
  "devices": [
    {
      "id": "speaker-main",
      "name": "Biamp Parle VBC 2800",
      "deviceId": "28e6:4074"
    }
  ]
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `id` | Yes | Unique identifier |
| `name` | Yes | Human-readable name |
| `deviceId` | No | USB/Network device identifier |

---

## Minimal Hardware Config (Just Lighting + System Audio)

If you only have DMX lighting and want system audio control:

```json
{
  "kiosk": {
    "defaultUrl": "https://your-frontend-url.com/wall/default",
    "targetMonitorIndex": 2
  },
  "debug": {
    "enabled": true
  },
  "exit": {
    "enabled": true
  },
  "hardware": {
    "displays": {
      "enabled": false
    },
    "cameras": {
      "enabled": false
    },
    "lighting": {
      "enabled": true,
      "fps": 25,
      "devices": [
        {
          "id": "light-1",
          "name": "Room Lights",
          "channelMapping": {
            "red": 1,
            "green": 2,
            "blue": 3,
            "white": 4
          }
        }
      ]
    },
    "systemAudio": {
      "enabled": true,
      "monitorInterval": 1.0
    },
    "microphones": {
      "enabled": false
    },
    "speakers": {
      "enabled": false
    }
  }
}
```

---

## Troubleshooting

### "Initializing with 0 devices"

This means the `devices` array is empty. Add device configurations as shown above.

### Display not connecting

1. Verify IP address is correct and reachable: `ping 10.1.1.40`
2. Check port is correct (default 8001)
3. Ensure Novastar display is powered on and network-connected

### Camera not detected

1. Verify USB cable is securely connected
2. Check Device Manager shows Huddly camera under "Cameras"
3. Verify `deviceId` in config matches actual camera serial number
4. Ensure no other application is exclusively using the camera
5. Check logs for SDK initialization errors

### DMX lights not responding

1. Check FTDI USB adapter is connected
2. Verify DMX channel mapping matches fixture configuration
3. Ensure DMX terminator is installed at end of chain

### Module toggle causes "ObjectDisposedException"

This bug has been fixed. Update to the latest version.

---

## See Also

- [Configuration Reference](configuration.md) - Full configuration schema
- [Debug Mode](DEBUG_MODE.md) - How to access hardware controls
- [Troubleshooting](TROUBLESHOOTING.md) - Common issues
