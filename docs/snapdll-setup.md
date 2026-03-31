# SnapDll.dll — Setup and Placement Guide

## What is SnapDll.dll?

`SnapDll.dll` is a native 32-bit (x86) Windows DLL provided by the Firefly Medical camera vendor.
It exposes hardware button detection for Firefly UVC otoscope cameras via two exports:

| Ordinal | Name | Signature | Description |
|---------|------|-----------|-------------|
| 1 | `IsButtonpress` | `bool __stdcall ()` | Returns `true` when the snap button is pressed |
| 2 | `ReleaseButton` | `void __stdcall ()` | Acknowledges the press so hardware can reset |

> **Important:** The DLL is 32-bit (PE32, machine `0x014C`). It cannot be loaded by the 64-bit
> kiosk process (`KioskApp.exe`). This is why `FireflyCapture.Bridge.exe` exists as a separate
> 32-bit host process.

## Placement

The `SnapDll.dll` file must live **in the same directory as `FireflyCapture.Bridge.exe`**
at runtime. The bridge project's `.csproj` already includes a build rule to copy it:

```xml
<Content Include="..\SnapDll.dll" Link="SnapDll.dll">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

This means if you place `SnapDll.dll` in the **repository root** (next to the `.sln`) it will
automatically be copied to the bridge output directory on build.

```
orh-winui-kiosk/
├── SnapDll.dll                        ← place it here (repository root)
├── FireflyCapture.Bridge/
│   └── bin/Release/net8.0-windows/
│       win-x86/
│       ├── FireflyCapture.Bridge.exe  ← bridge exe
│       └── SnapDll.dll               ← auto-copied on build
```

### MSIX / Production Deployment

For production, `FireflyCapture.Bridge.exe` and `SnapDll.dll` must be deployed to the tablet
alongside the kiosk MSIX package. Recommended layout:

```
C:\Program Files\OneRoomHealth\Kiosk\hardware\firefly\
├── FireflyCapture.Bridge.exe
├── SnapDll.dll
└── appsettings.json        (optional — overrides defaults)
```

Update `config.json` to match:

```json
{
  "hardware": {
    "firefly": {
      "enabled": true,
      "bridgeExePath": "hardware\\firefly\\FireflyCapture.Bridge.exe",
      "bridgePort": 5200,
      "snapDllPath": "SnapDll.dll"
    }
  }
}
```

> The kiosk app resolves `bridgeExePath` relative to its own executable directory if the path
> is not absolute.

## SnapDll Internals (for reference)

The following was extracted from the binary during integration work:

- The DLL internally references `%TEMP%\SnapShut.txt` for shutdown signalling — no action
  needed on the managed side.
- USB Vendor ID `21CD` (Firefly Medical) and PIDs `603B`, `603C`, `703A`, `703B` are embedded
  in the DLL string table and are used by the kiosk for device enumeration independently.

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `SnapDll not found at ...` in bridge logs | DLL not in bridge exe directory | Copy `SnapDll.dll` next to `FireflyCapture.Bridge.exe` |
| `BadImageFormatException` | Trying to load DLL in 64-bit process | Use the bridge; never P/Invoke from `KioskApp.exe` |
| Button presses not detected | Bridge process not running | Check `hardware.firefly.bridgeExePath` in config; verify process is alive |
| `EntryPointNotFoundException` | Wrong DLL version | Verify exports `IsButtonpress` (ordinal 1) and `ReleaseButton` (ordinal 2) exist |
