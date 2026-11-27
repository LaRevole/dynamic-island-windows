using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>Settings tab.</summary>
public partial class MainWindow
{
    private bool _settingsLoading;

    /// <summary>One row of the language list: its own name, and the English name as a hint.</summary>
    public sealed record LanguageOption(string Code, string Name, string Hint);

    private static readonly Dictionary<string, string> EnglishNames = new()
    {
        ["en"] = "", ["ru"] = "Russian", ["uk"] = "Ukrainian", ["es"] = "Spanish", ["de"] = "German", ["pt"] = "Portuguese", ["zh"] = "Chinese",
    };

    private void BuildLanguageList()
    {
        var auto = Services.Loc.Resolve("");
        var autoName = Services.Loc.Languages.First(l => l.Code == auto).Name;
        var options = new List<LanguageOption> { new("", L.T("Как в Windows"), autoName) };
        options.AddRange(Services.Loc.Languages.Select(l => new LanguageOption(l.Code, l.Name, EnglishNames[l.Code])));
        LanguageBox.ItemsSource = options;
        LanguageSub.Visibility = Services.Loc.Lang == "en" ? Visibility.Collapsed : Visibility.Visible;
        LanguageBox.SelectedItem = options.FirstOrDefault(o => o.Code == _settings.Language) ?? options[0];
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingsLoading || LanguageBox.SelectedItem is not LanguageOption opt || opt.Code == _settings.Language) return;
        SwitchLanguage(opt.Code);
    }

    /// <summary>Save the language and, if it really changes, relaunch: the interface is translated at start-up.</summary>
    private void SwitchLanguage(string code)
    {
        _settings.Language = code;
        _settings.Save();
        if (Services.Loc.Resolve(code) == Services.Loc.Lang) return;
        try
        {
            ((App)Application.Current).ReleaseSingleInstance();   // let the new copy start right away
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--restart") { UseShellExecute = false });
            Close();
        }
        catch (Exception ex) { ShowToast("", L.T("Не получилось"), ex.Message); }
    }

    /// <summary>Quick uninstall from the settings: the installer's uninstaller, or a clean self-removal for a portable copy.</summary>
    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, L.T("Удалить Atoll с этого компьютера?"), "Atoll",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        var uninstaller = Services.Uninstaller.InstalledUninstaller;
        if (uninstaller != null)
        {
            // installed with the setup: its uninstaller removes everything and asks about personal data
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uninstaller) { UseShellExecute = true });
            Close();
            return;
        }

        bool data = MessageBox.Show(this, L.T("Удалить также настройки, заметки и вход в чаты?"), "Atoll",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        Services.Uninstaller.RemoveRegistrations();
        Services.Uninstaller.ScheduleDelete(data, appFolder: true);
        Close();
    }

    private void InitSettingsPage()
    {
        BuildLanguageList();
        BuildThemeGallery();
        foreach (var hex in Theme.Palette)
        {
            var b = new Button
            {
                Style = (Style)FindResource("Swatch"),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                ToolTip = hex,
            };
            b.Click += (_, _) => SetIslandColor(hex);
            SwatchPanel.Children.Add(b);
        }

        BuildLayoutPage();
        ApplyTabLayout();
        BuildCharacterGallery();
        BuildSettingsNav();
        BuildAuraChips();
        RefreshSettingsPage();
    }

    private void BuildCharacterGallery()
    {
        foreach (var (id, name) in Views.CharacterView.All)
        {
            var preview = new Views.CharacterView { Placeholder = true, Kind = id, LayoutTransform = new ScaleTransform(1.5, 1.5), HorizontalAlignment = HorizontalAlignment.Center };
            _previewCharacters.Add(preview);
            var stack = new StackPanel();
            stack.Children.Add(new Border { Height = 40, Child = preview });
            stack.Children.Add(new TextBlock { Text = name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });

            var card = new Border
            {
                Width = 126, Padding = new Thickness(6, 8, 6, 8), Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1.5), Cursor = Cursors.Hand, Child = stack, Tag = id,
            };
            card.SetResourceReference(Border.BackgroundProperty, "ChipBg");
            card.MouseLeftButtonUp += (_, _) =>
            {
                if (_justDragged) return;
                _settings.Character = id;
                _settings.Save();
                Character.Kind = id;
                preview.Cheer();
                SetupAura(); // Claude Code lights the island orange
                HighlightCharacter();
            };
            CharacterGallery.Children.Add(card);
        }
        HighlightCharacter();
    }

    private void HighlightCharacter()
    {
        foreach (Border card in CharacterGallery.Children)
        {
            if ((string)card.Tag == _settings.Character) card.SetResourceReference(Border.BorderBrushProperty, "Accent");
            else card.BorderBrush = Brushes.Transparent;
        }
    }

    private void Reactive_Click(object sender, RoutedEventArgs e)
    {
        _settings.Reactive = ReactiveSwitch.IsChecked == true;
        _settings.MusicWaves = MusicWavesSwitch.IsChecked == true;
        _settings.Save();
        UpdateShape(false);
    }

    private void HandDrawn_Click(object sender, RoutedEventArgs e)
    {
        _settings.HandDrawn = HandDrawnSwitch.IsChecked == true;
        _settings.Save();
    }

    private void ClaudeGlow_Click(object sender, RoutedEventArgs e)
    {
        _settings.ClaudeGlow = ClaudeGlowSwitch.IsChecked == true;
        _settings.Save();
        _auraThinking = false;
        SetupAura();
    }

    private void RefreshSettingsPage()
    {
        _settingsLoading = true;
        DarkSwitch.IsChecked = _settings.DarkTheme;
        LiquidSwitch.IsChecked = _settings.LiquidWaves;
        // the scrubbers say what they set and show the value inside
        ScaleSlider.Label = L.T("Размер острова"); LiquidSlider.Label = L.T("Сила волн"); VoiceVolumeSlider.Label = L.T("Громкость ИИ-ассистента");
        ScaleSlider.Format = LiquidSlider.Format = VoiceVolumeSlider.Format = v => $"{v:0}%";
        LiquidSlider.Value = Math.Clamp(_settings.LiquidStrength, 10, 100);
        LiquidSlider.Visibility = _settings.LiquidWaves ? Visibility.Visible : Visibility.Collapsed;
        RefreshLookControls();
        ReactiveSwitch.IsChecked = _settings.Reactive;
        MusicWavesSwitch.IsChecked = _settings.MusicWaves;

        ClaudeGlowSwitch.IsChecked = _settings.ClaudeGlow;
        CleanDesktopSwitch.IsChecked = _settings.CleanDesktop;
        HandDrawnSwitch.IsChecked = _settings.HandDrawn;
        GameStatsSwitch.IsChecked = _settings.GameStats;
        GameOnlyListedSwitch.IsChecked = _settings.GameStatsOnlyListed;
        RefreshGameAppsBlock();
        LockSwitch.IsChecked = _settings.LockPosition;
        SmartSwitch.IsChecked = _settings.SmartIsland;
        FillHideAppPicker();
        CapsSwitch.IsChecked = _settings.ShowCapsLock;
        LayoutSwitch.IsChecked = _settings.ShowKeyboardLayout;
        LevelsSwitch.IsChecked = _settings.ShowLevels;
        ProgressSwitch.IsChecked = _settings.ShowProgress;
        MonitorSwitch.IsChecked = _settings.FollowMonitor;
        BuildThemeEditor();
        if (CheckHost.IsVisible) _ = RunSelfCheckAsync();
        DiscordSideSwitch.IsChecked = _settings.DiscordSidePill;
        DiscordShowSwitch.IsChecked = _settings.ShowDiscordCall;
        // only when it differs: setting it again on every refresh used to wipe a key that was being typed in
        if (GeminiVoiceApiKeyBox.Password != _settings.GeminiVoiceApiKey && !GeminiVoiceApiKeyBox.IsKeyboardFocusWithin)
            GeminiVoiceApiKeyBox.Password = _settings.GeminiVoiceApiKey;
        VoiceVolumeSlider.Value = Math.Clamp(_settings.GeminiVoiceVolume, 0, 100);
        DiscordSideSwitch.IsEnabled = _settings.ShowDiscordCall;
        CaptureSwitch.IsChecked = HideFromCapture;
        ShotModeBox.SelectedIndex = _settings.ScreenshotQuick ? 1 : 0;
        ShotRevealSwitch.IsChecked = _settings.ScreenshotReveal;
        ScaleSlider.Value = Math.Clamp(_settings.IslandScale, 80, 130);
        LiteSwitch.IsChecked = Lite;
        if (OpenHotkeyBox.Items.Count == 0)
        {
            foreach (var h in OpenHotkeys) OpenHotkeyBox.Items.Add(h.Name);
            OpenHotkeyBox.Items.Add(L.T("Выключено"));
        }
        OpenHotkeyBox.SelectedIndex = Math.Max(0, Array.FindIndex(OpenHotkeys, h => h.Name == _settings.OpenHotkey) is int i && i >= 0 ? i : OpenHotkeys.Length);
        OpenHotkeyStatus.Text = L.T("Ещё раз — свернуть. Esc или клик мимо — тоже.");
        if (CommandHotkeyBox.Items.Count == 0)
        {
            foreach (var h in CommandHotkeys) CommandHotkeyBox.Items.Add(h.Name);
            CommandHotkeyBox.Items.Add(L.T("Выключено"));
        }
        CommandHotkeyBox.SelectedIndex = Array.FindIndex(CommandHotkeys, h => h.Name == _settings.CommandHotkey) is int ci && ci >= 0 ? ci : CommandHotkeys.Length;
        CommandHotkeyStatus.Text = CommandHotkeyHint();
        if (TranslateHotkeyBox.Items.Count == 0)
        {
            foreach (var h in TranslateHotkeys) TranslateHotkeyBox.Items.Add(h.Name);
            TranslateHotkeyBox.Items.Add(L.T("Выключено"));
        }
        TranslateHotkeyBox.SelectedIndex = Array.FindIndex(TranslateHotkeys, h => h.Name == _settings.TranslateHotkey) is int ti && ti >= 0 ? ti : TranslateHotkeys.Length;
        TranslateHotkeyStatus.Text = L.T("Копирует выделение и показывает перевод под островом.");
        MicMuteDotSwitch.IsChecked = _settings.ShowMicMuteDot;
        DonateButton.Visibility = DonateUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshFpsAccess();
        KaraokeSwitch.IsChecked = _settings.Karaoke;
        NotifySwitch.IsChecked = _settings.MirrorNotifications;
        RefreshNotifyHint();
        NotifyAllSwitch.IsChecked = _settings.MirrorAllApps;
        ColorBox.Text = _settings.IslandColor;
        HomeActionsChip.IsChecked = _settings.HomeActions;
        HomeMusicChip.IsChecked = _settings.HomeMusic;
        HomeWeatherChip.IsChecked = _settings.HomeWeather;
        HomeAppsChip.IsChecked = _settings.HomeApps;
        HoverSwitch.IsChecked = _settings.ExpandOnHover;
        YieldSwitch.IsChecked = _settings.YieldToCursor;
        PlacePerAppSwitch.IsChecked = _settings.PlacePerApp;
        FullscreenSwitch.IsChecked = _settings.HideInFullscreen;
        AutostartSwitch.IsChecked = Autostart.IsEnabled;
        if (AppPackage.IsPackaged) UninstallButton.Visibility = Visibility.Collapsed;   // Store apps are removed in Windows Settings
        CommandBox.Text = _settings.Command;
        TranslateBox.Text = _settings.TranslateTo;
        RecSysSwitch.IsChecked = _settings.RecordSystemAudio;
        RecMicSwitch.IsChecked = _settings.RecordMicrophone;
        RefreshClaudeHooksUi();
        RefreshCodexHooksUi();
        _settingsLoading = false;
    }

    private void DarkSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsLoading) return;
        if ((DarkSwitch.IsChecked == true) != _settings.DarkTheme) ToggleTheme();
    }

    private void SetIslandColor(string hex)
    {
        if (Theme.Parse(hex) == null)
        {
            ColorBox.Text = _settings.IslandColor;
            return;
        }
        _settings.IslandColor = hex;
        _settings.ThemeId = "";
        _settings.Save();
        ColorBox.Text = hex;
        ApplyTheme();
        Kick(4);
    }



    private void Liquid_Click(object sender, RoutedEventArgs e)
    {
        _settings.LiquidWaves = LiquidSwitch.IsChecked == true;
        _settings.Save();
        LiquidSlider.Visibility = _settings.LiquidWaves ? Visibility.Visible : Visibility.Collapsed;
        Remeasure();
        if (_settings.LiquidWaves) Kick(5);
        else
        {
            _ripple = 0;
            _skew.Snap(0);
            _sx.Snap(1);
            _sy.Snap(1);
            UpdateShape(false);
        }
    }


    private void ColorApply_Click(object sender, RoutedEventArgs e) => SetIslandColor(NormalizeHex(ColorBox.Text));

    private void ColorBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SetIslandColor(NormalizeHex(ColorBox.Text));
    }

    private static string NormalizeHex(string s)
    {
        s = s.Trim();
        return s.StartsWith('#') ? s : "#" + s;
    }

    private void HomeLayout_Click(object sender, RoutedEventArgs e)
    {
        _settings.HomeActions = HomeActionsChip.IsChecked == true;
        _settings.HomeMusic = HomeMusicChip.IsChecked == true;
        _settings.HomeWeather = HomeWeatherChip.IsChecked == true;
        _settings.HomeApps = HomeAppsChip.IsChecked == true;
        _settings.Save();
        BuildHome();
    }

    private void Behavior_Click(object sender, RoutedEventArgs e)
    {
        // "hide in fullscreen" and "FPS in games" exclude each other: turning one on turns the other off
        if (sender == FullscreenSwitch && FullscreenSwitch.IsChecked == true) GameStatsSwitch.IsChecked = false;
        if (sender == GameStatsSwitch && GameStatsSwitch.IsChecked == true) FullscreenSwitch.IsChecked = false;
        _settings.ExpandOnHover = HoverSwitch.IsChecked == true;
        _settings.YieldToCursor = YieldSwitch.IsChecked == true;
        if (_settings.PlacePerApp != (PlacePerAppSwitch.IsChecked == true))
        {
            _settings.PlacePerApp = PlacePerAppSwitch.IsChecked == true;
            if (!_settings.PlacePerApp && _placeOwner != null) { _placeOwner = null; FloatToPlace(); }   // off: back to the usual place
        }
        _settings.HideInFullscreen = FullscreenSwitch.IsChecked == true;
        _settings.GameStats = GameStatsSwitch.IsChecked == true;
        _settings.GameStatsOnlyListed = GameOnlyListedSwitch.IsChecked == true;
        RefreshGameAppsBlock();
        Remeasure();
        _settings.LockPosition = LockSwitch.IsChecked == true;
        _settings.SmartIsland = SmartSwitch.IsChecked == true;
        _settings.ShowCapsLock = CapsSwitch.IsChecked == true;
        _settings.ShowKeyboardLayout = LayoutSwitch.IsChecked == true;
        _settings.ShowLevels = LevelsSwitch.IsChecked == true;
        _settings.ShowProgress = ProgressSwitch.IsChecked == true;
        _settings.FollowMonitor = MonitorSwitch.IsChecked == true;
        _settings.DiscordSidePill = DiscordSideSwitch.IsChecked == true;
        _settings.ShowDiscordCall = DiscordShowSwitch.IsChecked == true;
        _settings.ShowMicMuteDot = MicMuteDotSwitch.IsChecked == true;
        UpdateMicMuteBadge();
        DiscordSideSwitch.IsEnabled = _settings.ShowDiscordCall;
        if (HideFromCapture != (CaptureSwitch.IsChecked == true))
        {
            _settings.CaptureHidden = CaptureSwitch.IsChecked == true;
            ApplyCaptureExclusion();
            CaptureFrost();
            UpdateGlassFill();
        }
        _settings.ScreenshotReveal = ShotRevealSwitch.IsChecked == true;
        _settings.Karaoke = KaraokeSwitch.IsChecked == true;
        _settings.MirrorNotifications = NotifySwitch.IsChecked == true;
        RefreshNotifyHint();
        _settings.MirrorAllApps = NotifyAllSwitch.IsChecked == true;
        _settings.Save();
    }

    private void RefreshGameAppsBlock()
    {
        GameAppsBlock.Visibility = _settings.GameStats ? Visibility.Visible : Visibility.Collapsed;
        GameAppsList.Visibility = _settings.GameStatsOnlyListed ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Why messages may not show up: the notification center is off, or no access.</summary>
    private void RefreshNotifyHint()
    {
        string? hint = null;
        if (_settings.MirrorNotifications)
        {
            if (NotificationMirror.CenterDisabled())
                hint = L.T("Центр уведомлений Windows отключён (обычно это делают программы-«твикеры»), поэтому Windows не хранит уведомления и Atoll не видит их текст. Остров всё равно скажет «новое сообщение» — по счётчику непрочитанных в окне мессенджера. Чтобы видеть и текст, включите центр уведомлений обратно в той же программе.");
            else if (!_notifications.Available)
                hint = L.T("Windows не дал доступ к уведомлениям, поэтому остров показывает только «новое сообщение» без текста. Чтобы видеть текст: «Параметры → Конфиденциальность → Уведомления» — разрешите приложениям доступ.");
            else
                hint = L.T("В Telegram Desktop включите «Настройки → Уведомления → Системные уведомления», иначе Telegram показывает свои окна, которые Windows не видит.");
        }
        NotifyHint.Text = hint ?? "";
        NotifyHint.Visibility = hint == null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The Atoll channel on Telegram.</summary>
    private const string TelegramUrl = "https://t.me/atollisland";
    /// <summary>Where donations go; the button stays hidden while this is empty.</summary>
    private const string DonateUrl = "";

    private void Telegram_Click(object sender, RoutedEventArgs e) => SafeShell.OpenUrl(TelegramUrl);
    private void Donate_Click(object sender, RoutedEventArgs e) { if (DonateUrl.Length > 0) SafeShell.OpenUrl(DonateUrl); }

    private void OpenHotkey_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_settingsLoading || OpenHotkeyBox.SelectedIndex < 0) return;
        int i = OpenHotkeyBox.SelectedIndex;
        var chosen = i < OpenHotkeys.Length ? OpenHotkeys[i].Name : "";
        // Ctrl+Shift+Space is offered for both: one shortcut can do only one of them
        if (chosen.Length > 0 && chosen == _settings.CommandHotkey)
        {
            _settingsLoading = true;
            OpenHotkeyBox.SelectedIndex = Array.FindIndex(OpenHotkeys, h => h.Name == _settings.OpenHotkey) is int was && was >= 0 ? was : OpenHotkeys.Length;
            _settingsLoading = false;
            OpenHotkeyStatus.Text = L.T("Это сочетание уже открывает командную строку — выберите другое.");
            return;
        }
        _settings.OpenHotkey = chosen;
        _settings.Save();
        OpenHotkeyStatus.Text = RegisterOpenHotkey()
            ? L.T("Ещё раз — свернуть. Esc или клик мимо — тоже.")
            : L.T("Это сочетание уже занято другой программой — выберите другое.");
    }

    private string CommandHotkeyHint() => _settings.CommandHotkey != "Alt+Space" ? ""
        : _commandKeyBackupOnly ? L.T("Alt+Space занят другой программой — работает Ctrl+Alt+Space.")
        : L.T("Если Alt+Space занят другой программой, работает Ctrl+Alt+Space.");


    private void ShotMode_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_settingsLoading || ShotModeBox.SelectedIndex < 0) return;
        _settings.ScreenshotQuick = ShotModeBox.SelectedIndex == 1;
        _settings.Save();
    }

    private void CommandHotkey_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)

    {
        if (_settingsLoading || CommandHotkeyBox.SelectedIndex < 0) return;
        int i = CommandHotkeyBox.SelectedIndex;
        var chosen = i < CommandHotkeys.Length ? CommandHotkeys[i].Name : "";
        if (chosen.Length > 0 && chosen == _settings.OpenHotkey)
        {
            _settingsLoading = true;
            CommandHotkeyBox.SelectedIndex = Array.FindIndex(CommandHotkeys, h => h.Name == _settings.CommandHotkey) is int was && was >= 0 ? was : CommandHotkeys.Length;
            _settingsLoading = false;
            CommandHotkeyStatus.Text = L.T("Это сочетание уже открывает остров — выберите другое.");
            return;
        }
        _settings.CommandHotkey = chosen;
        _settings.Save();
        CommandHotkeyStatus.Text = RegisterCommandHotkey()
            ? CommandHotkeyHint()
            : L.T("Это сочетание уже занято другой программой — выберите другое.");
    }

    private void TranslateHotkey_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_settingsLoading || TranslateHotkeyBox.SelectedIndex < 0) return;
        int i = TranslateHotkeyBox.SelectedIndex;
        _settings.TranslateHotkey = i < TranslateHotkeys.Length ? TranslateHotkeys[i].Name : "";
        _settings.Save();
        TranslateHotkeyStatus.Text = RegisterTranslateHotkey()
            ? L.T("Копирует выделение и показывает перевод под островом.")
            : L.T("Это сочетание уже занято другой программой — выберите другое.");
    }

    private void LiquidSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)

    {
        if (_settingsLoading || !IsLoaded) return;
        _settings.LiquidStrength = (int)Math.Round(LiquidSlider.Value);
        _settings.SaveSoon();
        Kick(4);   // a wave to show the new strength
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settingsLoading || !IsLoaded) return;
        _settings.IslandScale = (int)Math.Round(ScaleSlider.Value);
        _settings.SaveSoon();
        ApplyIslandScale();
    }

    private void Lite_Click(object sender, RoutedEventArgs e)
    {
        _settings.LiteGraphics = LiteSwitch.IsChecked == true;
        _settings.Save();
        ApplyGlass();
        CaptureFrost();
        UpdateAuraVisibility();
        UpdateShape(false);
    }

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        try { Autostart.Set(AutostartSwitch.IsChecked == true); }
        catch (Exception ex) { ShowToast("", L.T("Автозапуск не изменён"), ex.Message); }
        AutostartSwitch.IsChecked = Autostart.IsEnabled;
    }

    private void CommandBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _settings.Command = CommandBox.Text.Trim();
        _settings.Save();
    }

    private void TranslateBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var lang = TranslateBox.Text.Trim().ToLowerInvariant();
        _settings.TranslateTo = lang.Length is >= 2 and <= 5 ? lang : "";
        TranslateBox.Text = _settings.TranslateTo;
        _settings.Save();
    }

    private void RecordAudio_Click(object sender, RoutedEventArgs e)
    {
        _settings.RecordSystemAudio = RecSysSwitch.IsChecked == true;
        _settings.RecordMicrophone = RecMicSwitch.IsChecked == true;
        _settings.Save();
    }

    /// <summary>The key is kept the moment it is pasted (spaces and quotes around it are dropped).</summary>
    private void GeminiVoiceApiKeyBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_settingsLoading) return;
        var key = GeminiVoiceApiKeyBox.Password.Trim().Trim('"', '\'').Trim();
        if (key == _settings.GeminiVoiceApiKey) return;
        _settings.GeminiVoiceApiKey = key;
        _settings.Save();
    }
}
