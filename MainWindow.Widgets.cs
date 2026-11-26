using System.IO;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Views;

namespace DynamicIsland;

/// <summary>
/// Desktop widgets, in the spirit of iPhone / Mac widgets: rounded cards of live glass in three sizes, a big
/// clear number or picture first and a small label second. They sit on the desktop under every window and are
/// fed by what the island already knows. A click opens a widget into a bigger, fully interactive card; a right
/// click gives its themes and settings. "Edit widgets" opens the gallery at the side of the screen (click or drag
/// a widget onto the desktop) and makes the cards on the desktop movable.
/// </summary>
public partial class MainWindow
{
    private enum WidgetSize { Small, Medium, Large }

    private sealed record WidgetKind(string Id, string Name, string About, WidgetSize Size, string Group, string Icon, bool Expands = true);

    /// <summary>A small widget is 2×2 cells; the gap between widgets is the grid's gutter.</summary>
    private static double SmallSide => Spec.Span(2);
    private static double Gap => Spec.Gutter;
    private static (double W, double H) SizeOf(WidgetSize s) => s switch
    {
        WidgetSize.Small => (SmallSide, SmallSide),
        WidgetSize.Medium => (SmallSide * 2 + Gap, SmallSide),
        _ => (SmallSide * 2 + Gap, SmallSide * 2 + Gap),
    };

    private static readonly string GMain = L.T("Главное"), GDo = L.T("Дела"), GTools = L.T("Инструменты"), GPc = L.T("Компьютер"), GCalm = L.T("Для души");

    private static readonly WidgetKind[] WidgetKinds =
    [
        new("music", L.T("Музыка"), L.T("Что играет, обложка и кнопки"), WidgetSize.Medium, GMain, ""),
        new("weather", L.T("Погода"), L.T("Сейчас и на ближайшие часы"), WidgetSize.Medium, GMain, ""),
        new("clock", L.T("Часы"), L.T("Циферблат со стрелками"), WidgetSize.Small, GMain, ""),
        new("calendar", L.T("Календарь"), L.T("Сегодня и ближайшее событие"), WidgetSize.Small, GMain, ""),
        new("month", L.T("Месяц"), L.T("Весь месяц, дни с событиями отмечены"), WidgetSize.Medium, GMain, ""),
        new("worldclock", L.T("Мировое время"), L.T("Время в трёх городах"), WidgetSize.Medium, GMain, ""),
        new("sun", L.T("Солнце"), L.T("Восход, закат и долгота дня"), WidgetSize.Small, GMain, ""),
        new("photos", L.T("Фото"), L.T("Ваши снимки из папки «Изображения», по очереди"), WidgetSize.Medium, GMain, ""),

        new("todo", L.T("Список дел"), L.T("Отмечайте сделанное, добавляйте новое"), WidgetSize.Medium, GDo, ""),
        new("reminders", L.T("Напоминания"), L.T("Что впереди сегодня и завтра"), WidgetSize.Medium, GDo, ""),
        new("notes", L.T("Заметки"), L.T("Последние заметки, новая — прямо с рабочего стола"), WidgetSize.Medium, GDo, ""),
        new("countdown", L.T("Обратный отсчёт"), L.T("Сколько дней осталось до важной даты"), WidgetSize.Small, GDo, ""),
        new("timer", L.T("Таймер"), L.T("Запуск в один клик: 5, 10, 25 минут"), WidgetSize.Small, GDo, ""),
        new("stopwatch", L.T("Секундомер"), L.T("С кругами, точный до десятых"), WidgetSize.Small, GDo, ""),
        new("focus", L.T("Не беспокоить"), L.T("Тишина на 30 минут, 1 или 2 часа"), WidgetSize.Small, GDo, ""),

        new("calc", L.T("Калькулятор"), L.T("Настоящий калькулятор прямо на столе"), WidgetSize.Large, GTools, "", Expands: false),
        new("volume", L.T("Громкость"), L.T("Ведите по карточке вверх и вниз"), WidgetSize.Small, GTools, "", Expands: false),
        new("launcher", L.T("Приложения"), L.T("Ваши приложения из дока острова"), WidgetSize.Medium, GTools, ""),
        new("clipboard", L.T("Буфер обмена"), L.T("Последнее скопированное — клик копирует снова"), WidgetSize.Medium, GTools, ""),
        new("actions", L.T("Быстрые действия"), L.T("Скриншот, запись, текст с экрана, пипетка"), WidgetSize.Small, GTools, ""),

        new("system", L.T("Система"), L.T("Процессор, видеокарта и память кольцами"), WidgetSize.Medium, GPc, ""),
        new("top", L.T("Процессы"), L.T("Кто занимает больше всего памяти"), WidgetSize.Medium, GPc, ""),
        new("cpu", L.T("Процессор"), L.T("Загрузка процессора"), WidgetSize.Small, GPc, ""),
        new("gpu", L.T("Видеокарта"), L.T("Загрузка видеокарты"), WidgetSize.Small, GPc, ""),
        new("ram", L.T("Память"), L.T("Сколько занято"), WidgetSize.Small, GPc, ""),
        new("disk", L.T("Диск"), L.T("Сколько свободно на системном диске"), WidgetSize.Small, GPc, ""),
        new("net", L.T("Сеть"), L.T("Скорость загрузки и отдачи"), WidgetSize.Small, GPc, ""),
        new("battery", L.T("Батарея"), L.T("Заряд ноутбука"), WidgetSize.Small, GPc, ""),

        new("water", L.T("Вода"), L.T("Стаканы за день: нажмите, когда выпили"), WidgetSize.Small, GCalm, ""),
        new("breathe", L.T("Дыхание"), L.T("Минута спокойного дыхания"), WidgetSize.Small, GCalm, ""),
        new("moon", L.T("Луна"), L.T("Фаза луны и до полнолуния"), WidgetSize.Small, GCalm, ""),
    ];

    private readonly Dictionary<string, DesktopWidgetWindow> _widgets = [];
    private readonly Dictionary<string, Action> _widgetUpdaters = [];
    /// <summary>Widgets that are open: their big view, what refreshes it and where they go back to.</summary>
    private readonly Dictionary<string, (Grid View, Action Update, Rect Home)> _openWidgets = [];
    private readonly DispatcherTimer _widgetTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _widgetsEditing;
    private Window? _widgetBar;
    private Rect _widgetBarRect;
    private readonly List<Action> _galleryUpdaters = [];

    /// <summary>The gallery's chips show which widgets are on now.</summary>
    private void RefreshGallery()
    {
        foreach (var u in _galleryUpdaters.ToList())
            try { u(); } catch (Exception ex) { Log.Write("widget gallery: " + ex.GetType().Name + ": " + ex.Message); }
    }
    private int _widgetTicks;

    private bool HasBattery => BatteryPercent() >= 0;
    private IEnumerable<WidgetKind> OfferedWidgets => WidgetKinds.Where(k => k.Id != "battery" || HasBattery);

    /// <summary>Live glass behind the widgets (not in Lite mode, not in demo pictures: they can't show it).</summary>
    private bool WidgetGlass => !Lite && _settings.WidgetLiveGlass && !Settings.ReadOnly && !Motion.NoTransparency;

    private readonly WallpaperService _wallpaper = new();

    /// <summary>
    /// The glass widgets get now: the desktop picture with a refracting rim when Windows' wallpaper is what's on
    /// screen; Windows' own live blur when a live-wallpaper program draws the desktop; solid when see-through is
    /// off (setting, Lite mode, Windows' transparency effects) or there's nothing to show.
    /// </summary>
    /// <summary>Tests: this kind of glass whatever the conditions.</summary>
    private GlassKind? _forceGlass;

    private GlassKind WidgetGlassKind()
    {
        if (_forceGlass is { } forced) return forced;
        if (Lite || !_settings.WidgetLiveGlass || Motion.NoTransparency) return GlassKind.Solid;
        if (_wallpaper.Override != null) return GlassKind.Image;
        if (Settings.ReadOnly) return GlassKind.Solid;   // demo pictures never show the real desktop
        if ((_wallpaper.LiveWallpaper && !_wallpaper.FromLivePreview) || !_wallpaper.Available) return GlassKind.Live;
        return GlassKind.Image;
    }

    /// <summary>The piece of the blurred desktop behind a widget, and the scrim that keeps its text at 4.5:1 or better.</summary>
    private void UpdateWidgetGlass(DesktopWidgetWindow w)
    {
        if (w.Kind != GlassKind.Image || _wallpaper.Monitors.Count == 0) return;
        var src = PresentationSource.FromVisual(w);
        var m = src?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var card = w.CardRect;
        var wide = new Rect(card.X - WidgetCard.Band, card.Y - WidgetCard.Band, card.Width + 2 * WidgetCard.Band, card.Height + 2 * WidgetCard.Band);
        var px = new Rect(m.Transform(wide.TopLeft), m.Transform(wide.BottomRight));
        var mon = _wallpaper.Monitors[WallpaperLayout.MonitorAt(_wallpaper.Monitors.Select(x => x.BoundsPx).ToList(), px)];
        var crop = WallpaperLayout.Crop(px, mon.BoundsPx, mon.Scale);
        var cardPx = new Rect(m.Transform(card.TopLeft), m.Transform(card.BottomRight));
        w.Card.SetBackdrop(mon.Blurred, mon.Clear, crop, _settings.GlassRefraction, ScrimFor(_wallpaper.AverageBehind(cardPx), w.Card.Theme));
    }

    /// <summary>
    /// The scrim: the desktop's own colour behind the widget, darkened (light text) or lightened (dark text) just
    /// enough for the text to reach 4.5:1 — the glass keeps the wallpaper's hue instead of turning grey.
    /// </summary>
    private static Color ScrimFor(Color? behind, WidgetTheme t)
    {
        if (behind is not { } bg) return Colors.Transparent;
        var toward = t.LightText ? Color.FromRgb((byte)(bg.R * 0.3), (byte)(bg.G * 0.3), (byte)(bg.B * 0.3)) : Color.FromRgb(0xF4, 0xF4, 0xF6);
        double textL = WallpaperLayout.Luminance(t.Fg);
        // the coloured titles (green, blue, orange) must stay readable too: 3:1 for an accent as light as green
        double accentL = t.LightText ? 0.45 : textL;
        for (double a = 0.1; a <= 0.86; a += 0.02)
        {
            var mix = Color.FromRgb((byte)(bg.R + (toward.R - bg.R) * a), (byte)(bg.G + (toward.G - bg.G) * a), (byte)(bg.B + (toward.B - bg.B) * a));
            double l = WallpaperLayout.Luminance(mix);
            if (WallpaperLayout.Contrast(l, textL) >= 4.5 && WallpaperLayout.Contrast(l, accentL) >= 3) return Color.FromArgb((byte)(a * 255), toward.R, toward.G, toward.B);
        }
        return Color.FromArgb(0xDB, toward.R, toward.G, toward.B);
    }

    /// <summary>
    /// Test wallpapers for the glass pictures: misty grey-white (the hardest case for white text) or night. Soft
    /// shapes with edges, so the rim's refraction has something to bend.
    /// </summary>
    private static BitmapSource TestWallpaper(bool dark, int w, int h)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var sky = dark ? new LinearGradientBrush(Color.FromRgb(0x07, 0x0B, 0x1E), Color.FromRgb(0x1E, 0x2B, 0x52), 90)
                           : new LinearGradientBrush(Color.FromRgb(0xF2, 0xF3, 0xF5), Color.FromRgb(0xC9, 0xCF, 0xD6), 90);
            dc.DrawRectangle(sky, null, new Rect(0, 0, w, h));
            var rnd = new Random(7);
            if (dark)
            {
                for (int i = 0; i < 260; i++)
                {
                    double r = rnd.NextDouble() * 1.8 + 0.4;
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)rnd.Next(90, 255), 255, 255, 255)), null, new Point(rnd.NextDouble() * w, rnd.NextDouble() * h * 0.7), r, r);
                }
                dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(0xFF, 0xF4, 0xF1, 0xE0), Color.FromArgb(0, 0x9A, 0xB0, 0xFF)), null, new Point(w * 0.72, h * 0.26), h * 0.16, h * 0.16);
            }
            else
            {
                for (int i = 0; i < 14; i++)
                {
                    double r = h * (0.08 + rnd.NextDouble() * 0.2);
                    dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF)), null, new Point(rnd.NextDouble() * w, rnd.NextDouble() * h), r * 1.6, r);
                }
            }
            // thin diagonal rays across the whole picture: lost in the frosted middle, bent by the rim
            var ray = new Pen(new SolidColorBrush(dark ? Color.FromArgb(0x90, 0xB8, 0xC8, 0xFF) : Color.FromArgb(0x70, 0x70, 0x7C, 0x88)), 3);
            for (int i = -4; i < 12; i++) dc.DrawLine(ray, new Point(i * w / 8.0, 0), new Point(i * w / 8.0 + h * 0.6, h));
            // hills along the bottom, two layers
            for (int layer = 0; layer < 2; layer++)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    double baseY = h * (layer == 0 ? 0.68 : 0.8);
                    c.BeginFigure(new Point(0, h), true, true);
                    for (int x = 0; x <= w; x += 20) c.LineTo(new Point(x, baseY + Math.Sin(x / (180.0 + layer * 90)) * h * 0.05 + Math.Sin(x / 57.0) * h * 0.012), true, false);
                    c.LineTo(new Point(w, h), true, false);
                }
                var fill = dark ? (layer == 0 ? Color.FromRgb(0x14, 0x1C, 0x36) : Color.FromRgb(0x0A, 0x0F, 0x20))
                                : (layer == 0 ? Color.FromRgb(0xB4, 0xBC, 0xC4) : Color.FromRgb(0x9C, 0xA6, 0xB0));
                dc.DrawGeometry(new SolidColorBrush(fill), null, g);
            }
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>The glass changed kind (wallpaper program started or stopped, settings): rebuild the widgets.</summary>
    private bool _wallpaperStarted;

    private void EnsureWallpaperStarted()
    {
        if (_wallpaperStarted) return;
        _wallpaperStarted = true;
        _wallpaper.BlurPx = _settings.GlassBlur;
        _wallpaper.Start();
    }

    private void RefreshWidgetGlass()
    {
        bool needWallpaper = !Lite && _settings.WidgetLiveGlass && !Motion.NoTransparency && !Settings.ReadOnly
            && (_widgets.Count > 0 || _wallpaper.Override != null);
        if (needWallpaper) EnsureWallpaperStarted();
        else if (_wallpaper.Available && _widgets.Count == 0 && _wallpaper.Override == null) _wallpaper.Clear();

        var kind = WidgetGlassKind();
        _wallpaper.BlurPx = _settings.GlassBlur;
        foreach (var (id, w) in _widgets.ToList())
        {
            if (w.Kind != kind) RebuildWidget(id);
            else UpdateWidgetGlass(w);
        }
    }

    /// <summary>Menus of live glass (not in Lite mode, not when transparency is off in Windows).</summary>
    private bool MenuGlass => !Lite && !Motion.NoTransparency;

    private WidgetTheme WidgetThemeOf(string id) =>
        WidgetThemes.Get(_settings.WidgetThemeOf.TryGetValue(id, out var t) ? t : _settings.WidgetTheme);

    private void InitWidgets()
    {
        _widgetTimer.Tick += (_, _) => Measure("widgets", TickWidgets);
        _wallpaper.Changed += RefreshWidgetGlass;
        if (IsToolRun) return;
        if (_settings.DesktopWidgets.Count > 0 && !_settings.WidgetsHidden && _settings.WidgetLiveGlass && !Lite && !Motion.NoTransparency)
            EnsureWallpaperStarted();
        Spec = GridLayout.Get(_settings.WidgetGrid);
        DesktopWidgetWindow.OnTop = _settings.WidgetsOnTop;
        _layoutKey = CurrentLayoutKey();
        if (_settings.WidgetLayouts.TryGetValue(_layoutKey, out var layout))
            foreach (var (lid, at) in layout) if (at.Length == 2) _settings.WidgetPlaces[lid] = at;
        if (!_settings.WidgetsHidden)
        {
            int i = 0;
            foreach (var id in _settings.DesktopWidgets.ToList())
            {
                try { ShowWidget(id); } catch (Exception e) { Log.Write($"widget {id} at start: {e}"); }
                if (_widgets.TryGetValue(id, out var w)) Materialize(w, 120 + 45 * i++);   // one after another, like a home screen waking up
            }
        }
        if (_widgets.Count > 0) _widgetTimer.Start();
        if (_settings.WidgetGridVersion < 2) { ReflowAll(); _settings.WidgetGridVersion = 2; _settings.Save(); }
        else RememberLayout();
        // monitors added, removed, resized: their own layout comes back, nothing stays off screen
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(OnMonitorsChanged, DispatcherPriority.Background);
    }

    private void EditWidgets_Click(object sender, RoutedEventArgs e) => EditWidgets(true, gallery: true);

    // ------------------------------------------------------------------ edit mode and the gallery

    private void EditWidgets(bool on, bool gallery = false)
    {
        if (on && _widgetsEditing) { if (gallery && _widgetBar == null) ShowWidgetGallery(true); return; }
        foreach (var id in _openWidgets.Keys.ToList()) CollapseWidget(id);
        if (on && _settings.WidgetsHidden) SetWidgetsHidden(false);
        _widgetsEditing = on;
        _widgetEditor?.Close();
        ShowGridOverlay(on);
        foreach (var w in _widgets.Values) w.EditMode = on;
        if (on) { if (gallery) ShowWidgetGallery(true); _widgetTimer.Start(); }
        else
        {
            var bar = _widgetBar;
            _widgetBar = null; _galleryUpdaters.Clear();
            if (bar != null && bar.IsLoaded && PresentationSource.FromVisual(bar) != null)
            {
                // glides back off the side of the screen
                var spring = new WindowSpring(bar) { Stiffness = 340, Damping = 32 };
                double off = _widgetBarRect.Left > SystemParameters.WorkArea.Left + SystemParameters.WorkArea.Width / 2 ? 80 : -80;
                spring.To(new Rect(bar.Left + off, bar.Top, bar.Width, bar.Height), () => bar.Close());
                if (bar.Content is UIElement content) content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
            }
            _settings.Save();
            if (_widgets.Count == 0) _widgetTimer.Stop();
        }
        if (_pinnedOpen || _mode != Mode.Compact) { _pinnedOpen = false; SetMode(Mode.Compact); }
        if (TabWidgets.IsChecked == true) RenderWidgetsPage();
    }

    /// <summary>
    /// The gallery: a tall glass panel at the side of the screen (the side with fewer widgets, so it hides as
    /// little as possible). Every widget is a live preview; a click puts it on the desktop (a tick takes it away
    /// again), or drag it out and drop it where it should go. The themes are on top.
    /// </summary>
    private void ShowWidgetGallery(bool slideIn)
    {
        var old = _widgetBar;
        _widgetBar = null;
        old?.Close();
        _galleryUpdaters.Clear();

        var area = SystemParameters.WorkArea;
        const double panelW = 452;
        bool onLeft = _widgets.Values.Count(w => w.Left + w.Width / 2 > area.Left + area.Width / 2) > _widgets.Values.Count(w => w.Left + w.Width / 2 <= area.Left + area.Width / 2);
        _widgetBarRect = new Rect(onLeft ? area.Left + 12 : area.Right - panelW - 12, area.Top + 12, panelW, area.Height - 24);
        bool glass = WidgetGlass;

        var title = new TextBlock { Text = L.T("Виджеты"), FontSize = 28, FontWeight = FontWeights.Bold, Foreground = Brushes.White, FontFamily = Display };
        var sub = new TextBlock { Text = L.T("Нажмите на виджет или перетащите его на рабочий стол. Правый клик по виджету — темы и настройки."), FontSize = 12.5,
                                  Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        // the one primary action: prominent (tinted) glass, on the trailing side
        var done = WKey(W(L.T("Готово"), 14, FontWeights.SemiBold, Brushes.White), Solid(0x0A, 0x84, 0xFF), null, CloseWidgetGallery, 96, 38, evenWhenEditing: true);
        done.VerticalAlignment = VerticalAlignment.Top;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(done, Dock.Right);
        head.Children.Add(done);
        var headText = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; headText.Children.Add(title); headText.Children.Add(sub);
        head.Children.Add(headText);

        var groups = new StackPanel { Margin = new Thickness(0, 12, 0, 28) };   // room for the soft scroll edges at rest
        groups.Children.Add(GalleryCaption(L.T("Тема")));
        groups.Children.Add(ThemePicker(null, 26, () => ShowWidgetGallery(false)));
        groups.Children.Add(GalleryCaption(L.T("Готовые наборы")));
        groups.Children.Add(WidgetSetCards(false));

        const double scale = 0.555;
        foreach (var group in OfferedWidgets.GroupBy(k => k.Group))
        {
            groups.Children.Add(GalleryCaption(group.Key));
            var wrap = new WrapPanel();
            foreach (var k in group) wrap.Children.Add(GalleryCell(k, scale, glass));
            groups.Children.Add(wrap);
        }
        var scroll = new ScrollViewer { Content = groups, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, PanningMode = PanningMode.VerticalOnly };
        // the scroll edge effect: content softly fades under the top and bottom edges instead of being cut
        scroll.OpacityMask = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 0.025), new GradientStop(Colors.Black, 0.96), new GradientStop(Colors.Transparent, 1) },
        };

        var panel = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        panel.Children.Add(head); panel.Children.Add(scroll);
        var sheet = new Border
        {
            CornerRadius = new CornerRadius(30), Padding = new Thickness(22, 20, 14, 0), Child = panel,
            Background = new LinearGradientBrush(Color.FromArgb(PanelGlass ? (byte)0x9C : (byte)0xF2, 0x16, 0x16, 0x1C), Color.FromArgb(PanelGlass ? (byte)0xB4 : (byte)0xF2, 0x10, 0x10, 0x16), 90),
            BorderThickness = new Thickness(1.2),
            BorderBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops = { new GradientStop(Color.FromArgb(0x90, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0x18, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(0x50, 255, 255, 255), 1) },
            },
        };
        TextElement.SetFontFamily(sheet, new FontFamily("Segoe UI Variable Text, Segoe UI"));

        var win = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true,
            ResizeMode = ResizeMode.NoResize, Content = GlassPanel(sheet, 30), Title = "Atoll widgets",
            Width = panelW, Height = _widgetBarRect.Height, Top = _widgetBarRect.Top,
            Left = slideIn ? _widgetBarRect.Left + (onLeft ? -90 : 90) : _widgetBarRect.Left,
        };
        win.SourceInitialized += (_, _) => FollowGlassPanel(win);
        win.Loaded += (_, _) =>
        {
            if (!slideIn) return;
            new WindowSpring(win) { Stiffness = 300, Damping = 27 }.To(_widgetBarRect);
            sheet.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        };
        win.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) CloseWidgetGallery(); };
        win.Closed += (_, _) => { if (ReferenceEquals(_widgetBar, win)) { _widgetBar = null; _galleryUpdaters.Clear(); } };
        _widgetBar = win;
        win.Show();
        RefreshGallery();
    }

    /// <summary>Big glass panels (gallery, widget editor): see-through over the blurred desktop, unless glass is off.</summary>
    private bool PanelGlass => WidgetGlassKind() == GlassKind.Image && _wallpaper.Monitors.Count > 0;

    /// <summary>
    /// A panel of glass: the sheet over the piece of blurred desktop behind its window, both cut to the radius by
    /// WPF (Windows' live blur would fill the window's whole square).
    /// </summary>
    private static Grid GlassPanel(Border sheet, double radius)
    {
        var holder = new Grid();
        holder.Children.Add(new Border { CornerRadius = new CornerRadius(radius), IsHitTestVisible = false, Tag = "backdrop" });
        holder.Children.Add(sheet);
        return holder;
    }

    /// <summary>Keeps a glass panel's backdrop showing what is behind its window, wherever the window goes.</summary>
    private void FollowGlassPanel(Window win)
    {
        void Update()
        {
            if (!PanelGlass || win.Content is not Grid g || g.Children[0] is not Border back) return;
            var m = PresentationSource.FromVisual(win)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            var px = new Rect(m.Transform(new Point(win.Left, win.Top)), m.Transform(new Point(win.Left + win.Width, win.Top + win.Height)));
            var mon = _wallpaper.Monitors[WallpaperLayout.MonitorAt(_wallpaper.Monitors.Select(x => x.BoundsPx).ToList(), px)];
            var crop = WallpaperLayout.Crop(px, mon.BoundsPx, mon.Scale);
            if (back.Background is ImageBrush b && ReferenceEquals(b.ImageSource, mon.Blurred)) b.Viewbox = crop;
            else back.Background = new ImageBrush(mon.Blurred) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = crop, Stretch = Stretch.Fill };
        }
        win.LocationChanged += (_, _) => Update();
        win.SizeChanged += (_, _) => Update();
        win.Loaded += (_, _) => Update();
    }

    /// <summary>The gallery glides away; edit mode stays on (the "+" brings the gallery back).</summary>
    private void CloseWidgetGallery()
    {
        var bar = _widgetBar;
        _widgetBar = null; _galleryUpdaters.Clear();
        if (bar == null || !bar.IsLoaded || PresentationSource.FromVisual(bar) == null) { bar?.Close(); return; }
        var spring = new WindowSpring(bar) { Stiffness = 340, Damping = 32 };
        double off = _widgetBarRect.Left > SystemParameters.WorkArea.Left + SystemParameters.WorkArea.Width / 2 ? 80 : -80;
        spring.To(new Rect(bar.Left + off, bar.Top, bar.Width, bar.Height), () => bar.Close());
        if (bar.Content is UIElement content) content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
    }

    /// <summary>A section header: title-style capitalisation, not all capitals (as on Apple platforms now).</summary>
    private static TextBlock GalleryCaption(string text) => new()
    {
        Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 16, 0, 10),
        Foreground = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)),
    };

    /// <summary>One preview in the gallery: click toggles it on the desktop, drag drops it where you let go.</summary>
    private FrameworkElement GalleryCell(WidgetKind k, double scale, bool glass)
    {
        var (w, h) = SizeOf(k.Size);
        var card = new WidgetCard(w, h);
        card.Apply(WidgetThemeOf(k.Id), glass ? GlassKind.Live : GlassKind.Solid, GlassOf(k.Id));
        var (view, content, wash) = NewWidgetView();
        card.Body.Children.Add(view);
        _buildSize = k.Size; _buildAccent = AccentOf(k.Id);
        try { _galleryUpdaters.Add(BuildWidget(k.Id, content, wash)); }
        catch (Exception e) { Log.Write($"widget preview {k.Id}: {e}"); }
        finally { _buildAccent = null; }
        var visual = card.Card;
        visual.LayoutTransform = new ScaleTransform(scale, scale);

        var add = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left,
                               VerticalAlignment = VerticalAlignment.Top, Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.4 } };
        var sign = new TextBlock { FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        add.Child = sign;
        var id = k.Id;
        void Paint()
        {
            bool on = _widgets.ContainsKey(id);
            add.Background = new SolidColorBrush(on ? Color.FromRgb(0x30, 0xD1, 0x58) : Color.FromRgb(0xF2, 0xF2, 0xF7));
            sign.Text = on ? "" : "";
            sign.Foreground = on ? Brushes.White : Brushes.Black;
        }
        Paint();
        _galleryUpdaters.Add(Paint);

        // press, then either a click (toggle) or a drag (the widget follows the pointer onto the desktop)
        Point? pressed = null; DesktopWidgetWindow? dragging = null;
        visual.Cursor = Cursors.Hand;
        visual.PreviewMouseLeftButtonDown += (_, e) => { e.Handled = true; pressed = ScreenCursorDip(visual); dragging = null; visual.CaptureMouse(); };
        visual.PreviewMouseMove += (_, e) =>
        {
            if (pressed is not { } from || e.LeftButton != MouseButtonState.Pressed) return;
            var now = ScreenCursorDip(visual);
            if (dragging == null)
            {
                if ((now - from).Length < 7) return;
                if (!_widgets.ContainsKey(id)) AddWidget(id, pop: false);
                if (!_widgets.TryGetValue(id, out dragging)) { pressed = null; return; }
                dragging.Front = true;
            }
            dragging.CardX = now.X - dragging.CardW / 2;
            dragging.CardY = now.Y - dragging.CardH / 2;
            ShowGridTarget(dragging);
        };
        visual.PreviewMouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            visual.ReleaseMouseCapture();
            if (pressed == null) return;
            pressed = null;
            if (dragging != null) { Placed(dragging); Materialize(dragging); dragging = null; }
            else if (_widgets.TryGetValue(id, out var placed)) Materialize(placed);   // already there: show where
            else AddWidget(id);
            RefreshGallery();
        };
        add.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (_widgets.ContainsKey(id)) RemoveWidget(id); else AddWidget(id); Paint(); };

        visual.Margin = new Thickness(11, 11, 0, 0);
        var holder = new Grid();
        holder.Children.Add(visual); holder.Children.Add(add);
        var name = new TextBlock { Text = k.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(8, 7, 0, 0) };
        var about = new TextBlock { Text = k.About, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(8, 2, 0, 0),
                                    TextWrapping = TextWrapping.Wrap, MaxWidth = w * scale };
        var cell = new StackPanel { Margin = new Thickness(0, 0, 10, 16) };
        cell.Children.Add(holder); cell.Children.Add(name); cell.Children.Add(about);
        return cell;
    }

    /// <summary>The pointer in DIPs on the screen.</summary>
    private static Point ScreenCursorDip(Visual v)
    {
        Native.GetCursorPos(out var p);
        var src = PresentationSource.FromVisual(v);
        return src?.CompositionTarget?.TransformFromDevice.Transform(new Point(p.X, p.Y)) ?? new Point(p.X, p.Y);
    }

    /// <summary>
    /// A row of theme swatches. id null: the theme of all widgets; otherwise of this one widget (with "as the
    /// others" first).
    /// </summary>
    private FrameworkElement ThemePicker(string? id, double size, Action? changed = null, bool withSame = false)
    {
        var wrap = new WrapPanel { Margin = new Thickness(2, 0, 0, 2) };
        string current = id == null ? _settings.WidgetTheme : _settings.WidgetThemeOf.GetValueOrDefault(id, "");
        if (withSame && id != null)
        {
            var same = new Border
            {
                Height = size + 8, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(size / 2 + 4), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2), BorderBrush = current == "" ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                Child = new TextBlock { Text = L.T("Как у всех"), FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center },
            };
            same.MouseLeftButtonUp += (_, e) => { e.Handled = true; SetWidgetTheme(id, ""); changed?.Invoke(); };
            wrap.Children.Add(same);
        }
        foreach (var t in WidgetThemes.All)
        {
            bool on = t.Id == current;
            var dot = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = t.Swatch, Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
            };
            var ring = new Border
            {
                Width = size + 8, Height = size + 8, CornerRadius = new CornerRadius(size / 2 + 4), Child = dot, Margin = new Thickness(0, 0, 6, 6),
                BorderThickness = new Thickness(2), BorderBrush = on ? Brushes.White : Brushes.Transparent, ToolTip = t.Name,
            };
            if (on) dot.Child = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = size * 0.4, HorizontalAlignment = HorizontalAlignment.Center,
                                                VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(t.LightText ? Colors.White : Color.FromRgb(0x1C, 0x1C, 0x1E)) };
            var themeId = t.Id;
            ring.MouseLeftButtonUp += (_, e) => { e.Handled = true; SetWidgetTheme(id, themeId); changed?.Invoke(); };
            ring.MouseEnter += (_, _) => dot.RenderTransform = new ScaleTransform(1.1, 1.1, size / 2, size / 2);
            ring.MouseLeave += (_, _) => dot.RenderTransform = null;
            wrap.Children.Add(ring);
        }
        // the swatches don't say their names: the chosen one is written under them
        var chosen = current == "" ? L.F("Как у всех: {0}", WidgetThemes.Get(_settings.WidgetTheme).Name)
                   : current.StartsWith("custom:") ? L.T("Свой цвет") : WidgetThemes.Get(current).Name;
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        panel.Children.Add(wrap);
        var label = new TextBlock { Text = chosen, FontSize = 12, Margin = new Thickness(4, 0, 0, 0), Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)) };
        panel.Children.Add(label);
        return panel;
    }

    private void SetWidgetTheme(string? id, string theme)
    {
        if (id == null) _settings.WidgetTheme = theme;   // widgets with a look of their own keep it
        else if (theme == "") _settings.WidgetThemeOf.Remove(id);
        else _settings.WidgetThemeOf[id] = theme;
        _settings.Save();
        foreach (var (wid, w) in _widgets) { w.ApplyTheme(WidgetThemeOf(wid), GlassOf(wid)); UpdateWidgetGlass(w); }
        if (TabWidgets.IsChecked == true && _mode == Mode.Expanded) RenderWidgetsPage();
    }

    private void SetWidgetsHidden(bool hidden)
    {
        _settings.WidgetsHidden = hidden;
        _settings.Save();
        if (hidden)
        {
            var all = _widgets.Values.ToList();
            _widgets.Clear(); _widgetUpdaters.Clear(); _openWidgets.Clear();
            foreach (var w in all) w.Close();
            _widgetTimer.Stop();
            if (_wallpaper.Override == null) _wallpaper.Clear();
        }
        else
        {
            foreach (var id in _settings.DesktopWidgets.ToList()) ShowWidget(id);
            if (_widgets.Count > 0) _widgetTimer.Start();
        }
    }

    // ------------------------------------------------------------------ adding, removing, placing

    private void AddWidget(string id, bool pop = true)
    {
        if (_widgets.ContainsKey(id) || WidgetKinds.All(k => k.Id != id)) return;
        if (!_settings.DesktopWidgets.Contains(id)) _settings.DesktopWidgets.Add(id);
        _settings.WidgetsHidden = false;
        ShowWidget(id);
        _settings.Save();
        _widgetTimer.Start();
        if (pop && _widgets.TryGetValue(id, out var w)) Materialize(w);
        MaybeWidgetTip();
        if (TabWidgets.IsChecked == true && _mode == Mode.Expanded) RenderWidgetsPage();
    }

    /// <summary>
    /// The "materialize" transition: the glass condenses out of a blur and settles into shape where it landed,
    /// so you see where it went.
    /// </summary>
    private static void Materialize(DesktopWidgetWindow w, int delayMs = 0)
    {
        if (Motion.Reduced) return;
        var card = w.Card.Card;
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var s = new ScaleTransform(0.92, 0.92);
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = s;
        var settle = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(520)) { BeginTime = begin, EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
        card.Opacity = 0;
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { BeginTime = begin });
        var blur = new BlurEffect { Radius = 18, RenderingBias = RenderingBias.Performance };
        card.Effect = blur;
        var clear = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        clear.Completed += (_, _) => { if (ReferenceEquals(card.Effect, blur)) card.Effect = null; };
        blur.BeginAnimation(BlurEffect.RadiusProperty, clear);
    }

    private void RemoveWidget(string id, bool undo = true)
    {
        if (undo && _widgets.ContainsKey(id) && !Settings.ReadOnly)
        {
            var name = WidgetKinds.FirstOrDefault(k => k.Id == id)?.Name ?? id;
            ShowToast("\uE7A7", L.F("«{0}» убран", name), L.T("Нажмите, чтобы вернуть"), () => { AddWidget(id); RefreshGallery(); }, ms: 5000);
        }
        _openWidgets.Remove(id);
        if (_widgets.Remove(id, out var w)) w.Close();
        _widgetUpdaters.Remove(id);
        _settings.DesktopWidgets.Remove(id);
        _settings.Save();
        if (_widgets.Count == 0 && !_widgetsEditing)
        {
            _widgetTimer.Stop();
            if (_wallpaper.Override == null) _wallpaper.Clear();
        }
        if (TabWidgets.IsChecked == true && _mode == Mode.Expanded) RenderWidgetsPage();
    }

    /// <summary>A view of a widget: its own colour wash under the content.</summary>
    private static (Grid View, Grid Content, Border Wash) NewWidgetView()
    {
        var view = new Grid();
        var wash = new Border();
        var content = new Grid { Margin = new Thickness(16, 14, 16, 14) };
        view.Children.Add(wash); view.Children.Add(content);
        return (view, content, wash);
    }

    private void ShowWidget(string id)
    {
        var kind = WidgetKinds.FirstOrDefault(k => k.Id == id);
        if (kind == null || _widgets.ContainsKey(id)) return;
        if (!Lite && _settings.WidgetLiveGlass && !Motion.NoTransparency && !Settings.ReadOnly) EnsureWallpaperStarted();
        var size = SizeFor(kind);
        var (cw, ch) = SizeOf(size);
        var w = new DesktopWidgetWindow(id, cw, ch, WidgetGlassKind());
        w.ApplyTheme(WidgetThemeOf(id), GlassOf(id));
        w.BoundsChanged += UpdateWidgetGlass;
        var (view, content, wash) = NewWidgetView();
        view.Width = cw; view.Height = ch;
        view.HorizontalAlignment = HorizontalAlignment.Left; view.VerticalAlignment = VerticalAlignment.Top;
        w.Host.Children.Add(view);
        _buildSize = size; _buildAccent = AccentOf(id);
        try { _widgetUpdaters[id] = BuildWidget(id, content, wash); }
        catch (Exception e)
        {
            // never a silent empty card: say so on the card and in the log
            Log.Write($"widget {id} build: {e}");
            content.Children.Clear();
            content.Children.Add(W(L.T("Виджет не загрузился — правый клик, чтобы убрать"), 12.5, null, Dim));
            ((TextBlock)content.Children[0]).TextWrapping = TextWrapping.Wrap;
            _widgetUpdaters[id] = () => { };
        }
        finally { _buildAccent = null; }

        if (_settings.WidgetPlaces.TryGetValue(id, out var at) && at.Length == 2 && OnSomeScreen(at[0], at[1], cw, ch))
        { w.CardX = at[0]; w.CardY = at[1]; }
        else
        {
            var (x, y) = FreeSpot(cw, ch);
            w.CardX = x; w.CardY = y;
            _settings.WidgetPlaces[id] = [x, y];
            RememberLayout();
        }
        w.Moved += Placed;
        w.DragStarted += win => { _widgetEditor?.Close(); if (!_widgetsEditing) ShowGridOverlay(true); win.Front = true; ShowGridTarget(win); };
        w.Dragging += ShowGridTarget;
        w.EditClicked += win => ShowWidgetEditor(win.Id);
        w.AltHeld += _ => EditWidgets(true);
        w.Resizing += ResizingWidget;
        w.Resized += ResizedWidget;
        w.RemoveClicked += win => { RemoveWidget(win.Id); RefreshGallery(); };
        w.Clicked += win => ExpandWidget(win.Id);
        w.ClickedAway += win => CollapseWidget(win.Id);
        w.Card.Card.ContextMenu = new ContextMenu();
        w.Card.Card.ContextMenuOpening += (_, _) => FillWidgetMenu(w.Card.Card.ContextMenu, id);
        w.Show();
        if (DesktopWidgetWindow.OnTop) w.Front = true;
        w.EditMode = _widgetsEditing;
        _widgets[id] = w;
        UpdateWidgetGlass(w);
        try { _widgetUpdaters[id](); } catch (Exception e) { Log.Write($"widget {id} first update: {e}"); }
    }

    /// <summary>Right click on a widget: open it, its theme, edit, remove.</summary>
    private void FillWidgetMenu(ContextMenu menu, string id)
    {
        menu.Items.Clear();
        var kind = WidgetKinds.First(k => k.Id == id);
        MenuItem Item(string header, string glyph, Action click)
        {
            var mi = new MenuItem { Header = header, Icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 14 } };
            mi.Click += (_, _) => click();
            return mi;
        }
        if (kind.Expands)
            menu.Items.Add(_openWidgets.ContainsKey(id) ? Item(L.T("Свернуть"), "\uE73F", () => CollapseWidget(id)) : Item(L.T("Открыть"), "\uE740", () => ExpandWidget(id)));
        menu.Items.Add(Item(L.T("Настроить виджет…"), "\uE790", () => ShowWidgetEditor(id)));
        var sizes = SizesOf(kind);
        if (sizes.Length > 1)
        {
            menu.Items.Add(new Separator());
            var now = SizeFor(kind);
            foreach (var ch in sizes)
            {
                var size = FromLetter(ch);
                var mi = new MenuItem { Header = size switch { WidgetSize.Small => L.T("Маленький"), WidgetSize.Medium => L.T("Средний"), _ => L.T("Большой") }, IsChecked = size == now };
                var letter = ch.ToString();
                mi.Click += (_, _) => { _settings.WidgetSizeOf[id] = letter; _settings.Save(); RebuildWidget(id); };
                menu.Items.Add(mi);
            }
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(L.T("Изменить виджеты…"), "", () => EditWidgets(true, gallery: true)));
        menu.Items.Add(Item(L.T("Убрать виджет"), "", () => RemoveWidget(id)));
    }

    /// <summary>Dropped: onto the widget grid (like icons on a home screen), kept on screen, remembered.</summary>
    private void Placed(DesktopWidgetWindow w)
    {
        ShowGridTarget(null);
        if (!_widgetsEditing) { ShowGridOverlay(false); w.EditMode = false; }
        var want = w.CardRect;
        if (!OnSomeScreen(want.X, want.Y, want.Width, want.Height)) { var (fx, fy) = FreeSpot(want.Width, want.Height); want = new Rect(fx, fy, want.Width, want.Height); }
        Land(w, want);
    }

    /// <summary>Is the middle of it on a real screen (not in a gap between screens of different sizes)?</summary>
    private bool OnSomeScreen(double x, double y, double w, double h)
    {
        var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var p = m.Transform(new Point(x + w / 2, y + h / 2));
        return System.Windows.Forms.Screen.AllScreens.Any(s => s.WorkingArea.Contains((int)p.X, (int)p.Y));
    }

    /// <summary>The first free place on the grid, from the top left (below the island's height), column by column, not under the gallery.</summary>
    private (double X, double Y) FreeSpot(double w, double h)
    {
        var others = OthersThan(null);
        foreach (var (_, g) in Grids())
            if (GridLayout.First(w, h, g, others, GalleryBlock) is { } r) return (r.X, r.Y);
        var first = Grids()[0].Grid;
        return (first.X0, first.Y0);
    }

    // ------------------------------------------------------------------ opening a widget

    private static (double W, double H) ExpandedSize(WidgetKind k) => k.Id switch
    {
        "music" => (380, 400),
        "weather" => (400, 640),
        "calendar" or "month" => (380, 420),
        "notes" or "todo" => (380, 450),
        "clipboard" or "reminders" => (380, 420),
        "system" or "top" => (400, 430),
        "timer" or "stopwatch" => (338, 400),
        "worldclock" => (380, 400),
        "photos" => (560, 400),
        "launcher" => (400, 320),
        "countdown" => (338, 360),
        _ => SizeOf(WidgetSize.Large),
    };

    /// <summary>
    /// Opens a widget: the card grows on a spring into its big view (towards where there's room), comes in front,
    /// takes the keyboard; a click anywhere else or Esc closes it again.
    /// </summary>
    private void ExpandWidget(string id)
    {
        if (_widgetsEditing || _openWidgets.ContainsKey(id) || !_widgets.TryGetValue(id, out var w) || w.Spring.Running) return;
        var kind = WidgetKinds.FirstOrDefault(k => k.Id == id);
        if (kind == null || !kind.Expands) return;
        _widgetEditor?.Close();
        foreach (var other in _openWidgets.Keys.ToList()) CollapseWidget(other);

        var (ew, eh) = ExpandedSize(kind);
        var home = w.CardRect;
        var dpi = VisualTreeHelper.GetDpi(w);
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)((home.X + home.Width / 2) * dpi.DpiScaleX), (int)((home.Y + home.Height / 2) * dpi.DpiScaleY))).WorkingArea;
        var wa = new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY, area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
        double x = home.X, y = home.Y;
        if (x + ew > wa.Right - 8) x = Math.Max(wa.Left + 8, home.Right - ew);
        if (y + eh > wa.Bottom - 8) y = Math.Max(wa.Top + 8, home.Bottom - eh);
        bool fromRight = x < home.X - 1, fromBottom = y < home.Y - 1;

        var (view, content, wash) = NewWidgetView();
        view.Width = ew; view.Height = eh;
        view.HorizontalAlignment = fromRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        view.VerticalAlignment = fromBottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        view.Opacity = 0;
        Action update;
        _buildSize = WidgetSize.Large; _buildAccent = AccentOf(id);
        try { update = BuildExpanded(id, content, wash); }
        catch (Exception e) { Log.Write($"widget {id} open: {e}"); return; }
        finally { _buildAccent = null; }

        // a round "close" button in the corner
        // the standard close button: an X in a glass circle, in the corner (concentric with the card)
        var close = WKey(Glyph("\uE8BB", 10), null, null, () => CollapseWidget(id), 28, 28, name: L.T("Закрыть"));
        close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Top; close.Margin = new Thickness(0, 10, 10, 0);
        view.Children.Add(close);

        var compact = w.Host.Children[0];
        while (w.Host.Children.Count > 1) w.Host.Children.RemoveAt(1);   // a view left over from an interrupted close
        compact.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        w.Host.Children.Add(view);
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(90) });
        var grow = new ScaleTransform(0.94, 0.94);
        view.RenderTransformOrigin = new Point(fromRight ? 1 : 0, fromBottom ? 1 : 0);
        view.RenderTransform = grow;
        var settle = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } };
        grow.BeginAnimation(ScaleTransform.ScaleXProperty, settle); grow.BeginAnimation(ScaleTransform.ScaleYProperty, settle);

        _openWidgets[id] = (view, update, home);
        if (Settings.ReadOnly) w.Front = true;   // demo pictures: never take the keyboard from whoever is at the computer
        else w.Raised = true;
        w.Spring.To(new Rect(x, y, ew, eh));
        try { update(); } catch (Exception e) { Log.Write($"widget {id} open update: {e}"); }
    }

    private void CollapseWidget(string id)
    {
        if (!_openWidgets.Remove(id, out var o) || !_widgets.TryGetValue(id, out var w)) return;
        o.View.IsHitTestVisible = false;
        o.View.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(130)));
        var compact = w.Host.Children[0];
        compact.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)) { BeginTime = TimeSpan.FromMilliseconds(120) });
        w.Spring.To(o.Home, () =>
        {
            if (_openWidgets.ContainsKey(id)) return;   // opened again meanwhile
            w.Host.Children.Remove(o.View);
            w.Raised = false;
        });
        try { _widgetUpdaters.GetValueOrDefault(id)?.Invoke(); } catch (Exception e) { Log.Write($"widget {id} update: {e.Message}"); }
    }

    // ------------------------------------------------------------------ every second

    private readonly HashSet<string> _widgetFailures = [];
    private DateTime _netAt;
    private double _netDown, _netUp;
    private long _netDownBytes, _netUpBytes;

    private void TickWidgets()
    {
        if (_widgets.Count == 0 && _galleryUpdaters.Count == 0) return;
        _widgetTicks++;
        SampleNet();
        if (_widgetTicks % 5 == 0) foreach (var w in _widgets.Values) w.EnsureOwned();
        VizControl();
        LyricsControl();
        // a live-wallpaper program started or quit: the picture behind the widgets is (no longer) the wallpaper
        if (_widgetTicks % 15 == 0 && _wallpaper.Available && _wallpaper.Override == null && WallpaperService.DetectLiveWallpaper() != _wallpaper.LiveWallpaper) _wallpaper.Rebuild();
        bool wantStats = _galleryUpdaters.Count > 0 || _openWidgets.ContainsKey("system") || _widgets.Any(p => p.Key is "cpu" or "gpu" or "ram" or "system" && !p.Value.Paused);
        if (wantStats && !_statsBusy && _widgetTicks % 2 == 0)
        {
            _statsBusy = true;
            bool history = _widgets.TryGetValue("system", out var sysWin) && !sysWin.Paused;
            _ = Task.Run(() => (_stats.Sample(), history ? GpuSensor.Temperature() : null)).ContinueWith(t => Dispatcher.BeginInvoke(() =>
            {
                _statsBusy = false;
                if (t.IsFaulted) { if (_widgetFailures.Add("stats")) Log.Write("stats: " + t.Exception?.GetBaseException()); return; }
                if (history) RecordSystem(t.Result.Item1, t.Result.Item2);
            }));
        }
        var all = _widgetUpdaters.Select(p => (p.Key, p.Value)).Concat(_openWidgets.Select(p => (p.Key + " (open)", p.Value.Update)))
                                 .Concat(_galleryUpdaters.Select(u => ("gallery", u))).ToList();
        foreach (var (key, update) in all)
        {
            if (_widgets.TryGetValue(key, out var hostWin) && hostWin.Paused) continue;   // nobody sees it: no work
            try { update(); }
            catch (Exception e) { if (_widgetFailures.Add(key + e.GetType().Name)) Log.Write($"widget {key}: {e}"); }
        }
    }

    private void SampleNet()
    {
        if (!_widgets.ContainsKey("net") && !_widgets.ContainsKey("system") && !_openWidgets.ContainsKey("system") && _galleryUpdaters.Count == 0) return;
        long down = 0, up = 0;
        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = n.GetIPStatistics();
                down += s.BytesReceived; up += s.BytesSent;
            }
        }
        catch (Exception e) { if (_widgetFailures.Add("net")) Log.Write("widget net: " + e.Message); return; }
        var now = DateTime.Now;
        double secs = (now - _netAt).TotalSeconds;
        if (_netAt != default && secs > 0.2)
        {
            _netDown = _netDown * 0.4 + Math.Max(0, (down - _netDownBytes) / secs) * 0.6;
            _netUp = _netUp * 0.4 + Math.Max(0, (up - _netUpBytes) / secs) * 0.6;
        }
        _netDownBytes = down; _netUpBytes = up; _netAt = now;
    }

    // ------------------------------------------------------------------ building blocks

    private static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");
    private static readonly Brush Dim = Frz(new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)));
    private static Brush Frz(Brush b) { b.Freeze(); return b; }
    private static SolidColorBrush Solid(byte r, byte g, byte b, byte a = 255) => new(Color.FromArgb(a, r, g, b));

    /// <summary>Text. "big" numbers use the display cut of the font, like on a phone.</summary>
    private static TextBlock W(string text, double size, FontWeight? weight = null, Brush? fg = null, bool big = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
        Themed(t, TextBlock.ForegroundProperty, fg);
        if (big) t.FontFamily = Display;
        return t;
    }

    private TextBlock Glyph(string glyph, double size, Brush? fg = null, bool symbol = false)
    {
        var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center, FontFamily = (FontFamily)FindResource(symbol ? "SymbolFont" : "IconFont") };
        Themed(t, TextBlock.ForegroundProperty, fg);
        return t;
    }

    /// <summary>The theme's text colours follow the card (WFg, WDim); any other brush is used as given.</summary>
    private static void Themed(FrameworkElement e, DependencyProperty p, Brush? b)
    {
        if (b == null) e.SetResourceReference(p, "WFg");
        else if (ReferenceEquals(b, Dim)) e.SetResourceReference(p, "WDim");
        else e.SetValue(p, b);
    }

    /// <summary>Content on a strong colour of its own (a sky, a cover): always light text, whatever the theme.</summary>
    private static void LightText(FrameworkElement e)
    {
        e.Resources["WFg"] = Brushes.White;
        e.Resources["WDim"] = new SolidColorBrush(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF));
        e.Resources["WFill"] = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        e.Resources["WTrack"] = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
    }

    /// <summary>The small caption at the top of a widget: a coloured icon and a name.</summary>
    private StackPanel Caption(string glyph, string text, Brush accent)
    {
        if (_buildAccent is { } own) accent = new SolidColorBrush(own);
        var s = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var g = Glyph(glyph, 12, accent); g.Margin = new Thickness(0, 0, 6, 0);
        s.Children.Add(g);
        s.Children.Add(W(text, 12.5, FontWeights.SemiBold, accent));
        return s;
    }

    private static ControlTemplate? _pillTemplate;
    private Button Pill(object content, Action click, Brush? bg = null, double w = double.NaN, double h = 30)
    {
        if (_pillTemplate == null)
        {
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.BorderBrushProperty, GlassRim);
            bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            _pillTemplate = new ControlTemplate(typeof(Button)) { VisualTree = bd };
        }
        var b = new Button
        {
            Content = content is string s ? W(s, 12.5, FontWeights.SemiBold) : content, Width = w, Height = h, Cursor = Cursors.Hand, Focusable = false,
            Template = _pillTemplate, Margin = new Thickness(0, 0, 6, 0),
        };
        if (bg == null) b.SetResourceReference(BackgroundProperty, "WFill"); else b.Background = bg;
        b.Click += (_, e) => { e.Handled = true; if (!_widgetsEditing) click(); };
        // interactive glass: swells a little while pressed
        var sc = new ScaleTransform(1, 1); b.RenderTransform = sc; b.RenderTransformOrigin = new Point(0.5, 0.5);
        void To(double v) { if (Motion.Reduced) return; var a = new DoubleAnimation(v, TimeSpan.FromMilliseconds(v > 1 ? 160 : 300)) { EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut } }; sc.BeginAnimation(ScaleTransform.ScaleXProperty, a); sc.BeginAnimation(ScaleTransform.ScaleYProperty, a); }
        b.PreviewMouseLeftButtonDown += (_, _) => { if (!_widgetsEditing) To(1.06); };
        b.PreviewMouseLeftButtonUp += (_, _) => To(1);
        b.MouseLeave += (_, _) => To(1);
        return b;
    }

    private static (Grid Track, Border Fill) Bar(double height, Brush fill)
    {
        var track = new Grid { Height = height };
        var bgTrack = new Border { CornerRadius = new CornerRadius(height / 2) };
        bgTrack.SetResourceReference(Border.BackgroundProperty, "WTrack");
        track.Children.Add(bgTrack);
        var f = new Border { CornerRadius = new CornerRadius(height / 2), Background = fill, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        track.Children.Add(f);
        return (track, f);
    }

    /// <summary>A ring like the Activity rings: a track and an arc from 12 o'clock.</summary>
    private static (Grid Ring, Action<double> Set) RingGauge(double size, double thickness, Color color)
    {
        var g = new Grid { Width = size, Height = size };
        g.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B)), StrokeThickness = thickness, Margin = new Thickness(0) });
        var arc = new System.Windows.Shapes.Path { Stroke = new SolidColorBrush(color), StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        g.Children.Add(arc);
        return (g, v => arc.Data = v <= 0.002 ? null : RingGeometry(size, thickness, Math.Clamp(v, 0, 1)));
    }

    private static string Bytes(double b) => b >= 1 << 30 ? $"{b / (1 << 30):0.0} GB" : b >= 1 << 20 ? $"{b / (1 << 20):0.0} MB" : $"{b / 1024:0} KB";

    /// <summary>Builds the widget into body (wash = its colour layer); returns what refreshes it every second.</summary>
    private Action BuildWidget(string id, Grid body, Border wash) => id switch
    {
        "worldclock" when _buildSize == WidgetSize.Large => WorldClockBig(body),
        "month" when _buildSize == WidgetSize.Large => MonthWidget(body, big: true),
        "music" => MusicWidget(body, wash),
        "weather" => WeatherWidget(body, wash),
        "clock" => ClockWidget(body),
        "calendar" => CalendarWidget(body),
        "worldclock" => WorldClockWidget(body),
        "sun" => SunWidget(body, wash),
        "timer" => TimerWidget(body),
        "reminders" => RemindersWidget(body),
        "notes" => NotesWidget(body),
        "clipboard" => ClipboardWidget(body),
        "focus" => FocusWidget(body, wash),
        "actions" => ActionsWidget(body),
        "system" => SystemWidget(body),
        "net" => NetWidget(body),
        "calc" => CalcWidget(body),
        "stopwatch" => StopwatchWidget(body),
        "water" => WaterWidget(body, wash),
        "todo" => TodoWidget(body, compact: true),
        "countdown" => CountdownWidget(body, wash),
        "volume" => VolumeWidget(body, wash),
        "launcher" => LauncherWidget(body, _buildSize == WidgetSize.Large ? 16 : 8),
        "photos" => PhotosWidget(body, wash, big: false),
        "moon" => MoonWidget(body),
        "month" => MonthWidget(body),
        "top" => TopWidget(body, _buildSize == WidgetSize.Large ? 8 : 4),
        "breathe" => BreatheWidget(body, wash),
        _ => LevelWidget(id, body, wash),
    };

    // ------------------------------------------------------------------ the widgets

    private WeatherData? _lastWeather;

    /// <summary>A sky for the weather card, by the condition and the time of day.</summary>
    private static Brush Sky(WeatherData? w)
    {
        (Color a, Color b) = w == null ? (Color.FromRgb(0x2E, 0x5E, 0xA8), Color.FromRgb(0x5E, 0x9C, 0xE0))
            : !w.IsDay ? (Color.FromRgb(0x0B, 0x16, 0x34), Color.FromRgb(0x2A, 0x2F, 0x5C))
            : w.Code >= 51 ? (Color.FromRgb(0x3E, 0x50, 0x66), Color.FromRgb(0x6E, 0x80, 0x96))     // rain, snow, storms
            : w.Code >= 3 ? (Color.FromRgb(0x4A, 0x6A, 0x8E), Color.FromRgb(0x86, 0xA2, 0xC0))      // overcast, fog
            : (Color.FromRgb(0x1E, 0x6F, 0xD9), Color.FromRgb(0x5C, 0xB4, 0xF5));                   // clear
        var g = new LinearGradientBrush(a, b, 90) { Opacity = 0.92 };
        g.Freeze();
        return g;
    }

    private Action CalendarWidget(Grid body)
    {
        var weekday = W("", 13, FontWeights.SemiBold, Solid(0xFF, 0x45, 0x3A));
        var day = W("", 40, FontWeights.Light, null, big: true); day.Margin = new Thickness(-1, -6, 0, 0);
        var next = W("", 12.5, FontWeights.SemiBold); next.TextWrapping = TextWrapping.Wrap; next.MaxHeight = 34;
        var when = W("", 11.5, null, Dim);
        var bar = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = Solid(0xFF, 0x45, 0x3A), Margin = new Thickness(0, 0, 7, 0) };
        var ev = new StackPanel(); ev.Children.Add(next); ev.Children.Add(when);
        var evRow = new DockPanel { VerticalAlignment = VerticalAlignment.Bottom };
        DockPanel.SetDock(bar, Dock.Left); evRow.Children.Add(bar); evRow.Children.Add(ev);
        var top = new StackPanel(); top.Children.Add(weekday); top.Children.Add(day);
        body.Children.Add(top); body.Children.Add(evRow);
        return () =>
        {
            var now = DateTime.Now;
            weekday.Text = now.ToString("dddd", Loc.Culture).ToUpper(Loc.Culture);
            day.Text = now.Day.ToString();
            var e = _calendar.Upcoming(7).FirstOrDefault(x => x.AllDay ? x.Start.Date >= now.Date : x.Start > now.AddMinutes(-5));
            next.Text = e?.Title ?? L.T("Нет событий");
            when.Text = e == null ? L.T("на этой неделе") : e.AllDay ? L.T("весь день") : e.Start.Date == now.Date ? e.Start.ToString("HH:mm") : e.Start.ToString("ddd, HH:mm", Loc.Culture);
            bar.Visibility = e == null ? Visibility.Collapsed : Visibility.Visible;
        };
    }

    private static readonly (string Name, string Zone)[] WorldCities =
    [
        ("Нью-Йорк", "Eastern Standard Time"), ("Лондон", "GMT Standard Time"), ("Токио", "Tokyo Standard Time"),
    ];

    private Action WorldClockWidget(Grid body)
    {
        var grid = new UniformGrid { Columns = 3, Rows = 1 };
        var cells = new List<(TextBlock Time, TextBlock Diff, TextBlock DayNight, TimeZoneInfo? Zone)>();
        foreach (var (name, zone) in ChosenCities().Take(3))
        {
            TimeZoneInfo? tz = null;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(zone); } catch { /* unknown time zone: local time */ }
            var city = W(L.T(name), 13, FontWeights.SemiBold);
            var diff = W("", 11, null, Dim);
            var time = W("", 30, FontWeights.Light, null, big: true);
            var dn = Glyph("", 12, Dim);
            var s = new StackPanel(); s.Children.Add(city); s.Children.Add(diff); s.Children.Add(new Border { Height = 16 }); s.Children.Add(time); s.Children.Add(dn);
            grid.Children.Add(s);
            cells.Add((time, diff, dn, tz));
        }
        body.Children.Add(grid);
        return () =>
        {
            var now = DateTime.Now;
            foreach (var (time, diff, dn, tz) in cells)
            {
                if (tz == null) { time.Text = "—"; continue; }
                var t = TimeZoneInfo.ConvertTime(now, tz);
                time.Text = t.ToString("HH:mm");
                int h = (int)Math.Round((t - now).TotalHours);
                diff.Text = (t.Date > now.Date ? L.T("Завтра") : t.Date < now.Date ? L.T("Вчера") : L.T("Сегодня")) + (h == 0 ? "" : $", {(h > 0 ? "+" : "")}{h} {L.T("ч")}");
                dn.Text = t.Hour is >= 6 and < 20 ? "" : "";
            }
        };
    }

    private Action SunWidget(Grid body, Border wash)
    {
        var cap = Caption("", L.T("Солнце"), Solid(0xFF, 0xD6, 0x0A));
        var arc = new Canvas { Height = 46, Margin = new Thickness(0, 30, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var path = new System.Windows.Shapes.Path { Stroke = Solid(0xFF, 0xFF, 0xFF, 0x60), StrokeThickness = 1.6, StrokeDashArray = [2, 2.5] };
        var dot = new Ellipse { Width = 12, Height = 12, Fill = Solid(0xFF, 0xD6, 0x0A), Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0xD6, 0x0A), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 } };
        arc.Children.Add(path); arc.Children.Add(dot);
        var rise = W("", 13, FontWeights.SemiBold); var set = W("", 13, FontWeights.SemiBold); set.HorizontalAlignment = HorizontalAlignment.Right;
        var length = W("", 11, null, Dim);
        var times = new Grid(); times.Children.Add(rise); times.Children.Add(set);
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom }; s.Children.Add(times); s.Children.Add(length);
        body.Children.Add(cap); body.Children.Add(arc); body.Children.Add(s);
        wash.Background = new LinearGradientBrush(Color.FromArgb(0x50, 0xFF, 0x9F, 0x0A), Color.FromArgb(0x10, 0xFF, 0x5E, 0x3A), 90);
        return () =>
        {
            var w = _lastWeather;
            if (w == null || w.Sunrise == default) { rise.Text = "—"; set.Text = ""; return; }
            rise.Text = "↑ " + w.Sunrise.ToString("HH:mm"); set.Text = w.Sunset.ToString("HH:mm") + " ↓";
            var len = w.Sunset - w.Sunrise;
            length.Text = L.F("День {0} ч {1} мин", (int)len.TotalHours, len.Minutes);
            double width = arc.ActualWidth > 0 ? arc.ActualWidth : SmallSide - 32;
            var g = new StreamGeometry();
            using (var c = g.Open()) { c.BeginFigure(new Point(0, 44), false, false); c.BezierTo(new Point(width * 0.25, -12), new Point(width * 0.75, -12), new Point(width, 44), true, true); }
            path.Data = g;
            double f = Math.Clamp((DateTime.Now - w.Sunrise) / len, 0, 1);
            double u = 1 - f;   // the point of the curve at f
            double x = 3 * u * u * f * width * 0.25 + 3 * u * f * f * width * 0.75 + f * f * f * width;
            double y = u * u * u * 44 + 3 * u * u * f * -12 + 3 * u * f * f * -12 + f * f * f * 44;
            Canvas.SetLeft(dot, x - 6); Canvas.SetTop(dot, y - 6);
            dot.Opacity = DateTime.Now > w.Sunrise && DateTime.Now < w.Sunset ? 1 : 0.35;
        };
    }

    private Action TimerWidget(Grid body)
    {
        var accent = Acc(0xFF, 0x9F, 0x0A);
        var (ring, set) = RingGauge(64, 6, AccC(Color.FromRgb(0xFF, 0x9F, 0x0A)));
        ring.HorizontalAlignment = HorizontalAlignment.Left; ring.VerticalAlignment = VerticalAlignment.Top;
        var mainIcon = Glyph("", 16, accent); mainIcon.HorizontalAlignment = HorizontalAlignment.Center;
        ring.Children.Add(mainIcon);
        ring.Background = Brushes.Transparent;
        ring.Cursor = Cursors.Hand;
        ring.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (_widgetsEditing) return; if (_timer.Running) _timer.Pause(); else if (_timer.Active) _timer.Start(); else StartQuickTimer(TimeSpan.FromMinutes(5)); };
        var display = W("", 24, FontWeights.Light, null, big: true); display.HorizontalAlignment = HorizontalAlignment.Right; display.VerticalAlignment = VerticalAlignment.Top; display.Margin = new Thickness(0, 14, 0, 0);
        var quick = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        foreach (var m in new[] { 5, 10, 25 }) quick.Children.Add(Pill(m.ToString(), () => StartQuickTimer(TimeSpan.FromMinutes(m)), null, 36, 28));
        body.Children.Add(ring); body.Children.Add(display); body.Children.Add(quick);
        return () =>
        {
            display.Text = _timer.Active ? _timer.Display : "";
            set(_timer.Active ? Math.Max(0.02, _timer.Progress) : 0);
            mainIcon.Text = _timer.Running ? "" : "";
        };
    }

    private bool _buildBig => _buildSize == WidgetSize.Large;

    private Action RemindersWidget(Grid body)
    {
        bool big = _buildBig;
        var accent = Acc(0x0A, 0x84, 0xFF);
        var head = new DockPanel();
        var count = W("", 28, FontWeights.Light, accent, big: true); DockPanel.SetDock(count, Dock.Right); count.Margin = new Thickness(0, -8, 0, 0);
        head.Children.Add(count); head.Children.Add(Caption("", L.T("Напоминания"), accent));
        var list = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var s = new StackPanel(); s.Children.Add(head); s.Children.Add(list);
        body.Children.Add(s);
        string last = "-";
        return () =>
        {
            var now = DateTime.Now;
            var items = _calendar.Upcoming(big ? 7 : 2).Where(e => e.AllDay || e.Start > now.AddMinutes(-5)).Take(big ? 7 : 3).ToList();
            var key = string.Join("|", items.Select(e => e.Title + e.Start.Ticks));
            if (key == last) return;
            last = key;
            count.Text = items.Count.ToString();
            list.Children.Clear();
            if (items.Count == 0) list.Children.Add(W(L.T("Ничего не запланировано"), 13, null, Dim));
            foreach (var e in items)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
                var dot = new Ellipse { Width = 14, Height = 14, Stroke = accent, StrokeThickness = 1.6, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
                var when = W(e.AllDay ? L.T("весь день") : e.Start.Date == now.Date ? e.Start.ToString("HH:mm") : e.Start.ToString("ddd HH:mm", Loc.Culture), 11.5, null, Dim);
                DockPanel.SetDock(dot, Dock.Left); DockPanel.SetDock(when, Dock.Right); when.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(dot); row.Children.Add(when); row.Children.Add(W(e.Title, 13));
                list.Children.Add(row);
            }
        };
    }

    private Action ClipboardWidget(Grid body)
    {
        bool big = _buildSize == WidgetSize.Large;
        var accent = Solid(0x64, 0xD2, 0xFF);
        var head = Caption("", L.T("Буфер обмена"), accent);
        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var s = new StackPanel(); s.Children.Add(head); s.Children.Add(list);
        body.Children.Add(s);
        string last = "-";
        return () =>
        {
            var items = _clips.Where(c => !string.IsNullOrWhiteSpace(c.Text)).Take(big ? 7 : 3).ToList();
            var key = string.Join("|", items.Select(c => c.Text)) + DateTime.Now.ToString("HHmm");
            if (key == last) return;
            last = key;
            list.Children.Clear();
            if (items.Count == 0) list.Children.Add(W(L.T("Скопируйте что-нибудь"), 13, null, Dim));
            foreach (var c in items)
            {
                var text = c.Text!.Replace('\n', ' ').Trim();
                var row = new Border { CornerRadius = new CornerRadius(9), Background = Solid(0xFF, 0xFF, 0xFF, 0x1A), Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 0, 5), Cursor = Cursors.Hand };
                var line = new DockPanel();
                var ago = W(Ago(c.At), 11, null, Dim); DockPanel.SetDock(ago, Dock.Right); ago.Margin = new Thickness(8, 0, 0, 0);
                line.Children.Add(ago); line.Children.Add(W(text, 12.5));
                row.Child = line;
                var copy = c.Text;
                row.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    if (_widgetsEditing) return;
                    try { Clipboard.SetText(copy); ShowToast("\uE8C8", L.T("Скопировано"), text.Length > 40 ? text[..40] + "…" : text); }
                    catch (Exception ex) { Log.Write("widget copy: " + ex.Message); ShowToast("\uE783", L.T("Не получилось скопировать"), L.T("Буфер обмена занят другой программой")); }
                };
                list.Children.Add(row);
            }
        };
    }

    private static string Ago(DateTime at)
    {
        var d = DateTime.Now - at;
        return d.TotalMinutes < 1 ? L.T("сейчас") : d.TotalHours < 1 ? L.F("{0} мин", (int)d.TotalMinutes) : d.TotalDays < 1 ? L.F("{0} ч", (int)d.TotalHours) : at.ToString("d MMM", Loc.Culture);
    }

    private Action FocusWidget(Grid body, Border wash)
    {
        var moon = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Solid(0xFF, 0xFF, 0xFF, 0x22), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand };
        var moonIcon = Glyph("", 18); moonIcon.HorizontalAlignment = HorizontalAlignment.Center; moon.Child = moonIcon;
        var state = W("", 15, FontWeights.SemiBold); var until = W("", 11.5, null, Dim);
        var info = new StackPanel { Margin = new Thickness(0, 48, 0, 0) }; info.Children.Add(state); info.Children.Add(until);
        var pills = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        foreach (var (label, min) in new[] { ("30", 30), ("1ч", 60), ("2ч", 120) })
            pills.Children.Add(Pill(L.T(label), () => { FocusMode.Start(TimeSpan.FromMinutes(min)); _focusByPomodoro = false; }, null, 36, 28));
        moon.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (_widgetsEditing) return; if (FocusMode.IsOn) FocusMode.End(); else FocusMode.Start(TimeSpan.FromHours(1)); _focusByPomodoro = false; };
        body.Children.Add(moon); body.Children.Add(info); body.Children.Add(pills);
        return () =>
        {
            bool on = FocusMode.IsOn;
            wash.Background = on ? new LinearGradientBrush(Color.FromArgb(0x90, 0x5E, 0x5C, 0xE6), Color.FromArgb(0x50, 0xBF, 0x5A, 0xF2), 90) : null;
            moon.Background = on ? Solid(0xFF, 0xFF, 0xFF) : Solid(0xFF, 0xFF, 0xFF, 0x22);
            moonIcon.Foreground = on ? Solid(0x5E, 0x5C, 0xE6) : Brushes.White;
            state.Text = on ? L.T("Не беспокоить") : L.T("Фокус выключен");
            until.Text = on ? L.F("до {0:HH:mm}", FocusMode.Until) : L.T("уведомления видны");
            pills.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        };
    }

    private Action ActionsWidget(Grid body)
    {
        body.Margin = new Thickness(12);
        var grid = new UniformGrid { Columns = 2, Rows = 2 };
        foreach (var (id, glyph, color) in new[] { ("region", "", Color.FromRgb(0x0A, 0x84, 0xFF)), ("record", "", Color.FromRgb(0xFF, 0x45, 0x3A)),
                                                   ("ocr", "", Color.FromRgb(0xFF, 0x9F, 0x0A)), ("picker", "", Color.FromRgb(0x30, 0xD1, 0x58)) })
        {
            var tile = new Border { CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromArgb(0x3A, color.R, color.G, color.B)), Margin = new Thickness(3), Cursor = Cursors.Hand };
            var g = Glyph(glyph, 20, new SolidColorBrush(color)); g.HorizontalAlignment = HorizontalAlignment.Center;
            tile.Child = g;
            tile.ToolTip = AllActions.FirstOrDefault(a => a.Id == id).Label;
            var action = id;
            tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (!_widgetsEditing) _ = RunAction(action); };
            grid.Children.Add(tile);
        }
        body.Children.Add(grid);
        return () => { };
    }

    private Action NetWidget(Grid body)
    {
        var accent = Solid(0x30, 0xD1, 0x58);
        var down = W("", 22, FontWeights.Light, null, big: true); var up = W("", 15, FontWeights.Light, null, big: true);
        StackPanel Row(string glyph, TextBlock value, Brush c, string label)
        {
            var r = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var l = new StackPanel { Orientation = Orientation.Horizontal };
            var i = Glyph(glyph, 10, c); i.Margin = new Thickness(0, 0, 4, 0);
            l.Children.Add(i); l.Children.Add(W(label, 11, FontWeights.SemiBold, Dim));
            r.Children.Add(l); r.Children.Add(value);
            return r;
        }
        var s = new StackPanel(); s.Children.Add(Caption("", L.T("Сеть"), accent));
        s.Children.Add(Row("", down, Solid(0x64, 0xD2, 0xFF), L.T("Загрузка")));
        s.Children.Add(Row("", up, Solid(0xFF, 0x9F, 0x0A), L.T("Отдача")));
        body.Children.Add(s);
        return () => { down.Text = Bytes(_netDown) + "/s"; up.Text = Bytes(_netUp) + "/s"; };
    }

    /// <summary>The level tiles: the card fills from the bottom with colour, the number large in the corner.</summary>
    private Action LevelWidget(string id, Grid body, Border wash)
    {
        var kind = WidgetKinds.First(k => k.Id == id);
        var (c1, c2, glyph) = id switch
        {
            "cpu" => (Color.FromRgb(0xFF, 0x37, 0x5F), Color.FromRgb(0xFF, 0x9F, 0x0A), ""),
            "gpu" => (Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0x2E, 0xD4, 0xFF), ""),
            "ram" => (Color.FromRgb(0xBF, 0x5A, 0xF2), Color.FromRgb(0x5E, 0x5C, 0xE6), ""),
            "disk" => (Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0xFF, 0xD6, 0x0A), ""),
            _ => (Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0x34, 0xC7, 0x59), ""),
        };
        if (_buildAccent is { } acc) { c1 = acc; c2 = Color.FromRgb((byte)(acc.R * 0.7), (byte)(acc.G * 0.7), (byte)(acc.B * 0.7)); }
        var fill = new Border { VerticalAlignment = VerticalAlignment.Bottom, Height = 0, Background = new LinearGradientBrush(Color.FromArgb(0xC8, c1.R, c1.G, c1.B), Color.FromArgb(0x90, c2.R, c2.G, c2.B), 90) };
        wash.Child = fill;
        var light = Color.FromRgb((byte)Math.Min(255, c1.R + 40), (byte)Math.Min(255, c1.G + 40), (byte)Math.Min(255, c1.B + 40));
        var cap = Caption(glyph, kind.Name, new SolidColorBrush(light));
        var value = W("", 34, FontWeights.Light, null, big: true); value.VerticalAlignment = VerticalAlignment.Bottom; value.Margin = new Thickness(0, 0, 0, 14);
        var sub = W("", 11.5, FontWeights.SemiBold, Dim); sub.VerticalAlignment = VerticalAlignment.Bottom;
        body.Children.Add(cap); body.Children.Add(value); body.Children.Add(sub);
        double shown = 0;
        return () =>
        {
            double level = 0; string v = "—", s = "";
            var st = _stats.Last;
            switch (id)
            {
                case "cpu": if (st != null) { level = st.Cpu / 100; v = $"{st.Cpu:0}%"; s = L.T("загрузка"); } break;
                case "gpu": if (st != null) { level = st.Gpu / 100; v = $"{st.Gpu:0}%"; s = L.T("загрузка"); } break;
                case "ram": if (st != null) { level = st.Ram / 100; v = $"{st.RamUsedGb:0.0}"; s = L.F("из {0:0} ГБ", st.RamTotalGb); } break;
                case "disk":
                    try
                    {
                        var d = new DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory)!);
                        level = (d.TotalSize - d.AvailableFreeSpace) / (double)d.TotalSize;
                        var free = Bytes(d.AvailableFreeSpace).Split(' ');
                        v = free[0]; s = L.F("{0} свободно", free[1]);
                    }
                    catch { /* drive not ready */ }
                    break;
                case "battery":
                    int b = BatteryPercent();
                    if (b >= 0) { level = b / 100.0; v = b + "%"; s = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? L.T("заряжается") : L.T("от батареи"); }
                    break;
            }
            shown += (Math.Clamp(level, 0, 1) - shown) * 0.5;
            fill.Height = Math.Max(0, (wash.ActualHeight > 0 ? wash.ActualHeight : SmallSide) * shown);
            value.Text = v; sub.Text = s;
        };
    }
}
