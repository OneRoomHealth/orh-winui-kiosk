
import subprocess
import time
import keyboard
import json
import os
import socket
from threading import Thread, Event
from ctypes import cast, POINTER
from comtypes import CLSCTX_ALL
from pycaw.pycaw import AudioUtilities, IAudioEndpointVolume

class FlicMPVController:
    def __init__(self, carescape_video_path, demo_video_path):
        self.carescape_video = carescape_video_path
        self.demo_video = demo_video_path
        self.mpv_process = None
        self.is_demo_playing = False
        self.stop_monitoring = Event()
        self.mpv_exe = None
        
        self.carescape_volume = 50
        self.demo_volume = 75
        
        self.volume_control = None
        try:
            devices = AudioUtilities.GetSpeakers()
            interface = devices.Activate(IAudioEndpointVolume._iid_, CLSCTX_ALL, None)
            self.volume_control = cast(interface, POINTER(IAudioEndpointVolume))
            print("✅ Windows audio control initialized")
        except Exception as e:
            print(f"⚠️  Warning: Could not initialize audio control: {e}")
            print("   Volume control will be disabled")
        
    def find_mpv(self):
        """Find MPV executable in common locations"""
        mpv_paths = [
            "mpv",
            r"C:\Users\CareWall\Downloads\mpv-x86_64-v3-20251012-git-ad59ff1",
            r"C:\Users\CareWall\Downloads\mpv-x86_64-v3-20251012-git-ad59ff1\mpv-x86_64-v3-20251012-git-ad59ff1\mpv.exe",
            r"C:\Users\CareWall\Downloads\mpv\mpv.exe",
            r"C:\Users\CareWall\Downloads\mpv-x86_64-v3-20251012-git-ad59ff1"
            r"C:\mpv\mpv.exe",
            r"C:\mpv\bin\mpv.exe",
            r"C:\Program Files\mpv\mpv.exe", 
            r"C:\Program Files (x86)\mpv\mpv.exe",
            r"C:\Users\CareWall\Desktop\Demo\mpv\mpv.exe",
        ]

        for path in mpv_paths:
            print(f"🔍 Checking: {path}")
            if path == "mpv":
                try:
                    result = subprocess.run([path, "--version"], 
                                          capture_output=True, 
                                          timeout=5, 
                                          text=True)
                    if result.returncode == 0:
                        self.mpv_exe = path
                        print(f"✅ Found MPV in PATH: {path}")
                        return True
                except Exception as e:
                    print(f"❌ Not in PATH: {path} - {e}")
            elif os.path.exists(path):
                try:
                    result = subprocess.run([path, "--version"], 
                                          capture_output=True, 
                                          timeout=5,
                                          text=True)
                    if result.returncode == 0:
                        self.mpv_exe = path
                        print(f"✅ Found working MPV at: {path}")
                        return True
                except Exception as e:
                    print(f"❌ Found but not working: {path} - {e}")
            else:
                print(f"❌ Not found: {path}")
        
        print("❌ MPV not found in any common locations")
        print("\n💡 Please:")
        print("   1. Download MPV from: https://sourceforge.net/projects/mpv-player-windows/files/")
        print("   2. Extract to C:\\mpv\\ so you have C:\\mpv\\mpv.exe")
        print("   3. Or add MPV to your system PATH")
        return False
    
    def set_volume(self, volume_percent):
        """Set Windows system volume to specified percentage (0-100)"""
        if not self.volume_control:
            print(f"⚠️  Cannot set volume - audio control not available")
            return False
            
        try:
            volume_scalar = volume_percent / 100.0
            self.volume_control.SetMasterVolumeLevelScalar(volume_scalar, None)
            print(f"🔊 Volume set to {volume_percent}%")
            return True
        except Exception as e:
            print(f"⚠️  Failed to set volume: {e}")
            return False
        
    def start_mpv(self, video_path, loop=True, volume=None):
        """Start MPV with specified video and optional volume"""
        if not self.mpv_exe:
            if not self.find_mpv():
                return False
        
        target_volume = None
        if volume is not None:
            target_volume = volume
        elif video_path == self.carescape_video:
            target_volume = self.carescape_volume
        elif video_path == self.demo_video:
            target_volume = self.demo_volume
            
        cmd = [
            self.mpv_exe,
            "--fullscreen",
            "--no-osc",
            "--no-border",
            "--ontop",
            "--screen=0",
            "--fs-screen=0",
            "--quiet",
            video_path
        ]
        
        if loop:
            cmd.insert(-1, "--loop-file=inf")
            
        try:
            print(f"🚀 Starting MPV on second monitor with: {os.path.basename(video_path)}")
            new_process = subprocess.Popen(cmd, 
                                         stdout=subprocess.DEVNULL, 
                                         stderr=subprocess.PIPE,
                                         text=True)
            
            time.sleep(0.3)
            
            if self.mpv_process and self.mpv_process.poll() is None:
                self.mpv_process.terminate()
            
            self.mpv_process = new_process
            time.sleep(0.2)
            
            if self.mpv_process.poll() is not None:
                _, stderr = self.mpv_process.communicate()
                print(f"❌ MPV failed to start. Error: {stderr}")
                return False
            
            if target_volume is not None:
                if video_path == self.demo_video and target_volume > self.carescape_volume:
                    print("⏱️  Delaying volume increase for smooth transition...")
                    time.sleep(0.5)
                self.set_volume(target_volume)
                
            print(f"✅ MPV started successfully on second monitor")
            return True
            
        except Exception as e:
            print(f"❌ Error starting MPV: {e}")
            return False
    
    def flic_button_pressed(self):
        """Handle Flic button press (Ctrl+Alt+D) - toggle between demo and carescape video"""
        
        if self.is_demo_playing:
            print("🔴 Flic button pressed! Returning to carescape video...")
            print(f"🎬 Switching back to: {os.path.basename(self.carescape_video)}")
            
            self.stop_monitoring.set()
            
            success = self.start_mpv(self.carescape_video, loop=True)
            if success:
                print("✅ Successfully returned to carescape video")
                self.is_demo_playing = False
                self.stop_monitoring.clear()
            else:
                print("❌ Failed to return to carescape video")
                
        else:
            print("🔴 Flic button pressed! Playing demo video...")
            print(f"🎬 Switching to: {os.path.basename(self.demo_video)}")
            
            self.is_demo_playing = True
            
            success = self.start_mpv(self.demo_video, loop=False)
                
            if success:
                print("👀 Starting demo monitor thread...")
                monitor_thread = Thread(target=self.monitor_demo_end)
                monitor_thread.daemon = True
                monitor_thread.start()
            else:
                print("❌ Failed to start demo video, resetting state...")
                self.is_demo_playing = False
    
    def monitor_demo_end(self):
        """Monitor when demo video ends and return to carescape video"""
        print("🔍 Monitoring demo video for completion...")
        
        while self.is_demo_playing and not self.stop_monitoring.is_set():
            if self.mpv_process and self.mpv_process.poll() is not None:
                print("✅ Demo video finished!")
                print("🔄 Returning to carescape video...")
                
                success = self.start_mpv(self.carescape_video, loop=True)
                if success:
                    print("✅ Successfully returned to carescape video")
                else:
                    print("❌ Failed to restart carescape video")
                    
                self.is_demo_playing = False
                break
                
            time.sleep(0.5)
        
        print("🏁 Demo monitoring thread ended")
    
    def stop_mpv(self):
        """Stop MPV playback (Ctrl+Alt+E)"""
        if self.mpv_process and self.mpv_process.poll() is None:
            print("⏹️  Stopping MPV...")
            self.stop_monitoring.set()
            self.mpv_process.terminate()
            try:
                self.mpv_process.wait(timeout=3)
                print("✅ MPV stopped")
            except subprocess.TimeoutExpired:
                print("⚠️  Force killing MPV...")
                self.mpv_process.kill()
            self.is_demo_playing = False
        else:
            print("⚠️  MPV is not running")
    
    def restart_carescape(self):
        """Restart carescape video (Ctrl+Alt+S)"""
        print("🔄 Restarting carescape video...")
        self.stop_monitoring.set()
        self.is_demo_playing = False
        success = self.start_mpv(self.carescape_video, loop=True)
        if success:
            print("✅ Carescape video restarted")
            self.stop_monitoring.clear()
        else:
            print("❌ Failed to restart carescape video")
    
    def setup_flic_hotkey(self):
        """Setup hotkeys for Flic button and manual controls"""
        try:
            print("⌨️  Setting up GLOBAL hotkeys...")
            # CHANGE: Added suppress=False to make hotkeys work globally regardless of focus
            # This allows hotkeys to work even when other applications are active
            keyboard.add_hotkey('ctrl+alt+d', self.flic_button_pressed, suppress=False)
            keyboard.add_hotkey('ctrl+alt+e', self.stop_mpv, suppress=False)
            keyboard.add_hotkey('ctrl+alt+r', self.restart_carescape, suppress=False)
            print("✅ Global hotkeys registered successfully")
            print("   - Ctrl+Alt+D: Toggle demo/carescape")
            print("   - Ctrl+Alt+E: Stop MPV")
            print("   - Ctrl+Alt+R: Restart carescape")
            print("   ⚠️  NOTE: Script must run as ADMINISTRATOR for global hotkeys")
            return True
        except Exception as e:
            print(f"❌ Failed to setup hotkeys: {e}")
            print("💡 MUST run as administrator for global hotkeys to work!")
            return False
    
    def run(self):
        """Main run function"""
        print("🎬 Starting Flic Button MPV Controller...")
        print("=" * 50)
        
        # CHANGE: Added administrator check warning
        try:
            import ctypes
            is_admin = ctypes.windll.shell32.IsUserAnAdmin()
            if not is_admin:
                print("⚠️  WARNING: Script is NOT running as Administrator!")
                print("   Global hotkeys may not work properly.")
                print("   Right-click the script and select 'Run as administrator'")
                print()
        except:
            pass
        
        if not os.path.exists(self.carescape_video):
            print(f"❌ Error: Carescape video not found: {self.carescape_video}")
            print(f"📁 Please check that the file exists at this exact path")
            return
            
        if not os.path.exists(self.demo_video):
            print(f"❌ Error: Demo video not found: {self.demo_video}")
            print(f"📁 Please check that the file exists at this exact path")
            return
        
        print(f"✅ Carescape video found: {os.path.basename(self.carescape_video)}")
        print(f"✅ Demo video found: {os.path.basename(self.demo_video)}")
        
        if not self.find_mpv():
            return
            
        print("\n🎥 Starting carescape video on second monitor...")
        if not self.start_mpv(self.carescape_video, loop=True):
            print("❌ Failed to start carescape video")
            return
        
        if not self.setup_flic_hotkey():
            print("❌ Failed to setup hotkey - continuing anyway")
        
        print(f"\n🖥️  Currently playing on second monitor: {os.path.basename(self.carescape_video)} (looping)")
        print(f"🎯 Demo video ready: {os.path.basename(self.demo_video)}")
        print(f"🔊 Carescape volume: {self.carescape_volume}% | Demo volume: {self.demo_volume}%")
        print("\n🔘 Controls:")
        print("- Ctrl+Alt+D (Flic button) → Toggle demo/carescape video")
        print("- Ctrl+Alt+E → Stop MPV")
        print("- Ctrl+Alt+R → Restart carescape video")
        print("- Ctrl+C → Exit script completely")
        print("- Q (in MPV window) → Quit MPV")
        print("\n✅ System ready! Global hotkeys active (work from any application)")
        
        try:
            print("\n⏳ Script running... (Press Ctrl+C to exit)")
            # CHANGE: Using keyboard.wait() without specific key keeps listener alive for global hotkeys
            keyboard.wait('ctrl+c')
        except KeyboardInterrupt:
            print("\n👋 Received exit signal...")
        finally:
            self.cleanup()
    
    def cleanup(self):
        """Clean up resources"""
        print("\n🛑 Shutting down...")
        self.stop_monitoring.set()
        
        if self.mpv_process and self.mpv_process.poll() is None:
            print("⏹️  Terminating MPV...")
            self.mpv_process.terminate()
            
            try:
                self.mpv_process.wait(timeout=3)
                print("✅ MPV closed gracefully")
            except subprocess.TimeoutExpired:
                print("⚠️  Force killing MPV...")
                self.mpv_process.kill()
                
        print("✅ Cleanup complete")

def main():
    carescape_video = r"C:\Users\CareWall\Desktop\Demo\chehaliscare.mp4"
    demo_video = r"C:\Users\CareWall\Desktop\Demo\demo_video.mp4"
    
    print("🎮 Flic MPV Controller - GLOBAL HOTKEY VERSION")
    print(f"📹 Carescape: {carescape_video}")
    print(f"🎬 Demo: {demo_video}")
    print(f"🖥️  Target: Second Monitor")
    print()
    
    controller = FlicMPVController(carescape_video, demo_video)
    controller.run()

if __name__ == "__main__":
    main()