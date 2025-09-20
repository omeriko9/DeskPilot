using DesktopAssist.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DesktopAssist.Llm.Models
{
    public sealed class OpenAIClient : OpenAIClientBase
    {
        private readonly string _responsesUrl;

        public OpenAIClient(string baseUrl, string apiKey, string model) : base(baseUrl, apiKey, model)
        {
            _responsesUrl = $"{BaseUrl}/responses";
        }


    public override async Task<string?> GetAIResponseAsync(LlmRequest request, CancellationToken ct = default)
        {
            var content = new LlmInferenceRequest
            {
                Model = Model,
                Input = new List<LlmRoleMessage>
                {
                    new LlmRoleMessage
                    {
                        Role = "system",
                        Content = new List<object>
                        {
                            new LlmTextContentPart { Text = request.SystemPrompt }
                        }
                    },
                    new LlmRoleMessage
                    {
                        Role = "user",
                        Content = new List<object>
                        {
                            new LlmTextContentPart { Text = "ORIGINAL_USER_REQUEST_UTF8:\n" + request.OriginalUserRequest },
                            new LlmTextContentPart { Text = "ORIGINAL_USER_REQUEST_BASE64:\n" + request.OriginalUserRequestBase64 },
                            new LlmTextContentPart { Text = "USER_CONTEXT_JSON:\n" + request.UserContextJson + "\n\nSTRICTLY FOLLOW THE SYSTEM RULES. Return ONLY the raw JSON object." },
                            new LlmImageContentPart { ImageUrl = $"data:image/png;base64,{request.ScreenshotPngBase64}" }
                        }
                    }
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, _responsesUrl);
            req.Content = new StringContent(JsonSerializer.Serialize(content, JsonHelper.Options), Encoding.UTF8, "application/json");

            // Log serialized request for diagnostics (size + preview)
            var serialized = JsonSerializer.Serialize(content, JsonHelper.Options);
            RaiseInfo($"REQ bytes={serialized.Length}");
            req.Content = new StringContent(serialized, Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                RaiseError($"HTTP {(int)resp.StatusCode}: {body}");
                return null;
            }

            var extracted = OpenAIResponseParser.ExtractText(body, m => RaiseInfo(m));
            return extracted ?? body;
        }
    }
}
