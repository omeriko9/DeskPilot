using System;
using System.Text.Json;

namespace DesktopAssist.Llm
{
    /// <summary>
    /// Shared helper functions for LLM client implementations (provider-agnostic).
    /// </summary>
    public static class LlmCommon
    {
        public static string ExtractOriginalUserRequest(string ctxJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(ctxJson);
                return doc.RootElement.GetProperty("original_user_request").GetString() ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        public static string ExtractOriginalUserRequestBase64(string ctxJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(ctxJson);
                return doc.RootElement.GetProperty("original_user_request_b64").GetString() ?? string.Empty;
            }
            catch { return string.Empty; }
        }
    }
}
