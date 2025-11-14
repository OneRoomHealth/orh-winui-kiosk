# Video Mode Quick Start Guide

## 🎬 Flic Button Video Player is Now Integrated!

The web-based video player has been successfully integrated into your OneRoom Health Kiosk application. Here's how to get it running:

---

## 1. Configure Video Mode (2 minutes)

### Option A: Use Sample Configuration
Copy the provided sample configuration:

```powershell
# Create config directory if it doesn't exist
New-Item -Path "$env:ProgramData\OneRoomHealth\Kiosk" -ItemType Directory -Force

# Copy sample config
Copy-Item "sample-video-config.json" "$env:ProgramData\OneRoomHealth\Kiosk\config.json"
```

### Option B: Manually Edit Configuration
Edit `%ProgramData%\OneRoomHealth\Kiosk\config.json`:

```json
{
  "kiosk": {
    "videoMode": {
      "enabled": true,
      "carescapeVideoPath": "C:\\Users\\CareWall\\Desktop\\Demo\\chehaliscare.mp4",
      "demoVideoPath": "C:\\Users\\CareWall\\Desktop\\Demo\\demo_video.mp4",
      "carescapeVolume": 50,
      "demoVolume": 75
    }
  }
}
```

**Important:** Update the video paths to match your actual file locations!

---

## 2. Build and Run (5 minutes)

### From Visual Studio:
1. Open `KioskApp/KioskApp.csproj` in Visual Studio 2022
2. Press `F5` to build and run

### From Command Line:
```powershell
# Build the app
msbuild KioskApp/KioskApp.csproj /p:Configuration=Debug /p:Platform=x64

# Run the app
.\KioskApp\bin\x64\Debug\win-x64\OneRoomHealthKioskApp.exe
```

---

## 3. Test Video Control

### Keyboard Controls:
- **Ctrl+Alt+D** - Toggle between carescape and demo videos (Flic button)
- **Ctrl+Alt+E** - Stop video playback
- **Ctrl+Alt+R** - Restart carescape video

### What to Expect:
1. App starts and loads carescape video (loops continuously)
2. Press `Ctrl+Alt+D` to switch to demo video
3. Demo video plays once, then automatically returns to carescape
4. Volume adjusts between videos (50% for carescape, 75% for demo)

---

## 4. Troubleshooting

### Videos Not Playing?
1. **Check video paths exist:**
   ```powershell
   Test-Path "C:\Users\CareWall\Desktop\Demo\chehaliscare.mp4"
   Test-Path "C:\Users\CareWall\Desktop\Demo\demo_video.mp4"
   ```

2. **Check logs:**
   - Open Event Viewer → Application Log
   - Look for OneRoomHealthKiosk entries

3. **Browser Console:**
   - Press `Ctrl+Shift+F12` to enter debug mode
   - Press `F12` to open developer tools
   - Check Console tab for errors

### Video Format Issues?
HTML5 video supports:
- MP4 (H.264/H.265)
- WebM (VP8/VP9)
- Ogg (Theora)

Convert if needed:
```powershell
# Using ffmpeg (if installed)
ffmpeg -i input.avi -c:v libx264 -c:a aac output.mp4
```

### Hotkeys Not Working?
- Make sure the kiosk window has focus
- Try clicking on the video area first
- Check if video mode is enabled in config

---

## 5. Features Implemented

✅ **Web-based video player** - No external dependencies  
✅ **Flic button support** - Ctrl+Alt+D toggles videos  
✅ **Auto-return** - Demo returns to carescape when done  
✅ **Volume control** - Different volumes per video  
✅ **Status display** - Shows current playing video  
✅ **Error handling** - Graceful fallback if videos missing  
✅ **Debug mode compatible** - Works with Ctrl+Shift+F12  

---

## 6. Configuration Options

```json
{
  "videoMode": {
    "enabled": true,                    // Enable/disable video mode
    "carescapeVideoPath": "C:\\...",   // Path to looping video
    "demoVideoPath": "C:\\...",        // Path to demo video
    "carescapeVolume": 50,             // Volume 0-100
    "demoVolume": 75,                  // Volume 0-100
    "targetMonitor": 1,                // Which monitor (1-based)
    "flicButtonEnabled": true          // Enable Flic button control
  }
}
```

---

## 7. Next Steps

1. **Test with your videos** - Update paths in config.json
2. **Configure Flic button** - Map to send Ctrl+Alt+D
3. **Deploy to kiosk** - Copy config to all devices
4. **Monitor logs** - Check for any playback issues

---

## 8. Advanced: Using Network Videos

You can also use network URLs:
```json
{
  "carescapeVideoPath": "https://example.com/videos/carescape.mp4",
  "demoVideoPath": "https://example.com/videos/demo.mp4"
}
```

---

## Quick Test Commands

```powershell
# 1. Set up config
New-Item -Path "$env:ProgramData\OneRoomHealth\Kiosk" -ItemType Directory -Force
Copy-Item "sample-video-config.json" "$env:ProgramData\OneRoomHealth\Kiosk\config.json"

# 2. Build
msbuild KioskApp/KioskApp.csproj /p:Configuration=Debug /p:Platform=x64

# 3. Run
.\KioskApp\bin\x64\Debug\win-x64\OneRoomHealthKioskApp.exe

# 4. Test hotkeys
# Press Ctrl+Alt+D to toggle videos
```

---

**🎉 Your Flic video player is ready to use!**

For detailed implementation info, see:
- `FLIC_INTEGRATION_PLAN.md` - Complete technical details
- `FLIC_INTEGRATION_QUICKSTART.md` - Alternative implementation options
- `sample-video-config.json` - Full configuration example
