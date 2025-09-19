using System;
using System.Collections.Generic;
using System.Threading;
using DesktopAssist.Automation.Input;
using DesktopAssist.Llm.Models;

namespace DesktopAssist.Automation;

public sealed class ActionExecutor
{
    private readonly (int width, int height) _screenSize;
    private readonly DesktopAssist.Settings.AppSettings _settings;

    // Central list of supported automation function names. Update here if adding new actions.
    // NOTE: "type" is accepted as an alias for "write" at dispatch time but canonical name is "write".
    public static readonly string[] SupportedFunctions = new[] { "sleep", "moveto", "click", "write", "press", "hotkey", "type" };

    public ActionExecutor((int width, int height) screenSize, DesktopAssist.Settings.AppSettings settings)
    {
        _screenSize = screenSize;
        _settings = settings;
    }

    public bool Execute(InstructionStep step, CancellationToken token)
    {
        try
        {
            if (token.IsCancellationRequested) return false;
            var fn = step.Function.ToLowerInvariant();
            if (fn == "type") fn = "write"; // alias
            Console.WriteLine($"[Action] Dispatch {fn} (orig='{step.Function}') params={(step.Parameters?.Count ?? 0)} justification='{step.Justification}'");
            return fn switch
            {
                "sleep" => Sleep(step),
                "moveto" => MoveTo(step),
                "click" => Click(step),
                "write" => Write(step),
                "press" => Press(step),
                "hotkey" => Hotkey(step),
                _ => throw new InvalidOperationException($"Unsupported function: {step.Function}")
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Action failed: {ex.Message}");
            return false;
        }
    }

    private bool Sleep(InstructionStep step)
    {
        if (step.Parameters == null) return false;
        if (!step.Parameters.TryGetValue("secs", out var val)) return false;
        if (double.TryParse(val.ToString(), out var secs))
        {
            var ms = (int)(secs * 1000);
            Console.WriteLine($"[Action] Sleep {secs:F2}s ({ms} ms)");
            Thread.Sleep(ms);
            return true;
        }
        return false;
    }

    private bool MoveTo(InstructionStep step)
    {
    if (!TryGetXY(step.Parameters, out var x, out var y)) return false;
    Console.WriteLine($"[Action] MoveTo ({x},{y})");
    NativeInput.MoveCursor(x, y);
        return true;
    }

    private bool Click(InstructionStep step)
    {
    if (!TryGetXY(step.Parameters, out var x, out var y)) return false;
    var button = GetString(step.Parameters, "button")?.ToLowerInvariant() ?? "left";
    var clicks = GetInt(step.Parameters, "clicks") ?? 1;
    var interval = GetInt(step.Parameters, "interval_ms") ?? 120;
    Console.WriteLine($"[Action] Click button={button} clicks={clicks} interval={interval}ms at ({x},{y})");
        if (button == "right") NativeInput.RightClick(x, y);
        else NativeInput.LeftClick(x, y, clicks, interval);
        return true;
    }

    private bool Write(InstructionStep step)
    {
        if (step.Parameters == null) return false;
        if (_settings.ForceEnglishLayoutForTyping)
        {
            NativeInput.EnsureEnglishLayout(_settings.EnglishLayoutHex, _settings.VerboseNetworkLogging);
        }
        var text = GetString(step.Parameters, "text") ?? GetString(step.Parameters, "string");
        if (string.IsNullOrEmpty(text)) return false;
        var interval = GetInt(step.Parameters, "interval_ms") ?? 50;
        Console.WriteLine($"[Action] Write '{text}' interval={interval}ms");
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                ushort vk = (ushort)char.ToUpperInvariant(ch);
                Console.WriteLine($"[Action][Key] '{ch}' vk=0x{vk:X2}");
                NativeInput.KeyTap(vk);
            }
            else if (ch == ' ') { Console.WriteLine("[Action][Key] space vk=0x20"); NativeInput.KeyTap(0x20); }
            else
            {
                Console.WriteLine($"[Action][Key][Skip] unsupported char: {ch}");
            }
            Thread.Sleep(interval);
        }
        return true;
    }

    private bool Press(InstructionStep step)
    {
        if (step.Parameters == null)
        {
            Console.WriteLine("[Action][Press][Fail] parameters null");
            return false;
        }

        // Accept 'keys': array OR delimited string; fallback to single 'key'
        if (step.Parameters.TryGetValue("keys", out var keysObj))
        {
            var resolved = new List<ushort>();
            Console.WriteLine($"[Action][Press] raw keys object type={keysObj?.GetType().Name}");
            if (keysObj is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in je.EnumerateArray())
                {
                    var ks = item.ToString();
                    if (TryMapVirtualKey(ks, out var vk))
                    {
                        resolved.Add(vk);
                        Console.WriteLine($"[Action][Press] mapped '{ks}' -> 0x{vk:X2}");
                    }
                    else Console.WriteLine($"[Action][Press][Warn] unrecognized key '{ks}'");
                }
            }
            else if (keysObj is IEnumerable<object> objList)
            {
                foreach (var k in objList)
                {
                    var ks = k?.ToString();
                    if (TryMapVirtualKey(ks, out var vk))
                    {
                        resolved.Add(vk);
                        Console.WriteLine($"[Action][Press] mapped '{ks}' -> 0x{vk:X2}");
                    }
                    else Console.WriteLine($"[Action][Press][Warn] unrecognized key '{ks}'");
                }
            }
            else if (keysObj is string keyString)
            {
                var parts = keyString.Split('+', ' ', ',', ';');
                foreach (var part in parts)
                {
                    var p = part.Trim();
                    if (string.IsNullOrEmpty(p)) continue;
                    if (TryMapVirtualKey(p, out var vk))
                    {
                        resolved.Add(vk);
                        Console.WriteLine($"[Action][Press] mapped '{p}' -> 0x{vk:X2}");
                    }
                    else Console.WriteLine($"[Action][Press][Warn] unrecognized token '{p}'");
                }
            }
            if (resolved.Count > 0)
            {
                Console.WriteLine($"[Action] Press sequence size={resolved.Count}");
                foreach (var vk in resolved) NativeInput.KeyTap(vk);
                return true;
            }
            Console.WriteLine("[Action][Press][Fail] no valid keys mapped from 'keys'");
            return false;
        }
        else if (step.Parameters.TryGetValue("key", out var keyObj))
        {
            var ks = keyObj?.ToString();
            Console.WriteLine($"[Action][Press] single key candidate='{ks}'");
            if (TryMapVirtualKey(ks, out var vk))
            {
                Console.WriteLine($"[Action] Press key={ks} vk=0x{vk:X2}");
                NativeInput.KeyTap(vk);
                return true;
            }
            Console.WriteLine($"[Action][Press][Fail] could not map key='{ks}'");
            return false;
        }
        Console.WriteLine("[Action][Press][Fail] neither 'keys' nor 'key' present. Raw params:");
        foreach (var kv in step.Parameters)
            Console.WriteLine($"  param {kv.Key}={kv.Value}");
        return false;
    }

    private bool Hotkey(InstructionStep step)
    {
        if (step.Parameters == null)
        {
            Console.WriteLine("[Action][Hotkey][Fail] parameters null");
            return false;
        }

        var collected = new List<ushort>();
        var sourceDescription = string.Empty;

        // 1. Preferred: explicit array in key 'keys'
        if (step.Parameters.TryGetValue("keys", out var keysObj))
        {
            sourceDescription = "keys-array";
            if (keysObj is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in je.EnumerateArray())
                {
                    var ks = item.ToString();
                    if (TryMapVirtualKey(ks, out var vk)) { collected.Add(vk); Console.WriteLine($"[Action][Hotkey] mapped '{ks}' -> 0x{vk:X2}"); }
                    else Console.WriteLine($"[Action][Hotkey][Warn] unrecognized key '{ks}'");
                }
            }
            else if (keysObj is IEnumerable<object> objList)
            {
                foreach (var k in objList)
                {
                    var ks = k?.ToString();
                    if (TryMapVirtualKey(ks, out var vk)) { collected.Add(vk); Console.WriteLine($"[Action][Hotkey] mapped '{ks}' -> 0x{vk:X2}"); }
                    else Console.WriteLine($"[Action][Hotkey][Warn] unrecognized key '{ks}'");
                }
            }
            else if (keysObj is string ksStr)
            {
                // Could be a single string like "win+r" or "ctrl+shift+esc"
                var split = ksStr.Split('+', ' ', ',', ';');
                foreach (var part in split)
                {
                    var p = part.Trim();
                    if (string.IsNullOrEmpty(p)) continue;
                    if (TryMapVirtualKey(p, out var vk)) { collected.Add(vk); Console.WriteLine($"[Action][Hotkey] mapped '{p}' -> 0x{vk:X2}"); }
                    else Console.WriteLine($"[Action][Hotkey][Warn] unrecognized part '{p}'");
                }
            }
        }

        // 2. Fallback: treat each parameter value as a key (previous behavior)
        if (collected.Count == 0)
        {
            sourceDescription = "param-values";
            foreach (var kv in step.Parameters)
            {
                var val = kv.Value?.ToString();
                if (TryMapVirtualKey(val, out var vk)) { collected.Add(vk); Console.WriteLine($"[Action][Hotkey] mapped '{val}' -> 0x{vk:X2}"); }
            }
        }

        if (collected.Count == 0)
        {
            Console.WriteLine("[Action][Hotkey][Fail] no valid keys resolved; raw params:");
            foreach (var kv in step.Parameters)
                Console.WriteLine($"  param {kv.Key}={kv.Value}");
            return false;
        }

        Console.WriteLine($"[Action] Hotkey combo size={collected.Count} source={sourceDescription}");
        NativeInput.KeyCombo(collected.ToArray());
        return true;
    }

    private bool TryGetXY(Dictionary<string, object>? dict, out int x, out int y)
    {
        x = y = 0;
        if (dict == null) return false;
        if (!dict.TryGetValue("x", out var xv) || !dict.TryGetValue("y", out var yv)) return false;
        if (!int.TryParse(xv.ToString(), out x) || !int.TryParse(yv.ToString(), out y)) return false;
        if (x < 0 || x >= _screenSize.width || y < 0 || y >= _screenSize.height) return false;
        return true;
    }

    private static string? GetString(Dictionary<string, object>? dict, string key)
        => dict != null && dict.TryGetValue(key, out var v) ? v?.ToString() : null;

    private static int? GetInt(Dictionary<string, object>? dict, string key)
        => dict != null && dict.TryGetValue(key, out var v) && int.TryParse(v.ToString(), out var i) ? i : null;

    private bool TryMapVirtualKey(string? key, out ushort vk)
    {
        vk = 0;
        if (string.IsNullOrWhiteSpace(key)) return false;
        key = key.ToLowerInvariant();
        return key switch
        {
            "enter" => (vk = 0x0D) > 0,
            "esc" or "escape" => (vk = 0x1B) > 0,
            "tab" => (vk = 0x09) > 0,
            "space" => (vk = 0x20) > 0,
            "win" or "meta" or "command" => (vk = 0x5B) > 0,
            "ctrl" or "control" => (vk = 0x11) > 0,
            "shift" => (vk = 0x10) > 0,
            "alt" => (vk = 0x12) > 0,
            "f5" => (vk = 0x74) > 0,
            _ => TryAlphaNumeric(key, out vk)
        };
    }

    private bool TryAlphaNumeric(string key, out ushort vk)
    {
        vk = 0;
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (char.IsLetterOrDigit(c)) { vk = (ushort)c; return true; }
        }
        return false;
    }
}
