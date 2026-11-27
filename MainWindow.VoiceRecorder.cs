using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Built-in voice recorder (tab + Home action + Ctrl+Shift+R): records 48 kHz mono PCM WAV
/// into Documents\LeFleur Atoll\Voice, shows live timer and level, and plays back saved clips.
/// </summary>
public partial class MainWindow
{
    private readonly VoiceRecorder _voiceRec = new();
    private TextBlock? _voiceTimerText, _voiceStatusText;
    private ScaleTransform? _voiceLevelScale;

    private void InitVoiceRecorder()
    {
        _voiceRec.Finished += (path, error) => Dispatcher.BeginInvoke(() =>
        {
            BuildHome();
            UpdateEyesVisibility();
            UpdateMicMuteBadge();
            if (_mode == Mode.Compact) MorphTo(TargetSize(Mode.Compact));
            if (TabVoice.IsChecked == true) RenderVoicePage();
            if (path != null)
            {
                ShowToast("\uE720", L.T("Голосовая запись сохранена"), Path.GetFileName(path),
                    () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }),
                    (Brush)FindResource("Accent"), ms: 5500, keep: true);
            }
            else if (!string.IsNullOrEmpty(error))
            {
                Log.Write("voice rec failed: " + error);
                ShowToast("\uE783", L.T("Запись голоса не удалась"), error);
            }
        });
    }

    private void ToggleVoiceRecording()
    {
        if (_voiceRec.IsRecording)
        {
            _voiceRec.Stop();
            return;
        }
        if (_recording.IsRecording)
        {
            ShowToast("\uE783", L.T("Идёт запись экрана"), L.T("Сначала остановите запись экрана"));
            return;
        }
        if (!_voiceRec.Start(out var err))
        {
            ShowToast("\uE783", L.T("Не удалось включить диктофон"), err ?? "");
            return;
        }

        BuildHome();
        UpdateEyesVisibility();
        UpdateMicMuteBadge();
        if (TabVoice.IsChecked == true && _mode == Mode.Expanded) RenderVoicePage();
        else
        {
            if (_mode != Mode.Compact) SetMode(Mode.Compact);
            MorphTo(TargetSize(Mode.Compact));
        }

        if (_audio.MicMuted == true)
        {
            ShowToast("\uEC54", L.T("Микрофон выключен"), L.T("Нажмите здесь, чтобы включить звук микрофона"),
                () => ToggleMicMute(), (Brush)FindResource("Danger"), ms: 4000);
        }
        else if (_mode != Mode.Expanded || TabVoice.IsChecked != true)
        {
            ShowToast("\uE720", L.T("Диктофон включён"), L.T("Стоп: Ctrl+Shift+R или кнопка на «Главной»"),
                null, (Brush)FindResource("Danger"), ms: 2200);
        }
    }

    private void ToggleVoicePause()
    {
        if (!_voiceRec.IsRecording) return;
        if (_voiceRec.IsPaused) _voiceRec.Resume(); else _voiceRec.Pause();
        if (TabVoice.IsChecked == true) RenderVoicePage();
    }

    private void UpdateVoiceLiveUi()
    {
        if (!VoicePage.IsVisible || _voiceTimerText == null || _voiceLevelScale == null) return;
        if (_voiceRec.IsRecording)
        {
            var t = _voiceRec.Elapsed;
            _voiceTimerText.Text = $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
            _voiceLevelScale.ScaleX = _voiceRec.IsPaused ? 0 : Math.Clamp(_voiceRec.Peak * 2.2, 0.03, 1);
        }
        else
        {
            _voiceTimerText.Text = "00:00";
            _voiceLevelScale.ScaleX = 0;
        }
    }

    private void RenderVoicePage()
    {
        VoicePage.Children.Clear();
        _voiceTimerText = null;
        _voiceStatusText = null;
        _voiceLevelScale = null;

        bool rec = _voiceRec.IsRecording;
        bool paused = _voiceRec.IsPaused;
        bool micMuted = _audio.MicMuted == true;

        // Top recorder card
        var topTile = new Border { Style = (Style)FindResource("Tile"), Margin = new Thickness(0, 0, 0, 10) };
        var topStack = new StackPanel();

        var header = new DockPanel();
        var badge = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(rec && !paused ? Color.FromRgb(0xFF, 0x45, 0x3A) : Color.FromArgb(0x28, 0xFF, 0x45, 0x3A)),
            Child = new TextBlock
            {
                Text = "\uE720",
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 15,
                Foreground = rec && !paused ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var timer = new TextBlock
        {
            Text = rec ? $"{(int)_voiceRec.Elapsed.TotalMinutes:00}:{_voiceRec.Elapsed.Seconds:00}" : "00:00",
            FontSize = 24,
            FontFamily = (FontFamily)FindResource("DisplayFont"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(timer, Dock.Right);
        header.Children.Add(timer);
        header.Children.Add(badge);

        var info = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = L.T("Диктофон"), FontSize = 14, FontWeight = FontWeights.SemiBold };
        var status = new TextBlock
        {
            Text = rec ? (paused ? L.T("На паузе · Ctrl+Shift+R — стоп") : L.T("Идёт запись… · Ctrl+Shift+R — стоп"))
                       : L.T("Готов к записи (48 кГц WAV) · Ctrl+Shift+R"),
            Style = (Style)FindResource("Muted"),
            Margin = new Thickness(0, 2, 0, 0),
        };
        info.Children.Add(title);
        info.Children.Add(status);
        header.Children.Add(info);
        topStack.Children.Add(header);

        _voiceTimerText = timer;
        _voiceStatusText = status;

        // Live microphone level bar
        var meterGrid = new Grid { Height = 5, Margin = new Thickness(0, 12, 0, 0) };
        var meterTrack = new Border { CornerRadius = new CornerRadius(2.5) };
        meterTrack.SetResourceReference(Border.BackgroundProperty, "TrackBg");
        var levelScale = new ScaleTransform(rec && !paused ? Math.Clamp(_voiceRec.Peak * 2.2, 0.03, 1) : 0, 1);
        var meterFill = new Border
        {
            CornerRadius = new CornerRadius(2.5),
            Background = new SolidColorBrush(rec && !paused ? Color.FromRgb(0xFF, 0x45, 0x3A) : Color.FromRgb(0x14, 0xD4, 0xC4)),
            RenderTransformOrigin = new Point(0, 0.5),
            RenderTransform = levelScale,
        };
        meterGrid.Children.Add(meterTrack);
        meterGrid.Children.Add(meterFill);
        topStack.Children.Add(meterGrid);
        _voiceLevelScale = levelScale;

        if (micMuted)
        {
            var warn = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var unmuteBtn = new Button
            {
                Style = (Style)FindResource("DangerPill"),
                Content = L.T("Включить микрофон"),
                Padding = new Thickness(12, 5, 12, 5),
            };
            unmuteBtn.Click += (_, _) => { ToggleMicMute(); RenderVoicePage(); };
            DockPanel.SetDock(unmuteBtn, Dock.Right);
            warn.Children.Add(unmuteBtn);
            warn.Children.Add(new TextBlock
            {
                Text = L.T("Системный микрофон выключен — запишется тишина."),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0x61)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
            topStack.Children.Add(warn);
        }

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var recBtn = new Button
        {
            Style = (Style)FindResource(rec ? "DangerPill" : "AccentPill"),
            Content = rec ? L.T("Остановить") : L.T("Начать запись"),
            Padding = new Thickness(16, 7, 16, 7),
            Margin = new Thickness(0, 0, 8, 0),
        };
        recBtn.Click += (_, _) => ToggleVoiceRecording();
        controls.Children.Add(recBtn);

        if (rec)
        {
            var pauseBtn = new Button
            {
                Style = (Style)FindResource("GhostPill"),
                Content = paused ? L.T("Продолжить") : L.T("Пауза"),
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(0, 0, 8, 0),
            };
            pauseBtn.Click += (_, _) => ToggleVoicePause();
            controls.Children.Add(pauseBtn);
        }

        var folderBtn = new Button
        {
            Style = (Style)FindResource("GhostPill"),
            Content = L.T("Папка записей"),
            Padding = new Thickness(14, 7, 14, 7),
        };
        folderBtn.Click += (_, _) =>
        {
            Directory.CreateDirectory(VoiceRecorder.DefaultFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{VoiceRecorder.DefaultFolder}\"") { UseShellExecute = true });
        };
        controls.Children.Add(folderBtn);
        topStack.Children.Add(controls);

        topTile.Child = topStack;
        VoicePage.Children.Add(topTile);

        // Saved recordings list
        var listTile = new Border { Style = (Style)FindResource("Tile") };
        var listStack = new StackPanel();
        listStack.Children.Add(new TextBlock { Text = L.T("ЗАПИСИ"), Style = (Style)FindResource("Caption"), Margin = new Thickness(0, 0, 0, 8) });

        var scroll = new ScrollViewer { MaxHeight = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var rows = new StackPanel();
        List<FileInfo> files = [];
        try
        {
            if (Directory.Exists(VoiceRecorder.DefaultFolder))
                files = new DirectoryInfo(VoiceRecorder.DefaultFolder).GetFiles("*.wav")
                    .OrderByDescending(f => f.LastWriteTime).Take(30).ToList();
        }
        catch (Exception ex) { Log.Write("voice list: " + ex.Message); }

        if (files.Count == 0)
        {
            rows.Children.Add(new TextBlock { Text = L.T("Пока нет записей"), Style = (Style)FindResource("Muted") });
        }
        else
        {
            foreach (var f in files)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                DockPanel.SetDock(actions, Dock.Right);

                var path = f.FullName;
                var playBtn = new Button { Style = (Style)FindResource("IconButton"), Content = "\uE768", Width = 28, Height = 28, FontSize = 11, ToolTip = L.T("Прослушать") };
                playBtn.Click += (_, _) => PlayOwn(() => _player.PlayFiles([path], 0));
                var revealBtn = new Button { Style = (Style)FindResource("IconButton"), Content = "\uE8B7", Width = 28, Height = 28, FontSize = 11, Margin = new Thickness(4, 0, 0, 0), ToolTip = L.T("Показать в папке") };
                revealBtn.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                var delBtn = new Button { Style = (Style)FindResource("IconButton"), Content = "\uE74D", Width = 28, Height = 28, FontSize = 11, Margin = new Thickness(4, 0, 0, 0), ToolTip = L.T("Удалить") };
                delBtn.Click += (_, _) =>
                {
                    try { File.Delete(path); } catch (Exception ex) { Log.Write("voice delete: " + ex.Message); }
                    RenderVoicePage();
                };
                actions.Children.Add(playBtn);
                actions.Children.Add(revealBtn);
                actions.Children.Add(delBtn);

                var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                labelStack.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(f.Name), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
                string sizeStr = f.Length >= 1048576 ? $"{f.Length / 1048576.0:0.0} МБ" : $"{Math.Max(1, f.Length / 1024)} КБ";
                labelStack.Children.Add(new TextBlock { Text = $"{f.LastWriteTime:dd.MM.yyyy HH:mm} · {sizeStr}", Style = (Style)FindResource("Muted"), FontSize = 11 });

                row.Children.Add(actions);
                row.Children.Add(labelStack);
                rows.Children.Add(row);
            }
        }
        scroll.Content = rows;
        listStack.Children.Add(scroll);
        listTile.Child = listStack;
        VoicePage.Children.Add(listTile);
        Remeasure();
    }
}
