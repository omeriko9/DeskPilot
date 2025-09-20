using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using DesktopAssist.Llm.Models;
using DesktopAssist.Llm;
using DesktopAssist.Screen;
using DesktopAssist.Settings;
using DesktopAssist.Llm;
using DesktopAssist.Engine;
using DesktopAssist.Automation.Input;

namespace DesktopAssist.Engine;

/// <summary>
/// Encapsulates the core iterative LLM -> action execution loop.
/// </summary>
public static class AutomationEngine
{
    public static async Task RunAsync(AppSettings settings, LLMClient client, string prompt, Action<string>? statusCb, string tmpFileName = "output.txt")
    {
        int outerStep = 0;
        string history = string.Empty;
        // Read system prompt once (avoid per-iteration disk I/O)
        var systemPrompt = File.ReadAllText("prompts/system_prompt.txt");

        while (outerStep < settings.MaxSteps)
        {
            outerStep++;
            var (screenshotPngB64, size) = Screenshot.CapturePrimaryPngBase64();
            // compute approximate decoded bytes (Base64 expands ~4/3)
            var screenshotKb = (int)Math.Round((screenshotPngB64.Length * 0.75) / 1024.0);
            var userContext = new
            {
                original_user_request = prompt,
                original_user_request_b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(prompt)),
                step_num = outerStep - 1,
                actions_history = history,
                keyboard_only_hint = settings.KeyboardOnlyMode,
                image_space = new { width = size.Width, height = size.Height },
                virtual_screen = new
                {
                    left = ScreenSnapshotInfo.VirtualLeft,
                    top = ScreenSnapshotInfo.VirtualTop,
                    width = ScreenSnapshotInfo.VirtualWidth,
                    height = ScreenSnapshotInfo.VirtualHeight
                }
            };

            Console.WriteLine($"[LLM] Turn {outerStep} -> sending screenshot ({screenshotKb} KB)");
            statusCb?.Invoke(AppForm.ThinkingBaseText);

            var serializedContext = JsonSerializer.Serialize(userContext);
            var request = new LlmRequest
            {
                SystemPrompt = systemPrompt,
                OriginalUserRequest = prompt,
                OriginalUserRequestBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(prompt)),
                UserContextJson = serializedContext,
                ScreenshotPngBase64 = screenshotPngB64
            };

            // Log outgoing request to console for diagnostics 
            var previewContext = serializedContext;

            Console.WriteLine($"Serialized Context: {serializedContext}");
            Console.WriteLine(); 

            var llmText = await client.GetAIResponseAsync(request);

            File.AppendAllText(tmpFileName, $"System Prompt:{Environment.NewLine}{systemPrompt}{Environment.NewLine}User Context:{Environment.NewLine}{userContext}llmText:{Environment.NewLine}{llmText}{Environment.NewLine}", Encoding.UTF8);
            File.AppendAllText("steps.txt", $"[{DateTime.Now}]: {llmText}");


            if (string.IsNullOrWhiteSpace(llmText))
            {
                Console.WriteLine("[LLM][Error] Empty response text.");
                break;
            }

            if (!InstructionParser.TryParseResponse(llmText, out StepsResponse plan, out string parseErr))
            {
                Console.WriteLine($"[Parse][Error] {parseErr}");
                continue;
            }

            plan.Steps ??= new List<Step>();
            if (plan.Steps.Count > settings.MaxSteps)
            {
                Console.WriteLine($"[Guard] steps > {settings.MaxSteps}, truncating.");
                plan.Steps = plan.Steps.GetRange(0, settings.MaxSteps);
            }

            if (plan.Steps.Count == 0 && !string.IsNullOrEmpty(plan.Done?.ToString()))
            {
                Console.WriteLine($"[Done] {plan.Done}");
                break;
            }

            Console.WriteLine($"Received {plan.Steps.Count} step(s):");
            foreach (var step in plan.Steps)
            {
                try
                {
                    Console.WriteLine($"[Do] {step.tool} :: {step.human_readable_justification}");
                    if (!string.IsNullOrWhiteSpace(step.human_readable_justification))
                        statusCb?.Invoke(step.human_readable_justification);
                    await Executor.ExecuteAsync(step);
                    history += $"Tool: {step.tool}, args: {step.args}{Environment.NewLine}";
                    Thread.Sleep(100);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Exec][Error] {ex.Message}");
                }
                Thread.Sleep(settings.StepDelayMs);
            }
        }
    }
}
