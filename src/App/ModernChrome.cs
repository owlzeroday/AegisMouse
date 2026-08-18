using System;
using System.Runtime.InteropServices;

namespace AegisMouse.App;

/// <summary>Applies dark window chrome: dark title bar + rounded corners. No Mica/Acrylic — flat solid black for Linux/terminal aesthetic.</summary>
static class ModernChrome
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Always returns false so the caller uses the solid black WPF background (no translucent backdrop).</summary>
    public static bool Apply(IntPtr hwnd)
    {
        // Dark mode FIRST — before anything else, so the title bar and chrome are dark.
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Win10 2004+), 19 = dark mode (1809-1903)
        SetAttr(hwnd, 20, 1);
        SetAttr(hwnd, 19, 1);

        // Rounded corners (Win11). 33 = DWMWA_WINDOW_CORNER_PREFERENCE, 2 = round
        SetAttr(hwnd, 33, 2);

        // Explicitly disable translucent backdrops — force solid black canvas.
        // 38 = DWMWA_SYSTEMBACKDROP_TYPE; 1 = None (disable Mica/Acrylic)
        SetAttr(hwnd, 38, 1);

        // 35 = DWMWA_SYSTEMBACKDROP_TYPE fallback (some Win11 builds use different attr id)
        SetAttr(hwnd, 35, 0);

        return false; // always solid — caller paints BgSolid
    }

    static bool SetAttr(IntPtr hwnd, int attr, int value)
    {
        int v = value;
        return DwmSetWindowAttribute(hwnd, attr, ref v, sizeof(int)) == 0;
    }
}
