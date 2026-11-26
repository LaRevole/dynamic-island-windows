using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DynamicIsland.Services;

public enum WallpaperFit { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

/// <summary>Where Windows draws a wallpaper picture on a monitor (pure maths, unit-tested).</summary>
public static class WallpaperLayout
{
    /// <summary>
    /// The rectangle the picture covers, in the same units as <paramref name="monitor"/> (Tile and Center use
    /// the picture's own size; Span uses <paramref name="virtualScreen"/> as the area to fill).
    /// </summary>
    public static Rect Place(WallpaperFit fit, Size image, Rect monitor, Rect virtualScreen)
    {
        if (image.Width <= 0 || image.Height <= 0) return monitor;
        var area = fit == WallpaperFit.Span ? virtualScreen : monitor;
        switch (fit)
        {
            case WallpaperFit.Stretch:
                return area;
            case WallpaperFit.Fit:
            {
                double s = Math.Min(area.Width / image.Width, area.Height / image.Height);
                return Centered(area, image.Width * s, image.Height * s);
            }
            case WallpaperFit.Center:
            case WallpaperFit.Tile:
                return fit == WallpaperFit.Tile ? new Rect(area.X, area.Y, image.Width, image.Height) : Centered(area, image.Width, image.Height);
            default:   // Fill, Span
            {
                double s = Math.Max(area.Width / image.Width, area.Height / image.Height);
                return Centered(area, image.Width * s, image.Height * s);
            }
        }
    }

    private static Rect Centered(Rect area, double w, double h) => new(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);

    /// <summary>
    /// The part of a monitor's (scaled-down) blurred picture behind a rectangle given in screen pixels.
    /// scale = bitmap pixels per screen pixel.
    /// </summary>
    public static Rect Crop(Rect screenPx, Rect monitorPx, double scale) =>
        new((screenPx.X - monitorPx.X) * scale, (screenPx.Y - monitorPx.Y) * scale, screenPx.Width * scale, screenPx.Height * scale);

    /// <summary>The monitor that holds the centre of r (the first one if none does).</summary>
    public static int MonitorAt(IReadOnlyList<Rect> monitors, Rect r)
    {
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        for (int i = 0; i < monitors.Count; i++) if (monitors[i].Contains(c)) return i;
        int best = 0; double bestD = double.MaxValue;
        for (int i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            double dx = Math.Max(0, Math.Max(m.Left - c.X, c.X - m.Right)), dy = Math.Max(0, Math.Max(m.Top - c.Y, c.Y - m.Bottom));
            if (dx * dx + dy * dy < bestD) { bestD = dx * dx + dy * dy; best = i; }
        }
        return best;
    }

    /// <summary>Relative luminance (sRGB, 0..1) as used for WCAG contrast.</summary>
    public static double Luminance(Color c)
    {
        static double Lin(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    public static double Contrast(double l1, double l2) => (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);

    /// <summary>
    /// How much black (light text) or white (dark text) must go over a background of luminance bg for the text to
    /// reach a 4.5:1 contrast — at least <paramref name="min"/>.
    /// </summary>
    public static double ScrimFor(double bg, bool lightText, double min = 0)
    {
        const double target = 4.5;
        if (lightText)
        {
            double maxBg = 1.05 / target - 0.05;                    // white text needs the background at or below this
            return Math.Clamp(bg <= maxBg ? min : Math.Max(min, 1 - maxBg / bg), 0, 0.85);
        }
        double minBg = target * (0.011 + 0.05) - 0.05;   // dark text #1C1C1E has luminance ≈ 0.011
        return Math.Clamp(bg >= minBg ? min : Math.Max(min, (minBg - bg) / (1 - bg)), 0, 0.85);
    }
}

/// <summary>
/// The desktop's picture for glass: what Windows shows on each monitor (picture + fit mode, or a plain colour),
/// built once per change, scaled down and blurred, kept in memory. Widgets show the piece behind them, so
/// nothing is blurred per frame. When a live-wallpaper program draws the desktop instead (Wallpaper Engine,
/// Lively), the picture Windows reports isn't what's on screen: <see cref="LiveWallpaper"/> says so.
/// </summary>
public sealed class WallpaperService
{
    /// <summary>Blurred: the frosted middle of the glass. Clear: barely blurred, what the rim's lens bends.</summary>
    public sealed record Monitor(Rect BoundsPx, BitmapSource Blurred, BitmapSource Clear, double Scale, byte[] Pixels, int Stride);

    private const double Downscale = 1 / 5.0;
    public double BlurPx { get; set; } = 36;

    public IReadOnlyList<Monitor> Monitors { get; private set; } = [];
    public bool Available => Monitors.Count > 0;
    public bool LiveWallpaper { get; private set; }
    /// <summary>A live wallpaper is drawn, but its program's preview picture stands in for it (close enough once blurred).</summary>
    public bool FromLivePreview { get; private set; }
    public event Action? Changed;

    /// <summary>Tests: a picture used instead of the real wallpaper (on the main monitor).</summary>
    public BitmapSource? Override { get; set; }

    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private FileSystemWatcher? _watch;

    public WallpaperService()
    {
        _debounce.Tick += (_, _) => { _debounce.Stop(); Rebuild(); };
    }

    public void Start()
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, e) => { if (e.Category == Microsoft.Win32.UserPreferenceCategory.Desktop) Soon(); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Soon();
        try
        {
            // slide shows and Spotlight replace this file without a settings change
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes");
            if (Directory.Exists(dir))
            {
                _watch = new FileSystemWatcher(dir, "TranscodedWallpaper") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size };
                _watch.Changed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Soon);
                _watch.EnableRaisingEvents = true;
            }
        }
        catch (Exception e) { Log.Write("wallpaper watch: " + e.Message); }
        Rebuild();
    }

    public void Soon() { _debounce.Stop(); _debounce.Start(); }

    /// <summary>Release cached monitor bitmaps and pixel arrays when desktop glass widgets are not in use.</summary>
    public void Clear()
    {
        _debounce.Stop();
        if (Monitors.Count == 0) return;
        Monitors = [];
        Changed?.Invoke();
    }

    /// <summary>Is a live-wallpaper program drawing the desktop right now?</summary>
    public static bool DetectLiveWallpaper()
    {
        foreach (var name in new[] { "wallpaper64", "wallpaper32", "Lively", "livelywpf" })
        {
            var ps = Process.GetProcessesByName(name);
            bool any = ps.Length > 0;
            foreach (var p in ps) p.Dispose();
            if (any) return true;
        }
        return false;
    }

    public void Rebuild()
    {
        var sw = Stopwatch.StartNew();
        LiveWallpaper = Override == null && DetectLiveWallpaper();
        FromLivePreview = false;
        var list = new List<Monitor>();
        try
        {
            if (Override != null) list.Add(Build(PrimaryPx(), (Override, new Size(Override.PixelWidth, Override.PixelHeight)), WallpaperFit.Fill, Colors.Black, PrimaryPx()));
            else list.AddRange(FromWindows());
        }
        catch (Exception e) { Log.Write("wallpaper: " + e.Message); }
        Monitors = list;
        Log.Write($"wallpaper: {list.Count} monitor(s), live wallpaper={LiveWallpaper}, from its preview={FromLivePreview}, built in {sw.ElapsedMilliseconds} ms");
        Changed?.Invoke();
    }

    private static Rect PrimaryPx()
    {
        var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        return new Rect(b.X, b.Y, b.Width, b.Height);
    }

    private IEnumerable<Monitor> FromWindows()
    {
        var dw = (IDesktopWallpaper)new DesktopWallpaperClass();
        try
        {
            uint n = dw.GetMonitorDevicePathCount();
            var fit = WallpaperFit.Fill;
            try { fit = (WallpaperFit)dw.GetPosition(); } catch { /* unknown: Fill */ }
            var bg = dw.GetBackgroundColor();   // COLORREF 0x00BBGGRR
            var color = Color.FromRgb((byte)bg, (byte)(bg >> 8), (byte)(bg >> 16));
            var monitors = new List<(string Id, Rect Px)>();
            for (uint i = 0; i < n; i++)
            {
                var id = dw.GetMonitorDevicePathAt(i);
                RECT r;
                try { r = dw.GetMonitorRECT(id); } catch { continue; }   // a monitor that is off
                monitors.Add((id, new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)));
            }
            var virt = monitors.Count == 0 ? Rect.Empty : monitors.Select(m => m.Px).Aggregate(Rect.Union);
            var previews = LiveWallpaper ? LivePreviews() : [];
            if (LiveWallpaper && previews.Count == 0) return [];   // nothing like what's on screen: the widgets use live blur
            FromLivePreview = previews.Count > 0;
            var result = new List<Monitor>();
            int index = 0;
            foreach (var (id, px) in monitors)
            {
                string? path = null;
                var monitorFit = fit;
                if (FromLivePreview)
                {
                    path = previews.GetValueOrDefault(index) ?? previews.Values.First();
                    monitorFit = WallpaperFit.Fill;
                }
                else try { path = dw.GetWallpaper(id); } catch { /* no wallpaper on that monitor */ }
                index++;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    var t = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                    path = File.Exists(t) ? t : null;
                }
                (BitmapSource, Size)? img = null;
                if (path != null)
                {
                    try { img = Load(path, monitorFit == WallpaperFit.Span ? virt.Width : px.Width); }
                    catch (Exception e) { Log.Write("wallpaper picture: " + e.Message); }
                }
                result.Add(Build(px, img, monitorFit, color, virt));
            }
            return result;
        }
        finally { Marshal.ReleaseComObject(dw); }
    }

    /// <summary>
    /// Wallpaper Engine draws an animated wallpaper Windows knows nothing about; the preview picture of the
    /// wallpaper selected for each monitor (its config.json, next to the program) is the closest still image.
    /// Monitor index → preview file. Read only, nothing about Wallpaper Engine is changed.
    /// </summary>
    private static Dictionary<int, string> LivePreviews()
    {
        var result = new Dictionary<int, string>();
        try
        {
            string? exe = null;
            foreach (var name in new[] { "wallpaper64", "wallpaper32" })
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { exe ??= p.MainModule?.FileName; } catch (Exception e) { Log.Write("wallpaper engine path: " + e.Message); }
                    p.Dispose();
                }
            if (exe == null) return result;
            var cfg = Path.Combine(Path.GetDirectoryName(exe)!, "config.json");
            if (!File.Exists(cfg)) return result;
            using var doc = JsonDocument.Parse(File.ReadAllText(cfg));
            foreach (var user in doc.RootElement.EnumerateObject())
            {
                if (user.Value.ValueKind != JsonValueKind.Object || !user.Value.TryGetProperty("general", out var g) || g.ValueKind != JsonValueKind.Object
                    || !g.TryGetProperty("wallpaperconfig", out var wc) || !wc.TryGetProperty("selectedwallpapers", out var sel) || sel.ValueKind != JsonValueKind.Object) continue;
                int order = 0;
                foreach (var m in sel.EnumerateObject())
                {
                    int idx = m.Name.StartsWith("Monitor") && int.TryParse(m.Name[7..], out var n) ? n : order;
                    order++;
                    if (m.Value.ValueKind != JsonValueKind.Object || !m.Value.TryGetProperty("file", out var f) || f.GetString() is not { Length: > 0 } file) continue;
                    var dir = Path.GetDirectoryName(file);
                    var preview = dir == null ? null : new[] { "preview.jpg", "preview.png", "preview.gif" }.Select(x => Path.Combine(dir, x)).FirstOrDefault(File.Exists);
                    if (preview != null) result[idx] = preview;
                }
            }
        }
        catch (Exception e) { Log.Write("wallpaper engine previews: " + e.Message); }
        return result;
    }

    /// <summary>The picture, decoded no bigger than it will be drawn (never the full 24-megapixel original), and its real size.</summary>
    private static (BitmapSource, Size) Load(string path, double targetWidthPx)
    {
        var bytes = File.ReadAllBytes(path);   // TranscodedWallpaper has no extension: decode from the bytes
        var frame = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        int w = frame.PixelWidth;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.StreamSource = new MemoryStream(bytes);
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        int want = (int)Math.Ceiling(targetWidthPx * Downscale * 1.2);
        if (w > want) bi.DecodePixelWidth = want;
        bi.EndInit();
        bi.Freeze();
        return (bi, new Size(frame.PixelWidth, frame.PixelHeight));   // Center and Tile place it at its real size
    }

    private Monitor Build(Rect px, (BitmapSource Bitmap, Size Native)? img, WallpaperFit fit, Color background, Rect virt)
    {
        int w = Math.Max(16, (int)Math.Round(px.Width * Downscale)), h = Math.Max(16, (int)Math.Round(px.Height * Downscale));
        double blur = Math.Max(2, BlurPx * Downscale);
        int pad = (int)Math.Ceiling(blur * 2);
        // the desktop as Windows draws it, on a canvas a little larger than the monitor (so the blur has
        // something to pull in at the edges instead of fading to black)
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, w + 2 * pad, h + 2 * pad));
            if (img is var (bitmap, native))
            {
                var place = WallpaperLayout.Place(fit, native, px, virt);
                // to canvas units, then stretched over the padding so the edges continue
                double sx = (w + 2.0 * pad) / w, sy = (h + 2.0 * pad) / h;
                Rect ToCanvas(Rect r) => new(((r.X - px.X) * Downscale) * sx, ((r.Y - px.Y) * Downscale) * sy, r.Width * Downscale * sx, r.Height * Downscale * sy);
                if (fit == WallpaperFit.Tile)
                    for (double ty = place.Y; ty < px.Bottom; ty += place.Height)
                        for (double tx = place.X; tx < px.Right; tx += place.Width)
                            dc.DrawImage(bitmap, ToCanvas(new Rect(tx, ty, place.Width, place.Height)));
                else dc.DrawImage(bitmap, ToCanvas(place));
            }
        }
        var sharp = new RenderTargetBitmap(w + 2 * pad, h + 2 * pad, 96, 96, PixelFormats.Pbgra32);
        sharp.Render(dv);
        BitmapSource Blur(double radius)
        {
            var img = new System.Windows.Controls.Image { Source = sharp, Width = w + 2 * pad, Height = h + 2 * pad, Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian } };
            img.Measure(new Size(w + 2 * pad, h + 2 * pad));
            img.Arrange(new Rect(0, 0, w + 2 * pad, h + 2 * pad));
            var rtb = new RenderTargetBitmap(w + 2 * pad, h + 2 * pad, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(img);
            var c = new WriteableBitmap(new CroppedBitmap(rtb, new Int32Rect(pad, pad, w, h)));
            c.Freeze();
            return c;
        }
        var clear = Blur(Math.Max(1, 4 * Downscale));
        var final = new WriteableBitmap(Blur(blur));
        int stride = w * 4;
        var pixels = new byte[stride * h];
        final.CopyPixels(pixels, stride, 0);
        final.Freeze();
        return new Monitor(px, final, clear, Downscale, pixels, stride);
    }

    /// <summary>The average colour of the blurred desktop behind a rectangle in screen pixels (null: unknown).</summary>
    public Color? AverageBehind(Rect screenPx)
    {
        if (Monitors.Count == 0) return null;
        var m = Monitors[WallpaperLayout.MonitorAt(Monitors.Select(x => x.BoundsPx).ToList(), screenPx)];
        var c = WallpaperLayout.Crop(screenPx, m.BoundsPx, m.Scale);
        int w = m.Blurred.PixelWidth, h = m.Blurred.PixelHeight;
        int x0 = Math.Clamp((int)c.X, 0, w - 1), y0 = Math.Clamp((int)c.Y, 0, h - 1);
        int x1 = Math.Clamp((int)c.Right, x0 + 1, w), y1 = Math.Clamp((int)c.Bottom, y0 + 1, h);
        long r = 0, g = 0, b = 0, n = 0;
        int step = Math.Max(1, (x1 - x0) / 24);
        for (int y = y0; y < y1; y += step)
            for (int x = x0; x < x1; x += step)
            {
                int i = y * m.Stride + x * 4;
                b += m.Pixels[i]; g += m.Pixels[i + 1]; r += m.Pixels[i + 2]; n++;
            }
        return n == 0 ? null : Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    // ------------------------------------------------------------------ IDesktopWallpaper (shobjidl_core.h)

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
    }

    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass { }
}
