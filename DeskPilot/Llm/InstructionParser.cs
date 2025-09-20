using System;
using System.Text.Json;
using DesktopAssist.Llm.Models;
using DesktopAssist.Util;

namespace DesktopAssist.Llm;

public static class InstructionParser
{
    public static bool TryParseResponse(string raw, out StepsResponse parsed, out string error)
    {
        parsed = default!;
        error = "";

        // Model was told to output raw JSON only. Still, be robust to stray whitespace/newlines, code fences, or formatting differences.
        var span = raw.AsSpan().Trim();
        var content = "";

        // Normalize possible markdown code fences (```json ... ```)
        string normalized = NormalizeCodeFence(raw);
        var normSpan = normalized.AsSpan().Trim();

        try
        {
            bool looksLikeStepsObject = false;
            if (normSpan.Length > 0 && normSpan[0] == '{')
            {
                // Fast path: attempt to parse and check for a top-level "steps" property
                try
                {
                    using var doc = JsonDocument.Parse(normSpan.ToString(), new JsonDocumentOptions { AllowTrailingCommas = true });
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("steps", out _))
                        looksLikeStepsObject = true;
                }
                catch { /* fall back to wrapper parsing */ }
            }

            if (looksLikeStepsObject)
            {
                content = normSpan.ToString();
            }
            else
            {
                // Fall back to legacy wrapper extraction logic
                using var outer = JsonDocument.Parse(raw, new JsonDocumentOptions { AllowTrailingCommas = true });
                if (outer.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
                {
                    if (output.GetArrayLength() <= 1)
                    {
                        // Some providers might just return string at root OR inside output[0]
                        if (outer.RootElement.TryGetProperty("output_text", out var ot) && ot.ValueKind == JsonValueKind.String)
                            content = ot.GetString();
                        else if (output.GetArrayLength() > 0 && output[0].ValueKind == JsonValueKind.String)
                            content = output[0].GetString();
                        else
                            content = outer.RootElement.GetString();
                    }
                    else
                    {
                        // Original path used: second element -> content[0].text
                        try
                        {
                            var c = output[1].GetProperty("content");
                            if (c.ValueKind == JsonValueKind.Array && c.GetArrayLength() > 0)
                            {
                                var textNode = c[0];
                                if (textNode.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                                    content = t.GetString();
                            }
                        }
                        catch { }
                    }
                }

                // As final fallback, if still empty and normalized looked JSON-like, just use normalized text.
                if (string.IsNullOrWhiteSpace(content) && normSpan.Length > 0 && normSpan[0] == '{')
                    content = normSpan.ToString();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InstructionParser] Extraction error: {ex.Message}\nRaw:{Environment.NewLine}{raw}");
        }


        if (span.Length == 0) { error = "Empty content."; return false; }
        try
        {
            if (string.IsNullOrWhiteSpace(content)) { error = "Inner content empty."; return false; }
            parsed = JsonSerializer.Deserialize<StepsResponse>(content!, JsonHelper.Options) ?? new StepsResponse();
            // Validate schema minimally
            if (parsed.Done is not (null or string or JsonElement)) { error = "Invalid 'done' (must be null/false|string)."; return false; }
            if (parsed.Steps != null)
            {
                foreach (var s in parsed.Steps)
                {
                    if (string.IsNullOrWhiteSpace(s.tool)) { error = "Step missing 'tool'."; return false; }
                    // JsonElement is a struct; check ValueKind instead of null
                    if (s.args.ValueKind != JsonValueKind.Object) { error = "Step missing 'args' object."; return false; }
                    if (string.IsNullOrWhiteSpace(s.human_readable_justification)) { error = "Step missing 'human_readable_justification'."; return false; }
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            error = "JSON parse failed: " + ex.Message;
            return false;
        }
    }
    private static string NormalizeCodeFence(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```")) return trimmed; // no fence

        // Remove opening line
        int firstNl = trimmed.IndexOf('\n');
        if (firstNl < 0) return trimmed; // single-line fence, nothing to do
        var after = trimmed[(firstNl + 1)..];
        // Find last closing fence
        int lastFence = after.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence >= 0)
            after = after.Substring(0, lastFence);
        return after.Trim();
    }
}

