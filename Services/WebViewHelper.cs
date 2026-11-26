using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace DynamicIsland.Services;

/// <summary>
/// Shared WebView2 environment, memory trimming, suspend/resume, and visible vertical scrollbars
/// for embedded Music and Chat web views.
/// </summary>
public static class WebViewHelper
{
    private static CoreWebView2Environment? _env;
    private static Task<CoreWebView2Environment>? _envTask;

    /// <summary>
    /// Single user data folder shared by Music and Chat so Windows runs one Chromium browser/GPU/network
    /// process tree instead of duplicating it (~150-250 MB saved when both are used).
    /// </summary>
    public static string UserDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DynamicIsland",
        App.TestRun ? "WebView2Test" : "WebView2");

    private const string BrowserArgs =
        "--disable-features=DeviceBoundSessions,EnableBoundSessionCredentials,DeviceBoundSessionsRefreshQuota " +
        "--process-per-site " +
        "--renderer-process-limit=2 " +
        "--disk-cache-size=33554432 " +
        "--disable-background-networking " +
        "--disable-component-update " +
        "--disable-domain-reliability " +
        "--disable-breakpad " +
        "--disable-sync " +
        "--no-pings " +
        "--js-flags=--optimize-for-size";

    private const string ScrollbarCss = """
        * {
            scrollbar-width: auto !important;
            scrollbar-color: rgba(255, 255, 255, 0.34) rgba(255, 255, 255, 0.06) !important;
        }
        ::-webkit-scrollbar {
            width: 10px !important;
            height: 10px !important;
            display: block !important;
        }
        ::-webkit-scrollbar-track {
            background: rgba(255, 255, 255, 0.05) !important;
            border-radius: 5px !important;
        }
        ::-webkit-scrollbar-thumb {
            background: rgba(255, 255, 255, 0.30) !important;
            border-radius: 5px !important;
            border: 2px solid transparent !important;
            background-clip: padding-box !important;
        }
        ::-webkit-scrollbar-thumb:hover {
            background: rgba(255, 255, 255, 0.50) !important;
            border: 2px solid transparent !important;
            background-clip: padding-box !important;
        }
        """;

    private static readonly string ScrollbarScript =
        "(function(){var css=" + JsonSerializer.Serialize(ScrollbarCss) + ";" +
        "function put(){try{if(document.getElementById('atoll-scrollbar-style'))return;" +
        "var s=document.createElement('style');s.id='atoll-scrollbar-style';s.textContent=css;" +
        "(document.head||document.documentElement).appendChild(s);}catch(e){}}" +
        "if(document.head||document.documentElement)put();" +
        "else document.addEventListener('DOMContentLoaded',put,{once:true});})();";

    public static Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_env != null) return Task.FromResult(_env);
        return _envTask ??= CreateEnvironmentAsync();
    }

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        try
        {
            var options = new CoreWebView2EnvironmentOptions(BrowserArgs)
            {
                AreBrowserExtensionsEnabled = false,
                ScrollBarStyle = CoreWebView2ScrollbarStyle.Default,
            };
            var env = await CoreWebView2Environment.CreateAsync(null, UserDataFolder, options);
            env.BrowserProcessExited += (_, _) => { _env = null; _envTask = null; };
            _env = env;
            return env;
        }
        catch
        {
            _envTask = null;
            throw;
        }
    }

    /// <summary>Disable unused browser features that hold extra memory or UI overlays.</summary>
    public static void ApplyLeanSettings(CoreWebView2Settings settings)
    {
        settings.AreDevToolsEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
    }

    /// <summary>Ensure websites render a clearly visible, draggable vertical scrollbar.</summary>
    public static Task InstallVisibleScrollbarsAsync(CoreWebView2 core) =>
        core.AddScriptToExecuteOnDocumentCreatedAsync(ScrollbarScript);

    /// <summary>
    /// Switch between normal memory target when visible and low memory target when hidden/folded.
    /// Audio continues playing under Low memory target while Blink/V8 trims caches.
    /// </summary>
    public static void SetActive(CoreWebView2? core, bool active)
    {
        if (core == null) return;
        try
        {
            if (active && core.IsSuspended) core.Resume();
            core.MemoryUsageTargetLevel = active
                ? CoreWebView2MemoryUsageTargetLevel.Normal
                : CoreWebView2MemoryUsageTargetLevel.Low;
        }
        catch (Exception ex) { Log.Write("webview: active: " + ex.GetType().Name); }
    }

    /// <summary>
    /// Suspend an idle, silent WebView2 renderer so memory drops near minimum while keeping page state.
    /// </summary>
    public static async Task TrySuspendIfIdleAsync(CoreWebView2? core)
    {
        if (core == null) return;
        try
        {
            if (core.IsSuspended || core.IsDocumentPlayingAudio) return;
            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            await core.TrySuspendAsync();
        }
        catch (Exception ex) { Log.Write("webview: suspend: " + ex.GetType().Name); }
    }

    /// <summary>Sum private working bytes across all WebView2 processes for diagnostics.</summary>
    public static long TotalWebViewMb(CoreWebView2? core)
    {
        if (core == null) return 0;
        try
        {
            return core.Environment.GetProcessInfos().Sum(i =>
            {
                try { using var p = Process.GetProcessById(i.ProcessId); return p.PrivateMemorySize64; }
                catch { return 0L; }
            }) / 1048576;
        }
        catch { return 0; }
    }

    public static void WaitForExit(int? browserPid)
    {
        if (browserPid is not > 0) return;
        try
        {
            using var p = Process.GetProcessById(browserPid.Value);
            p.WaitForExit(2500);
        }
        catch { /* already exited */ }
    }
}
