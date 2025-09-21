using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DesktopAssist.Util;

namespace DesktopAssist.Llm.Models
{
    /// <summary>
    /// Remote LLM client: sends the already prepared structured payload to a trusted remote server.
    /// Assumes remote server returns JSON in the same format OpenAI parsing expects.
    /// </summary>
    public sealed class RemoteLLMClient : LLMClient
    {
        private readonly HttpClient _http = new();
        private readonly string _endpoint;

        public event EventHandler<string>? Info;
        public event EventHandler<string>? Error;

        public RemoteLLMClient(string endpoint)
        {
            _endpoint = endpoint.TrimEnd('/');
        }

        public async Task<string?> GetAIResponseAsync(LlmRequest request, CancellationToken ct = default)
        {
            try
            {
                var payload = new
                {
                    systemPrompt = request.SystemPrompt,
                    originalUserRequest = request.OriginalUserRequest,
                    originalUserRequestBase64 = request.OriginalUserRequestBase64,
                    userContextJson = request.UserContextJson,
                    screenshotPngBase64 = request.ScreenshotPngBase64
                };

                var json = JsonSerializer.Serialize(payload, JsonHelper.Options);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                Info?.Invoke(this, $"POST {_endpoint} ({json.Length} bytes)");

                //Console.WriteLine(json); // For diagnostics

                using var resp = await _http.PostAsync(_endpoint, content, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    Error?.Invoke(this, $"Remote HTTP {(int)resp.StatusCode}: {body}");
                    return null;
                }
                // Try to reuse OpenAI style parser if shape matches
                
                return body;
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, ex.Message);
                return null;
            }
        }
    }
}
