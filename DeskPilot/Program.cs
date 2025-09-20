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

        
        await AutomationEngine.RunAsync(initResult.Settings, initResult.Client, initResult.Prompt, initResult.StatusCallback);

        try
        {
            if (initResult.Settings.ShowProgressOverlay && initResult.OverlayForm != null)
            {
                initResult.OverlayForm.UpdateStatus("Done");

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
            
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex}");
            return 2;
        }
    
    }
}
