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
        var (settings, overlayForm, uiThread, statusCb, client, prompt) = Initialize(args);
        if (prompt == null) return 1; // initialization already reported error
        await AutomationEngine.RunAsync(settings, client, prompt, statusCb);


        try
        {
            if (settings.ShowProgressOverlay && overlayForm != null)
            {
                overlayForm.UpdateStatus("Done");

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

    // Packs initialization concerns (settings, UI thread, console config, prompt acquisition, LLM client)
    private static (AppSettings settings, AppForm? overlayForm, Thread? uiThread, Action<string>? statusCb, OpenAIClient client, string? prompt) Initialize(string[] args)
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

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var hasConsole = GetConsoleWindow() != IntPtr.Zero;
                if (settings.DebugConsole)
                {
                    if (!hasConsole) AllocConsole();
                    Console.OutputEncoding = Encoding.UTF8;
                    Console.InputEncoding = Encoding.UTF8;
                }
                else if (hasConsole)
                {
                    var hWnd = GetConsoleWindow();
                    if (hWnd != IntPtr.Zero) ShowWindow(hWnd, SW_HIDE);
                }
            }
            catch { }
        }

        string prompt = args.Length > 0 ? string.Join(" ", args) : ReadPromptFromConsole();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Console.WriteLine("No prompt provided. Exiting.");
            return (settings, overlayForm, uiThread, null, new OpenAIClient(settings.BaseUrl, settings.ApiKey, settings.Model), null);
        }

        Console.WriteLine($"Starting DesktopAssist with prompt: {prompt}");

        Action<string>? statusCb = null;
        if (settings.ShowProgressOverlay && overlayForm != null)
        {
            statusCb = s => { try { overlayForm.UpdateStatus(s); } catch { } };
        }

        var client = new OpenAIClient(settings.BaseUrl, settings.ApiKey, settings.Model);
        return (settings, overlayForm, uiThread, statusCb, client, prompt);
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
