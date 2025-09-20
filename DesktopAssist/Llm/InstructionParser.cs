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

        // Model was told to output raw JSON only. Still, be robust to stray whitespace/newlines.
        var span = raw.AsSpan().Trim();
        var content = "";
        try
        {
            if (raw.StartsWith("{\n  \"steps\":"))
            {
                content = raw;
            }
            else
            {
                var output = JsonSerializer.Deserialize<JsonElement>(raw).GetProperty("output");
                if (output.GetArrayLength() <= 1)
                {
                    content = JsonSerializer.Deserialize<JsonElement>(raw).GetString();
                }
                else
                {
                    content = output[1].GetProperty("content")[0].GetProperty("text").GetString();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error! raw:{Environment.NewLine}{raw}{Environment.NewLine}ex: {ex.Message}");

        }


        if (span.Length == 0) { error = "Empty content."; return false; }
        try
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                error = "Inner content empty.";
                return false;
            }
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
}

