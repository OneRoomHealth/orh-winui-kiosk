using System;
using System.Runtime.InteropServices;

namespace KioskApp.Helpers;

/// <summary>
/// Win32 API native declarations for window management and keyboard handling.
/// Centralizes all P/Invoke code for cleaner separation from business logic.
/// Note: Named Win32Native to avoid conflict with WinRT.Interop.Win32Interop.
/// </summary>
internal static class Win32Native
{
    #region Window Style Constants

    /// <summary>Window style index for GetWindowLong/SetWindowLong.</summary>
    public const int GWL_STYLE = -16;

    /// <summary>Extended window style index.</summary>
    public const int GWL_EXSTYLE = -20;

    /// <summary>Window has a title bar.</summary>
    public const int WS_CAPTION = 0x00C00000;

    /// <summary>Window has a sizing border.</summary>
    public const int WS_THICKFRAME = 0x00040000;

    /// <summary>Window has a minimize button.</summary>
    public const int WS_MINIMIZEBOX = 0x00020000;

    /// <summary>Window has a maximize button.</summary>
    public const int WS_MAXIMIZEBOX = 0x00010000;

    /// <summary>Window has a system menu.</summary>
    public const int WS_SYSMENU = 0x00080000;

    /// <summary>Window is topmost (always on top).</summary>
    public const int WS_EX_TOPMOST = 0x00000008;

    #endregion

    #region SetWindowPos Flags

    /// <summary>Show the window.</summary>
    public const uint SWP_SHOWWINDOW = 0x0040;

    /// <summary>Retain current Z order.</summary>
    public const uint SWP_NOZORDER = 0x0004;

    /// <summary>Apply new frame styles.</summary>
    public const uint SWP_FRAMECHANGED = 0x0020;

    /// <summary>Retain current position.</summary>
    public const uint SWP_NOMOVE = 0x0002;

    /// <summary>Retain current size.</summary>
    public const uint SWP_NOSIZE = 0x0001;

    /// <summary>HWND value for topmost window.</summary>
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    /// <summary>HWND value for non-topmost window.</summary>
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    #endregion

    #region Keyboard Hook Constants

    /// <summary>Low-level keyboard hook type.</summary>
    public const int WH_KEYBOARD_LL = 13;

    /// <summary>Key down message.</summary>
    public const int WM_KEYDOWN = 0x0100;

    /// <summary>Key up message.</summary>
    public const int WM_KEYUP = 0x0101;

    /// <summary>System key down message.</summary>
    public const int WM_SYSKEYDOWN = 0x0104;

    /// <summary>System key up message.</summary>
    public const int WM_SYSKEYUP = 0x0105;

    #endregion

    #region Structures

    /// <summary>
    /// Represents a rectangle with left, top, right, bottom coordinates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    /// <summary>
    /// Contains information about a low-level keyboard input event.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    #endregion

    #region Delegates

    /// <summary>
    /// Callback delegate for low-level keyboard hook.
    /// </summary>
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    #endregion

    #region Window Functions

    /// <summary>
    /// Retrieves information about the specified window.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    /// <summary>
    /// Changes an attribute of the specified window.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>
    /// Changes the size, position, and Z order of a window.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    /// <summary>
    /// Retrieves the dimensions of the bounding rectangle of the specified window.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    #endregion

    #region Keyboard Functions

    /// <summary>
    /// Retrieves the status of the specified virtual key.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    /// <summary>
    /// Installs a hook procedure that monitors low-level keyboard input events.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    /// <summary>
    /// Removes a hook procedure installed by SetWindowsHookEx.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    /// <summary>
    /// Passes the hook information to the next hook procedure in the chain.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Retrieves a module handle for the specified module.
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    #endregion

    #region Helper Methods

    /// <summary>
    /// Removes standard window chrome (caption, borders, system menu).
    /// </summary>
    public static void RemoveWindowChrome(IntPtr hwnd)
    {
        int style = GetWindowLong(hwnd, GWL_STYLE);
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
        SetWindowLong(hwnd, GWL_STYLE, style);
    }

    /// <summary>
    /// Makes window topmost (always on top).
    /// </summary>
    public static void MakeTopmost(IntPtr hwnd)
    {
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_TOPMOST;
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
    }

    /// <summary>
    /// Positions a window with topmost flag.
    /// </summary>
    public static void SetWindowPositionTopmost(IntPtr hwnd, int x, int y, int width, int height)
    {
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, width, height, SWP_SHOWWINDOW | SWP_FRAMECHANGED);
    }

    /// <summary>
    /// Checks if a key is currently pressed.
    /// </summary>
    public static bool IsKeyPressed(int virtualKey)
    {
        return (GetKeyState(virtualKey) & 0x8000) != 0;
    }

    #endregion
}
