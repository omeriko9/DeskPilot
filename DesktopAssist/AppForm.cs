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
                _label.Text = text;
            }
        }
        catch { /* non-fatal */ }
    }
}
