using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using DesktopAssist.Automation;
using DesktopAssist.Llm;
using DesktopAssist.Llm.Models;
using DesktopAssist.Screen;
using DesktopAssist.Settings;

namespace DesktopAssist.Engine;

public sealed class ExecutionEngine
{
    private readonly AppSettings _settings;
    private readonly ScreenshotService _screenshot = new();
    private readonly HttpClient _http;
    private string? _cachedContext; // cache for context.txt contents
    private int _requestSeq = 0; // incremental request id for instrumentation
    private readonly Action<string>? _statusCallback;

    public ExecutionEngine(AppSettings settings, Action<string>? statusCallback = null)
    {
        _settings = settings;
        _http = CreateHttpClient();
        _statusCallback = statusCallback;
    }

    private HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            UseCookies = false,
            AllowAutoRedirect = false,
            Expect100ContinueTimeout = TimeSpan.FromMilliseconds(1),
            EnableMultipleHttp2Connections = true,
            // Ensure connect attempts don't hang forever; respect a reasonable bound
            ConnectTimeout = TimeSpan.FromSeconds(Math.Max(10,  _settings?.RequestTimeoutSeconds > 0 ? Math.Min(_settings.RequestTimeoutSeconds, 60) : 10))
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan // we manage timeout externally
        };
    }

    public async Task<string> RunAsync(string userRequest, CancellationToken token)
    {
        Console.WriteLine("[Exec] Enter RunAsync");
        Console.WriteLine($"[Exec] VerboseNetworkLogging={_settings.VerboseNetworkLogging} MaxSteps={_settings.MaxSteps}");
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            throw new InvalidOperationException("API key missing in settings or environment.");
        Console.WriteLine("[Exec] API key present (length hidden)");

        var size = _screenshot.GetPrimarySize();
    var executor = new ActionExecutor(size, _settings);
        int stepNum = 0;

        Console.WriteLine("[Exec] Starting main loop");
        while (true)
        {
            Console.WriteLine($"[Exec] Top of loop step={stepNum}");
            token.ThrowIfCancellationRequested();
            if (stepNum > _settings.MaxSteps)
                return $"Max steps ({_settings.MaxSteps}) reached without completion.";

            var base64 = _screenshot.CaptureBase64Jpeg(_settings);
            var payload = BuildRequestPayload(userRequest, stepNum, base64, size);
            if (_settings.VerboseNetworkLogging)
                Console.WriteLine($"Step {stepNum}: screenshot chars={base64.Length}");

            var reqId = System.Threading.Interlocked.Increment(ref _requestSeq);
            Console.WriteLine($"[Req {reqId}] Preparing LLM call for step {stepNum} at {DateTime.UtcNow:O}");
            _statusCallback?.Invoke("Thinking...");
            var raw = await SendToLlmWithTimeoutAsync(payload, token, reqId).ConfigureAwait(false);
            Console.WriteLine($"[Exec] Received LLM raw length={raw.Length} for step {stepNum}");
            if (_settings.VerboseNetworkLogging)
            {
                Console.WriteLine($"Step {stepNum}: raw HTTP body length={raw.Length}");
            }

            Console.WriteLine("[Exec] Begin parse attempt");
            var parsed = InstructionParser.TryParse(raw) ?? InstructionParser.TryParse(raw, false);
            Console.WriteLine(parsed == null ? "[Exec] Parse result: null" : $"[Exec] Parse result: steps={parsed.Steps.Count} done='{parsed.Done}'");
            if (_settings.VerboseNetworkLogging)
            {
                Console.WriteLine("--- Parsed Attempt ---");
                if (parsed != null)
                {
                    var preview = new StringBuilder();
                    preview.Append("steps=[");
                    for (int i = 0; i < Math.Min(parsed.Steps.Count, 3); i++)
                    {
                        var s = parsed.Steps[i];
                        preview.Append($"{{{s.Function} params={s.Parameters?.Count ?? 0}}}");
                        if (i < parsed.Steps.Count - 1) preview.Append(", ");
                    }
                    if (parsed.Steps.Count > 3) preview.Append(" …");
                    preview.Append("] done=").Append(parsed.Done ?? "null");
                    Console.WriteLine(preview.ToString());
                }
                else
                {
                    Console.WriteLine("Parsed object is null (will retry if first step).");
                }
                Console.WriteLine("--- End Parsed Attempt ---");
            }
            if (parsed == null)
            {
                if (stepNum == 0)
                {
                    Console.WriteLine("Retrying once due to malformed JSON...");
                    var retryPayload = payload with { original_user_request = userRequest + " Please reply in valid JSON" };
                    if (_settings.VerboseNetworkLogging)
                    {
                        Console.WriteLine("--- Retry LLM Prompt (simplified combined prompt already used in first attempt) ---");
                        Console.WriteLine(retryPayload.original_user_request);
                        Console.WriteLine("--- End Retry Prompt ---");
                    }
                    var retryReqId = System.Threading.Interlocked.Increment(ref _requestSeq);
                    Console.WriteLine($"[Req {retryReqId}] Retry LLM call (malformed JSON) step {stepNum} at {DateTime.UtcNow:O}");
                    raw = await SendToLlmWithTimeoutAsync(retryPayload, token, retryReqId).ConfigureAwait(false);
                    if (_settings.VerboseNetworkLogging)
                        Console.WriteLine($"Retry: raw HTTP body length={raw.Length}");
                    parsed = InstructionParser.TryParse(raw, false);
                    if (_settings.VerboseNetworkLogging)
                    {
                        Console.WriteLine("--- Retry Parsed Attempt ---");
                        if (parsed != null)
                        {
                            var preview = new StringBuilder();
                            preview.Append("steps=[");
                            for (int i = 0; i < Math.Min(parsed.Steps.Count, 3); i++)
                            {
                                var s = parsed.Steps[i];
                                preview.Append($"{{{s.Function} params={s.Parameters?.Count ?? 0}}}");
                                if (i < parsed.Steps.Count - 1) preview.Append(", ");
                            }
                            if (parsed.Steps.Count > 3) preview.Append(" …");
                            preview.Append("] done=").Append(parsed.Done ?? "null");
                            Console.WriteLine(preview.ToString());
                        }
                        else
                        {
                            Console.WriteLine("Retry parse still null.");
                        }
                        Console.WriteLine("--- End Retry Parsed Attempt ---");
                    }
                }
            }
            if (parsed == null) 
            {
                Console.WriteLine("Failed to parse LLM response after retry.");
                return "Failed to parse LLM response.";
            }

            Console.WriteLine($"Parsed {parsed.Steps.Count} steps, done: '{parsed.Done}'");

            try
            {
                Console.WriteLine("[Exec] Enter step execution loop");
                foreach (var step in parsed.Steps)
                {
                    if (token.IsCancellationRequested) return "Cancelled";
                    _statusCallback?.Invoke(step.Function + (string.IsNullOrWhiteSpace(step.Justification) ? string.Empty : $": {step.Justification}"));
                    Console.WriteLine($"Executing: {step.Function} -> {step.Justification}");
                    bool success = executor.Execute(step, token);
                    Console.WriteLine(success ? $"[Exec] Step '{step.Function}' completed" : $"[Exec][Warn] Step '{step.Function}' reported failure");
                    if (!success) return $"Failed executing step: {step.Function}";
                    if (_settings.StepDelayMs > 0)
                    {
                        Console.WriteLine($"[Exec] Inter-step delay {_settings.StepDelayMs}ms");
                        await Task.Delay(_settings.StepDelayMs, token).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Exec][Error] Unhandled exception during step execution: {ex}");
                return $"Execution error: {ex.Message}";
            }

            if (!string.IsNullOrWhiteSpace(parsed.Done))
            {
                if (_settings.PlayBeepOnCompletion) Console.Beep();
                if (_settings.EnablePostCompletionVerification)
                {
                    return await RunVerificationFlow(parsed.Done!, userRequest, executor, token).ConfigureAwait(false);
                }
                return parsed.Done!;
            }

            // Heuristic completion: if original request was to open a specific app and we've just pressed Enter after typing it, stop.
            if (parsed.Steps.Count > 0 && IsLikelyFinalLaunch(parsed.Steps, userRequest))
            {
                Console.WriteLine("[Exec] Heuristic: concluding task after launch sequence.");
                if (_settings.PlayBeepOnCompletion) Console.Beep();
                var summary = $"Likely launched: {userRequest}";
                if (_settings.EnablePostCompletionVerification)
                {
                    return await RunVerificationFlow(summary, userRequest, executor, token).ConfigureAwait(false);
                }
                return summary;
            }

            Console.WriteLine("Fetching further instructions...");
            stepNum++;
        }
    }

    private async Task<string> SendToLlmAsync(LlmPayload payload, CancellationToken token)
    {
        Console.WriteLine($"[Dbg] Verbose? = {_settings.VerboseNetworkLogging}");
        Console.WriteLine($"[Dbg] Thread = {Environment.CurrentManagedThreadId}");
        Console.WriteLine("[Dbg] Enter SendToLlmAsync");

        var url = _settings.BaseUrl.TrimEnd('/') + "/chat/completions";
        if (_settings.VerboseNetworkLogging)
            Console.WriteLine($"POST {url}");
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        // New approach: mimic python implementation – single user message with
        // Python style but with explicit schema appended every time to force structured steps.
        var baseContext = _cachedContext ??= LoadCompositeContext();
        var requestDataJson = System.Text.Json.JsonSerializer.Serialize(new { original_user_request = payload.original_user_request, step_num = payload.step_num });
        var sbPrompt = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(baseContext)) sbPrompt.Append(baseContext);
        sbPrompt.Append(requestDataJson);
        sbPrompt.AppendLine();
    // Explicitly enumerate allowed function names pulled dynamically from ActionExecutor to avoid drift.
    var allowedFunctions = string.Join(", ", ActionExecutor.SupportedFunctions);
    sbPrompt.AppendLine("Allowed function names (use ONLY these, no others): [" + allowedFunctions + "]");
    sbPrompt.AppendLine("- 'type' is an alias for 'write'. Prefer 'write' unless mirroring prior context.");
        sbPrompt.AppendLine("\nReturn ONLY valid JSON matching exactly this shape (no markdown, no extra keys):");
        sbPrompt.AppendLine("{\n  \"steps\": [ { \"function\": string, \"parameters\": { ... }, \"human_readable_justification\": string } ],\n  \"done\": null | string\n}");
        sbPrompt.AppendLine("Each element of steps MUST be an object (not a string) with keys function, parameters (object or {}), human_readable_justification.");
        sbPrompt.AppendLine("When the task is complete set steps to [] and put a concise summary in done.");
    sbPrompt.AppendLine("Do not include explanations outside the JSON.");
    sbPrompt.AppendLine("All function names and parameter keys must be in English. Justifications may be concise.");
    sbPrompt.AppendLine("Reject inventing new function names; if an action can't be expressed with the allowed list, choose the closest valid primitive sequence.");
    sbPrompt.AppendLine("Keyboard layout handling (deterministic): Use the 'setlayout' function to explicitly set the keyboard layout before typing text. For English commands / program names / file paths use: { \"function\": \"setlayout\", \"parameters\": { \"hex\": \"00000409\" }, ... }. For Hebrew (example) use its KLID (e.g. 0000040D). Never rely on toggling with win+space to guess current layout—always set the needed layout before a sequence of related write steps if uncertain. Omit redundant setlayout calls when the required layout is already active by your own prior step.");
    sbPrompt.AppendLine("Summarized rule: Before writing, if the intended text language differs from the last explicitly set layout in this conversation, insert a 'setlayout' step with the proper 8-hex KLID (e.g., 00000409 English US). Then issue 'write' steps.");
        sbPrompt.AppendLine("Example:\n{\n  \"steps\": [ { \"function\": \"press\", \"parameters\": { \"key\": \"win\" }, \"human_readable_justification\": \"Open start menu\" } ],\n  \"done\": null\n}");
        var combinedText = sbPrompt.ToString();

    // Token budget: allow override via settings.MaxCompletionTokens (>0). If unset, preserve prior manual constant (2800).
    // NOTE: We no longer silently force 800; whatever you set in settings.json (MaxCompletionTokens) wins.
    int desiredTokens = payload.step_num == 0 ? _settings.InitialStepMaxTokens : _settings.FollowupStepMaxTokens;
    if (_settings.EnableAdaptiveTokenScaling && _lastHeaderLatencyMs > _settings.AdaptiveLatencyMsThreshold)
    {
        desiredTokens = Math.Max(desiredTokens / 2, 200);
    }
    if (_forceConstrainedRetry)
    {
        desiredTokens = _settings.ConstrainedRetryMaxTokens;
        _forceConstrainedRetry = false; // consume flag
    }

        var payloadObj = new Dictionary<string, object?>
        {
            ["model"] = (_currentModelFallbackUsed ? _settings.FallbackModel : _settings.Model),
            ["messages"] = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]{
                        new { type = "text", text = combinedText },
                        new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{payload.screenshot_base64}", detail = "low" } }
                    }
                }
            }
        };
    // Some newer reasoning-capable models reject max_tokens and require max_completion_tokens.
    // We'll default to max_completion_tokens; if the server later requires max_tokens for a non-reasoning older model,
    // a lightweight feature flag or model name check could be reintroduced.
    payloadObj["max_completion_tokens"] = desiredTokens;
    if (_settings.UseJsonSchemaResponseFormat)
    {
        payloadObj["response_format"] = new {
            type = "json_schema",
            json_schema = new {
                name = "automation_steps",
                schema = new {
                    type = "object",
                    required = new [] { "steps", "done" },
                    properties = new {
                        steps = new { type = "array", items = new { type = "object", required = new [] { "function", "parameters", "human_readable_justification"}, properties = new { function = new { type = "string"}, parameters = new { type = "object"}, human_readable_justification = new { type = "string"} } } },
                        done = new { anyOf = new object[] { new { type = "string" }, new { type = "null" } } }
                    }
                }
            }
        };
    }

        var json = JsonSerializer.Serialize(payloadObj);

        if (json.Length > 450_000)
            throw new InvalidOperationException($"Payload too large ({json.Length} chars) - aborting before send.");

        if (_settings.VerboseNetworkLogging)
        {
            Console.WriteLine($"Request JSON size: {json.Length} chars");
            Console.WriteLine($"Context length: {(baseContext?.Length ?? 0)} chars");
            Console.WriteLine("Python parity mode: context + request_data only");
        }

        if (_settings.VerboseNetworkLogging)
        {
            var promptText = combinedText;
            Console.WriteLine("--- LLM Prompt ---");
            Console.WriteLine(promptText);
            Console.WriteLine("--- End Prompt ---");
        }

        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        // Per-request CTS so we can cancel independently of global CancelPendingRequests.
        using var perRequestCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        // Optional aggressive cancellation if upstream token fires.
        using var reg = perRequestCts.Token.Register(() =>
        {
            try { _http.CancelPendingRequests(); } catch { }
        });

        // Header-phase monitoring: Instead of hard-failing at 10s, we warn then continue up to overall timeout.
        var overallTimeoutSec = Math.Max(1, _settings.RequestTimeoutSeconds);
        var payloadSizeClass = json.Length switch
        {
            < 50_000 => "small",
            < 150_000 => "medium",
            < 300_000 => "large",
            _ => "huge"
        };
        // Dynamic advisory threshold: smaller of 12s or 40% of overall for small; scale upward for larger payloads.
        double advisory = overallTimeoutSec * (payloadSizeClass switch
        {
            "small" => 0.35,
            "medium" => 0.45,
            "large" => 0.55,
            _ => 0.65
        });
        var advisorySeconds = (int)Math.Clamp(Math.Ceiling(advisory), 5, Math.Min(25, overallTimeoutSec - 3));
        if (_settings.VerboseNetworkLogging)
            Console.WriteLine($"[Net] Sending request (advisory header threshold {advisorySeconds}s, overall {overallTimeoutSec}s, payload={payloadSizeClass}) at {DateTime.UtcNow:O}");

        var sendTask = _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, perRequestCts.Token);
        var headerStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var warned = false;
    int earlyCutoffMs = _settings.EarlyHeaderCutoffMs;
    while (!sendTask.IsCompleted)
        {
            try
            {
                await Task.WhenAny(sendTask, Task.Delay(TimeSpan.FromSeconds(1), token)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw; // propagate external cancellation
            }
            if (!sendTask.IsCompleted && headerStopwatch.Elapsed.TotalSeconds >= advisorySeconds && !warned)
            {
                Warn($"[Net][Warn] Headers still pending after {headerStopwatch.ElapsedMilliseconds} ms (payload {payloadSizeClass}); continuing until overall timeout.");
                warned = true;
            }
            if (!sendTask.IsCompleted && earlyCutoffMs > 0 && headerStopwatch.ElapsedMilliseconds > earlyCutoffMs)
            {
                Console.WriteLine($"[Net][EarlyCutoff] No headers after {headerStopwatch.ElapsedMilliseconds}ms; switching to fallback model + constrained retry.");
                try { perRequestCts.Cancel(); } catch { }
                _currentModelFallbackUsed = true;
                _forceConstrainedRetry = true;
                throw new TimeoutException("Early header cutoff");
            }
            if (_settings.VerboseNetworkLogging && !sendTask.IsCompleted)
            {
                Console.WriteLine($"[Net] Waiting for headers... {headerStopwatch.ElapsedMilliseconds} ms elapsed");
            }
        }
        var resp = await sendTask.ConfigureAwait(false);
        var headerMs = swTotal.ElapsedMilliseconds;
        _lastHeaderLatencyMs = (int)headerMs;
        if (_settings.VerboseNetworkLogging) Console.WriteLine($"[Net] Headers in {headerMs} ms Status {(int)resp.StatusCode}");

        // Stream body manually with stall watchdog using per-chunk timeout.
        await using var stream = await resp.Content.ReadAsStreamAsync(perRequestCts.Token);
        using var ms = new System.IO.MemoryStream();
        var buffer = new byte[8192];
        long totalRead = 0;
        var firstByteLogged = false;
        var lastLog = DateTime.UtcNow;
        // Stall threshold: smaller of 15s or (overall timeout - 1) but at least 5s.
        var stallThresholdSec = Math.Max(5, Math.Min(15, overallTimeoutSec - 1));
        if (_settings.VerboseNetworkLogging) Console.WriteLine($"[Net] Begin reading body (stall threshold {stallThresholdSec}s)" );
        while (true)
        {
            var readTask = stream.ReadAsync(buffer.AsMemory(0, buffer.Length), perRequestCts.Token).AsTask();
            var stallTask = Task.Delay(TimeSpan.FromSeconds(stallThresholdSec), perRequestCts.Token);
            var completedTask = await Task.WhenAny(readTask, stallTask).ConfigureAwait(false);
            if (completedTask == stallTask)
            {
                perRequestCts.Cancel();
                Console.WriteLine($"[Net][Warn] Body stall: no data for >{stallThresholdSec}s after {swTotal.ElapsedMilliseconds} ms total");
                throw new TimeoutException($"Response body stalled with no data for >{stallThresholdSec}s");
            }
            var read = readTask.Result;
            if (read == 0) break; // EOF
            ms.Write(buffer, 0, read);
            totalRead += read;
            if (!firstByteLogged)
            {
                firstByteLogged = true;
                if (_settings.VerboseNetworkLogging)
                    Console.WriteLine($"[Net] First body bytes after {swTotal.ElapsedMilliseconds} ms (size now {totalRead})");
            }
            if (_settings.VerboseNetworkLogging)
            {
                var now = DateTime.UtcNow;
                if ((now - lastLog).TotalSeconds >= 1)
                {
                    Console.WriteLine($"[Net] Body {totalRead} bytes in {swTotal.ElapsedMilliseconds} ms");
                    lastLog = now;
                }
            }
        }
        var bodyBytes = ms.ToArray();
        var body = Encoding.UTF8.GetString(bodyBytes);
        if (_settings.VerboseNetworkLogging)
            Console.WriteLine($"[Net] Completed body {bodyBytes.Length} bytes in {swTotal.ElapsedMilliseconds} ms");
        if (_settings.VerboseNetworkLogging)
        {
            Console.WriteLine($"HTTP {(int)resp.StatusCode} {resp.StatusCode}, body len={body.Length}");
            Console.WriteLine("--- Raw HTTP Body (truncated) ---");
            var truncated = body.Length > 4000 ? body.Substring(0, 4000) + " ...[truncated]" : body;
            Console.WriteLine(truncated);
            Console.WriteLine("--- End Raw HTTP Body ---");
        }
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"LLM error {(int)resp.StatusCode}: {body}");

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string? content = root.TryGetProperty("choices", out var choicesEl) &&
                               choicesEl.ValueKind == JsonValueKind.Array &&
                               choicesEl.GetArrayLength() > 0 &&
                               choicesEl[0].TryGetProperty("message", out var msgEl) &&
                               msgEl.TryGetProperty("content", out var contentEl)
                               ? contentEl.GetString()
                               : null;

            if (_settings.VerboseNetworkLogging)
            {
                try
                {
                    var usage = root.TryGetProperty("usage", out var usageEl) ? usageEl : default;
                    if (usage.ValueKind != JsonValueKind.Undefined)
                    {
                        int? promptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : null;
                        int? completionTokens = usage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : null;
                        int? reasoningTokens = usage.TryGetProperty("reasoning_tokens", out var rt) ? rt.GetInt32() : null;
                        Console.WriteLine($"[Usage] prompt={promptTokens} completion={completionTokens} reasoning={reasoningTokens}");
                    }
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                Warn("LLM returned empty content; attempting JSON extraction fallback.");
                var fallback = ExtractAutomationJson(body);
                if (fallback != null)
                {
                    Console.WriteLine("[Fallback] Using extracted JSON block.");
                    return fallback;
                }
                Warn("No JSON block could be extracted from response.");
                try
                {
                    if (root.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object &&
                        usageEl.TryGetProperty("completion_tokens", out var ctEl) && ctEl.ValueKind == JsonValueKind.Number)
                    {
                        var ct = ctEl.GetInt32();
                        if (ct >= desiredTokens * 0.9)
                        {
                            Console.WriteLine("[Heuristic] High completion token usage with empty content. Enabling constrained retry & fallback model.");
                            _currentModelFallbackUsed = true;
                            _forceConstrainedRetry = true;
                        }
                    }
                }
                catch { }
                return string.Empty;
            }

            if (_settings.VerboseNetworkLogging)
            {
                var preview = content.Length > 400 ? content.Substring(0, 400) + "..." : content;
                Console.WriteLine($"[ContentPreview] {preview}");
            }
            return content;
        }
        catch
        {
            return body; // fallback to raw
        }
    }

    private static string? ExtractAutomationJson(string body)
    {
        // Heuristic: locate "\"steps\"" and backtrack to nearest '{', forward to matching '}' while counting braces.
        var idx = body.IndexOf("\"steps\"", StringComparison.OrdinalIgnoreCase);
        if (idx == -1) return null;
        int start = body.LastIndexOf('{', idx);
        if (start == -1) return null;
        int braceDepth = 0;
        for (int i = start; i < body.Length; i++)
        {
            if (body[i] == '{') braceDepth++;
            else if (body[i] == '}')
            {
                braceDepth--;
                if (braceDepth == 0)
                {
                    var candidate = body.Substring(start, i - start + 1).Trim();
                    // Basic validation
                    if (candidate.Contains("\"steps\"") && candidate.Contains("\"done\""))
                        return candidate;
                    return null;
                }
            }
        }
        return null;
    }

    private string LoadCompositeContext()
    {
        try
        {
            // Attempt to mirror python context loading; look for app/resources/context.txt relative to root.
            // We traverse upward a few levels from current directory to locate a resources/context.txt file.
            var cwd = AppContext.BaseDirectory;
            for (int depth = 0; depth < 6; depth++)
            {
                var candidate = Path.Combine(cwd, "resources", "context.txt");
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }
                // Also try app/resources for python parity
                candidate = Path.Combine(cwd, "app", "resources", "context.txt");
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }
                var parent = Directory.GetParent(cwd)?.FullName;
                if (string.IsNullOrEmpty(parent) || parent == cwd) break;
                cwd = parent;
            }
        }
        catch (Exception ex)
        {
            if (_settings.VerboseNetworkLogging)
                Console.WriteLine($"[Ctx] Failed loading context.txt: {ex.Message}");
        }
        return string.Empty;
    }

    private static void Warn(string message)
    {
        var prev = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
        }
        finally
        {
            Console.ForegroundColor = prev;
        }
    }

    private static bool IsLikelyFinalLaunch(System.Collections.Generic.List<InstructionStep> steps, string originalRequest)
    {
        // Simple heuristic: look for a 'press' with key enter AND a preceding write/type containing a token from the request.
        if (steps.Count == 0) return false;
        bool hasEnter = false;
        bool hasQuery = false;
        var requestCore = originalRequest.ToLowerInvariant();
        foreach (var s in steps)
        {
            var fn = s.Function.ToLowerInvariant();
            if (fn == "press" && s.Parameters != null && s.Parameters.TryGetValue("key", out var keyObj))
            {
                if (keyObj?.ToString()?.Equals("enter", StringComparison.OrdinalIgnoreCase) == true)
                    hasEnter = true;
            }
            else if ((fn == "write" || fn == "type") && s.Parameters != null && s.Parameters.TryGetValue("text", out var textObj))
            {
                var text = textObj?.ToString()?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(text) && requestCore.Contains(text))
                    hasQuery = true;
            }
        }
        return hasEnter && hasQuery;
    }

    private async Task<string> SendToLlmWithTimeoutAsync(LlmPayload payload, CancellationToken outerToken, int reqId)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        // don't rely only on CancelAfter; use explicit timeout watcher below to ensure we can cancel and observe.
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));
        var start = DateTime.UtcNow;

        if (_settings.VerboseNetworkLogging)
            Console.WriteLine($"[Req {reqId}][Net][Wrap] Begin timeout wrapper limit={_settings.RequestTimeoutSeconds}s at {start:O} cancelled={outerToken.IsCancellationRequested}");

        var sendTask = SendToLlmAsync(payload, cts.Token);
        var delay = Task.Delay(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds), outerToken);

        var completed = await Task.WhenAny(sendTask, delay).ConfigureAwait(false);
        if (completed != sendTask)
        {
            if (_settings.VerboseNetworkLogging)
                Console.WriteLine($"[Req {reqId}][Net][Wrap][Timeout] Exceeded {_settings.RequestTimeoutSeconds}s; cancelling in-flight request at {DateTime.UtcNow:O}");
            try
            {
                cts.Cancel(); // attempt to cancel the in-flight request
            }
            catch { }
            throw new TimeoutException($"LLM request timed out after {_settings.RequestTimeoutSeconds}s");
        }

        try
        {
            var result = await sendTask.ConfigureAwait(false);
            if (_settings.VerboseNetworkLogging)
                Console.WriteLine($"[Req {reqId}][Net][Wrap] LLM round-trip {(DateTime.UtcNow - start).TotalMilliseconds:F0} ms resultLen={result.Length}");
            return result;
        }
        catch (OperationCanceledException) when (!outerToken.IsCancellationRequested)
        {
            if (_settings.VerboseNetworkLogging)
                Console.WriteLine($"[Req {reqId}][Net][Wrap][Timeout] Caught OperationCanceledException after {(DateTime.UtcNow - start).TotalMilliseconds:F0} ms");
            throw new TimeoutException($"LLM request timed out after {_settings.RequestTimeoutSeconds}s");
        }
    }

        // Old system prompt template removed (logic now embedded directly in combined prompt)

    private static LlmPayload BuildRequestPayload(string request, int step, string b64, (int w, int h) size) => new()
    {
        original_user_request = request,
        step_num = step,
        screenshot_base64 = b64,
        screen_width = size.w,
        screen_height = size.h
    };

    // ----------------- Verification Flow (Post-completion) -----------------
    private async Task<string> RunVerificationFlow(string completionSummary, string originalUserRequest, ActionExecutor executor, CancellationToken token)
    {
        Console.WriteLine("[Verify] Starting verification flow...");
        int attempts = 0;
        string lastReason = string.Empty;
        while (attempts < _settings.VerificationMaxAttempts)
        {
            attempts++;
            if (token.IsCancellationRequested)
            {
                Console.WriteLine("[Verify] Cancelled during verification attempts.");
                return completionSummary + " (verification cancelled)";
            }

            var screenshotB64 = _screenshot.CaptureBase64Jpeg(_settings);
            var prompt = BuildVerificationPrompt(originalUserRequest, completionSummary, screenshotB64);
            Console.WriteLine($"[Verify] Attempt {attempts} sending verification prompt (len={prompt.Length}).");
            string raw;
            try
            {
                // Reuse existing LLM wrapper: build minimal payload with current screenshot
                var payload = new LlmPayload
                {
                    original_user_request = prompt,
                    step_num = -1, // special marker for verification
                    screenshot_base64 = screenshotB64,
                    screen_width = 0,
                    screen_height = 0
                };
                var reqId = System.Threading.Interlocked.Increment(ref _requestSeq);
                raw = await SendToLlmWithTimeoutAsync(payload, token, reqId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Verify] LLM error: {ex.Message}");
                lastReason = "LLM error during verification: " + ex.Message;
                break;
            }

            var verification = ParseVerificationResponse(raw);
            if (verification == null)
            {
                Console.WriteLine("[Verify] Failed to parse verification JSON; aborting verification path.");
                lastReason = "Could not parse verification response";
                break;
            }
            Console.WriteLine($"[Verify] parsed: verified={verification.Verified} reason='{verification.Reason}' correctiveSteps={verification.IfNotVerifiedSteps?.Count ?? 0}");
            if (verification.Verified)
            {
                return completionSummary + " (verified)";
            }
            lastReason = verification.Reason ?? "Unverified for unspecified reason";
            var corrective = verification.IfNotVerifiedSteps ?? new List<InstructionStep>();
            if (corrective.Count == 0)
            {
                Console.WriteLine("[Verify] Unverified but no corrective steps provided; stopping.");
                break;
            }
            Console.WriteLine($"[Verify] Executing {corrective.Count} corrective steps (attempt {attempts}).");
            foreach (var step in corrective)
            {
                if (token.IsCancellationRequested) break;
                executor.Execute(step, token);
                if (_settings.StepDelayMs > 0)
                {
                    try { await Task.Delay(_settings.StepDelayMs, token).ConfigureAwait(false); } catch { }
                }
            }
        }
        return completionSummary + (string.IsNullOrEmpty(lastReason) ? " (verification incomplete)" : $" (verification incomplete: {lastReason})");
    }

    private string BuildVerificationPrompt(string originalUserRequest, string completionSummary, string screenshotBase64)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a strict verifier. The original user request was:");
        sb.AppendLine(originalUserRequest);
        sb.AppendLine("The assistant believes it is done with this summary:");
        sb.AppendLine(completionSummary);
        sb.AppendLine("You have a fresh screenshot of the desktop to judge if the objective is satisfied.");
        sb.AppendLine("Return ONLY JSON with this exact schema (no extra keys, no text outside JSON):\n{\n  \"verified\": true|false,\n  \"reason\": string,\n  \"if_not_verified_steps\": [ { \"function\": string, \"parameters\": { ... }, \"human_readable_justification\": string } ]\n}\nIf verified is true provide an empty array for if_not_verified_steps.");
        sb.AppendLine("SCREENSHOT_BASE64:" + screenshotBase64);
        return sb.ToString();
    }

    private VerificationResult? ParseVerificationResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string json = ExtractJsonBlock(raw);
        if (string.IsNullOrWhiteSpace(json)) json = raw.Trim();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var result = new VerificationResult
            {
                Verified = root.TryGetProperty("verified", out var vEl) && vEl.ValueKind == JsonValueKind.True,
                Reason = root.TryGetProperty("reason", out var rEl) && rEl.ValueKind == JsonValueKind.String ? rEl.GetString() : null,
                IfNotVerifiedSteps = new List<InstructionStep>()
            };
            if (root.TryGetProperty("if_not_verified_steps", out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var sEl in stepsEl.EnumerateArray())
                {
                    if (sEl.ValueKind != JsonValueKind.Object) continue;
                    string? fn = sEl.TryGetProperty("function", out var fEl) && fEl.ValueKind == JsonValueKind.String ? fEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(fn)) continue;
                    Dictionary<string, object>? parms = null;
                    if (sEl.TryGetProperty("parameters", out var pEl) && pEl.ValueKind == JsonValueKind.Object)
                    {
                        parms = new Dictionary<string, object>();
                        foreach (var prop in pEl.EnumerateObject())
                        {
                            parms[prop.Name] = prop.Value.ValueKind switch
                            {
                                JsonValueKind.String => prop.Value.GetString()!,
                                JsonValueKind.Number => prop.Value.TryGetInt64(out var li) ? li : (object)prop.Value.GetDouble(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                _ => prop.Value.ToString() ?? string.Empty
                            };
                        }
                    }
                    string? just = sEl.TryGetProperty("human_readable_justification", out var jEl) && jEl.ValueKind == JsonValueKind.String ? jEl.GetString() : null;
                    result.IfNotVerifiedSteps.Add(new InstructionStep
                    {
                        Function = fn!,
                        Parameters = parms,
                        Justification = just
                    });
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Verify] Parse error: " + ex.Message);
            return null;
        }
    }

    private static string ExtractJsonBlock(string raw)
    {
        int firstBrace = raw.IndexOf('{');
        if (firstBrace < 0) return raw;
        int depth = 0;
        for (int i = firstBrace; i < raw.Length; i++)
        {
            if (raw[i] == '{') depth++;
            else if (raw[i] == '}') depth--;
            if (depth == 0)
            {
                return raw.Substring(firstBrace, i - firstBrace + 1);
            }
        }
        return raw; // fallback
    }

    private sealed class VerificationResult
    {
        public bool Verified { get; set; }
        public string? Reason { get; set; }
        public List<InstructionStep>? IfNotVerifiedSteps { get; set; }
    }
    // -----------------------------------------------------------------------

    private record LlmPayload
    {
        public string original_user_request { get; init; } = string.Empty;
        public int step_num { get; init; }
        public string screenshot_base64 { get; init; } = string.Empty;
        public int screen_width { get; init; }
        public int screen_height { get; init; }
    }

    // Adaptive model / token control state
    private int _lastHeaderLatencyMs = 0;
    private bool _currentModelFallbackUsed = false;
    private bool _forceConstrainedRetry = false;
}
