using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DesktopAssist.Engine;
using DesktopAssist.Settings;
using System.Runtime.InteropServices;

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
        var engine = new ExecutionEngine(settings, statusCb);
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) => {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("Cancellation requested...");
        };

        try
        {
            var result = await engine.RunAsync(prompt, cts.Token).ConfigureAwait(false);
            if (settings.ShowProgressOverlay && overlayForm != null)
            {
                overlayForm.UpdateStatus("Done");
                try { await Task.Delay(1200, CancellationToken.None); } catch { }
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
            Console.WriteLine($"Result: {result}");
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
