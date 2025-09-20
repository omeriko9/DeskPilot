using System.Threading;
using System.Threading.Tasks;

namespace DesktopAssist.Llm
{
    /// <summary>
    /// Abstraction for any Large Language Model client used by the application.
    /// Implementations should perform a single multimodal call returning the raw text response
    /// (already extracted from provider-specific response envelopes).
    /// </summary>
    public interface LLMClient
    {
        /// <summary>
        /// Raised for non-error diagnostic/status messages (e.g., HTTP status, parse fallbacks).
        /// </summary>
        event EventHandler<string>? Info;

        /// <summary>
        /// Raised for error conditions (HTTP failures, parsing exceptions, etc.).
        /// </summary>
        event EventHandler<string>? Error;

        /// <summary>
        /// Issue an inference request.
        /// </summary>
        /// <param name="systemPrompt">System / instruction prompt to steer behavior.</param>
        /// <param name="userContextJson">Structured JSON context provided to the model.</param>
        /// <param name="screenshotPngBase64">Base64 encoded PNG screenshot.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Model textual response (already flattened) or null on failure.</returns>
    Task<string?> GetAIResponseAsync(LlmRequest request, CancellationToken ct = default);
    }
}
