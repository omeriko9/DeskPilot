using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DesktopAssist.Util;

/// <summary>
/// Provides snapshot information about the currently focused UI Automation element.
/// Designed to give the LLM minimal, low-risk awareness of focus without a full hierarchy.
/// All failures are swallowed and represented with null/empty fields to avoid throwing in the hot path.
/// </summary>
internal static class FocusInfoProvider
{
    public static FocusInfo Capture()
    {
        try
        {
            var info = new FocusInfo();
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero)
                return FocusInfo.Empty("no_foreground_window");

            uint tid = NativeMethods.GetWindowThreadProcessId(fg, out uint pid);
            info.ProcessId = (int)pid;
            info.NativeWindowHandle = fg.ToInt32();
            info.MainWindowTitle = SafeGetWindowText(fg);
            info.ProcessName = TryGetProcessName((int)pid);

            // Get GUI thread info to find focused handle
            var gti = new GUITHREADINFO();
            gti.cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>();
            if (NativeMethods.GetGUIThreadInfo(tid, ref gti))
            {
                IntPtr focusHwnd = gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus : fg;
                info.Ok = true;
                info.HasKeyboardFocus = gti.hwndFocus != IntPtr.Zero;
                info.ClassName = SafeGetClassName(focusHwnd);
                info.ControlType = info.ClassName; // placeholder mapping
                info.Name = SafeGetWindowText(focusHwnd);
                var rect = new RECT();
                if (NativeMethods.GetWindowRect(focusHwnd, out rect))
                {
                    info.BoundingRectangle = new FocusRect
                    {
                        Left = rect.Left,
                        Top = rect.Top,
                        Width = rect.Right - rect.Left,
                        Height = rect.Bottom - rect.Top
                    };
                }
            }
            else
            {
                info.Error = "gui_thread_info_failed";
            }

            return info;
        }
        catch (Exception ex)
        {
            return FocusInfo.Empty(ex.GetType().Name);
        }
    }

    private static string? TryGetProcessName(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.ProcessName; } catch { return null; }
    }

    private static string? SafeGetWindowText(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return null;
        var sb = new StringBuilder(512);
        int len = NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : null;
    }

    private static string? SafeGetClassName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return null;
        var sb = new StringBuilder(256);
        int len = NativeMethods.GetClassName(hWnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : null;
    }
}

internal sealed class FocusInfo
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? ControlType { get; set; }
    public string? Name { get; set; }
    public string? AutomationId { get; set; }
    public string? ClassName { get; set; }
    public string? FrameworkId { get; set; }
    public int ProcessId { get; set; }
    public string? ProcessName { get; set; }
    public string? MainWindowTitle { get; set; }
    public int NativeWindowHandle { get; set; }
    public bool? HasKeyboardFocus { get; set; }
    public FocusRect? BoundingRectangle { get; set; }
    public FocusSelection? Selection { get; set; }

    public static FocusInfo Empty(string? error = null) => new FocusInfo { Ok = false, Error = error };
}

internal sealed class FocusRect
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

internal sealed class FocusSelection { public string[]? SelectedItems { get; set; } public string? Value { get; set; } }

// Win32 interop helpers
[StructLayout(LayoutKind.Sequential)]
internal struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

[StructLayout(LayoutKind.Sequential)]
internal struct GUITHREADINFO
{
    public uint cbSize;
    public uint flags;
    public IntPtr hwndActive;
    public IntPtr hwndFocus;
    public IntPtr hwndCapture;
    public IntPtr hwndMenuOwner;
    public IntPtr hwndMoveSize;
    public IntPtr hwndCaret;
    public RECT rcCaret;
}

internal static class NativeMethods
{
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}