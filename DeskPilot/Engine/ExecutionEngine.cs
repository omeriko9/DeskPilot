using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using DesktopAssist.Automation;
using DesktopAssist.Llm;
using DesktopAssist.Llm.Models;
using DesktopAssist.Screen;
using DesktopAssist.Settings;
using DesktopAssist.Automation.Input;
using DesktopAssist.Util;
using System.Runtime.InteropServices;

namespace DesktopAssist.Engine;

public sealed class Executor
{
    // Executes a single automation step emitted by the LLM plan

    public static Task ExecuteAsync(Step s)
    {
        var t = s.tool.Trim().ToLowerInvariant();
        switch (t)
        {
            case "sleep": Sleep(s.args); break;
            case "press": Press(s.args); break;
            case "hotkey": Hotkey(s.args); break;
            case "write":
            case "type": TypeText(s.args); break;
            case "paste": Paste(s.args); break;
            case "launch": Launch(s.args); break;
            case "mouse": Mouse(s.args); break;
            case "diag_mouse": DiagMouse(s.args); break;
            case "focus_window":
                {
                    string? title = null;
                    if (s.args.TryGetProperty("title", out var tv) && tv.ValueKind == JsonValueKind.String)
                        title = tv.GetString();
                    var ok = WindowFocus.FocusByTitleSubstring(title);
                    if (ok) Log.Info("Exec.focus_window", "focused"); else Log.Warn("Exec.focus_window", "not found");
                    break;
                }
                //case "set_keyboard_layout":
                // default: throw new InvalidOperationException($"Unsupported tool: {s.tool}");
        }

        Thread.Sleep(500);

        return Task.CompletedTask;
    }

    private static void DiagMouse(JsonElement args)
    {
        try
        {
            Log.Info("DiagMouse", "begin");
            bool remote = Native.GetSystemMetrics(Native.SM_REMOTESESSION) != 0;
            Log.Info("DiagMouse", $"remote_session={remote}");
            ScreenSnapshotInfo.RefreshVirtualMetrics();
            Log.Info("DiagMouse", $"virtual=({ScreenSnapshotInfo.VirtualLeft},{ScreenSnapshotInfo.VirtualTop},{ScreenSnapshotInfo.VirtualWidth}x{ScreenSnapshotInfo.VirtualHeight}) lastImage={ScreenSnapshotInfo.LastImageWidth}x{ScreenSnapshotInfo.LastImageHeight}");

            int baseX = args.TryGetProperty("x", out var xv) && xv.TryGetInt32(out var xi) ? xi : 200;
            int baseY = args.TryGetProperty("y", out var yv) && yv.TryGetInt32(out var yi) ? yi : 200;
            string mode = args.TryGetProperty("coord_type", out var ct) && ct.ValueKind == JsonValueKind.String ? ct.GetString()!.ToLowerInvariant() : "screen";

            // Build a small square path
            var pts = new List<(int x,int y)> { (baseX,baseY), (baseX+120,baseY), (baseX+120,baseY+120), (baseX,baseY+120), (baseX,baseY) };
            int step = 0;
            foreach (var (x,y) in pts)
            {
                int sx = x, sy = y;
                if (mode != "screen")
                {
                    var mapped = ScreenSnapshotInfo.MapFromImagePx(x,y);
                    sx = mapped.x; sy = mapped.y;
                }
                bool ok = Native.SetCursorPos(sx, sy);
                int err = ok ? 0 : Marshal.GetLastWin32Error();
                Thread.Sleep(30);
                if (Native.GetCursorPos(out var cur))
                {
                    Log.Info("DiagMouse.step", $"i={step} target=({sx},{sy}) setOk={ok} err={err} actual=({cur.X},{cur.Y}) delta=({cur.X-sx},{cur.Y-sy})");
                }
                else
                {
                    Log.Warn("DiagMouse.step", $"i={step} GetCursorPos failed after setOk={ok} err={err}");
                }
                step++;
            }
            Log.Info("DiagMouse", "end");
        }
        catch (Exception ex)
        {
            Log.Error("DiagMouse", ex, "exception");
        }
    }

    private static void Sleep(JsonElement args)
    {
        double secs = 1.0;
        if (args.TryGetProperty("secs", out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) secs = d;
            else if (v.TryGetInt32(out var i)) secs = i;
        }
        secs = Math.Clamp(secs, 0, 5);
        Log.Info("Exec.sleep", $"secs={secs:0.###}");
        int ms = (int)Math.Round(secs * 1000);
        Thread.Sleep(ms);
    }

    private static void TypeText(JsonElement args)
    {
        if (!args.TryGetProperty("text", out var t) || t.ValueKind != JsonValueKind.String) return;
        string text = t.GetString() ?? "";
        int interval = args.TryGetProperty("interval_ms", out var iv) && iv.TryGetInt32(out var i) ? i : 20;
    Log.Info("Exec.write", $"len={text.Length} interval_ms={interval}");
        Input.SendUnicodeString(text, interval);
    }

    private static void Paste(JsonElement args)
    {
        if (!args.TryGetProperty("text", out var t) || t.ValueKind != JsonValueKind.String) return;
        string text = t.GetString() ?? "";
        bool restore = args.TryGetProperty("restore_clipboard", out var rv) && rv.ValueKind == JsonValueKind.True;
        int delayMs = args.TryGetProperty("pre_paste_delay_ms", out var dv) && dv.TryGetInt32(out var dVal) ? Math.Clamp(dVal, 0, 500) : 60;
        Log.Info("Exec.paste", $"len={text.Length} restore={restore} delayMs={delayMs}");
        string? prev = ClipboardUtil.SetText(text, retries:5, retryDelayMs:55, getPrevious:restore);
        Thread.Sleep(delayMs); // ensure target app observes clipboard update
        Input.KeyChord(new[] { VirtualKey.VK_CONTROL }, new[] { VirtualKey.VK_V });
        if (restore) {
            // restore after a short delay so paste occurs with new text
            ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(120); ClipboardUtil.RestoreText(prev); });
        }
    }

    private static void Press(JsonElement args)
    {
        // sequential taps
        var keys = ReadKeys(args);
    Log.Info("Exec.press", $"count={keys.Count} keys=[{string.Join(',', keys)}]");
        foreach (var k in keys)
        {
            Input.KeyTap(k);
            Thread.Sleep(50);
        }
    }

    private static void Hotkey(JsonElement args)
    {
        // chord: hold modifiers, press normals
        var keys = ReadKeys(args);
        // Split into modifiers and normals
        var (mods, normals) = KeySplit(keys);
    Log.Info("Exec.hotkey", $"mods=[{string.Join(',', mods)}] normals=[{string.Join(',', normals)}]");
        Input.KeyChord(mods.ToArray(), normals.ToArray());
    }

    private static void Launch(JsonElement args)
    {
        // Deterministic: Win+R -> type command -> Enter
        if (!args.TryGetProperty("command", out var c) || c.ValueKind != JsonValueKind.String) return;
        string command = c.GetString() ?? "";
    Log.Info("Exec.launch", $"command={command}");
        Input.KeyChord(new[] { VirtualKey.VK_LWIN }, new[] { VirtualKey.VK_R }); // Win+R
        Thread.Sleep(150);
        Input.SendUnicodeString(command, 8);
        Input.KeyTap(VirtualKey.VK_RETURN);
    }

    private static void Mouse(JsonElement args)
    {
        // Expect image-space integers like MS Paint.
        if (!(args.TryGetProperty("x", out var xj) && args.TryGetProperty("y", out var yj)
              && xj.TryGetInt32(out var xImg) && yj.TryGetInt32(out var yImg)))
        {
            Log.Warn("Exec.mouse", "Reject: Provide integer x,y (image pixels)");
            return;
        }

        var iw = ScreenSnapshotInfo.LastImageWidth;
        var ih = ScreenSnapshotInfo.LastImageHeight;
        bool haveImage = iw > 0 && ih > 0;

        // If no image captured in this session, we will treat coordinates as screen space unless coord_type forces image.
        if (haveImage)
        {
            if (xImg < 0 || yImg < 0 || xImg >= iw || yImg >= ih)
            {
                Log.Warn("Exec.mouse", $"Reject: OOB ({xImg},{yImg}) not in [0..{iw - 1}]x[0..{ih - 1}] image_mode");
                return;
            }
        }

        string coordType = args.TryGetProperty("coord_type", out var ctVal) && ctVal.ValueKind == JsonValueKind.String ? ctVal.GetString()!.ToLowerInvariant() : "image";
        int sx, sy;
        if (coordType == "screen" || !haveImage)
        {
            // Interpret provided x,y directly as screen pixels (debug path)
            sx = xImg; sy = yImg;
            if (!haveImage && coordType != "screen") coordType = "screen_auto";
        }
        else
        {
            var mapped = ScreenSnapshotInfo.MapFromImagePx(xImg, yImg);
            sx = mapped.x; sy = mapped.y;
        }

        string button = args.TryGetProperty("button", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()!.ToLowerInvariant() : "left";
        int clicks = args.TryGetProperty("clicks", out var cv) && cv.TryGetInt32(out var ci) ? Math.Clamp(ci, 1, 4) : 1;
        int interval = args.TryGetProperty("interval_ms", out var iv) && iv.TryGetInt32(out var ii) ? Math.Clamp(ii, 10, 1000) : 120;
        string? action = args.TryGetProperty("action", out var av) && av.ValueKind == JsonValueKind.String ? av.GetString()!.ToLowerInvariant() : null;
        bool moveOnly = (action == "move");

        bool focusUnder = args.TryGetProperty("focus_under_cursor", out var fval) && fval.ValueKind == JsonValueKind.True;
    Log.Info("Exec.mouse", $"mode={coordType} src=({xImg},{yImg}) -> screen=({sx},{sy}) btn={button} clicks={clicks} moveOnly={moveOnly} focusUnder={focusUnder}");

        // Record pre-move position
    Native.POINT before;
    bool haveBefore = Native.GetCursorPos(out before);
        if (haveBefore) Log.Info("Exec.mouse.pos.before", $"({before.X},{before.Y})");

        bool setOk = Native.SetCursorPos(sx, sy);
        if (!setOk)
        {
            int err = Marshal.GetLastWin32Error();
            Log.Warn("Exec.mouse.setcursor", $"SetCursorPos failed err={err} target=({sx},{sy})");
        }
        Thread.Sleep(20);
        if (!Native.GetCursorPos(out var after1))
        {
            Log.Warn("Exec.mouse", "GetCursorPos failed after SetCursorPos");
        }
        else
        {
            int dx1 = after1.X - sx, dy1 = after1.Y - sy;
            if (Math.Abs(dx1) > 2 || Math.Abs(dy1) > 2)
            {
                Log.Warn("Exec.mouse.pos.mismatch", $"afterSetCursorPos=({after1.X},{after1.Y}) target=({sx},{sy}) retrying");
                // Retry once more
                Native.SetCursorPos(sx, sy);
                Thread.Sleep(25);
                if (Native.GetCursorPos(out var after2))
                {
                    int dx2 = after2.X - sx, dy2 = after2.Y - sy;
                    if (Math.Abs(dx2) > 2 || Math.Abs(dy2) > 2)
                    {
                        Log.Warn("Exec.mouse.pos.fallback", $"stillMismatch=({after2.X},{after2.Y}) -> attempting absolute SendInput move");
                        // Fallback: absolute normalized move via SendInput
                        TrySendInputAbsoluteMove(sx, sy);
                        Thread.Sleep(30);
                        if (Native.GetCursorPos(out var after3))
                        {
                            int dx3 = after3.X - sx, dy3 = after3.Y - sy;
                            if (Math.Abs(dx3) > 2 || Math.Abs(dy3) > 2)
                                Log.Warn("Exec.mouse.pos.fail", $"postFallback=({after3.X},{after3.Y}) still off target");
                            else
                                Log.Info("Exec.mouse.pos.fallback_ok", $"({after3.X},{after3.Y})");
                        }
                    }
                    else
                    {
                        Log.Info("Exec.mouse.pos.retry_ok", $"({after2.X},{after2.Y})");
                    }
                }
            }
            else
            {
                Log.Info("Exec.mouse.pos.ok", $"({after1.X},{after1.Y})");
            }
        }

        if (!moveOnly)
        {
            if (focusUnder)
            {
                TryFocusWindowAt(sx, sy);
                Thread.Sleep(30);
            }
            for (int i = 0; i < clicks; i++)
            {
                Input.MouseClick(button);
                Thread.Sleep(interval);
            }
        }
    }

    // Fallback absolute move using SendInput normalized coords
    private static void TrySendInputAbsoluteMove(int x, int y)
    {
        try
        {
            int vx = ScreenSnapshotInfo.VirtualLeft;
            int vy = ScreenSnapshotInfo.VirtualTop;
            int vw = ScreenSnapshotInfo.VirtualWidth;
            int vh = ScreenSnapshotInfo.VirtualHeight;
            if (vw <= 0 || vh <= 0)
            {
                Log.Warn("Exec.mouse.absmove", "No virtual metrics");
                return;
            }
            // Normalize to 0..65535 range (per docs). Use virtual metrics to account for multi-monitor.
            double nx = (double)(x - vx) * 65535.0 / Math.Max(1, vw - 1);
            double ny = (double)(y - vy) * 65535.0 / Math.Max(1, vh - 1);
            var inp = new INPUT
            {
                type = Native.INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = (int)Math.Round(nx),
                        dy = (int)Math.Round(ny),
                        mouseData = 0,
                        dwFlags = Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            // Use reflection of internal Dispatch? Simpler: replicate minimal SendInput here.
            uint sent = Native.SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
            if (sent == 0)
            {
                int err = Marshal.GetLastWin32Error();
                Log.Warn("Exec.mouse.absmove", $"SendInput failed err={err}");
            }
            else
            {
                Log.Info("Exec.mouse.absmove", $"sent target=({x},{y}) norm=({nx:0},{ny:0})");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Exec.mouse.absmove", ex, "exception");
        }
    }

    private static void TryFocusWindowAt(int sx, int sy)
    {
        try
        {
            // Basic P/Invoke locally declared to avoid adding to Native unless needed widely
            IntPtr hWnd = WindowFromPointLocal(sx, sy);
            if (hWnd == IntPtr.Zero) { Log.Warn("Exec.mouse.focus", "WindowFromPoint returned null"); return; }
            if (!Native.IsWindowVisible(hWnd)) { Log.Warn("Exec.mouse.focus", "target window not visible"); }
            // Attempt to bring to foreground
            if (!Native.SetForegroundWindow(hWnd))
            {
                Log.Warn("Exec.mouse.focus", "SetForegroundWindow failed (may already be foreground or blocked)");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Exec.mouse.focus", ex, "exception");
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(int x, int y);
    private static IntPtr WindowFromPointLocal(int x, int y) => WindowFromPoint(x, y);


    private static List<VirtualKey> ReadKeys(JsonElement args)
    {
        var keys = new List<VirtualKey>();
        if (args.TryGetProperty("key", out var single) && single.ValueKind == JsonValueKind.String)
        {
            if (KeyMap.TryMap(single.GetString() ?? "", out var vk)) keys.Add(vk);
        }
        if (args.TryGetProperty("keys", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String && KeyMap.TryMap(el.GetString() ?? "", out var vk)) keys.Add(vk);
            }
        }
        return keys;
    }

    private static (List<VirtualKey> mods, List<VirtualKey> normals) KeySplit(List<VirtualKey> keys)
    {
        var mods = new List<VirtualKey>();
        var normals = new List<VirtualKey>();
        foreach (var k in keys)
        {
            if (k is VirtualKey.VK_CONTROL or VirtualKey.VK_MENU or VirtualKey.VK_SHIFT or VirtualKey.VK_LWIN or VirtualKey.VK_RWIN)
                mods.Add(k);
            else
                normals.Add(k);
        }
        if (mods.Count == 0 && normals.Count == 1)
        {
            // Treat single key chord as a tap
            Input.KeyTap(normals[0]);
            return (new List<VirtualKey>(), new List<VirtualKey>());
        }
        return (mods, normals);
    }
}
