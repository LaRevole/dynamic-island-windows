using System.IO;
using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class MusicPlayersTests
{
    [Fact]
    public void AnUnknownSourceIsTheFirstOne()
    {
        Assert.Equal("radio", MusicPlayers.ById("radio").Id);
        Assert.Equal("ytmusic", MusicPlayers.ById("gone").Id);
    }

    [Fact]
    public void EverySiteIsHttpsAndOnItsOwnHosts()
    {
        foreach (var p in MusicPlayers.All.Where(p => p.IsSite))
        {
            Assert.True(SafeShell.IsOnHost(p.Home, MusicPlayers.Hosts), p.Home);
            Assert.True(SafeShell.IsOnHost(p.Playlists, MusicPlayers.Hosts), p.Playlists);
            Assert.True(SafeShell.IsOnHost(MusicPlayers.AddressFor(p, "a/b?c&d"), MusicPlayers.Hosts));
            Assert.Equal(p.Home, MusicPlayers.AddressFor(p, "  "));
        }
    }

    [Fact]
    public void TheQueryIsEscaped() =>
        Assert.Equal("https://open.spotify.com/search/AC%2FDC%20%D0%9A%D0%B8%D0%BD%D0%BE",
            MusicPlayers.AddressFor(MusicPlayers.All.Single(p => p.Id == "spotify"), " AC/DC Кино "));

    [Fact]
    public void YandexMusicHasValidUrlsAndSignInHosts()
    {
        var yandex = MusicPlayers.ById("yandex");
        Assert.Equal("yandex", yandex.Id);
        Assert.Equal("https://music.yandex.ru/", yandex.Home);
        Assert.Equal("https://music.yandex.ru/collection", yandex.Playlists);
        Assert.Equal("https://music.yandex.ru/search?text=%D0%9A%D0%B8%D0%BD%D0%BE%20%D0%9F%D0%B0%D1%87%D0%BA%D0%B0",
            MusicPlayers.AddressFor(yandex, "Кино Пачка"));
        Assert.True(SafeShell.IsOnHost("https://passport.yandex.ru/auth", MusicPlayers.Hosts));
        Assert.True(SafeShell.IsOnHost("https://id.yandex.ru/", MusicPlayers.Hosts));
        Assert.True(SafeShell.IsOnHost("https://passport.yandex.com/auth", MusicPlayers.Hosts));
        Assert.True(SafeShell.IsOnHost("https://ya.ru/", MusicPlayers.Hosts));
        Assert.False(SafeShell.IsOnHost("https://evil.example/", MusicPlayers.Hosts));
    }

    [Fact]
    public void StationsWithoutHttpsAndSomaFmAreLeftOut()
    {
        var found = RadioDirectory.Parse("""
            [{"stationuuid":"a","name":" Jazz ","url_resolved":"https://example.org/jazz","countrycode":"DE","codec":"MP3","bitrate":128},
             {"stationuuid":"b","name":"Jazz again","url_resolved":"https://example.org/jazz"},
             {"stationuuid":"c","name":"Plain","url_resolved":"http://example.org/plain"},
             {"stationuuid":"d","name":"Groove Salad","url_resolved":"https://ice1.somafm.com/groovesalad","bitrate":256},
             {"stationuuid":"e","name":"Soma FM Lush","url_resolved":"https://example.org/lush"},
             {"stationuuid":"f","name":"","url_resolved":"https://example.org/noname"},
             {"stationuuid":"g","name":"Bare","url_resolved":"https://example.org/bare","bitrate":null}]
            """);
        Assert.Equal(["Jazz", "Bare"], found.Select(s => s.Name));
        Assert.Equal("DE · MP3 · 128 kbps", found[0].Info);
        Assert.Equal("", found[1].Info);
    }

    [Fact]
    public void FilesAreFoundByEveryWordOfTheirPath()
    {
        var dir = Directory.CreateTempSubdirectory("atoll-music").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "Кино"));
            foreach (var f in new[] { "Кино/Кукушка.mp3", "Кино/cover.jpg", "Muse.FLAC", "note.txt" }) File.WriteAllText(Path.Combine(dir, f), "");
            List<string?> Names(string q) => MusicPlayers.FindFiles(dir, q).Select(Path.GetFileName).ToList();
            Assert.Equal(2, Names("").Count);
            Assert.Equal(["Кукушка.mp3"], Names("кино кук"));
            Assert.Empty(Names("cover"));
            Assert.Equal([Path.Combine(dir, "Кино")], MusicPlayers.FindFolders(dir));
            Assert.Single(MusicPlayers.FindFiles(dir, "", max: 1));
            Assert.Empty(MusicPlayers.FindFiles(Path.Combine(dir, "gone"), ""));
        }
        finally { Directory.Delete(dir, true); }
    }
}
