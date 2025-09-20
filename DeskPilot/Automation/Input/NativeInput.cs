using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DesktopAssist.Automation.Input;

public enum VirtualKey : ushort
{
    VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12,
    VK_PAUSE = 0x13, VK_CAPITAL = 0x14, VK_ESCAPE = 0x1B, VK_SPACE = 0x20,
    VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23, VK_HOME = 0x24,
    VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28,
    VK_SNAPSHOT = 0x2C, VK_INSERT = 0x2D, VK_DELETE = 0x2E,
    VK_0 = 0x30, VK_1 = 0x31, VK_2 = 0x32, VK_3 = 0x33, VK_4 = 0x34, VK_5 = 0x35, VK_6 = 0x36, VK_7 = 0x37, VK_8 = 0x38, VK_9 = 0x39,
    VK_A = 0x41, VK_B = 0x42, VK_C = 0x43, VK_D = 0x44, VK_E = 0x45, VK_F = 0x46, VK_G = 0x47, VK_H = 0x48, VK_I = 0x49,
    VK_J = 0x4A, VK_K = 0x4B, VK_L = 0x4C, VK_M = 0x4D, VK_N = 0x4E, VK_O = 0x4F, VK_P = 0x50, VK_Q = 0x51, VK_R = 0x52,
    VK_S = 0x53, VK_T = 0x54, VK_U = 0x55, VK_V = 0x56, VK_W = 0x57, VK_X = 0x58, VK_Y = 0x59, VK_Z = 0x5A,
    VK_LWIN = 0x5B, VK_RWIN = 0x5C,
    VK_F1 = 0x70, VK_F2 = 0x71, VK_F3 = 0x72, VK_F4 = 0x73, VK_F5 = 0x74, VK_F6 = 0x75, VK_F7 = 0x76, VK_F8 = 0x77,
    VK_F9 = 0x78, VK_F10 = 0x79, VK_F11 = 0x7A, VK_F12 = 0x7B, VK_F13 = 0x7C, VK_F14 = 0x7D, VK_F15 = 0x7E, VK_F16 = 0x7F,
    VK_F17 = 0x80, VK_F18 = 0x81, VK_F19 = 0x82, VK_F20 = 0x83, VK_F21 = 0x84, VK_F22 = 0x85, VK_F23 = 0x86, VK_F24 = 0x87
}

// Correct SendInput interop layout (include full union). A too-small cbSize causes ERROR_INVALID_PARAMETER (87).
[StructLayout(LayoutKind.Sequential)]
public struct INPUT
{
    public uint type; // 0=mouse,1=keyboard,2=hardware
    public InputUnion U;
}

[StructLayout(LayoutKind.Explicit)]
public struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
    [FieldOffset(0)] public HARDWAREINPUT hi;
}

[StructLayout(LayoutKind.Sequential)]
public struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct HARDWAREINPUT
{
    public uint uMsg;
    public ushort wParamL;
    public ushort wParamH;
}

[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}


public static class ScreenSnapshotInfo
{
    public static int VirtualLeft { get; private set; }
    public static int VirtualTop { get; private set; }
    public static int VirtualWidth { get; private set; }
    public static int VirtualHeight { get; private set; }

    public static int LastImageWidth { get; private set; }
    public static int LastImageHeight { get; private set; }

    public static void RefreshVirtualMetrics()
    {
        VirtualLeft = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
        VirtualTop = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
        VirtualWidth = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
        VirtualHeight = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
    }

    public static void SetImageSize(Size s)
    {
        LastImageWidth = s.Width;
        LastImageHeight = s.Height;
    }

    public static (int x, int y) MapFromImagePx(int imgX, int imgY)
    {
        if (LastImageWidth <= 0 || LastImageHeight <= 0 || VirtualWidth <= 0 || VirtualHeight <= 0)
            return (imgX + VirtualLeft, imgY + VirtualTop); // fallback

        var sx = (double)VirtualWidth / LastImageWidth;
        var sy = (double)VirtualHeight / LastImageHeight;
        int x = (int)Math.Round(VirtualLeft + imgX * sx);
        int y = (int)Math.Round(VirtualTop + imgY * sy);
        return ClampToVirtual(x, y);
    }

    public static (int x, int y) MapFromNorm(double xNorm, double yNorm)
    {
        if (VirtualWidth <= 0 || VirtualHeight <= 0) RefreshVirtualMetrics();
        int x = (int)Math.Round(VirtualLeft + xNorm * VirtualWidth);
        int y = (int)Math.Round(VirtualTop + yNorm * VirtualHeight);
        return ClampToVirtual(x, y);
    }

    public static (int x, int y) ClampToVirtual(int x, int y)
    {
        int rx = Math.Max(VirtualLeft, Math.Min(VirtualLeft + VirtualWidth - 1, x));
        int ry = Math.Max(VirtualTop, Math.Min(VirtualTop + VirtualHeight - 1, y));
        return (rx, ry);
    }
}

public static class Native
{
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    internal const int SW_RESTORE = 9;


    [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    internal static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetCursorPos(out POINT lpPoint);
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")] internal static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] internal static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("gdi32.dll")]
    internal static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
                                                               IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetCursorPos(int X, int Y);

    internal const int SRCCOPY = 0x00CC0020;

    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int nIndex);
    internal const int SM_XVIRTUALSCREEN = 76;
    internal const int SM_YVIRTUALSCREEN = 77;
    internal const int SM_CXVIRTUALSCREEN = 78;
    internal const int SM_CYVIRTUALSCREEN = 79;
    internal const int SM_REMOTESESSION = 0x1000; // 4096

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = false)]
    public static extern bool AllocConsole();

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    internal const int SW_HIDE = 0;
    internal const int SW_MINIMIZE = 6;

    // Console control handler
    internal delegate bool ConsoleCtrlHandlerRoutine(uint dwCtrlType);
    [DllImport("kernel32.dll")]
    internal static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandlerRoutine handler, bool add);

    internal const uint CTRL_C_EVENT = 0;
    internal const uint CTRL_BREAK_EVENT = 1;
    internal const uint CTRL_CLOSE_EVENT = 2;
    internal const uint CTRL_LOGOFF_EVENT = 5;
    internal const uint CTRL_SHUTDOWN_EVENT = 6;

    internal const uint INPUT_MOUSE = 0;
    internal const uint INPUT_KEYBOARD = 1;
    internal const uint INPUT_HARDWARE = 2;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_UNICODE = 0x0004;
    internal const uint KEYEVENTF_SCANCODE = 0x0008;
    internal const uint KEYEVENTF_EXTENDEDKEY = 0x0001; // for certain keys (arrows, etc.) when using scan codes
    // Mouse flags
    internal const uint MOUSEEVENTF_MOVE = 0x0001;
    internal const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
    internal const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    internal const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    internal const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    internal const uint MOUSEEVENTF_MIDDLEUP = 0x0040;


    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint MapVirtualKey(uint uCode, uint uMapType);
}

internal static class WindowFocus
{
    public static bool FocusByTitleSubstring(string? titleSubstr)
    {
        if (string.IsNullOrWhiteSpace(titleSubstr)) return false;
        titleSubstr = titleSubstr.Trim();
        IntPtr target = IntPtr.Zero;

        Native.EnumWindows((h, l) =>
        {
            if (!Native.IsWindowVisible(h)) return true;
            var sb = new StringBuilder(512);
            if (Native.GetWindowText(h, sb, sb.Capacity) > 0)
            {
                var text = sb.ToString();
                if (text.IndexOf(titleSubstr, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    target = h;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);

        if (target != IntPtr.Zero)
        {
            Native.ShowWindowAsync(target, Native.SW_RESTORE);
            return Native.SetForegroundWindow(target);
        }
        return false;
    }
}


internal static class ConsoleWindowManager
{
    // Hides the console window; if already hidden does nothing.
    public static void Hide()
    {
        var h = Native.GetConsoleWindow();
        if (h != IntPtr.Zero)
        {
            // Hide instead of minimize to fully remove focus target. User can Alt+Tab to desired app.
            Native.ShowWindow(h, Native.SW_HIDE);
        }
    }
}

internal static class Input
{
    public static void KeyDown(VirtualKey key) => SendKey(key, false);
    public static void KeyUp(VirtualKey key) => SendKey(key, true);

    public static void KeyTap(VirtualKey key)
    {
        KeyDown(key);
        Thread.Sleep(10);
        KeyUp(key);
    }

    private static void SendKey(VirtualKey key, bool up)
    {
        // Use scan code path for better reliability with function and navigation keys.
        uint scan = Native.MapVirtualKey((uint)key, 0); // MAPVK_VK_TO_VSC
        bool useScan = scan != 0;
        uint flags = 0;
        if (up) flags |= Native.KEYEVENTF_KEYUP;
        if (useScan) flags |= Native.KEYEVENTF_SCANCODE;
        if (useScan && IsExtended(key)) flags |= Native.KEYEVENTF_EXTENDEDKEY;

        var ki = new KEYBDINPUT
        {
            wVk = useScan ? (ushort)0 : (ushort)key, // if using scancode, wVk often set 0
            wScan = (ushort)scan,
            dwFlags = flags,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };
        var inp = new INPUT { type = Native.INPUT_KEYBOARD, U = new InputUnion { ki = ki } };
        Dispatch(new[] { inp });
    }

    private static bool IsExtended(VirtualKey k) => k switch
    {
        VirtualKey.VK_MENU or VirtualKey.VK_CONTROL or VirtualKey.VK_SHIFT => false, // modifiers not marked extended here
        VirtualKey.VK_LEFT or VirtualKey.VK_RIGHT or VirtualKey.VK_UP or VirtualKey.VK_DOWN => true,
        VirtualKey.VK_HOME or VirtualKey.VK_END or VirtualKey.VK_PRIOR or VirtualKey.VK_NEXT => true,
        VirtualKey.VK_INSERT or VirtualKey.VK_DELETE => true,
        VirtualKey.VK_LWIN or VirtualKey.VK_RWIN => true,
        _ => false
    };

    public static void SendUnicodeString(string text, int intervalMs = 8)
    {
        foreach (var ch in text)
        {
            SendUnicodeChar(ch, false);
            Thread.Sleep(intervalMs);
            SendUnicodeChar(ch, true);
        }
    }

    private static void SendUnicodeChar(char ch, bool up)
    {
        var inp = new INPUT
        {
            type = Native.INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = ch,
                    dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        Dispatch(new[] { inp });
    }

    public static void KeyChord(IReadOnlyList<VirtualKey> modifiers, IReadOnlyList<VirtualKey> normals)
    {
        foreach (var m in modifiers) KeyDown(m);
        if (normals.Count == 0)
        {
            // Pure modifier chord -> tap modifiers
            for (int i = modifiers.Count - 1; i >= 0; i--) KeyUp(modifiers[i]);
            return;
        }
        foreach (var n in normals) KeyDown(n);
        for (int i = normals.Count - 1; i >= 0; i--) KeyUp(normals[i]);
        for (int i = modifiers.Count - 1; i >= 0; i--) KeyUp(modifiers[i]);
    }

    // Central dispatch wrapper so we can detect and log injection failures.
    private static void Dispatch(INPUT[] inputs)
    {
        uint sent = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            int err = Marshal.GetLastWin32Error();
            // This commonly fails if targeting a higher integrity (elevated) window from a normal process.
            Console.WriteLine($"[SendInput][Error] injected=0 expected={inputs.Length} lastError={err}");
        }
    }

    // Mouse helpers (absolute positioning based on primary screen)

    public static void MouseMoveAbsolute(int x, int y)
    {
        // Primary attempt: SetCursorPos (pixel-accurate, honors DPI)
        if (!Native.SetCursorPos(x, y))
        {
            int err = Marshal.GetLastWin32Error();
            Console.WriteLine($"[MouseMove][Warn] SetCursorPos failed err={err} target=({x},{y}) attempting SendInput fallback");
        }
    }



    public static void MouseClick(string button)
    {
        uint down, up;
        switch (button)
        {
            case "right": down = Native.MOUSEEVENTF_RIGHTDOWN; up = Native.MOUSEEVENTF_RIGHTUP; break;
            case "middle": down = Native.MOUSEEVENTF_MIDDLEDOWN; up = Native.MOUSEEVENTF_MIDDLEUP; break;
            default: down = Native.MOUSEEVENTF_LEFTDOWN; up = Native.MOUSEEVENTF_LEFTUP; break;
        }
        var downInp = new INPUT { type = Native.INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dx = 0, dy = 0, mouseData = 0, dwFlags = down, time = 0, dwExtraInfo = IntPtr.Zero } } };
        var upInp = new INPUT { type = Native.INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dx = 0, dy = 0, mouseData = 0, dwFlags = up, time = 0, dwExtraInfo = IntPtr.Zero } } };
        uint sentBefore = 0; // for potential future aggregation
        Dispatch(new[] { downInp, upInp });
        if (!Native.GetCursorPos(out var p))
        {
            Console.WriteLine("[MouseClick][Warn] GetCursorPos failed after click");
        }
    }
}

internal static class ClipboardUtil
{
    /// <summary>
    /// Robustly sets clipboard text (Unicode) on an STA thread with retries.
    /// Optionally returns previous text so caller may restore if desired.
    /// </summary>
    public static string? SetText(string text, int retries = 4, int retryDelayMs = 40, bool getPrevious = true)
    {
        text ??= string.Empty;
        string? previous = null;

        var thread = new Thread(() =>
        {
            for (int attempt = 0; attempt < retries; attempt++)
            {
                try
                {
                    if (getPrevious && attempt == 0)
                    {
                        try { if (Clipboard.ContainsText()) previous = Clipboard.GetText(); } catch { /* ignore */ }
                    }
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                    return; // success
                }
                catch (Exception ex)
                {
                    if (attempt == retries - 1)
                    {
                        Console.WriteLine($"[Clipboard][Error] failed attempts={retries} msg={ex.Message}");
                    }
                    Thread.Sleep(retryDelayMs);
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        return previous;
    }

    public static void RestoreText(string? previous)
    {
        if (previous == null) return;
        var thread = new Thread(() =>
        {
            try { Clipboard.SetText(previous, TextDataFormat.UnicodeText); } catch { /* ignore */ }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
    }
}


internal static class KeyMap
{
    public static bool TryMap(string keyStr, out VirtualKey vk)
    {
        vk = default;
        if (string.IsNullOrWhiteSpace(keyStr)) return false;
        var k = keyStr.Trim().ToLowerInvariant();

        // aliases
        switch (k)
        {
            case "ctrl": case "control": vk = VirtualKey.VK_CONTROL; return true;
            case "alt": case "menu": vk = VirtualKey.VK_MENU; return true;
            case "shift": vk = VirtualKey.VK_SHIFT; return true;
            case "win": case "meta": case "command": vk = VirtualKey.VK_LWIN; return true;
            case "enter": case "return": vk = VirtualKey.VK_RETURN; return true;
            case "esc": case "escape": vk = VirtualKey.VK_ESCAPE; return true;
            case "space": vk = VirtualKey.VK_SPACE; return true;
            case "tab": vk = VirtualKey.VK_TAB; return true;
            case "backspace": vk = VirtualKey.VK_BACK; return true;
            case "delete": case "del": vk = VirtualKey.VK_DELETE; return true;
            case "home": vk = VirtualKey.VK_HOME; return true;
            case "end": vk = VirtualKey.VK_END; return true;
            case "pageup": case "pgup": vk = VirtualKey.VK_PRIOR; return true;
            case "pagedown": case "pgdn": vk = VirtualKey.VK_NEXT; return true;
            case "up": vk = VirtualKey.VK_UP; return true;
            case "down": vk = VirtualKey.VK_DOWN; return true;
            case "left": vk = VirtualKey.VK_LEFT; return true;
            case "right": vk = VirtualKey.VK_RIGHT; return true;
        }

        if (k.Length == 1)
        {
            char c = k[0];
            if (c >= '0' && c <= '9') { vk = (VirtualKey)('0' + (c - '0')); return true; }
            if (c >= 'a' && c <= 'z') { vk = (VirtualKey)('A' + (c - 'a')); return true; }
        }

        if (k.StartsWith("f") && int.TryParse(k.Substring(1), out var fn) && fn is >= 1 and <= 24)
        {
            vk = (VirtualKey)((int)VirtualKey.VK_F1 + (fn - 1));
            return true;
        }

        return false;
    }
}