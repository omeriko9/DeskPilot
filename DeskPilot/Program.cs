using DesktopAssist.Automation.Input;
using DesktopAssist.Engine;
using DesktopAssist.Llm;
using DesktopAssist.Llm.Models;
using DesktopAssist.Screen;
using DesktopAssist.Settings;
using DesktopAssist.Util;
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
        var initResult = Initialization.Initialize(args);
        if (initResult.Prompt == null)
            return 1; // initialization already reported error

        bool isRemoteProvider = initResult.Settings.LlmProvider.Equals("remote", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (isRemoteProvider)
            {
                // Remote loop: after each run, poll again for new work
                while (true)
                {
                    var prompt = initResult.Prompt;
                    await AutomationEngine.RunAsync(initResult.Settings, initResult.Client, prompt!, initResult.StatusCallback);

                    initResult.StatusCallback?.Invoke("Idle - waiting for remote work...");
                    Log.Info("RemoteLoop", "Task completed. Returning to polling for next work item.");

                    // Poll again for next request (blocking)
                    var next = Initialization.PollRemoteForPrompt(initResult.Settings.RemoteUrl);
                    initResult.Prompt = next; // update prompt for next iteration
                }
            }
            else
            {
                await AutomationEngine.RunAsync(initResult.Settings, initResult.Client, initResult.Prompt, initResult.StatusCallback);
            }
        }
        finally
        {
            if (initResult.Settings.ShowProgressOverlay && initResult.OverlayForm != null)
            {
                try
                {
                    initResult.OverlayForm.Invoke(() => initResult.OverlayForm.Close());
                }
                catch { }
                if (initResult.UiThread != null)
                {
                    initResult.UiThread.Join(TimeSpan.FromSeconds(2));
                }
            }
        }

        return 0;
    }
}
