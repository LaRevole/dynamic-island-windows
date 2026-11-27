using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Selected-text translation on hotkey (Ctrl+Shift+T by default): first reads UI Automation selection
/// directly without touching the clipboard; falls back to waiting for held modifiers to release, sending
/// Ctrl+C without polluting Atoll's clipboard history, and restoring the previous clipboard text.
/// </summary>
public partial class MainWindow
{
    public static readonly (string Name, uint Mods, uint Vk)[] TranslateHotkeys =
    [
        ("Ctrl+Shift+T", Native.MOD_CONTROL | Native.MOD_SHIFT, 0x54u),
        ("Ctrl+Alt+Y", Native.MOD_CONTROL | Native.MOD_ALT, 0x59u),
    ];

    private bool _translateBusy;

    internal static string NormalizeSelectionForTranslation(string? text, int maxChars = 3000)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var clean = text.Trim();
        if (clean.Length <= maxChars) return clean;
        int cut = clean.LastIndexOfAny([' ', '\n', '\r', '.', ',', ';'], maxChars);
        return (cut > maxChars / 2 ? clean[..cut] : clean[..maxChars]).TrimEnd() + "…";
    }

    private bool RegisterTranslateHotkey()
    {
        if (_hwnd == IntPtr.Zero) return true;
        Native.UnregisterHotKey(_hwnd, 9);
        var hk = TranslateHotkeys.FirstOrDefault(h => h.Name == _settings.TranslateHotkey);
        if (hk.Name == null) return true;
        bool ok = Native.RegisterHotKey(_hwnd, 9, hk.Mods | Native.MOD_NOREPEAT, hk.Vk);
        if (!ok) Log.Write($"{hk.Name} is taken by another program");
        return ok;
    }

    private static async Task<string> TryReadSelectionViaUiaAsync()
    {
        var task = Task.Run(() =>
        {
            try
            {
                var el = AutomationElement.FocusedElement;
                if (el != null && el.TryGetCurrentPattern(TextPattern.Pattern, out var pat) && pat is TextPattern tp)
                {
                    var ranges = tp.GetSelection();
                    if (ranges.Length > 0)
                    {
                        var text = string.Join("\n", ranges.Select(r => r.GetText(-1)).Where(s => !string.IsNullOrEmpty(s)));
                        if (!string.IsNullOrWhiteSpace(text)) return text;
                    }
                }
            }
            catch { /* app doesn't expose UIA TextPattern */ }
            return "";
        });
        return await Task.WhenAny(task, Task.Delay(250)) == task ? await task : "";
    }

    private async Task TranslateSelectionAsync()
    {
        if (_translateBusy) return;
        _translateBusy = true;
        try
        {
            string selected = await TryReadSelectionViaUiaAsync();

            if (string.IsNullOrWhiteSpace(selected))
            {
                // Wait up to ~650 ms for the user to let go of Shift/Alt/T so Ctrl+C isn't turned into Ctrl+Shift+C.
                for (int w = 0; w < 44 && (Native.KeyDown(0x10) || Native.KeyDown(0x12) || Native.KeyDown(0x54)); w++)
                    await Task.Delay(15);

                string? prevText = null;
                bool hadText = false;
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        hadText = true;
                        prevText = Clipboard.GetText();
                    }
                }
                catch { /* clipboard busy */ }

                uint seqBefore = Native.GetClipboardSequenceNumber();
                _suppressClipboard = true;
                Native.SendCtrlC();

                bool changed = false;
                for (int i = 0; i < 25; i++)
                {
                    await Task.Delay(20);
                    if (Native.GetClipboardSequenceNumber() != seqBefore) { changed = true; break; }
                }

                if (changed)
                {
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        try { if (Clipboard.ContainsText()) selected = Clipboard.GetText(); break; }
                        catch { await Task.Delay(25); }
                    }
                }

                if (changed && hadText && prevText != null)
                {
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        try { Clipboard.SetText(prevText); break; }
                        catch { await Task.Delay(25); }
                    }
                }
                _ = Dispatcher.InvokeAsync(async () => { await Task.Delay(100); _suppressClipboard = false; });
            }

            var query = NormalizeSelectionForTranslation(selected);
            if (query.Length == 0)
            {
                ShowKeyPill("\uE8C1", L.T("Выделите текст для перевода"),
                    string.IsNullOrEmpty(_settings.TranslateHotkey) ? null : _settings.TranslateHotkey, null, kind: "translate");
                return;
            }

            ShowKeyPill("\uE8C1", L.T("Перевожу…"), null, null, kind: "translate");
            var (translated, from, to) = await CaptureService.TranslateDetailedAsync(query, _settings.TranslateTarget);
            ShowTranslationCard(query, translated, from, to);
        }
        catch (Exception ex)
        {
            _suppressClipboard = false;
            Log.Write("translate hotkey: " + ex.GetType().Name + ": " + ex.Message);
            ShowKeyPill("\uE783", L.T("Нет связи с сервисом перевода"), null, Color.FromRgb(0xFF, 0x45, 0x3A), kind: "translate");
        }
        finally
        {
            _translateBusy = false;
        }
    }

    private void ShowTranslationCard(string original, string translated, string from, string to)
    {
        var (win, body, buttons, title) = OpenCard(430);
        title.Text = $"{from.ToUpperInvariant()} → {to.ToUpperInvariant()}";

        var preview = new TextBlock
        {
            Text = original,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 38,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10),
        };
        preview.SetResourceReference(TextBlock.ForegroundProperty, "Fg3");
        body.Children.Add(preview);

        var resultBox = new TextBox
        {
            Text = translated,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 220,
            Padding = new Thickness(0),
        };
        resultBox.SetResourceReference(TextBox.ForegroundProperty, "Fg");
        body.Children.Add(resultBox);

        buttons.Children.Add(CardButton(L.T("В заметку"), "GhostPill", () =>
        {
            NotesStore.Notes.Insert(0, new Note { Text = $"{from.ToUpperInvariant()} → {to.ToUpperInvariant()}: {translated}" });
            NotesStore.Save();
            RenderNotes();
            win.Close();
            ShowToast("\uE70B", L.T("Сохранено в заметки"), translated);
        }));
        buttons.Children.Add(CardButton(L.T("Скопировать"), "AccentPill", () =>
        {
            try { Clipboard.SetText(translated); } catch { }
            win.Close();
            ShowKeyPill("\uE73E", L.T("Перевод скопирован"), null, Color.FromRgb(0x30, 0xD1, 0x58), kind: "translate");
        }));
        buttons.Children.Add(CardButton(L.T("Закрыть"), "GhostPill", () => win.Close()));

        win.Show();
        win.Activate();
    }
}
