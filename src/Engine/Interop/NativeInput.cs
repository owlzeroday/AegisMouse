using System.Runtime.InteropServices;
using AegisMouse.Core;
using Serilog;

namespace AegisMouse.Engine.Interop;

/// <summary>Windows virtual-key codes (subset used by the app).</summary>
public static class Vk
{
    public const int Escape = 0x1B, Space = 0x20, Tab = 0x09, CapsLock = 0x14, Shift = 0x10,
        Control = 0x11, Alt = 0x12, Enter = 0x0D, F1 = 0x70, F2 = 0x71, F3 = 0x72, F4 = 0x73, F5 = 0x74,
        F6 = 0x75, F7 = 0x76, F8 = 0x77, F9 = 0x78, F10 = 0x79, F11 = 0x7A, F12 = 0x7B,
        A = 0x41, D = 0x44, S = 0x53, W = 0x57, R = 0x52, Q = 0x51, E = 0x45, V = 0x56,
        C = 0x43, X = 0x58, G = 0x47, B = 0x42, K = 0x4B, T = 0x54, P = 0x50, Z = 0x5A, F = 0x46,
        Digit1 = 0x31, Digit2 = 0x32, Digit3 = 0x33, Digit4 = 0x34, Digit5 = 0x35,
        Digit6 = 0x36, Digit7 = 0x37, Digit8 = 0x38, Digit9 = 0x39, Digit0 = 0x30;

    public static string Name(int vk) => vk switch
    {
        Escape => "Esc", Space => "Space", Tab => "Tab", CapsLock => "CapsLock", Enter => "Enter",
        Shift => "Shift", Control => "Ctrl", Alt => "Alt",
        >= F1 and <= F12 => $"F{vk - F1 + 1}",
        >= Digit1 and <= Digit5 => (vk - Digit1 + 1).ToString(),
        >= Digit6 and <= Digit9 => (vk - Digit6 + 6).ToString(),
        Digit0 => "0",
        >= A and <= Z => ((char)vk).ToString(),
        _ => $"VK0x{vk:X2}"
    };
}

/// <summary>
/// Kernel-level input synthesis via SendInput. Works with ANY mouse brand because input
/// is injected at the OS level — no vendor driver, no on-board memory required.
/// </summary>
public static class NativeInput
{
    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
        MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010,
        MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040,
        MOUSEEVENTF_XDOWN = 0x0080, MOUSEEVENTF_XUP = 0x0100,
        MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_WHEEL = 0x0800;
    const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_SCANCODE = 0x0008;
    const int XBUTTON1 = 0x0001, XBUTTON2 = 0x0002;
    const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [DllImport("winmm.dll")]
    static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    static extern uint timeEndPeriod(uint uPeriod);

    static INPUT[] OneMouse(uint flags, uint data = 0, int dx = 0, int dy = 0)
    {
        var inp = new INPUT { type = INPUT_MOUSE };
        inp.U.mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags, dwExtraInfo = IntPtr.Zero };
        return [inp];
    }

    static void Inject(INPUT[] inputs)
    {
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            Log.Warning("SendInput injected {Sent}/{Total} events (win32 error {Error})",
                sent, inputs.Length, Marshal.GetLastWin32Error());
    }

    public static void ButtonDown(MouseButton b) => Inject(OneMouse(FlagsFor(b).down, DataFor(b)));
    public static void ButtonUp(MouseButton b) => Inject(OneMouse(FlagsFor(b).up, DataFor(b)));

    /// <summary>Full click: down, (natural delay handled by the player), up.</summary>
    public static void Click(MouseButton b, int holdMs)
    {
        ButtonDown(b);
        Thread.Sleep(Math.Max(0, holdMs));
        ButtonUp(b);
    }

    public static void MoveRelative(int dx, int dy) => Inject(OneMouse(MOUSEEVENTF_MOVE, dx: dx, dy: dy));

    /// <summary>Absolute move (0..65535 space). Used for tests / telemetry only.</summary>
    public static void MoveAbsolute(int x, int y)
    {
        int w = GetSystemMetrics(SM_CXSCREEN), h = GetSystemMetrics(SM_CYSCREEN);
        if (w <= 0 || h <= 0) return;
        var inp = new INPUT { type = INPUT_MOUSE };
        inp.U.mi = new MOUSEINPUT
        {
            dx = (int)(x * 65535.0 / (w - 1)), dy = (int)(y * 65535.0 / (h - 1)),
            dwFlags = MOUSEEVENTF_MOVE | 0x8000 /* MOUSEEVENTF_ABSOLUTE */, dwExtraInfo = IntPtr.Zero
        };
        Inject([inp]);
    }

    public static void Wheel(int ticks) => Inject(OneMouse(MOUSEEVENTF_WHEEL, (uint)(ticks * 120)));

    public static void KeyDown(int vk) => Inject(OneKey(vk, false));
    public static void KeyUp(int vk) => Inject(OneKey(vk, true));
    public static void KeyTap(int vk)
    {
        KeyDown(vk);
        Thread.Sleep(8);
        KeyUp(vk);
    }

    static INPUT[] OneKey(int vk, bool up)
    {
        var inp = new INPUT { type = INPUT_KEYBOARD };
        inp.U.ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = 0, dwFlags = up ? KEYEVENTF_KEYUP : 0, dwExtraInfo = IntPtr.Zero };
        return [inp];
    }

    public static bool IsDown(MouseButton b) => b switch
    {
        MouseButton.Left => GetAsyncKeyState(0x01) < 0,
        MouseButton.Right => GetAsyncKeyState(0x02) < 0,
        MouseButton.Middle => GetAsyncKeyState(0x04) < 0,
        MouseButton.Side1 => GetAsyncKeyState(0x05) < 0,
        MouseButton.Side2 => GetAsyncKeyState(0x06) < 0,
        _ => false
    };

    public static bool IsKeyDown(int vk) => GetAsyncKeyState(vk) < 0;

    public static (int X, int Y) Cursor
    {
        get { GetCursorPos(out var pt); return (pt.X, pt.Y); }
    }

    static (uint down, uint up) FlagsFor(MouseButton b) => b switch
    {
        MouseButton.Left => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
        MouseButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
        MouseButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
        _ => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP)
    };

    static uint DataFor(MouseButton b) => b switch
    {
        MouseButton.Side1 => XBUTTON1,
        MouseButton.Side2 => XBUTTON2,
        _ => 0
    };

    static readonly object _timerLock = new();
    static int _beginPeriodRefs;

    /// <summary>Raises the OS timer resolution to 1 ms (reference-counted).</summary>
    public static void BeginHighResTimer()
    {
        lock (_timerLock)
        {
            if (_beginPeriodRefs++ == 0) timeBeginPeriod(1);
        }
    }

    public static void EndHighResTimer()
    {
        lock (_timerLock)
        {
            if (--_beginPeriodRefs <= 0) { _beginPeriodRefs = 0; timeEndPeriod(1); }
        }
    }
}