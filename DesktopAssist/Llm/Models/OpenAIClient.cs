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
    public sealed class OpenAIClient
    {
        private readonly HttpClient _http;
        private readonly string _model;
        private readonly string _responsesUrl;

        public OpenAIClient(string baseUrl, string apiKey, string model)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _responsesUrl = $"{baseUrl}/responses";
            _model = model;
        }

        public async Task<string?> CallAsyncOLD(string systemPrompt,
            string userContextJson, string screenshotPngBase64, CancellationToken ct = default)
        {
            // Compose a single-turn "responses" call with two items in input:
            // 1) system + user context + strict instructions + response format expectation
            // 2) the screenshot as data URL
            var content = new
            {
                model = _model,
                input = new object[]
                {
                new {
                    role = "system",
                    content = new object[]
                    {
                        // 'text' content type rejected by API (expects 'input_text')
                        new { type = "input_text", text = systemPrompt }
                    }
                },
                new {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = $"USER_CONTEXT_JSON:\n{userContextJson}\n\nSTRICTLY FOLLOW THE SYSTEM RULES. Return ONLY the raw JSON object." },
                        new { type = "input_image", image_url = $"data:image/png;base64,{screenshotPngBase64}" }
                    }
                }
                }
                // We want a single text output that is exactly the JSON.
                // Some servers return "output_text", others "content" — we handle below.
                //temperature = 0.1
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, _responsesUrl);
            req.Content = new StringContent(JsonSerializer.Serialize(content, JsonHelper.Options), Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[LLM][HTTP {(int)resp.StatusCode}] {body}");
                return null;
            }

            // Raw body log (adaptive). If body <= 4000 chars, print all. Otherwise print head+tail with length info.
            if (!string.IsNullOrEmpty(body))
            {
                const int maxFull = 4000; // safety ceiling to avoid flooding console
                if (body.Length <= maxFull)
                {
                    // Console.WriteLine($"[LLM][Raw][len={body.Length}] {body}");
                }
                else
                {
                    int headLen = 1800;
                    int tailLen = 1200;
                    if (headLen + tailLen > maxFull)
                    {
                        tailLen = maxFull - headLen - 20; // leave room for ellipsis marker
                    }
                    var head = body.Substring(0, headLen);
                    var tail = body.Substring(body.Length - tailLen);
                    //Console.WriteLine($"[LLM][Raw][len={body.Length}] {head} … <{body.Length - headLen - tailLen} bytes omitted> … {tail}");
                }
            }

            // Try to extract model's top-level text. Support either Responses "output_text" or message.content[...].text patterns.
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("output_text", out var ot) && ot.ValueKind == JsonValueKind.String)
                    return ot.GetString();

                // Fallback: data[0].content[0].text or output[0].content[0].text
                if (doc.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array && output.GetArrayLength() > 0)
                {
                    var first = output[0];
                    if (first.TryGetProperty("content", out var cont) && cont.ValueKind == JsonValueKind.Array && cont.GetArrayLength() > 0)
                    {
                        var c0 = cont[0];
                        if (c0.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String) return t.GetString();
                    }
                }

                // Chat-like: choices[0].message.content (string) OR content array with text parts
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var ch0 = choices[0];
                    if (ch0.TryGetProperty("message", out var msg))
                    {
                        if (msg.TryGetProperty("content", out var mc))
                        {
                            if (mc.ValueKind == JsonValueKind.String) return mc.GetString();
                            if (mc.ValueKind == JsonValueKind.Array && mc.GetArrayLength() > 0)
                            {
                                foreach (var part in mc.EnumerateArray())
                                {
                                    if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String) return t.GetString();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[LLM][ParseResp] " + ex.Message);
            }
            return body; // last resort: return raw; upstream parser may still succeed if it's plain JSON
        }


        public async Task<string?> CallAsync(string systemPrompt,
        string userContextJson, string screenshotPngBase64, CancellationToken ct = default)
        {
            var content = new
            {
                model = _model,
                input = new object[]
                {
            new {
                role = "system",
                content = new object[]
                {
                    new { type = "input_text", text = systemPrompt }
                }
            },
            new {
                role = "user",
                content = new object[]
                {
                    // 1) Plain request for maximal attention
                    new { type = "input_text", text = "ORIGINAL_USER_REQUEST_UTF8:\n" +
                        ExtractOriginalUserRequestFromContext(userContextJson) },
                    // 2) Base64 mirror, declared explicitly
                    new { type = "input_text", text = "ORIGINAL_USER_REQUEST_BASE64:\n" +
                        ExtractOriginalUserRequestB64FromContext(userContextJson) },
                    // 3) The structured context (unchanged)
                    new { type = "input_text", text = "USER_CONTEXT_JSON:\n" + userContextJson +
                        "\n\nSTRICTLY FOLLOW THE SYSTEM RULES. Return ONLY the raw JSON object." },
                    // 4) Screenshot
                    new { type = "input_image", image_url = $"data:image/png;base64,{screenshotPngBase64}" }
                }
            }
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, _responsesUrl);
            req.Content = new StringContent(JsonSerializer.Serialize(content, JsonHelper.Options), Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[LLM][HTTP {(int)resp.StatusCode}] {body}");
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("output_text", out var ot) && ot.ValueKind == JsonValueKind.String)
                    return ot.GetString();
                if (doc.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array && output.GetArrayLength() > 0)
                {
                    var first = output[0];
                    if (first.TryGetProperty("content", out var cont) && cont.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var part in cont.EnumerateArray())
                            if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String) return t.GetString();
                    }
                }
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var ch0 = choices[0];
                    if (ch0.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var mc))
                    {
                        if (mc.ValueKind == JsonValueKind.String) return mc.GetString();
                        if (mc.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var part in mc.EnumerateArray())
                                if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String) return t.GetString();
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("[LLM][ParseResp] " + ex.Message); }

            return body;
        }

        private static string ExtractOriginalUserRequestFromContext(string ctxJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(ctxJson);
                return doc.RootElement.GetProperty("original_user_request").GetString() ?? "";
            }
            catch { return ""; }
        }

        private static string ExtractOriginalUserRequestB64FromContext(string ctxJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(ctxJson);
                return doc.RootElement.GetProperty("original_user_request_b64").GetString() ?? "";
            }
            catch { return ""; }
        }

    }
}
