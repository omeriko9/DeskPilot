using DesktopAssist.Automation.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DesktopAssist.Screen
{
    internal static class Screenshot
    {

        private static byte[] DrawAxesOverlay(byte[] png, int w, int h)
        {
            using var msIn = new MemoryStream(png);
            using var bmp = new Bitmap(msIn);
            using var g = Graphics.FromImage(bmp);

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

            // Grid setup: 50px minor, 200px major
            const int minor = 50, major = 200;
            using var penMinor = new Pen(Color.FromArgb(80, 255, 255, 255), 1);
            using var penMajor = new Pen(Color.FromArgb(220, 255, 255, 255), 2);

            // Fonts & brushes: large, bold, with stroke outline for contrast
            using var font = new Font("Consolas", 28, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brushText = new SolidBrush(Color.White);
            using var brushShadow = new SolidBrush(Color.Black);

            void DrawOutlinedString(string s, float x, float y)
            {
                // 1px “stroke” by drawing 4 black offsets then white text
                g.DrawString(s, font, brushShadow, x + 1, y);
                g.DrawString(s, font, brushShadow, x - 1, y);
                g.DrawString(s, font, brushShadow, x, y + 1);
                g.DrawString(s, font, brushShadow, x, y - 1);
                g.DrawString(s, font, brushText, x, y);
            }

            // Vertical lines + labels on top & bottom
            for (int x = 0; x <= w; x += minor)
            {
                bool isMajor = (x % major) == 0;
                g.DrawLine(isMajor ? penMajor : penMinor, x, 0, x, h);
                if (isMajor)
                {
                    var label = x.ToString();
                    DrawOutlinedString(label, x + 4, 2);            // top
                    DrawOutlinedString(label, x + 4, h - 34);       // bottom
                }
            }

            // Horizontal lines + labels on left & right
            for (int y = 0; y <= h; y += minor)
            {
                bool isMajor = (y % major) == 0;
                g.DrawLine(isMajor ? penMajor : penMinor, 0, y, w, y);
                if (isMajor)
                {
                    var label = y.ToString();
                    DrawOutlinedString(label, 2, y + 2);            // left
                    DrawOutlinedString(label, w - 90, y + 2);       // right (room for 4 digits)
                }
            }

            // Corner banner with exact image size and origin
            string banner = $"image={w}x{h} px | origin=(0,0) top-left | grid: minor=50, major=200";
            var size = g.MeasureString(banner, font);
            var rect = new RectangleF(8, h - size.Height - 10, size.Width + 14, size.Height + 6);
            using (var bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0))) g.FillRectangle(bg, rect);
            DrawOutlinedString(banner, rect.X + 7, rect.Y + 3);

            using var msOut = new MemoryStream();
            bmp.Save(msOut, System.Drawing.Imaging.ImageFormat.Png);
            return msOut.ToArray();
        }

        private static byte[] DrawCursorOverlay(byte[] png, int w, int h)
        {
            using var msIn = new MemoryStream(png);
            using var bmp = new Bitmap(msIn);
            using var g = Graphics.FromImage(bmp);

            // get cursor pos in SCREEN px, map to IMAGE px
            if (!Native.GetCursorPos(out var p)) return png;
            // map screen→image: inverse of MapFromImagePx
            // Our image is the virtual desktop [left,top]..[left+width,top+height]
            int vx = ScreenSnapshotInfo.VirtualLeft;
            int vy = ScreenSnapshotInfo.VirtualTop;
            int vw = ScreenSnapshotInfo.VirtualWidth;
            int vh = ScreenSnapshotInfo.VirtualHeight;
            if (vw <= 0 || vh <= 0) return png;

            double ix = (p.X - vx) * (double)w / vw;
            double iy = (p.Y - vy) * (double)h / vh;
            int cx = (int)Math.Round(ix);
            int cy = (int)Math.Round(iy);

            // draw crosshair + coordinates text
            using var pen = new Pen(Color.FromArgb(240, 255, 64, 64), 2);
            int len = 14;
            g.DrawLine(pen, Math.Max(0, cx - len), cy, Math.Min(w - 1, cx + len), cy);
            g.DrawLine(pen, cx, Math.Max(0, cy - len), cx, Math.Min(h - 1, cy + len));

            using var font = new Font("Consolas", 24, FontStyle.Bold, GraphicsUnit.Pixel);
            using var bg = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            using var fg = new SolidBrush(Color.White);
            var label = $"cursor=({cx},{cy})";
            var size = g.MeasureString(label, font);
            var rect = new RectangleF(8, 8, size.Width + 12, size.Height + 8);
            g.FillRectangle(bg, rect);
            g.DrawString(label, font, fg, rect.X + 6, rect.Y + 4);

            using var msOut = new MemoryStream();
            bmp.Save(msOut, System.Drawing.Imaging.ImageFormat.Png);
            return msOut.ToArray();
        }


        private static (byte[] png, Size size) CaptureVirtualDesktopPng()
        {
            ScreenSnapshotInfo.RefreshVirtualMetrics();

            int vx = ScreenSnapshotInfo.VirtualLeft;
            int vy = ScreenSnapshotInfo.VirtualTop;
            int vw = ScreenSnapshotInfo.VirtualWidth;
            int vh = ScreenSnapshotInfo.VirtualHeight;

            using var bmp = new Bitmap(vw, vh, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                // Copy from the virtual desktop coordinates into our 0,0 destination.
                // Handles negative vx/vy correctly.
                g.CopyFromScreen(vx, vy, 0, 0, new Size(vw, vh), CopyPixelOperation.SourceCopy);
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);

            var png = ms.ToArray();
            ScreenSnapshotInfo.SetImageSize(bmp.Size);
            return (png, bmp.Size);
        }

        private static byte[] DrawInsetAroundCursor(byte[] png, int w, int h)
        {
            using var msIn = new MemoryStream(png);
            using var baseBmp = new Bitmap(msIn);
            using var gBase = Graphics.FromImage(baseBmp);

            // Current cursor -> image px
            if (!Native.GetCursorPos(out var p)) return png;
            int vx = ScreenSnapshotInfo.VirtualLeft, vy = ScreenSnapshotInfo.VirtualTop;
            int vw = ScreenSnapshotInfo.VirtualWidth, vh = ScreenSnapshotInfo.VirtualHeight;
            if (vw <= 0 || vh <= 0) return png;

            int cx = (int)Math.Round((p.X - vx) * (double)w / vw);
            int cy = (int)Math.Round((p.Y - vy) * (double)h / vh);

            // Crop region (source space)
            const int srcBox = 160; // 160×160 px around cursor
            int half = srcBox / 2;
            int sx = Math.Max(0, Math.Min(w - srcBox, cx - half));
            int sy = Math.Max(0, Math.Min(h - srcBox, cy - half));

            // Create magnified bitmap
            const int scale = 6;
            var magBmp = new Bitmap(srcBox * scale, srcBox * scale, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var gMag = Graphics.FromImage(magBmp))
            {
                gMag.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                gMag.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                gMag.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                gMag.DrawImage(baseBmp,
                    new Rectangle(0, 0, magBmp.Width, magBmp.Height),
                    new Rectangle(sx, sy, srcBox, srcBox),
                    GraphicsUnit.Pixel);

                // fine grid: every 10 px (source), bold at 50
                using var penMinor = new Pen(Color.FromArgb(140, 255, 255, 255), 1);
                using var penMajor = new Pen(Color.FromArgb(220, 255, 255, 255), 2);
                for (int i = 0; i <= srcBox; i += 10)
                {
                    bool major = (i % 50) == 0;
                    int X = i * scale, Y = i * scale;
                    gMag.DrawLine(major ? penMajor : penMinor, X, 0, X, magBmp.Height - 1);
                    gMag.DrawLine(major ? penMajor : penMinor, 0, Y, magBmp.Width - 1, Y);
                }

                // mark cursor exact point inside inset
                int localX = (cx - sx) * scale;
                int localY = (cy - sy) * scale;
                using var penCursor = new Pen(Color.FromArgb(255, 255, 64, 64), 2);
                gMag.DrawLine(penCursor, Math.Max(0, localX - 12), localY, Math.Min(magBmp.Width - 1, localX + 12), localY);
                gMag.DrawLine(penCursor, localX, Math.Max(0, localY - 12), localX, Math.Min(magBmp.Height - 1, localY + 12));

                // legend with absolute image coords at inset center
                using var font = new Font("Consolas", 22, FontStyle.Bold, GraphicsUnit.Pixel);
                using var bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
                using var fg = new SolidBrush(Color.White);
                var centerAbs = $"inset_center_img=({sx + srcBox / 2},{sy + srcBox / 2})  cursor_img=({cx},{cy})";
                var sz = gMag.MeasureString(centerAbs, font);
                var rect = new RectangleF(6, 6, sz.Width + 12, sz.Height + 10);
                gMag.FillRectangle(bg, rect);
                gMag.DrawString(centerAbs, font, fg, rect.X + 6, rect.Y + 4);
            }

            // paste inset onto base (bottom-right with padding)
            int pad = 10;
            int dstX = Math.Max(pad, w - magBmp.Width - pad);
            int dstY = Math.Max(pad, h - magBmp.Height - pad);
            gBase.DrawImage(magBmp, dstX, dstY);

            using var msOut = new MemoryStream();
            baseBmp.Save(msOut, System.Drawing.Imaging.ImageFormat.Png);
            return msOut.ToArray();
        }


        public static (string b64, Size size) CapturePrimaryPngBase64()
        {
            /*
            var primary = Screen.PrimaryScreen;
            if (primary == null)
            {
                // Fallback: return empty 1x1 PNG
                using var tiny = new Bitmap(1, 1, PixelFormat.Format24bppRgb);
                using var msTiny = new System.IO.MemoryStream();
                tiny.Save(msTiny, ImageFormat.Png);
                return Convert.ToBase64String(msTiny.ToArray());
            }
            var bounds = primary.Bounds;
            using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                try
                {
                    // Lightweight grid every 200px (visual guidance for LLM). Subtle alpha to reduce distraction.
                    int step = 200;
                    using var pen = new Pen(Color.FromArgb(80, 0, 180, 255), 1f);
                    using var font = new Font(FontFamily.GenericSansSerif, 8f, FontStyle.Regular, GraphicsUnit.Pixel);
                    using var labelBrush = new SolidBrush(Color.FromArgb(160, 0, 120, 200));
                    // Vertical lines
                    for (int x = step; x < bounds.Width; x += step)
                    {
                        g.DrawLine(pen, x, 0, x, bounds.Height);
                        g.DrawString(x.ToString(), font, labelBrush, x + 2, 2);
                    }
                    // Horizontal lines
                    for (int y = step; y < bounds.Height; y += step)
                    {
                        g.DrawLine(pen, 0, y, bounds.Width, y);
                        g.DrawString(y.ToString(), font, labelBrush, 2, y + 2);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[GridOverlay][Warn] " + ex.Message);
                }
            }
            */


            var (pngBytes, size) = CaptureVirtualDesktopPng();
            var pngWithAxes = DrawAxesOverlay(pngBytes, size.Width, size.Height);
            var pngFinal = DrawCursorOverlay(pngWithAxes, size.Width, size.Height);
            var final2 = DrawInsetAroundCursor(pngFinal, size.Width, size.Height);
            string screenshotBase64 = Convert.ToBase64String(final2);

            // (Optionally) log:
            Console.WriteLine($"[Snap] image={size.Width}x{size.Height} virtual={ScreenSnapshotInfo.VirtualWidth}x{ScreenSnapshotInfo.VirtualHeight} origin=({ScreenSnapshotInfo.VirtualLeft},{ScreenSnapshotInfo.VirtualTop})");
            // save screenshotBase64 to disk as a PNG for debugging:
            System.IO.File.WriteAllBytes("screenshot.png", pngFinal);


            return (screenshotBase64, size);
        }
    }
}
