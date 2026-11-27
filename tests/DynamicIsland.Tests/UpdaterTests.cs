using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class UpdaterTests
{
    private const string Sha = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    private static string Release(string tag, string url = "https://github.com/LaRevole/dynamic-island-windows/releases/download/v2.3.0/Atoll-2.3.0-setup.exe", string? digest = "sha256:" + Sha) => $$"""
        { "tag_name": "{{tag}}", "html_url": "https://github.com/LaRevole/dynamic-island-windows/releases/tag/{{tag}}",
          "assets": [
            { "name": "DynamicIsland-win-x64.zip", "browser_download_url": "https://github.com/x/y.zip", "size": 1 },
            { "name": "Atoll-2.3.0-setup.exe", "browser_download_url": "{{url}}", "size": 4200000 {{(digest == null ? "" : $", \"digest\": \"{digest}\"")}} } ] }
        """;

    [Fact]
    public void UpdatesCheckForkRepository()
    {
        Assert.Equal("LaRevole/dynamic-island-windows", Updater.Repo);
        Assert.Equal("https://github.com/LaRevole/dynamic-island-windows/releases/latest", Updater.ReleasesPage);
    }

    [Theory]
    [InlineData("v2.0.1", "2.0.0", true)]
    [InlineData("2.0.1", "2.0.0", true)]
    [InlineData("v2.1", "2.0.9", true)]
    [InlineData("v2.0.0", "2.0.0", false)]
    [InlineData("v1.9.9", "2.0.0", false)]
    [InlineData("v2.0.0.7", "2.0.0", false)]   // the fourth number is not a new version
    [InlineData("nightly", "2.0.0", false)]
    public void ANewerVersionIsAnUpdate(string tag, string current, bool update) =>
        Assert.Equal(update, Updater.Parse(Release(tag), Version.Parse(current)) != null);

    [Fact]
    public void TheInstallerAndItsDigestAreFound()
    {
        var u = Updater.Parse(Release("v2.0.1"), new Version(2, 0, 0))!;
        Assert.Equal(new Version(2, 0, 1), u.Version);
        Assert.EndsWith("-setup.exe", u.SetupUrl);
        Assert.Equal(Sha, u.Sha256);
        Assert.Equal(4200000, u.SetupSize);
    }

    [Fact]
    public async Task WithoutSha256NothingIsDownloaded()
    {
        var u = Updater.Parse(Release("v2.0.1", digest: null), new Version(2, 0, 0))!;
        Assert.Null(u.Sha256);
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Updater.DownloadAsync(u, null));
        Assert.Contains("checksum", e.Message);
        u = u with { Sha256 = "abc" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Updater.DownloadAsync(u, null));
    }

    [Theory]
    [InlineData("https://example.com/Atoll-2.0.1-setup.exe")]
    [InlineData("https://github.com.evil.example/Atoll-2.0.1-setup.exe")]
    [InlineData("http://github.com/LaRevole/dynamic-island-windows/releases/download/v2.3.0/Atoll-2.3.0-setup.exe")]
    [InlineData("file:///C:/Windows/Temp/Atoll-2.0.1-setup.exe")]
    [InlineData("not a url")]
    public async Task OnlyAnHttpsInstallerFromGitHubIsDownloaded(string url)
    {
        var u = Updater.Parse(Release("v2.0.1", url), new Version(2, 0, 0))!;
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Updater.DownloadAsync(u, null));
        Assert.Contains("No installer", e.Message);
    }
}

public class ClaudeBridgeTests
{
    private static readonly byte[] Token = System.Text.Encoding.ASCII.GetBytes(new string('A', 64));
    private static readonly string Good = new('A', 64);

    private static bool Trusted(string method = "POST", string? origin = null, string? referer = null, string? type = "application/json; charset=utf-8",
                                long length = 100, bool local = true, string? token = null) =>
        ClaudeBridge.IsTrusted(method, origin, referer, type, length, local, token ?? Good, Token);

    [Fact] public void TheHookScriptsCallIsTrusted() => Assert.True(Trusted());
    [Fact] public void OnlyPost() => Assert.False(Trusted(method: "GET"));
    [Fact] public void NotFromAWebPage() { Assert.False(Trusted(origin: "https://evil.example")); Assert.False(Trusted(referer: "https://evil.example/")); }
    [Fact] public void OnlyJson() { Assert.False(Trusted(type: "text/plain")); Assert.False(Trusted(type: null)); }
    [Fact] public void NoHugeOrUnknownBodies() { Assert.False(Trusted(length: 1_000_001)); Assert.False(Trusted(length: -1)); Assert.True(Trusted(length: 1_000_000)); }
    [Fact] public void OnlyFromThisComputer() => Assert.False(Trusted(local: false));
    [Fact] public void OnlyWithTheSecret() { Assert.False(Trusted(token: "")); Assert.False(Trusted(token: new string('B', 64))); Assert.False(Trusted(token: Good[..63])); }
}

public class HookEntryTests
{
    [Theory]
    [InlineData(@"powershell -NoProfile -ExecutionPolicy Bypass -File ""C:\Users\a\.atoll\claude-hook.ps1"" Stop", true)]
    [InlineData(@"""C:\Users\a\AppData\Local\Programs\DynamicIsland\DynamicIsland.exe"" --hook Stop", true)]
    [InlineData(@"""C:\Program Files\Some\other.exe"" --hook Stop", false)]
    [InlineData(@"""C:\x\DynamicIsland.exe"" --safe", false)]
    [InlineData("npx prettier --write", false)]
    [InlineData(null, false)]
    public void OnlyTheIslandsOwnHooksAreRecognised(string? command, bool ours) =>
        Assert.Equal(ours, DynamicIsland.Services.ClaudeHooks.IsOurs(command));
}
