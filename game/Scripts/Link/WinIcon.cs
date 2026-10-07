using System;
using System.Runtime.InteropServices;
using Godot;

namespace GameNight.Link;

/// <summary>
/// Windows: gives the game window the icon built into GameNight.exe, loaded by Windows itself
/// (the same loader Explorer uses, at the sizes the taskbar and Alt+Tab ask for), on the window
/// and on its window class. Godot registers its class with the blank IDI_WINLOGO icon and only
/// swaps it through its own icon builders, which left the running taskbar button blank.
/// </summary>
static class WinIcon
{
    const uint WmSetIcon = 0x0080;
    const int GclpHIcon = -14, GclpHIconSm = -34;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint ExtractIconExW(string file, int index, IntPtr[] large, IntPtr[] small, uint count);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr SetClassLongPtrW(IntPtr hWnd, int index, IntPtr value);

    static IntPtr _big, _small;

    /// <summary>Put the exe's icon on the main window. Cheap; safe to call again after mode changes.</summary>
    public static void Apply()
    {
        if (OS.GetName() != "Windows" || OS.HasFeature("editor")) return;
        try
        {
            if (_big == IntPtr.Zero)
            {
                var big = new IntPtr[1];
                var small = new IntPtr[1];
                ExtractIconExW(OS.GetExecutablePath().Replace('/', '\\'), 0, big, small, 1);
                _big = big[0];
                _small = small[0] != IntPtr.Zero ? small[0] : big[0];
                if (_big == IntPtr.Zero) return;
            }
            var hwnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
            if (hwnd == IntPtr.Zero) return;
            SetClassLongPtrW(hwnd, GclpHIcon, _big);
            SetClassLongPtrW(hwnd, GclpHIconSm, _small);
            SendMessageW(hwnd, WmSetIcon, 0, _small);
            SendMessageW(hwnd, WmSetIcon, 1, _big);
        }
        catch (Exception e)
        {
            GD.PushWarning($"Window icon: {e.Message}");
        }
    }
}
