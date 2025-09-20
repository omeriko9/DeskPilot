using System;

namespace DesktopAssist.Llm
{
    /// <summary>
    /// Encapsulates all inputs required for a single LLM inference call.
    /// </summary>
    public sealed class LlmRequest
    {
        public required string SystemPrompt { get; init; }
        public required string OriginalUserRequest { get; init; }
        public required string OriginalUserRequestBase64 { get; init; }
        public required string UserContextJson { get; init; }
        public required string ScreenshotPngBase64 { get; init; }
    }
}
