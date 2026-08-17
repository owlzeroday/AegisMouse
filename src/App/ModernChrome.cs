using System;
using System.Runtime.InteropServices;

namespace AegisMouse.App;

/// <summary>Applies modern OS window chrome: dark title bar, acrylic/mica backdrop, rounded corners.</summary>
static class ModernChrome
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Returns true when a translucent backdrop (Mica or Acrylic) was applied.</summary>
    public static bool Apply(IntPtr hwnd)
    {
        // 38 = DWMWA_SYSTEMBACKDROP_TYPE (Win11 22H2+, 3 = Mica)
        // 17 = DWMWA_USE_HOSTBACKDROPBRUSH (Win10 2004+, acrylic)
        bool backdrop = SetAttr(hwnd, 38, 3) || SetAttr(hwnd, 17, 1);

        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Win10 2004+), 19 = dark mode (1809-1903)
        // 1 = dark (matches the app's dark theme)
        SetAttr(hwnd, 20, 1);
        SetAttr(hwnd, 19, 1);

        // 33 = DWMWA_WINDOW_CORNER_PREFERENCE, 2 = round (Win11)
        SetAttr(hwnd, 33, 2);

        return backdrop;
    }

    static bool SetAttr(IntPtr hwnd, int attr, int value)
    {
        int v = value;
        return DwmSetWindowAttribute(hwnd, attr, ref v, sizeof(int)) == 0;
    }
}