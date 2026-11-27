using System.IO;
using System.Windows;
using DynamicIsland.Services;
using DynamicIsland.Views;
using Xunit;

namespace DynamicIsland.Tests;

public class V23FeaturesTests
{
    [Fact]
    public void NormalizeSelection_TrimsAndCapsLongText()
    {
        Assert.Equal("", MainWindow.NormalizeSelectionForTranslation("   \r\n\t "));
        Assert.Equal("hello world", MainWindow.NormalizeSelectionForTranslation("  hello world  "));

        var longText = string.Join(" ", Enumerable.Repeat("word", 900));
        var normalized = MainWindow.NormalizeSelectionForTranslation(longText, maxChars: 100);
        Assert.True(normalized.Length <= 101);
        Assert.EndsWith("…", normalized);
    }

    [Fact]
    public void VoiceRecorder_BuildFilePath_CreatesFolderAndWavName()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atoll-voice-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var stamp = new DateTime(2026, 11, 27, 2, 15, 30);
            var path = VoiceRecorder.BuildFilePath(dir, stamp);
            Assert.True(Directory.Exists(dir));
            Assert.EndsWith("2026-11-27 02-15-30.wav", path);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void LeFleurThemesAndBrandColors_ParseCleanly()
    {
        Assert.NotNull(Theme.Parse("#F3B4CE"));
        Assert.NotNull(Theme.Parse("#E03E7B"));
        Assert.NotNull(Theme.Parse("#1A080C"));
        Assert.NotNull(Theme.Parse("#14D4C4"));
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x14, 0xD4, 0xC4), BrandArt.Accent);
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x28, 0x0A, 0x13), BrandArt.Deep);
    }

    [Fact]
    public void HotkeyLists_ContainExpectedDefaults()
    {
        Assert.Contains(MainWindow.TranslateHotkeys, h => h.Name == "Ctrl+Shift+T");
        Assert.Contains(MainWindow.TranslateHotkeys, h => h.Name == "Ctrl+Alt+Y");
        Assert.Contains(MainWindow.AllActions, a => a.Id == "voice");
    }
}
