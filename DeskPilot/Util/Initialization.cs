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
using DesktopAssist.Util;

namespace DesktopAssist.Util;

internal class InitializationResult
{
    public required AppSettings Settings { get; set; }
    public AppForm? OverlayForm { get; set; }
    public Thread? UiThread { get; set; }
    public Action<string>? StatusCallback { get; set; }
    public required OpenAIClient Client { get; set; }
    public string? Prompt { get; set; }
}

internal static class Initialization
{
    // Keep references to prevent garbage collection
    private static Native.ConsoleCtrlHandlerRoutine? _consoleCtrlHandler;
    private static Thread? _consoleMonitorThread;
    private static bool _consoleClosed = false;

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
                    Log.Error("UI", ex);
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
                    
                    // Start console monitoring thread to detect window close
                    _consoleMonitorThread = new Thread(MonitorConsoleWindow)
                    {
                        IsBackground = true,
                        Name = "ConsoleMonitor"
                    };
                    _consoleMonitorThread.Start();
                    
                    // Also set up console control handler as backup
                    _consoleCtrlHandler = new Native.ConsoleCtrlHandlerRoutine(ConsoleCtrlHandler);
                    Native.SetConsoleCtrlHandler(_consoleCtrlHandler, true);
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
            Log.Warn("Init", "No prompt provided. Exiting.");
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

    Log.Info("Init", $"Starting DesktopAssist with prompt: {prompt}");

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

    private static void MonitorConsoleWindow()
    {
        IntPtr consoleHwnd = IntPtr.Zero;
        
        // Wait a bit for console to be fully created
        Thread.Sleep(500);
        
        while (!_consoleClosed)
        {
            try
            {
                // Get current console window handle
                IntPtr currentHwnd = Native.GetConsoleWindow();
                
                if (currentHwnd != IntPtr.Zero)
                {
                    consoleHwnd = currentHwnd;
                }
                else if (consoleHwnd != IntPtr.Zero)
                {
                    // Console window was closed
                    Log.Warn("Console", "Console window closed. Exiting application...");
                    Environment.Exit(0);
                }
                
                Thread.Sleep(100); // Check every 100ms
            }
            catch
            {
                // If we can't check, assume console is closed
                break;
            }
        }
    }

    private static bool ConsoleCtrlHandler(uint dwCtrlType)
    {
        // Handle console close event (CTRL_CLOSE_EVENT = 2)
        if (dwCtrlType == Native.CTRL_CLOSE_EVENT)
        {
            Log.Warn("Console", "Console window closed. Exiting application...");
            // Use Environment.Exit to immediately terminate the process
            // This is appropriate for console applications when the console window is closed
            Environment.Exit(0);
        }
        return false; // Let other handlers process the event
    }

    private static string ReadPromptFromConsole()
    {
        Console.Write("Enter objective: ");
        return Console.ReadLine() ?? string.Empty;
    }
}