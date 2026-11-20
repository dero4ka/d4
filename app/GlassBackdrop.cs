using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace D4;

/// <summary>
/// «Жидкое стекло» как в iOS: снимок того, что лежит под панелью, сильно размывается, подсвечивается
/// и становится фоном панели. Работает и для прозрачных окон (виджеты, всплывающие меню), где акрил Windows недоступен.
/// </summary>
internal static class GlassBackdrop
{
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    /// <summary>Фон-стекло для элемента. Вызывать, пока сама панель ещё невидима (иначе в снимок попадёт она сама).</summary>
    public static Brush? Bake(FrameworkElement el)
    {
        try
        {
            if (el.ActualWidth < 8 || el.ActualHeight < 8) return null;
            var dpi = VisualTreeHelper.GetDpi(el);
            var origin = el.PointToScreen(new Point(0, 0));
            int w = (int)Math.Round(el.ActualWidth * dpi.DpiScaleX);
            int h = (int)Math.Round(el.ActualHeight * dpi.DpiScaleY);
            int x = (int)Math.Round(origin.X), y = (int)Math.Round(origin.Y);
            if (w < 8 || h < 8 || w > 3000 || h > 2200) return null;

            const int pad = 36; // запас по краям, чтобы размытие не темнело у границ
            int cx = x - pad, cy = y - pad, cw = w + pad * 2, ch = h + pad * 2;

            BitmapSource shot;
            using (var bmp = new System.Drawing.Bitmap(cw, ch, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.CopyFromScreen(cx, cy, 0, 0, bmp.Size, System.Drawing.CopyPixelOperation.SourceCopy);
                var hb = bmp.GetHbitmap();
                try
                {
                    shot = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hb, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    shot.Freeze();
                }
                finally
                {
                    DeleteObject(hb);
                }
            }

            // сильное размытие
            var image = new Image
            {
                Source = shot,
                Width = cw,
                Height = ch,
                Stretch = Stretch.Fill,
                Effect = new BlurEffect { Radius = 26, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
            };
            image.Measure(new Size(cw, ch));
            image.Arrange(new Rect(0, 0, cw, ch));
            var blurred = new RenderTargetBitmap(cw, ch, 96, 96, PixelFormats.Pbgra32);
            blurred.Render(image);

            // собираем стекло: размытие + цвет темы + белая «дымка» + блик сверху
            var t = ThemeManager.Current;
            var bg = ThemeManager.C(t.Bg);
            var acc = ThemeManager.C(t.Accent);
            var acc2 = ThemeManager.C(t.Accent2);
            var base1 = Color.FromRgb((byte)(bg.R + (acc.R - bg.R) * .3), (byte)(bg.G + (acc.G - bg.G) * .3), (byte)(bg.B + (acc.B - bg.B) * .3));
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var rect = new Rect(0, 0, w, h);
                dc.DrawImage(new CroppedBitmap(blurred, new Int32Rect(pad, pad, w, h)), rect);

                // основа: тёмный (или светлый) оттенок темы, чтобы текст читался
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(t.Dark ? 0x58 : 0x3A), base1.R, base1.G, base1.B)), null, rect);

                // цветная «дымка»: от акцента к второму акценту по диагонали
                var haze = new LinearGradientBrush(
                    Color.FromArgb((byte)(t.Dark ? 0x42 : 0x50), acc.R, acc.G, acc.B),
                    Color.FromArgb((byte)(t.Dark ? 0x26 : 0x34), acc2.R, acc2.G, acc2.B), 45);
                dc.DrawRectangle(haze, null, rect);

                // мягкий блик сверху в оттенке темы
                var hi = Color.FromRgb((byte)(acc.R + (255 - acc.R) * .6), (byte)(acc.G + (255 - acc.G) * .6), (byte)(acc.B + (255 - acc.B) * .6));
                var gloss = new LinearGradientBrush(Color.FromArgb((byte)(t.Dark ? 0x34 : 0x66), hi.R, hi.G, hi.B), Color.FromArgb(0, hi.R, hi.G, hi.B), 90);
                dc.DrawRectangle(gloss, null, new Rect(0, 0, w, h * 0.45));
            }
            var final = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            final.Render(dv);
            final.Freeze();

            var brush = new ImageBrush(final) { Stretch = Stretch.Fill };
            brush.Freeze();
            return brush;
        }
        catch (Exception ex)
        {
            Settings.Log("glass bake: " + ex.Message);
            return null;
        }
    }
}
