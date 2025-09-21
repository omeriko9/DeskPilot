using System;
using System.Drawing;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using DesktopAssist.Engine;
using DesktopAssist.Llm.Models;
using DesktopAssist.Screen;

namespace DesktopAssist;

public class ReplayForm : Form
{
    private TextBox _input = null!;
    private Button _btnReplay = null!;
    private Button _btnClose = null!;
    private ListBox _log = null!;
    private CheckBox _chkSequentialDelay = null!;
    private NumericUpDown _delayMs = null!;
    private bool _isReplaying;

    public ReplayForm() => InitializeComponents();

    private void InitializeComponents()
    {
        Text = "Replay Steps (Ctrl+R)";
        Width = 820; Height = 620;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font(FontFamily.GenericSansSerif, 9);

        _input = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Top,
            Height = 260,
            AcceptsReturn = true,
            AcceptsTab = true,
            Font = new Font(FontFamily.GenericMonospace, 9)
        };
        _btnReplay = new Button { Text = "Replay", Width = 120 };
        _btnClose = new Button { Text = "Close", Width = 100 };
        _chkSequentialDelay = new CheckBox { Text = "Inter-step delay (ms)", Checked = true, AutoSize = true };
        _delayMs = new NumericUpDown { Minimum = 0, Maximum = 5000, Value = 150, Width = 80 };
        _log = new ListBox { Dock = DockStyle.Fill };

        var panelButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight, AutoSize = false };
        panelButtons.Controls.AddRange(new Control[] { _btnReplay, _btnClose, _chkSequentialDelay, _delayMs });

        Controls.Add(_log);
        Controls.Add(panelButtons);
        Controls.Add(_input);

        _btnReplay.Click += async (_, _) => await ReplayAsync();
        _btnClose.Click += (_, _) => Close();
    }

    private void LogLine(string msg)
    {
        try
        {
            _log.Items.Add(DateTime.Now.ToString("HH:mm:ss") + " " + msg);
            _log.TopIndex = _log.Items.Count - 1;
        }
        catch { }
    }

    private async Task ReplayAsync()
    {
        if (_isReplaying) return;
        _isReplaying = true;
        _btnReplay.Enabled = false;
        try
        {
            string raw = _input.Text;
            if (string.IsNullOrWhiteSpace(raw)) { LogLine("No JSON provided."); return; }
            StepsResponse? resp = null;
            try
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                resp = JsonSerializer.Deserialize<StepsResponse>(raw, opts);
            }
            catch (Exception ex)
            {
                LogLine("Parse error: " + ex.Message);
                return;
            }
            if (resp?.Steps == null || resp.Steps.Count == 0) { LogLine("No steps found in JSON."); return; }
            LogLine($"Replaying {resp.Steps.Count} step(s)...");

            var steps = resp.Steps;
            int delay = (int)_delayMs.Value;
            bool doDelay = _chkSequentialDelay.Checked;
            foreach (var step in steps)
            {
                LogLine($"-> {step.tool} : {Truncate(step.human_readable_justification, 80)}");
                try { await Executor.ExecuteAsync(step); }
                catch (Exception ex) { LogLine("Step error: " + ex.Message); }
                if (doDelay && delay > 0) await Task.Delay(delay);
            }

            Screenshot.CapturePrimaryPngBase64();
            LogLine("Replay complete.");
        }
        finally
        {
            _btnReplay.Enabled = true;
            _isReplaying = false;
        }
    }

    private static string Truncate(string s, int max) => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max] + "...");
}