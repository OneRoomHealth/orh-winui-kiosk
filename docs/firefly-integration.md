# Firefly Otoscope Camera Integration

## Overview

Firefly UVC otoscope cameras (DE300 / DE400 / DE500 / GT700 / GT800) are integrated into
the `techtablet` machine type via a two-component architecture that works around the 32-bit
constraint imposed by the vendor's `SnapDll.dll`.

## Architecture

```
KioskApp.exe (64-bit, x64)
├── FireflyModule (IHardwareModule)
│   ├── Starts FireflyCapture.Bridge.exe as child process
│   ├── Enumerates Firefly devices via Windows.Devices.Enumeration (VID 21CD)
│   ├── Consumes SSE from bridge → button press → MediaCapture trigger
│   ├── Windows.Media.Capture.MediaCapture → JPEG bytes (WinRT, 64-bit safe)
│   └── IImageDeliveryStrategy → downstream HTTP POST
│
└── HardwareApiServer (port 8081)
    └── FireflyController
        ├── GET  /api/v1/firefly            → list devices
        ├── GET  /api/v1/firefly/{id}       → device status
        └── POST /api/v1/firefly/{id}/capture → trigger + return JPEG

FireflyCapture.Bridge.exe (32-bit, x86) — separate process
├── SnapDll.dll (vendor, 32-bit)
│   ├── IsButtonpress()   → polling every N ms
│   └── ReleaseButton()   → called immediately on detection
├── FireflyPollingService (BackgroundService)
│   └── Broadcasts ButtonPressEvent via ButtonEventBroadcaster
└── HTTP API (localhost:5200)
    ├── GET  /health        → liveness
    ├── GET  /button-state  → current state
    ├── POST /release       → manual release (testing)
    └── GET  /events        → SSE stream of button presses
```

## Why a Separate Bridge Process?

`SnapDll.dll` is a **32-bit (x86) PE binary**. Windows does not allow a 64-bit process to
load a 32-bit DLL via `LoadLibrary` / `NativeLibrary.Load`. Since `KioskApp.exe` targets x64,
we run `FireflyCapture.Bridge.exe` as a child process compiled for x86, which can safely host
the 32-bit DLL. The bridge exposes a local HTTP API that the 64-bit `FireflyModule` calls.

This pattern mirrors the existing Huddly camera architecture and ensures clean process isolation:
if the bridge crashes, `FireflyModule` detects it and restarts it automatically.

## USB Device Identification

Firefly cameras are identified by USB Vendor ID `0x21CD`. Known PID → model mapping:

| USB PID | Model |
|---------|-------|
| `0x603B` | DE300 |
| `0x603C` | DE400 |
| `0x703A` | GT700 |
| `0x703B` | GT800 |
| `0x703A` | DE500 (shares PID with GT700; differentiated by friendly name) |

These values were confirmed by extracting the string table from `SnapDll.dll`.

## Configuration (config.json)

Add the following block under `hardware` in the kiosk configuration file
(`%ProgramData%\OneRoomHealth\Kiosk\config.json`):

```json
{
  "hardware": {
    "firefly": {
      "enabled": true,
      "monitorInterval": 10.0,
      "bridgeExePath": "hardware\\firefly\\FireflyCapture.Bridge.exe",
      "bridgePort": 5200,
      "snapDllPath": "SnapDll.dll",
      "pollingIntervalMs": 10,
      "startupGracePeriodSeconds": 10,
      "maxRestartAttempts": 5,
      "downstream": {
        "enabled": false,
        "url": "https://your-service/api/images",
        "method": "multipart",
        "authHeader": "Bearer __TOKEN__",
        "multipartFieldName": "image",
        "timeoutSeconds": 30
      }
    }
  }
}
```

### Downstream Delivery Methods

| `method` value | Content-Type | Payload |
|---------------|--------------|---------|
| `multipart`   | `multipart/form-data` | File field named by `multipartFieldName` |
| `base64`      | `application/json` | `{ "image": "<base64>", "contentType": "image/jpeg" }` |
| `raw`         | `image/jpeg` | Raw JPEG bytes |

## File Locations

| File | Location |
|------|----------|
| `SnapDll.dll` | Repository root → auto-copied to bridge output |
| `FireflyCapture.Bridge.exe` | `FireflyCapture.Bridge/bin/…/win-x86/` |
| `FireflyModule.cs` | `OneRoomHealth.Hardware/Modules/Firefly/` |
| `FireflyController.cs` | `OneRoomHealth.Hardware/Api/Controllers/` |
| Image delivery strategies | `OneRoomHealth.Hardware/Services/ImageDelivery/` |

## Running Locally (Development)

1. Build the bridge for x86:
   ```powershell
   dotnet build FireflyCapture.Bridge/FireflyCapture.Bridge.csproj -r win-x86
   ```
2. Start the bridge manually to verify SnapDll loads:
   ```powershell
   .\FireflyCapture.Bridge\bin\Debug\net8.0-windows\win-x86\FireflyCapture.Bridge.exe
   ```
3. Test endpoints:
   ```powershell
   Invoke-RestMethod http://localhost:5200/health
   Invoke-RestMethod http://localhost:5200/button-state
   ```
4. Start the kiosk in Hardware API mode via the debug panel toggle.
   Enable `hardware.firefly.enabled: true` in config first.
5. Test capture:
   ```powershell
   Invoke-RestMethod -Method POST http://localhost:8081/api/v1/firefly/firefly-0/capture `
     -OutFile capture.jpg
   ```

## Monitoring & Health

- Bridge health is checked every `monitorInterval` seconds via `GET /health`.
- If the bridge exits unexpectedly, `FireflyModule` restarts it with exponential back-off
  (5s × attempt, max 30s). After `maxRestartAttempts` failures the module is marked Unhealthy.
- Device enumeration runs on the same `monitorInterval` to detect plug/unplug events.
- Button-press detection relies on the SSE consumer in `FireflyModule`. If the SSE connection
  drops (e.g., bridge restart), the consumer reconnects automatically after 2 seconds.

## Future Work

- **ACS streaming**: Route the live Firefly video stream through Azure Communication Services,
  mirroring the Huddly camera approach used in `carewall` deployments.
- **Capture preview**: Add a native `CameraPreviewPage` with `CaptureElement` to the
  `techtablet` machine type for inline otoscope video.
- **Multi-device**: The current implementation routes hardware button presses to the first
  connected device; extend to allow per-device button mapping in config.
- **DE500 disambiguation**: DE500 shares PID `0x703A` with GT700; use USB device friendly
  name to distinguish if both models are in the field.
