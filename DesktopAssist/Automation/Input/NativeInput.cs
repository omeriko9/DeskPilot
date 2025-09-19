using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DesktopAssist.Automation.Input;

internal static class NativeInput
{
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    // Keyboard layout management
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint Flags);

    private const uint KLF_ACTIVATE = 0x00000001;
    private const uint KLF_SETFORPROCESS = 0x00000100;

    public static void EnsureEnglishLayout(string desiredLayoutHex, bool verbose)
    {
        try
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) { if (verbose) Console.WriteLine("[NativeInput][Layout] No foreground window."); return; }
            GetWindowThreadProcessId(fg, out _);
            var current = GetKeyboardLayout(0); // 0 -> current thread
            string currentHex = ((ulong)current).ToString("X16");
            if (currentHex.EndsWith(desiredLayoutHex, StringComparison.OrdinalIgnoreCase))
            {
                if (verbose) Console.WriteLine($"[NativeInput][Layout] Already {desiredLayoutHex}");
                return;
            }
            if (verbose) Console.WriteLine($"[NativeInput][Layout] Switching {currentHex} -> {desiredLayoutHex}");
            var hkl = LoadKeyboardLayout(desiredLayoutHex, KLF_ACTIVATE | KLF_SETFORPROCESS);
            if (hkl == IntPtr.Zero)
            {
                Console.WriteLine($"[NativeInput][Layout][Warn] LoadKeyboardLayout failed for {desiredLayoutHex} (err={Marshal.GetLastWin32Error()})");
                return;
            }
            var act = ActivateKeyboardLayout(hkl, KLF_ACTIVATE);
            if (act == IntPtr.Zero)
            {
                Console.WriteLine($"[NativeInput][Layout][Warn] ActivateKeyboardLayout failed (err={Marshal.GetLastWin32Error()})");
            }
            else if (verbose)
            {
                Console.WriteLine("[NativeInput][Layout] Activated English layout");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeInput][Layout][Error] {ex.Message}");
        }
    }

    public static void SetLayout(string layoutHex, bool verbose)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(layoutHex)) return;
            layoutHex = layoutHex.Trim();
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) { if (verbose) Console.WriteLine("[NativeInput][Layout] No foreground window for SetLayout."); }
            var current = GetKeyboardLayout(0);
            string currentHex = ((ulong)current).ToString("X16");
            if (currentHex.EndsWith(layoutHex, StringComparison.OrdinalIgnoreCase))
            {
                if (verbose) Console.WriteLine($"[NativeInput][Layout] Already {layoutHex}");
                return;
            }
            if (verbose) Console.WriteLine($"[NativeInput][Layout] Activating {layoutHex} (prev {currentHex})");
            var hkl = LoadKeyboardLayout(layoutHex, KLF_ACTIVATE | KLF_SETFORPROCESS);
            if (hkl == IntPtr.Zero)
            {
                Console.WriteLine($"[NativeInput][Layout][Warn] LoadKeyboardLayout failed for {layoutHex} (err={Marshal.GetLastWin32Error()})");
                return;
            }
            var act = ActivateKeyboardLayout(hkl, KLF_ACTIVATE);
            if (act == IntPtr.Zero)
            {
                Console.WriteLine($"[NativeInput][Layout][Warn] ActivateKeyboardLayout failed for {layoutHex} (err={Marshal.GetLastWin32Error()})");
            }
            else if (verbose)
            {
                Console.WriteLine($"[NativeInput][Layout] Activated {layoutHex}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeInput][Layout][Error] SetLayout: {ex.Message}");
        }
    }

    public static string? GetCurrentLayoutHex()
    {
        try
        {
            var kl = GetKeyboardLayout(0);
            return ((ulong)kl).ToString("X16");
        }
        catch { return null; }
    }

    public static void MoveCursor(int x, int y)
    {
        SetCursorPos(x, y);
    }

    public static void LeftClick(int x, int y, int clicks = 1, int intervalMs = 120)
    {
        MoveCursor(x, y);
        for (int i = 0; i < clicks; i++)
        {
            Console.WriteLine($"[NativeInput] LeftClick down/up #{i+1} at ({x},{y})");
            MouseButton(true, false);
            MouseButton(false, false);
            if (i < clicks - 1) Thread.Sleep(intervalMs);
        }
    }

    public static void RightClick(int x, int y)
    {
        MoveCursor(x, y);
        Console.WriteLine($"[NativeInput] RightClick at ({x},{y})");
        MouseButton(true, true);
        MouseButton(false, true);
    }

    private static void MouseButton(bool down, bool right)
    {
        uint flags = right
            ? (down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP)
            : (down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP);

        var inp = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
    }

    public static void KeyTap(ushort vk)
    {
        Console.WriteLine($"[NativeInput] KeyTap vk=0x{vk:X2}");
        KeyEvent(vk, false);
        KeyEvent(vk, true);
    }

    public static void KeyCombo(params ushort[] vks)
    {
        Console.WriteLine($"[NativeInput] KeyCombo down seq length={vks.Length}");
        foreach (var vk in vks) KeyEvent(vk, false);
        Console.WriteLine("[NativeInput] KeyCombo releasing");
        for (int i = vks.Length - 1; i >= 0; i--) KeyEvent(vks[i], true);
    }

    private static void KeyEvent(ushort vk, bool keyUp)
    {
        var inp = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        Console.WriteLine($"[NativeInput] KeyEvent {(keyUp ? "UP" : "DOWN")} vk=0x{vk:X2}");
        SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
    }

    public static void KeyUnicode(char ch)
    {
        // Send a Unicode char irrespective of current layout.
        var down = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = ch,
                    dwFlags = KEYEVENTF_UNICODE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        var up = down;
        up.U.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
        Console.WriteLine($"[NativeInput] KeyUnicode '{ch}' (U+{(int)ch:X4})");
        SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
    }
}
