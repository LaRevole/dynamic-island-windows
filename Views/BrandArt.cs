using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Views;

/// <summary>
/// The LeFleur Atoll mark, drawn in code so it stays sharp at every size: an obsidian-crimson
/// squircle with a pearl-white atoll ring, subtle petal flare, and turquoise live dot in its lagoon.
/// </summary>
public static class BrandArt
{
    public static readonly Color Ink = Color.FromRgb(0x09, 0x05, 0x07);
    public static readonly Color Accent = Color.FromRgb(0x14, 0xD4, 0xC4);     // LaRCorp turquoise
    public static readonly Color Orange = Color.FromRgb(0xFF, 0x7E, 0xB6);     // LeFleur floral rose
    public static readonly Color Pink = Color.FromRgb(0xE0, 0x3E, 0x7B);
    public static readonly Color Violet = Color.FromRgb(0x9D, 0x4E, 0xDD);
    public static readonly Color Deep = Color.FromRgb(0x28, 0x0A, 0x13);       // LaRCorp dark crimson

    private static Geometry Squircle(Rect r)
    {
        double k = r.Width * 0.5, n = 5.0;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            for (int i = 0; i <= 160; i++)
            {
                double a = i / 160.0 * Math.PI * 2;
                double ca = Math.Cos(a), sa = Math.Sin(a);
                double x = Math.Sign(ca) * Math.Pow(Math.Abs(ca), 2 / n) * k;
                double y = Math.Sign(sa) * Math.Pow(Math.Abs(sa), 2 / n) * k;
                var p = new Point(r.X + k + x, r.Y + r.Height / 2 + y * r.Height / r.Width);
                if (i == 0) c.BeginFigure(p, true, true); else c.LineTo(p, true, true);
            }
        }
        g.Freeze();
        return g;
    }

    private static Geometry PetalDiamond(Point center, double rx, double ry)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(center.X, center.Y - ry), true, true);
            c.QuadraticBezierTo(new Point(center.X + rx * 0.55, center.Y - ry * 0.55), new Point(center.X + rx, center.Y), true, true);
            c.QuadraticBezierTo(new Point(center.X + rx * 0.55, center.Y + ry * 0.55), new Point(center.X, center.Y + ry), true, true);
            c.QuadraticBezierTo(new Point(center.X - rx * 0.55, center.Y + ry * 0.55), new Point(center.X - rx, center.Y), true, true);
            c.QuadraticBezierTo(new Point(center.X - rx * 0.55, center.Y - ry * 0.55), new Point(center.X, center.Y - ry), true, true);
        }
        g.Freeze();
        return g;
    }

    /// <param name="expand">0 = resting ring, 1 = stretched wide (used by the intro animation).</param>
    /// <param name="blink">0 = round dot, 1 = squashed flat (a "blink" of the live dot).</param>
    /// <param name="lookX">-1 … 1: where the dot sits inside the lagoon (-0.6 = its resting place on the left).</param>
    public static void DrawIcon(DrawingContext dc, Rect r, double expand = 0, double blink = 0, double lookX = -0.6)
    {
        double s = r.Width;
        bool tiny = s < 40;
        var shape = Squircle(r);

        // Obsidian-crimson LaRCorp tile with subtle top bloom
        var bg = new LinearGradientBrush(Color.FromRgb(0x2D, 0x0C, 0x16), Color.FromRgb(0x0D, 0x07, 0x0A), 90);
        bg.Freeze();
        dc.DrawGeometry(bg, null, shape);
        if (!tiny)
        {
            dc.PushClip(shape);
            var bloom = new RadialGradientBrush(Color.FromArgb(0x36, 0xFF, 0x7E, 0xB6), Color.FromArgb(0x00, 0xFF, 0x7E, 0xB6))
            {
                Center = new Point(0.5, 0.2), GradientOrigin = new Point(0.5, 0.2), RadiusX = 0.65, RadiusY = 0.55,
            };
            dc.DrawRectangle(bloom, null, r);
            var sheen = new LinearGradientBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 90);
            dc.DrawRectangle(sheen, null, new Rect(r.X, r.Y, s, s * 0.44));
            dc.Pop();
        }
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xB8, 0xD9)), Math.Max(0.7, s * 0.012)), shape);

        // Pearl-white atoll pill ring
        double stroke = s * (tiny ? 0.095 : 0.056);
        double w = s * (tiny ? 0.70 : 0.62) * (1 + 0.22 * expand);
        double h = s * (tiny ? 0.36 : 0.27) * (1 + 0.12 * expand);
        var ring = new Rect(r.X + (s - w) / 2 + stroke / 2, r.Y + (s - h) / 2 + stroke / 2, w - stroke, h - stroke);
        var pearl = new SolidColorBrush(Color.FromRgb(0xFD, 0xF4, 0xF8));
        pearl.Freeze();
        dc.DrawRoundedRectangle(null, new Pen(pearl, stroke), ring, ring.Height / 2, ring.Height / 2);

        // Subtle LeFleur 4-petal star accent on the right inside the lagoon (or scaled down cleanly)
        double inner = ring.Height - stroke;
        double dr = inner * (tiny ? 0.42 : 0.37);
        double travel = (ring.Width - stroke - inner) / 2;
        var dot = new Point(ring.X + ring.Width / 2 + lookX * travel, ring.Y + ring.Height / 2);

        if (!tiny)
        {
            var rose = new SolidColorBrush(Color.FromArgb(0xD8, 0xFF, 0x7E, 0xB6));
            rose.Freeze();
            var blossom = new Point(ring.X + ring.Width / 2 + 0.55 * travel, ring.Y + ring.Height / 2);
            dc.DrawGeometry(rose, null, PetalDiamond(blossom, dr * 0.72, dr * 0.72));
        }

        // Turquoise live dot in the lagoon
        var accent = new SolidColorBrush(Accent);
        accent.Freeze();
        dc.DrawEllipse(accent, null, dot, dr, dr * (1 - 0.75 * Math.Clamp(blink, 0, 1)));
    }
}
