# Hardware Integration Analysis - workstation-api to orh-winui-kiosk

**Last Updated:** January 13, 2026
**workstation-api Version:** 1.1.3
**Target Framework:** .NET 8.0 (Windows 10.0.19041+)

---

## Executive Summary

This document analyzes the **workstation-api** Python hardware control system (v1.1.3) and documents the complete port to the **orh-winui-kiosk** WinUI 3 C# application.

**Current Status:**
- ✅ **Phase 0:** Infrastructure complete
- ✅ **Phase 1:** Core services complete
- ✅ **Phase 2:** Display module complete
- ✅ **Phase 3:** Audio modules complete (SystemAudio, Microphone, Speaker)
- ✅ **Phase 4:** Camera & Lighting modules complete
- ✅ **Phase 5:** Debug mode, health visualization, logging complete
- ✅ **Phase 6:** Resource management & performance monitoring complete

**Architecture Decision:** Complete integration - all Python hardware modules ported to C# and embedded in single WinUI 3 application running on port 8081.

**Note:** The Chromium module was removed as WebView2 handles all browser functionality natively.

---

## Table of Contents

1. [workstation-api Architecture Overview](#workstation-api-architecture-overview)
2. [Module Comparison Matrix](#module-comparison-matrix)
3. [Implementation Status](#implementation-status)
4. [Critical Technical Patterns](#critical-technical-patterns)
5. [Module-by-Module Migration Guide](#module-by-module-migration-guide)
6. [API Endpoint Mapping](#api-endpoint-mapping)
7. [Configuration Schema](#configuration-schema)
8. [Deployment Considerations](#deployment-considerations)

---

## workstation-api Architecture Overview

### System Components

```
workstation-api (Python 3.12)
├── TFCC Framework (libs/tfcc/)
│   ├── ApplicationManager (DI container)
│   ├── Config (JSON with dot-notation)
│   ├── WebServer (aiohttp on port 8081)
│   └── Decorators (@api_route)
├── Hardware Modules (src/)
│   ├── CameraModule (Huddly via subprocess)
│   ├── DisplayModule (Novastar HTTP)
│   ├── LightingModule (DMX512 via FTDI)
│   ├── SystemModule (Windows audio via pycaw)
│   ├── MicrophoneModule (per-device audio)
│   ├── SpeakerModule (per-device audio)
│   └── ChromiumModule (browser control)
├── API Routes (src/api_routes.py)
│   └── ~40 RESTful endpoints
└── Build System
    ├── Nuitka compilation
    ├── Code signing
    └── Inno Setup installer
```

### Thread Model

**Python Implementation:**
- **Main thread:** ApplicationManager initialization, web server start
- **Web server thread:** aiohttp event loop (daemon)
- **Monitor threads:** One per module, each with own asyncio event loop (daemon)
- **Thread throttling:** Global lock + 0.2-0.6s delays to prevent "thread storm"

**C# Equivalent:**
- **Main thread:** WinUI 3 UI thread
- **Kestrel server:** ASP.NET Core async/await (background)
- **Monitor tasks:** `BackgroundService` per module with `CancellationToken`
- **Thread safety:** `SemaphoreSlim` for state locks

---

## Module Comparison Matrix

| Module | Python Implementation | C# Status | Port Complexity | Notes |
|--------|----------------------|-----------|-----------------|-------|
| **Display** | Novastar HTTP API | ✅ Complete | Low | Full feature parity |
| **System Audio** | pycaw (COM) | ✅ Complete | Medium | NAudio CoreAudioApi |
| **Microphone** | pycaw + device enum | ✅ Complete | Medium | Network microphone support |
| **Speaker** | pycaw + device enum | ✅ Complete | Medium | Network speaker support |
| **Camera** | Subprocess + REST | ✅ Complete | High | CameraController.exe subprocess |
| **Lighting** | PyFTDI + DMX512 | ✅ Complete | High | FTD2XX_NET integration |
| **Chromium** | Process + CDP | ❌ Removed | N/A | Replaced by WebView2 |

---

## Implementation Status

### ✅ All Modules Complete

#### 1. Display Module

**Files:**
- `OneRoomHealth.Hardware/Modules/Display/DisplayModule.cs`
- `OneRoomHealth.Hardware/Modules/Display/DisplayDeviceState.cs`
- `OneRoomHealth.Hardware/Api/Controllers/DisplayController.cs`

**Features Implemented:**
- ✅ Novastar HTTP API integration (port 8001)
- ✅ Brightness control (0-100 → 0-1 conversion)
- ✅ Enable/disable display (blackout mode)
- ✅ Multi-IP redundancy with per-IP health tracking
- ✅ Background monitoring (1s interval)
- ✅ Health states: Healthy (all IPs), Unhealthy (partial), Offline (none)

**API Endpoints:**
- `GET /api/v1/displays` - List all displays
- `GET /api/v1/displays/{id}` - Display status
- `PUT /api/v1/displays/{id}/brightness` - Set brightness
- `PUT /api/v1/displays/{id}/enable` - Enable/disable

**Gap Analysis:** ✅ Complete - matches workstation-api functionality

#### 2. Chromium Module

**Files:**
- `OneRoomHealth.Hardware/Modules/Chromium/ChromiumModule.cs`
- `OneRoomHealth.Hardware/Modules/Chromium/ChromeDevToolsProtocol.cs`
- `OneRoomHealth.Hardware/Modules/Chromium/ChromiumDeviceState.cs`
- `OneRoomHealth.Hardware/Api/Controllers/ChromiumController.cs`

**Features Implemented:**
- ✅ Process lifecycle management
- ✅ Auto-detect Chromium/Chrome executable
- ✅ Kiosk/fullscreen/normal display modes
- ✅ Chrome DevTools Protocol (CDP) integration
- ✅ URL navigation with fallback to restart
- ✅ Auto-start support
- ✅ Per-instance CDP port (9222 + instance ID)

**API Endpoints:**
- `GET /api/v1/chromium` - List all browser instances
- `GET /api/v1/chromium/{id}` - Browser status
- `POST /api/v1/chromium/{id}/open` - Start browser
- `POST /api/v1/chromium/{id}/close` - Stop browser
- `PUT /api/v1/chromium/{id}/url` - Navigate to URL

**Gap Analysis:** ⚠️ Minor gaps identified

**Missing Features:**
1. **Display targeting** - workstation-api supports `target_display` (primary/secondary/all)
   - Action: Add display targeting to ChromiumModule.BuildChromiumArguments()
   - Impact: Medium priority

2. **Window size control** - workstation-api supports `window_size` [width, height]
   - Action: Add `--window-size=W,H` argument
   - Impact: Low priority

3. **Audio control** - workstation-api has `mute_audio` setting
   - Action: Add `--mute-audio` flag when muted
   - Impact: Low priority

#### 3. System Audio Module ✅ Complete

**Python Implementation:**
```python
# pycaw (COM wrapper for Windows audio)
from comtypes import CoInitialize, CoUninitialize
from pycaw.pycaw import AudioUtilities, IAudioEndpointVolume

# Get default devices
speaker = AudioUtilities.GetSpeakers()
microphone = AudioUtilities.GetMicrophone()

# Volume control (0.0-1.0 scalar)
speaker_volume = speaker.Activate(IAudioEndpointVolume._iid_, ...)
speaker_volume.SetMasterVolumeLevelScalar(0.75, None)
```

**C# Port Strategy:**
```csharp
// Use NAudio.CoreAudioApi (already installed)
using NAudio.CoreAudioApi;

var enumerator = new MMDeviceEnumerator();
var speakers = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
var microphone = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

// Volume control (0.0-1.0 scalar)
speakers.AudioEndpointVolume.MasterVolumeLevelScalar = 0.75f;
microphone.AudioEndpointVolume.MasterVolumeLevelScalar = 0.5f;
```

**API Endpoints to Implement:**
- `GET /api/v1/system/volume` - Get system speaker volume
- `PUT /api/v1/system/volume` - Set system speaker volume
- `POST /api/v1/system/volume-up` - Increase volume (+5)
- `POST /api/v1/system/volume-down` - Decrease volume (-5)
- `GET /api/v1/system` - System status (volume, mute, mic state)

**Complexity:** Medium - NAudio is well-documented, COM interop handled by library

#### 4. Microphone Module ✅ Complete

**Python Implementation:**
```python
# Per-device microphone control
devices = AudioUtilities.GetAllDevices()
capture_devices = [d for d in devices if d.DataFlow == DataFlow.Capture]

# Device matching by ID or name
device = next(d for d in capture_devices if d.FriendlyName == "Jabra Speak 750")
```

**C# Port Strategy:**
```csharp
// Enumerate all capture devices
var enumerator = new MMDeviceEnumerator();
var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

// Device matching
var device = captureDevices.FirstOrDefault(d => d.FriendlyName.Contains("Jabra"));
device.AudioEndpointVolume.MasterVolumeLevelScalar = 0.8f;
device.AudioEndpointVolume.Mute = false;
```

**API Endpoints to Implement:**
- `GET /api/v1/microphones` - List all microphones
- `GET /api/v1/microphones/{id}` - Microphone status
- `GET /api/v1/microphones/{id}/volume` - Get volume
- `PUT /api/v1/microphones/{id}/volume` - Set volume
- `GET /api/v1/microphones/{id}/mute` - Get mute state
- `PUT /api/v1/microphones/{id}/mute` - Set mute state

**Complexity:** Medium - Similar to System Audio but with device enumeration

#### 5. Speaker Module ✅ Complete

**Implementation:** Nearly identical to Microphone Module but with `DataFlow.Render`

**API Endpoints:**
- `GET /api/v1/speakers` - List all speakers
- `GET /api/v1/speakers/{id}` - Speaker status
- `GET /api/v1/speakers/{id}/volume` - Get volume
- `PUT /api/v1/speakers/{id}/volume` - Set volume

**Note:** workstation-api currently has minimal speaker endpoints, mostly relies on system audio

#### 6. Camera Module ✅ Complete

**Python Implementation:**
```python
# Subprocess management for CameraController.exe
process = subprocess.Popen(
    ["hardware/huddly/CameraController.exe", "api", "5000"],
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    creationflags=subprocess.CREATE_NEW_PROCESS_GROUP
)

# Proxy all commands to subprocess REST API
async with aiohttp.ClientSession() as session:
    async with session.put(
        f"http://localhost:5000/cameras/{device_id}/ptz",
        json={"pan": 0, "tilt": 0, "zoom": 1}
    ) as response:
        return await response.json()
```

**C# Port Strategy:**
```csharp
// Process lifecycle management
var startInfo = new ProcessStartInfo
{
    FileName = "hardware\\huddly\\CameraController.exe",
    Arguments = "api 5000",
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true
};
_controllerProcess = Process.Start(startInfo);
_controllerProcess.EnableRaisingEvents = true;
_controllerProcess.Exited += OnControllerExited;

// HTTP client for subprocess communication
_httpClient.BaseAddress = new Uri("http://localhost:5000");
var response = await _httpClient.PutAsJsonAsync($"/cameras/{deviceId}/ptz", ptzCommand);
```

**Key Features:**
- Process lifecycle: Start on init, auto-restart on failure
- Health monitoring: Poll `/health` every 5s
- Restart strategy: Escalating retry with exponential backoff (max 5 attempts)
- Phantom process cleanup: Use `psutil` equivalent (System.Diagnostics)
- ID mapping: Simple IDs (0, 1) ↔ Device IDs (52446F0141-52508A0230)

**API Endpoints to Implement:**
- `GET /api/v1/cameras` - List all cameras
- `GET /api/v1/cameras/{id}` - Camera status
- `PUT /api/v1/cameras/{id}/enable` - Enable/disable camera
- `GET /api/v1/cameras/{id}/ptz` - Get PTZ position
- `PUT /api/v1/cameras/{id}/ptz` - Set PTZ (pan/tilt/zoom)
- `GET /api/v1/cameras/{id}/auto-tracking` - Get auto-tracking state
- `PUT /api/v1/cameras/{id}/auto-tracking` - Enable/disable auto-tracking
- `GET /api/v1/cameras/{id}/auto-framing` - Get auto-framing state
- `PUT /api/v1/cameras/{id}/auto-framing` - Enable/disable auto-framing

**Complexity:** High - subprocess management, health monitoring, auto-restart logic

**CameraController.exe Dependency:**
- **Location:** `hardware\huddly\CameraController.exe`
- **Port:** 5000 (configurable)
- **Technology:** .NET 8.0 executable (separate project)
- **Purpose:** Wraps Huddly SDK (native DLL) in REST API

#### 7. Lighting Module ✅ Complete

**Python Implementation:**
```python
# DMX512 via FTDI USB adapter
from pylibftdi import Device

device = Device(mode='b')  # Binary mode
device.baudrate = 250000   # DMX512 standard
device.ftdi_fn.ftdi_set_line_property(8, 2, 0)  # 8N2

# DMX universe (512 channels)
universe = bytearray([0] * 512)
universe[17] = red_value    # Channel 17: Red
universe[18] = green_value  # Channel 18: Green
universe[19] = blue_value   # Channel 19: Blue
universe[20] = white_value  # Channel 20: White

# Send frame @ 25 FPS
device.write(universe)
```

**C# Port Strategy:**
```csharp
// Use FTD2XX_NET (already installed)
using FTD2XX_NET;

// Initialize FTDI device
var ftdi = new FTDI();
ftdi.OpenBySerialNumber(deviceSerial);
ftdi.SetBaudRate(250000);
ftdi.SetDataCharacteristics(FTDI.FT_DATA_BITS.FT_BITS_8, FTDI.FT_STOP_BITS.FT_STOP_BITS_2, FTDI.FT_PARITY.FT_PARITY_NONE);
ftdi.SetFlowControl(FTDI.FT_FLOW_CONTROL.FT_FLOW_NONE, 0, 0);

// DMX universe
byte[] universe = new byte[512];
universe[16] = redValue;   // DMX channels are 1-indexed, array is 0-indexed
universe[17] = greenValue;
universe[18] = blueValue;
universe[19] = whiteValue;

// Background thread: Send frames @ configured FPS
uint bytesWritten;
ftdi.Write(universe, universe.Length, ref bytesWritten);
```

**Key Features:**
- Multi-endpoint support: Single device can have multiple DMX endpoints
- Channel mapping: Configurable R/G/B/W channel assignments per endpoint
- Brightness scaling: Store full RGBW, apply brightness as scale factor
- Config persistence: Auto-save color/brightness changes to config.json
- Background thread: Continuous DMX frame transmission @ 25-30 FPS

**API Endpoints to Implement:**
- `GET /api/v1/lighting` - List all lights
- `GET /api/v1/lighting/{id}` - Light status
- `PUT /api/v1/lighting/{id}/enable` - Enable/disable light
- `GET /api/v1/lighting/{id}/color` - Get RGBW color
- `PUT /api/v1/lighting/{id}/color` - Set RGBW color
- `GET /api/v1/lighting/{id}/brightness` - Get brightness
- `PUT /api/v1/lighting/{id}/brightness` - Set brightness

**Complexity:** High - DMX512 protocol, FTDI low-level control, background transmission

**Hardware Dependency:**
- **Device:** FTDI USB-to-DMX adapter (FT232R chip)
- **Driver:** FTDI D2XX driver (installed with FTD2XX_NET)
- **Protocol:** DMX512-A (250kbps, 8N2, 512 channels)

---

## Critical Technical Patterns

### 1. Background Monitoring Pattern

**Python (asyncio):**
```python
def _run_monitor_thread(self):
    self._monitoring_loop = asyncio.new_event_loop()
    asyncio.set_event_loop(self._monitoring_loop)
    try:
        self._monitoring_loop.run_until_complete(self._monitor_loop())
    finally:
        self._monitoring_loop.close()

async def _monitor_loop(self):
    while not self._shutdown_event.is_set():
        await self._check_devices()
        await asyncio.sleep(self.monitor_interval)
```

**C# (BackgroundService):**
```csharp
protected override async Task MonitorDevicesAsync(CancellationToken cancellationToken)
{
    var interval = TimeSpan.FromSeconds(_config.MonitorInterval);

    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            await CheckDevicesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in monitoring loop");
        }

        await Task.Delay(interval, cancellationToken);
    }
}
```

### 2. Error Response Pattern

**Consistent error structure across all endpoints:**

```csharp
// Success
return Results.Ok(ApiResponse<T>.Ok(data));

// Not Found
return Results.Json(
    ApiErrorResponse.FromMessage("NOT_FOUND", $"Device '{id}' not found"),
    statusCode: 404
);

// Bad Request
return Results.Json(
    ApiErrorResponse.FromMessage("BAD_REQUEST", "Invalid brightness value"),
    statusCode: 400
);

// Service Unavailable
return Results.Json(
    ApiErrorResponse.FromMessage("SERVICE_UNAVAILABLE", "Module not initialized"),
    statusCode: 503
);

// Internal Error
return Results.Json(
    ApiErrorResponse.FromException(ex),
    statusCode: 500
);
```

### 3. Device State Management

**Thread-safe state access:**

```csharp
private readonly Dictionary<string, DeviceState> _deviceStates = new();
private readonly SemaphoreSlim _stateLock = new(1, 1);

public async Task UpdateDeviceAsync(string deviceId, Action<DeviceState> updateAction)
{
    await _stateLock.WaitAsync();
    try
    {
        if (_deviceStates.TryGetValue(deviceId, out var state))
        {
            updateAction(state);
            state.LastSeen = DateTime.UtcNow;
        }
    }
    finally
    {
        _stateLock.Release();
    }
}
```

### 4. Configuration Persistence

**Auto-save on changes:**

```csharp
private void UpdateDeviceConfig(string deviceId, string key, object value)
{
    // Update runtime config
    var device = _config.Devices.FirstOrDefault(d => d.Id == deviceId);
    if (device != null)
    {
        // Use reflection or specific property setters
        SetProperty(device, key, value);

        // Persist to disk
        _configurationManager.Save();
    }
}
```

---

## API Endpoint Mapping

### Complete Endpoint Comparison

| Endpoint | Python | C# Status | Notes |
|----------|--------|-----------|-------|
| **System** |
| `GET /api/v1/status` | ✅ | ✅ | Complete |
| `GET /api/v1/devices` | ✅ | ✅ | Complete |
| `GET /api/v1/config` | ✅ | 🟡 | Optional - config viewer |
| `PUT /api/v1/config` | ✅ | 🟡 | Optional - config updates |
| **Displays** |
| `GET /api/v1/displays` | ✅ | ✅ | Complete |
| `GET /api/v1/displays/{id}` | ✅ | ✅ | Complete |
| `PUT /api/v1/displays/{id}/brightness` | ✅ | ✅ | Complete |
| `PUT /api/v1/displays/{id}/enable` | ✅ | ✅ | Complete |
| `GET /api/v1/displays/{id}/enable` | ✅ | ✅ | Complete |
| `GET /api/v1/displays/{id}/brightness` | ✅ | ✅ | Complete |
| **Chromium** |
| `GET /api/v1/chromium` | ✅ | ✅ | Complete |
| `GET /api/v1/chromium/{id}` | ✅ | ✅ | Complete |
| `POST /api/v1/chromium/{id}/open` | ✅ | ✅ | Complete |
| `POST /api/v1/chromium/{id}/close` | ✅ | ✅ | Complete |
| `PUT /api/v1/chromium/{id}/url` | ✅ | ✅ | Complete |
| **System Audio** |
| `GET /api/v1/system/volume` | ✅ | ✅ | Complete |
| `PUT /api/v1/system/volume` | ✅ | ✅ | Complete |
| `POST /api/v1/system/volume-up` | ✅ | ✅ | Complete |
| `POST /api/v1/system/volume-down` | ✅ | ✅ | Complete |
| **Microphones** |
| `GET /api/v1/microphones` | ✅ | ✅ | Complete |
| `GET /api/v1/microphones/{id}` | ✅ | ✅ | Complete |
| `GET /api/v1/microphones/{id}/volume` | ✅ | ✅ | Complete |
| `PUT /api/v1/microphones/{id}/volume` | ✅ | ✅ | Complete |
| `GET /api/v1/microphones/{id}/mute` | ✅ | ✅ | Complete |
| `PUT /api/v1/microphones/{id}/mute` | ✅ | ✅ | Complete |
| **Speakers** |
| `GET /api/v1/speakers/volume` | ✅ | ✅ | Complete |
| `PUT /api/v1/speakers/volume` | ✅ | ✅ | Complete |
| **Cameras** |
| `GET /api/v1/cameras` | ✅ | ✅ | Complete |
| `GET /api/v1/cameras/{id}` | ✅ | ✅ | Complete |
| `PUT /api/v1/cameras/{id}/enable` | ✅ | ✅ | Complete |
| `GET /api/v1/cameras/{id}/ptz` | ✅ | ✅ | Complete |
| `PUT /api/v1/cameras/{id}/ptz` | ✅ | ✅ | Complete |
| `GET /api/v1/cameras/{id}/auto-tracking` | ✅ | ✅ | Complete |
| `PUT /api/v1/cameras/{id}/auto-tracking` | ✅ | ✅ | Complete |
| `GET /api/v1/cameras/{id}/auto-framing` | ✅ | ✅ | Complete |
| `PUT /api/v1/cameras/{id}/auto-framing` | ✅ | ✅ | Complete |
| **Lighting** |
| `GET /api/v1/lighting` | ✅ | ✅ | Complete |
| `GET /api/v1/lighting/{id}` | ✅ | ✅ | Complete |
| `PUT /api/v1/lighting/{id}/enable` | ✅ | ✅ | Complete |
| `GET /api/v1/lighting/{id}/color` | ✅ | ✅ | Complete |
| `PUT /api/v1/lighting/{id}/color` | ✅ | ✅ | Complete |
| `GET /api/v1/lighting/{id}/brightness` | ✅ | ✅ | Complete |
| `PUT /api/v1/lighting/{id}/brightness` | ✅ | ✅ | Complete |

**Total Endpoints:** 45+ (Chromium removed, replaced by WebView2)
**Completed:** 100%

---

## Configuration Schema

### Python Configuration (config.json)

```json
{
  "app": {
    "name": "Workstation API Agent",
    "version": "1.1.3",
    "debug_mode": false,
    "launch_browser": false,
    "auto_save_config": true,
    "logging": {
      "level": "info",
      "file_enabled": true,
      "console_enabled": true
    }
  },
  "web": {
    "host": "localhost",
    "port": 8081,
    "enable_cors_policy": true,
    "media_base_dir": "C:\\Users\\CareWall\\Downloads"
  },
  "modules": {
    "cameras": {
      "enabled": true,
      "monitor_interval": 5.0,
      "controller_api_port": 5000,
      "controller_exe_path": "hardware/huddly/CameraController.exe",
      "devices": [...]
    },
    "displays": {
      "enabled": true,
      "monitor_interval": 1.0,
      "devices": [...]
    },
    "lighting": {
      "enabled": true,
      "monitor_interval": 5.0,
      "dmx": {
        "backend": "d2xx",
        "url": "ftdi:///1",
        "fps": 25
      },
      "devices": [...]
    },
    "chromium": {
      "enabled": true,
      "devices": [...]
    }
  }
}
```

### C# Configuration (Strongly Typed)

**Already Implemented:**
```csharp
public class KioskConfiguration
{
    public KioskSettings Kiosk { get; set; }
    public HttpApiSettings HttpApi { get; set; }
    public HardwareConfiguration Hardware { get; set; }
}

public class HardwareConfiguration
{
    public CameraConfiguration? Cameras { get; set; }
    public DisplayConfiguration? Displays { get; set; }
    public LightingConfiguration? Lighting { get; set; }
    public SystemAudioConfiguration? SystemAudio { get; set; }
    public MicrophoneConfiguration? Microphones { get; set; }
    public SpeakerConfiguration? Speakers { get; set; }
    public ChromiumConfiguration? Chromium { get; set; }
}
```

**Gap Analysis:**
- ✅ All module configurations defined
- ⚠️ Missing `media_base_dir` for media playback endpoint
- Action: Add to `HttpApiSettings`

---

## Deployment Considerations

### Python Deployment

**Files:**
- `WorkstationAPIAgent.exe` (Nuitka compiled, ~50 MB)
- `hardware\huddly\CameraController.exe` (.NET 8.0 executable)
- `data\config\config.json`
- Certificate: `agent_root.cer`

**Installation:**
- Inno Setup installer with prerequisites
- Installs to `C:\Program Files\OneRoomHealth\WorkstationAPI\`
- Data directory: `C:\ORH\WorkstationAPIAgent\data\`
- Auto-install Python runtime, VC++ redist, .NET 8.0

### C# Deployment (WinUI 3)

**Files:**
- `OneRoomHealthKioskApp.exe` (WinUI 3 application, ~20 MB)
- `OneRoomHealth.Hardware.dll` (embedded)
- `hardware\huddly\CameraController.exe` (.NET 8.0 - same as Python version)
- `config.json` (in ProgramData)

**Installation:**
- MSIX package (Windows 11 App Installer)
- Single executable with embedded DLLs
- Data directory: `%ProgramData%\OneRoomHealth\Kiosk\`
- Runtime: .NET 8.0 Desktop Runtime (Windows App SDK)

**Advantages over Python:**
- Smaller deployment size (~50% reduction)
- Faster startup time (no Python interpreter)
- Better Windows integration (native UI, system tray)
- Single app instead of separate workstation-api + kiosk
- Simplified certificate management (single code signing)

---

## Completed Implementation

### All Phases Complete

All hardware modules have been successfully ported from Python to C#/.NET 8:

1. **Display Module** - Novastar LED control with multi-IP redundancy
2. **Camera Module** - Huddly PTZ via CameraController.exe subprocess
3. **Lighting Module** - DMX512 via FTD2XX_NET
4. **System Audio Module** - Windows volume via NAudio CoreAudioApi
5. **Microphone Module** - Per-device capture with network mic support
6. **Speaker Module** - Per-device playback with network speaker support

### Additional Features Implemented

- **Debug Mode** - Hotkey-activated developer panel (Ctrl+Shift+F12)
- **Health Visualization** - Real-time module health monitoring
- **Unified Logging** - Integrated Serilog with per-module filtering
- **Performance Monitoring** - GC metrics, memory tracking, CPU usage
- **Resource Management** - Proper disposal of SemaphoreSlim, HttpClient, Process

### Architecture

The Chromium module was removed as WebView2 handles all browser functionality natively within the WinUI 3 application.

---

## Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| FTD2XX_NET compatibility issues | High | Test with real FTDI hardware early |
| Camera subprocess instability | High | Implement robust restart logic |
| DMX timing requirements | Medium | Use dedicated background thread |
| NAudio COM threading | Medium | Proper COM apartment initialization |
| Config migration from Python | Low | Create migration tool |

---

## Success Criteria

1. **Functional Parity:** All 46 endpoints from workstation-api working
2. **Performance:** < 50ms response time for non-hardware operations
3. **Stability:** 99.9% uptime over 30-day test period
4. **Hardware Compatibility:** Works with all existing devices
5. **Deployment:** Single MSIX installer < 100 MB

---

## References

- **workstation-api Source:** `C:\Users\JeremySteinhafel\Code\workstation-api`
- **Nuitka Compilation:** `workstation-api/build_nuitka.bat`
- **API Routes:** `workstation-api/src/api_routes.py`
- **TFCC Framework:** `workstation-api/libs/tfcc/`
- **NAudio Documentation:** https://github.com/naudio/NAudio
- **FTD2XX_NET:** https://www.ftdichip.com/Support/Documents/ProgramGuides.htm
- **DMX512 Protocol:** ANSI E1.11 (250kbps, 8N2)

---

**Document Status:** Living document - updated as implementation progresses
**Next Review:** After Phase 3 completion
