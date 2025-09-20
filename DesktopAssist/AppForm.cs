using System;
using System.Drawing;
using System.Windows.Forms;

namespace DesktopAssist;

/// <summary>
/// Lightweight always-on-top overlay used (optionally) to surface real-time status:
///  - "Thinking..." while waiting for LLM response
///  - Individual step function & justification while executing
/// Enabled via AppSettings.ShowProgressOverlay (default true).
/// Sits at top-left quarter of the primary screen.
/// </summary>
public class AppForm : Form
{
    private readonly Label _label;
    private readonly object _lock = new();
    private readonly System.Windows.Forms.Timer _thinkingTimer;
    private DateTime _thinkingStartUtc;
    private bool _isThinking;
    public const string ThinkingBaseText = "Thinking..."; // public canonical baseline
    private string _thinkingBaseText = ThinkingBaseText; // internal mutable baseline

    public AppForm()
    {
        Text = "DesktopAssist Progress";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        ForeColor = Color.White;
        Opacity = 0.88;

    var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        Width = Math.Max(400, screen.Width / 4);
        Height = Math.Max(250, screen.Height / 4);
        Left = 0;
        Top = 0;

        _label = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font(FontFamily.GenericSansSerif, 20, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Text = string.Empty,
            AutoEllipsis = true
        };
        Controls.Add(_label);

        _thinkingTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000 // 1s updates
        };
        _thinkingTimer.Tick += (_, _) => UpdateThinkingElapsed();
    }

    public void UpdateStatus(string text)
    {
        if (IsDisposed) return;
        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateStatus), text);
                return;
            }
            lock (_lock)
            {
                bool nextThinking = text.StartsWith(_thinkingBaseText, StringComparison.OrdinalIgnoreCase);
                if (nextThinking)
                {
                    // Normalize base text to exactly what was passed (to allow future variants like "Thinking (retry)...")
                    _thinkingBaseText = text;
                    if (!_isThinking)
                    {
                        _isThinking = true;
                        _thinkingStartUtc = DateTime.UtcNow;
                        _thinkingTimer.Start();
                    }
                    // Immediate update (0s)
                    _label.Text = _thinkingBaseText + "\n0s";
                }
                else
                {
                    if (_isThinking)
                    {
                        _thinkingTimer.Stop();
                        _isThinking = false;
                    }
                    _label.Text = text;
                }
            }
        }
        catch { /* non-fatal */ }
    }

    private void UpdateThinkingElapsed()
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(UpdateThinkingElapsed)); } catch { }
            return;
        }
        lock (_lock)
        {
            if (!_isThinking) return;
            var secs = (int)Math.Floor((DateTime.UtcNow - _thinkingStartUtc).TotalSeconds);
            _label.Text = _thinkingBaseText + "\n" + secs + "s";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _thinkingTimer?.Stop(); } catch { }
            _thinkingTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
