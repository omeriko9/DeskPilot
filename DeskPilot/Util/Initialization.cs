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

namespace DesktopAssist.Util;

internal class InitializationResult
{
    public AppSettings Settings { get; set; }
    public AppForm? OverlayForm { get; set; }
    public Thread? UiThread { get; set; }
    public Action<string>? StatusCallback { get; set; }
    public OpenAIClient Client { get; set; }
    public string? Prompt { get; set; }
}

internal static class Initialization
{
    // Packs initialization concerns (settings, UI thread, console config, prompt acquisition, LLM client)
    internal static InitializationResult Initialize(string[] args)
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
                var hasConsole = Native.GetConsoleWindow() != IntPtr.Zero;
                if (settings.DebugConsole)
                {
                    if (!hasConsole) Native.AllocConsole();
                    Console.OutputEncoding = Encoding.UTF8;
                    Console.InputEncoding = Encoding.UTF8;
                }
                else if (hasConsole)
                {
                    var hWnd = Native.GetConsoleWindow();
                    if (hWnd != IntPtr.Zero) Native.ShowWindow(hWnd, Native.SW_HIDE);
                }
            }
            catch { }
        }

        string prompt = args.Length > 0 ? string.Join(" ", args) : ReadPromptFromConsole();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Console.WriteLine("No prompt provided. Exiting.");
            return new InitializationResult
            {
                Settings = settings,
                OverlayForm = overlayForm,
                UiThread = uiThread,
                StatusCallback = null,
                Client = new OpenAIClient(settings.BaseUrl, settings.ApiKey, settings.Model),
                Prompt = null
            };
        }

        Console.WriteLine($"Starting DesktopAssist with prompt: {prompt}");

        Action<string>? statusCb = null;
        if (settings.ShowProgressOverlay && overlayForm != null)
        {
            statusCb = s => { try { overlayForm.UpdateStatus(s); } catch { } };
        }

        var client = new OpenAIClient(settings.BaseUrl, settings.ApiKey, settings.Model);
        return new InitializationResult
        {
            Settings = settings,
            OverlayForm = overlayForm,
            UiThread = uiThread,
            StatusCallback = statusCb,
            Client = client,
            Prompt = prompt
        };
    }

    private static string ReadPromptFromConsole()
    {
        Console.Write("Enter objective: ");
        return Console.ReadLine() ?? string.Empty;
    }
}