using System;
using System.Text.Json;

namespace DesktopAssist.Llm.Models
{
    /// <summary>
    /// Shared parsing utilities for OpenAI style responses (responses, chat, or legacy choice arrays).
    /// </summary>
    public static class OpenAIResponseParser
    {
        /// <summary>
        /// Attempts to extract the primary text content from a provider response JSON string.
        /// Returns null if no suitable text node is found.
        /// </summary>
        public static string? ExtractText(string json, Action<string>? diag = null)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 1. Direct unified field
                if (root.TryGetProperty("output_text", out var outText) && outText.ValueKind == JsonValueKind.String)
                    return outText.GetString();

                // 2. responses endpoint: output[] -> content[] -> text
                if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array && output.GetArrayLength() > 0)
                {
                    foreach (var item in output.EnumerateArray())
                    {
                        if (item.TryGetProperty("content", out var cont) && cont.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var part in cont.EnumerateArray())
                                if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                                    return t.GetString();
                        }
                    }
                }

                // 3. chat/completions style: choices[0].message.content (string or array)
                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var ch0 = choices[0];
                    if (ch0.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var mc))
                    {
                        if (mc.ValueKind == JsonValueKind.String) return mc.GetString();
                        if (mc.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var part in mc.EnumerateArray())
                                if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                                    return t.GetString();
                        }
                    }
                }

                // 4. Fallback: if root is string (rare)
                if (root.ValueKind == JsonValueKind.String) return root.GetString();
            }
            catch (Exception ex)
            {
                diag?.Invoke($"Parse error: {ex.Message}");
            }
            return null;
        }
    }
}
