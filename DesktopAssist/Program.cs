using DesktopAssist.Automation.Input;
using DesktopAssist.Engine;
using DesktopAssist.Llm;
using DesktopAssist.Llm.Models;
using DesktopAssist.Screen;
using DesktopAssist.Settings;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DesktopAssist;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var settings = AppSettings.Load();
        AppForm? overlayForm = null;
        Thread? uiThread = null;
        var uiReady = new ManualResetEventSlim(false);
       
        if (settings.ShowProgressOverlay)
        {
            uiThread = new Thread(() =>
            {
                try
                {
                    ApplicationConfiguration.Initialize();
                    overlayForm = new AppForm();
                    uiReady.Set();
                    Application.Run(overlayForm);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UI][Error] {ex.Message}");
                    uiReady.Set();
                }
            }) { IsBackground = true };
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            uiReady.Wait();
        }


        try { Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
        catch { /* ignore; best effort */ }

        // Console allocation / visibility based on DebugConsole setting.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var hasConsole = GetConsoleWindow() != IntPtr.Zero;
                if (settings.DebugConsole)
                {
                    // Ensure a console is available (helpful for diagnostics).
                    if (!hasConsole) AllocConsole();

                    Console.OutputEncoding = Encoding.UTF8;
                    Console.InputEncoding = Encoding.UTF8;
                }
                else
                {
                    // If a console exists but user disabled it, attempt to hide.
                    if (hasConsole)
                    {
                        var hWnd = GetConsoleWindow();
                        if (hWnd != IntPtr.Zero) ShowWindow(hWnd, SW_HIDE);
                    }
                }
            }
            catch { /* non-fatal */ }
        }

        string prompt = args.Length > 0 ? string.Join(" ", args) : ReadPromptFromConsole();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Console.WriteLine("No prompt provided. Exiting.");
            return 1;
        }

        Console.WriteLine($"Starting DesktopAssist with prompt: {prompt}");
        Action<string>? statusCb = null;
        if (settings.ShowProgressOverlay && overlayForm != null)
        {
            statusCb = s => {
                try { overlayForm.UpdateStatus(s); } catch { }
            };
        }

        var client = new OpenAIClient(settings.BaseUrl, settings.ApiKey, settings.Model);

        int outerStep = 0;
        string history = "";

        var tmpFileName = @"output.txt";

        while (outerStep < settings.MaxSteps)
        {
            outerStep++;

            // Capture CURRENT screenshot (single display primary)
            var (screenshotPngB64, size) = Screenshot.CapturePrimaryPngBase64();

            // Compose LLM call
            var systemPrompt = File.ReadAllText("prompts/system_prompt.txt");
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

            Console.WriteLine($"[LLM] Turn {outerStep} -> sending screenshot ({
                //screenshotPngBase64SizeKb(screenshotPngB64)
                (int)(Math.Round(Screenshot.CapturePrimaryPngBase64().b64.Length * 0.75) / 1024.0)
                } KB)");
            var llmText = await client.CallAsync(systemPrompt,
                JsonSerializer.Serialize(userContext),
                screenshotPngB64);

            File.AppendAllText(tmpFileName, $"System Prompt:{Environment.NewLine}" +
                $"{systemPrompt}{Environment.NewLine}User Context:{Environment.NewLine}{userContext}" +
                $"llmText:{Environment.NewLine}{llmText}{Environment.NewLine}", Encoding.UTF8);


            if (string.IsNullOrWhiteSpace(llmText))
            {
                Console.WriteLine("[LLM][Error] Empty response text.");
                break;
            }

            // LLM is instructed to return EXACT ONE JSON object. Parse strictly.
            if (!InstructionParser.TryParseResponse(llmText, out StepsResponse plan, out string parseErr))
            {
                Console.WriteLine($"[Parse][Error] {parseErr}");
                // Ask the model to self-correct next turn by continuing loop (it sees previous invalidity via prompt rule 11).
                continue;
            }

            if (plan.Steps == null) plan.Steps = new List<Step>();

            if (plan.Steps.Count > settings.MaxSteps)
            {
                Console.WriteLine($"[Guard] steps > {settings.MaxSteps}, truncating.");
                plan.Steps = plan.Steps.GetRange(0, settings.MaxSteps);
            }

            // Finish condition: no steps and done is a non-empty string
            if (plan.Steps.Count == 0 && !String.IsNullOrEmpty(plan.Done?.ToString()))
            {
                Console.WriteLine($"[Done] {plan.Done.ToString()}");
                break;
            }

            // Execute steps

            Console.WriteLine($"Received {plan.Steps.Count} step(s):");

            foreach (var step in plan.Steps)
            {
                try
                {
                    Console.WriteLine($"[Do] {step.tool} :: {step.human_readable_justification}");
                    await Executor.ExecuteAsync(step);
                    history += $"Tool: {step.tool}, args: {step.args}{Environment.NewLine}";

                    Thread.Sleep(100);

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Exec][Error] {ex.Message}");
                    // Continue; LLM should recover next turn
                }
                Thread.Sleep(settings.StepDelayMs);
            }
        }


        try
        {
            if (settings.ShowProgressOverlay && overlayForm != null)
            {
                overlayForm.UpdateStatus("������");

                try
                {
                    overlayForm.Invoke(() => overlayForm.Close());
                }
                catch { }
                if (uiThread != null)
                {
                    uiThread.Join(TimeSpan.FromSeconds(2));
                }
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex}");
            return 2;
        }
    
    }

            

    private static string ReadPromptFromConsole()
    {
        Console.Write("Enter objective: ");
        return Console.ReadLine() ?? string.Empty;
    }

#region Win32 Console Allocation
    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE = 0;
#endregion
}
