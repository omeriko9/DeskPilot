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

namespace DesktopAssist.Engine;

public sealed class Executor
{


    public static void ExecuteAsync()
    {

    }

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
            case "focus_window":
                {
                    string? title = null;
                    if (s.args.TryGetProperty("title", out var tv) && tv.ValueKind == JsonValueKind.String)
                        title = tv.GetString();
                    var ok = WindowFocus.FocusByTitleSubstring(title);
                    Console.WriteLine(ok ? "[Exec][focus_window] focused" : "[Exec][focus_window][Warn] not found");
                    break;
                }
                //case "set_keyboard_layout":
                // default: throw new InvalidOperationException($"Unsupported tool: {s.tool}");
        }
        return Task.CompletedTask;
    }

    private static void Sleep(JsonElement args)
    {
        int secs = args.TryGetProperty("secs", out var v) && v.TryGetInt32(out var i) ? i : 1;
        Console.WriteLine($"[Exec][sleep] secs={secs}");
        Thread.Sleep(TimeSpan.FromSeconds(Math.Clamp(secs, 0, 5)));
    }

    private static void TypeText(JsonElement args)
    {
        if (!args.TryGetProperty("text", out var t) || t.ValueKind != JsonValueKind.String) return;
        string text = t.GetString() ?? "";
        int interval = args.TryGetProperty("interval_ms", out var iv) && iv.TryGetInt32(out var i) ? i : 20;
        Console.WriteLine($"[Exec][write] len={text.Length} interval_ms={interval}");
        Input.SendUnicodeString(text, interval);
    }

    private static void Paste(JsonElement args)
    {
        if (!args.TryGetProperty("text", out var t) || t.ValueKind != JsonValueKind.String) return;
        string text = t.GetString() ?? "";
        Console.WriteLine($"[Exec][paste] len={text.Length}");
        ClipboardUtil.SetText(text);
        Input.KeyChord(new[] { VirtualKey.VK_CONTROL }, new[] { VirtualKey.VK_V });
    }

    private static void Press(JsonElement args)
    {
        // sequential taps
        var keys = ReadKeys(args);
        Console.WriteLine($"[Exec][press] count={keys.Count} keys=[{string.Join(',', keys)}]");
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
        Console.WriteLine($"[Exec][hotkey] mods=[{string.Join(',', mods)}] normals=[{string.Join(',', normals)}]");
        Input.KeyChord(mods.ToArray(), normals.ToArray());
    }

    private static void Launch(JsonElement args)
    {
        // Deterministic: Win+R -> type command -> Enter
        if (!args.TryGetProperty("command", out var c) || c.ValueKind != JsonValueKind.String) return;
        string command = c.GetString() ?? "";
        Console.WriteLine($"[Exec][launch] command={command}");
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
            Console.WriteLine("[Exec][mouse][Reject] Provide integer x,y (image pixels).");
            return;
        }

        var iw = ScreenSnapshotInfo.LastImageWidth;
        var ih = ScreenSnapshotInfo.LastImageHeight;
        if (iw <= 0 || ih <= 0) { Console.WriteLine("[Exec][mouse][Reject] No image size in session."); return; }
        if (xImg < 0 || yImg < 0 || xImg >= iw || yImg >= ih)
        {
            Console.WriteLine($"[Exec][mouse][Reject] Out of bounds: ({xImg},{yImg}) not in [0..{iw - 1}]x[0..{ih - 1}]");
            return;
        }

        var (sx, sy) = ScreenSnapshotInfo.MapFromImagePx(xImg, yImg);

        string button = args.TryGetProperty("button", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()!.ToLowerInvariant() : "left";
        int clicks = args.TryGetProperty("clicks", out var cv) && cv.TryGetInt32(out var ci) ? Math.Clamp(ci, 1, 4) : 1;
        int interval = args.TryGetProperty("interval_ms", out var iv) && iv.TryGetInt32(out var ii) ? Math.Clamp(ii, 10, 1000) : 120;
        string? action = args.TryGetProperty("action", out var av) && av.ValueKind == JsonValueKind.String ? av.GetString()!.ToLowerInvariant() : null;
        bool moveOnly = (action == "move");

        Console.WriteLine($"[Exec][mouse] img=({xImg},{yImg}) -> screen=({sx},{sy}) btn={button} clicks={clicks} moveOnly={moveOnly}");

        Native.SetCursorPos(sx, sy);
        Thread.Sleep(25);
        if (Native.GetCursorPos(out var p) && (Math.Abs(p.X - sx) > 2 || Math.Abs(p.Y - sy) > 2))
        {
            Console.WriteLine($"[Exec][mouse][Adjust] actual=({p.X},{p.Y}) -> retry ({sx},{sy})");
            Native.SetCursorPos(sx, sy);
            Thread.Sleep(25);
        }

        if (!moveOnly)
        {
            for (int i = 0; i < clicks; i++) { Input.MouseClick(button); Thread.Sleep(interval); }
        }
    }


    private static void MouseAt(int x, int y, JsonElement args)
    {
        string button = args.TryGetProperty("button", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()!.ToLowerInvariant() : "left";
        int clicks = args.TryGetProperty("clicks", out var cv) && cv.TryGetInt32(out var ci) ? Math.Clamp(ci, 1, 4) : 1;
        int interval = args.TryGetProperty("interval_ms", out var iv) && iv.TryGetInt32(out var ii) ? Math.Clamp(ii, 10, 1000) : 120;
        string? action = args.TryGetProperty("action", out var av) && av.ValueKind == JsonValueKind.String ? av.GetString()!.ToLowerInvariant() : null;
        bool moveOnly = (action == "move");

        Console.WriteLine($"[Exec][mouse] map-> x={x} y={y} btn={button} clicks={clicks} moveOnly={moveOnly}");
        Native.SetCursorPos(x, y);
        Thread.Sleep(25);

        if (Native.GetCursorPos(out var p))
        {
            if (Math.Abs(p.X - x) > 2 || Math.Abs(p.Y - y) > 2)
            {
                Console.WriteLine($"[Exec][mouse][Adjust] actual=({p.X},{p.Y}) -> retry ({x},{y})");
                Native.SetCursorPos(x, y);
                Thread.Sleep(25);
            }
        }

        if (!moveOnly)
        {
            for (int i = 0; i < clicks; i++)
            {
                Input.MouseClick(button);
                Thread.Sleep(interval);
            }
        }
    }



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
