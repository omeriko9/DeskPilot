using DesktopAssist.Settings;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace DesktopAssist.Screen;

public sealed class ScreenshotServiceOld
{
    private DateTime _lastCaptureUtc = DateTime.MinValue;
    private string _lastBase64 = string.Empty;

    public (int width, int height) GetPrimarySize()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        return (bounds.Width, bounds.Height);
    }

    public string CaptureBase64Jpeg(AppSettings settings)
    {
        var now = DateTime.UtcNow;
        if (settings.ReuseScreenshotWithinMs > 0 && (now - _lastCaptureUtc).TotalMilliseconds <= settings.ReuseScreenshotWithinMs && !string.IsNullOrEmpty(_lastBase64))
        {
            return _lastBase64; // reuse recent
        }
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        using var full = new Bitmap(bounds.Width, bounds.Height);
        using (var g = Graphics.FromImage(full))
        {
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, full.Size);
        }

        Bitmap working = full;
        if (settings.ReducedScreenshotMode)
        {
            double ratio = Math.Min((double)settings.ScreenshotMaxWidth / working.Width,
                                    (double)settings.ScreenshotMaxHeight / working.Height);
            if (ratio < 1.0)
            {
                int nw = Math.Max(2, (int)(working.Width * ratio));
                int nh = Math.Max(2, (int)(working.Height * ratio));
                var resized = new Bitmap(nw, nh);
                using var rg = Graphics.FromImage(resized);
                rg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                rg.DrawImage(working, 0, 0, nw, nh);
                working = resized;
            }
        }
        // Additional downscale for follow-up steps if requested (applied when max differs)
        if (settings.ReducedScreenshotMode && settings.FollowupScreenshotMaxWidth > 0 && settings.FollowupScreenshotMaxHeight > 0 &&
            (settings.FollowupScreenshotMaxWidth < settings.ScreenshotMaxWidth || settings.FollowupScreenshotMaxHeight < settings.ScreenshotMaxHeight))
        {
            double ratio2 = Math.Min((double)settings.FollowupScreenshotMaxWidth / working.Width,
                                     (double)settings.FollowupScreenshotMaxHeight / working.Height);
            if (ratio2 < 1.0)
            {
                int nw2 = Math.Max(2, (int)(working.Width * ratio2));
                int nh2 = Math.Max(2, (int)(working.Height * ratio2));
                var resized2 = new Bitmap(nw2, nh2);
                using var rg2 = Graphics.FromImage(resized2);
                rg2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                rg2.DrawImage(working, 0, 0, nw2, nh2);
                if (!ReferenceEquals(working, full)) working.Dispose();
                working = resized2;
            }
        }

        using var ms = new MemoryStream();
        try
        {
            ImageCodecInfo? jpegEncoder = Array.Find(ImageCodecInfo.GetImageEncoders(), e => e.FormatID == ImageFormat.Jpeg.Guid);
            if (jpegEncoder != null)
            {
                using var encParams = new EncoderParameters(1);
                encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Clamp(settings.ScreenshotJpegQuality, 1, 100));
                working.Save(ms, jpegEncoder, encParams);
            }
            else
            {
                working.Save(ms, ImageFormat.Jpeg);
            }
        }
        finally
        {
            if (!ReferenceEquals(working, full)) working.Dispose();
        }
        _lastBase64 = Convert.ToBase64String(ms.ToArray());
        _lastCaptureUtc = now;
        return _lastBase64;
    }
}
