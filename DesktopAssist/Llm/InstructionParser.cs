using System;
using System.Text.Json;
using DesktopAssist.Llm.Models;
using DesktopAssist.Util;

namespace DesktopAssist.Llm;

public static class InstructionParser
{
    public static InstructionResponse? TryParse(string raw, bool allowSubstringExtraction = true)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();

        try
        {
            var parsed = JsonSerializer.Deserialize<InstructionResponse>(raw, JsonOptions());
            if (parsed != null) return parsed;
        }
        catch
        {
            if (!allowSubstringExtraction) return null;
            var extracted = JsonExtraction.ExtractJsonBlock(raw);
            if (extracted == null) return null;
            try
            {
                var parsed = JsonSerializer.Deserialize<InstructionResponse>(extracted, JsonOptions());
                if (parsed != null) return parsed;
            }
            catch
            {
                // fall through to manual fallback
            }
        }

        // Manual fallback: attempt to parse a structure where steps are an array of strings
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("steps", out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array)
            {
                var resp = new InstructionResponse();
                // done field
                if (root.TryGetProperty("done", out var doneEl) && doneEl.ValueKind == JsonValueKind.String)
                {
                    // InstructionResponse isn't a record; manually assign
                    var doneVal = doneEl.GetString();
                    if (!string.IsNullOrEmpty(doneVal))
                    {
                        // We must reflect the property via a temporary subclass or rebuild; simpler: new instance copy
                        resp = new InstructionResponse { Steps = resp.Steps, Done = doneVal };
                    }
                }

                foreach (var item in stepsEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var text = item.GetString() ?? string.Empty;
                        var tuple = InstructionParserHelpers.HeuristicFromText(text);
                        var fn = tuple.fn;
                        var paramObj = tuple.parameters;
                        resp.Steps.Add(new InstructionStep
                        {
                            Function = fn,
                            Parameters = paramObj,
                            Justification = text
                        });
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        // Try mapping into InstructionStep manually to allow alias normalization
                        string fn = item.TryGetProperty("function", out var fEl) && fEl.ValueKind == JsonValueKind.String ? fEl.GetString() ?? string.Empty : string.Empty;
                        fn = InstructionParserHelpers.NormalizeFunction(fn);
                        Dictionary<string, object>? parameters = null;
                        if (item.TryGetProperty("parameters", out var pEl) && pEl.ValueKind == JsonValueKind.Object)
                        {
                            parameters = new Dictionary<string, object>();
                            foreach (var prop in pEl.EnumerateObject())
                            {
                                parameters[prop.Name] = InstructionParserHelpers.JsonElementToObject(prop.Value);
                            }
                        }
                        var just = item.TryGetProperty("human_readable_justification", out var jEl) && jEl.ValueKind == JsonValueKind.String ? jEl.GetString() : null;
                        resp.Steps.Add(new InstructionStep
                        {
                            Function = fn,
                            Parameters = parameters,
                            Justification = just
                        });
                    }
                }
                if (resp.Steps.Count > 0) return resp;
            }
        }
        catch { }
        return null;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

internal static class InstructionParserHelpers
{
    internal static (string fn, Dictionary<string, object>? parameters) HeuristicFromText(string text)
    {
        text = text.Trim();
        // Very naive mapping: detect phrases like "click", "press", "type"; extend as needed.
        if (text.StartsWith("click", StringComparison.OrdinalIgnoreCase))
        {
            // No coordinates known; return a no-op placeholder requiring LLM fix.
            return ("click", new Dictionary<string, object>());
        }
        if (text.StartsWith("press", StringComparison.OrdinalIgnoreCase))
        {
            // Attempt to extract key after 'press'
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return ("press", new Dictionary<string, object>{{"key", parts[1].Trim().ToLowerInvariant()}});
            }
            return ("press", new Dictionary<string, object>());
        }
        if (text.StartsWith("type", StringComparison.OrdinalIgnoreCase) || text.StartsWith("enter", StringComparison.OrdinalIgnoreCase))
        {
            var idx = text.IndexOf(' ');
            if (idx > 0)
            {
                var remainder = text.Substring(idx + 1).Trim(' ', '\"');
                return ("write", new Dictionary<string, object>{{"text", remainder}});
            }
            return ("write", new Dictionary<string, object>());
        }
        return ("sleep", new Dictionary<string, object>{{"secs", 0.5}});
    }

    internal static string NormalizeFunction(string fn)
    {
        if (string.Equals(fn, "type", StringComparison.OrdinalIgnoreCase)) return "write";
        return fn;
    }

    internal static object JsonElementToObject(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? string.Empty,
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.TryGetDouble(out var d) ? d : (object)0,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => JsonArrayToList(el),
            JsonValueKind.Object => JsonObjectToDict(el),
            _ => string.Empty
        };
    }

    private static List<object> JsonArrayToList(JsonElement el)
    {
        var list = new List<object>();
        foreach (var item in el.EnumerateArray()) list.Add(JsonElementToObject(item));
        return list;
    }

    private static Dictionary<string, object> JsonObjectToDict(JsonElement el)
    {
        var dict = new Dictionary<string, object>();
        foreach (var prop in el.EnumerateObject()) dict[prop.Name] = JsonElementToObject(prop.Value);
        return dict;
    }
}
