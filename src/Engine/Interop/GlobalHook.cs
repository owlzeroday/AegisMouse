using System.Runtime.InteropServices;
using AegisMouse.Core;
using Serilog;

namespace AegisMouse.Engine.Interop;

/// <summary>Low-level mouse/keyboard hooks (WH_MOUSE_LL / WH_KEYBOARD_LL).</summary>
public sealed class GlobalHook : IDisposable
{
    const int WH_MOUSE_LL = 14, WH_KEYBOARD_LL = 13;
    const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202,
        WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208,
        WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C, WM_MOUSEWHEEL = 0x020A;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const uint LLMHF_INJECTED = 0x00000001;
    const uint LLKHF_INJECTED = 0x00000010;

    public delegate void MouseButtonHandler(MouseButton button, bool down);
    public delegate void KeyHandler(int vk, bool down);
    public delegate void WheelHandler(bool down, int ticks);

    public event MouseButtonHandler? Button;
    public event KeyHandler? Key;
    public event WheelHandler? Wheel;

    /// <summary>When set and returning true for a button, the event is swallowed (never reaches apps).</summary>
    public Func<MouseButton, bool>? ShouldSuppress { get; set; }

    IntPtr _mouseHook, _keyHook;
    Thread? _thread;
    uint _threadId;
    volatile bool _running;
    HookProc? _mouseProc, _keyProc;

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(ThreadProc) { IsBackground = true, Name = "GlobalHook" };
        _thread.Start();
        // wait until hooks are live so the UI cannot miss early events
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 2000 && (_mouseHook == IntPtr.Zero || _keyHook == IntPtr.Zero))
            Thread.Sleep(5);
        if (_mouseHook == IntPtr.Zero || _keyHook == IntPtr.Zero)
            Log.Warning("Global hook failed to install (mouse={M}, key={K})", _mouseHook, _keyHook);
    }

    void ThreadProc()
    {
        // The OS thread id is what PostThreadMessage needs.
        _threadId = NativeThreadId();
        _mouseProc = MouseProc;
        _keyProc = KeyProc;
        using var module = new SafeModule(GetModuleHandle(null!));
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module.Handle, 0);
        _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc, module.Handle, 0);
        while (_running && GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        if (_keyHook != IntPtr.Zero) UnhookWindowsHookEx(_keyHook);
        _mouseHook = _keyHook = IntPtr.Zero;
        Log.Information("Global hooks removed");
    }

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    static uint NativeThreadId() => GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    const uint WM_QUIT = 0x0012;

    IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        bool blocked = false;
        try
        {
            if (nCode >= 0)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                switch ((int)wParam)
                {
                    case WM_LBUTTONDOWN: blocked = Raise(MouseButton.Left, true, data); break;
                    case WM_LBUTTONUP: blocked = Raise(MouseButton.Left, false, data); break;
                    case WM_RBUTTONDOWN: blocked = Raise(MouseButton.Right, true, data); break;
                    case WM_RBUTTONUP: blocked = Raise(MouseButton.Right, false, data); break;
                    case WM_MBUTTONDOWN: blocked = Raise(MouseButton.Middle, true, data); break;
                    case WM_MBUTTONUP: blocked = Raise(MouseButton.Middle, false, data); break;
                    case WM_XBUTTONDOWN:
                    case WM_XBUTTONUP:
                        var which = ((uint)data.mouseData >> 16) & 0xFFFF;
                        var btn = which == 1 ? MouseButton.Side1 : MouseButton.Side2;
                        blocked = Raise(btn, (int)wParam == WM_XBUTTONDOWN, data);
                        break;
                    case WM_MOUSEWHEEL:
                        if ((data.flags & LLMHF_INJECTED) == 0)
                        {
                            int ticks = (short)((uint)data.mouseData >> 16) / 120;
                            if (ticks != 0) Wheel?.Invoke(ticks < 0, Math.Abs(ticks));
                        }
                        break;
                }
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Mouse hook callback failed"); }
        // Returning 1 swallows the event so no other app sees it (KeyRemap).
        return blocked ? new IntPtr(1) : CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    /// <summary>Returns true when the event must be blocked from other applications.</summary>
    bool Raise(MouseButton b, bool down, MSLLHOOKSTRUCT data)
    {
        // Never react to our own injected input - prevents infinite loops.
        if ((data.flags & LLMHF_INJECTED) != 0) return false;
        bool suppress = ShouldSuppress?.Invoke(b) == true;
        Button?.Invoke(b, down); // engine always sees the event
        return suppress;
    }

    IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if ((data.flags & LLKHF_INJECTED) == 0)
                    Key?.Invoke((int)data.vkCode, (int)wParam is WM_KEYDOWN or WM_SYSKEYDOWN);
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Keyboard hook callback failed"); }
        return CallNextHookEx(_keyHook, nCode, wParam, lParam);
    }

    sealed class SafeModule : IDisposable
    {
        public IntPtr Handle { get; }
        public SafeModule(IntPtr h) => Handle = h;
        public void Dispose() { }
    }

    public void Dispose()
    {
        _running = false;
        // Wake the message loop (GetMessage blocks forever without a window)
        // so the hooks are actually unhooked and the thread exits.
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        try { _thread?.Join(1000); } catch { }
        _thread = null;
    }
}