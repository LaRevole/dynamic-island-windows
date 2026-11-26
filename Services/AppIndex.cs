using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Services;

/// <summary>Installed programs for the command bar: every shortcut in the Start menu.</summary>
public sealed class AppIndex
{
    public sealed record App(string Name, string Path);

    private const int MaxCachedIcons = 40;
    private List<App> _apps = [];
    private readonly Dictionary<string, ImageSource?> _icons = new();
    private readonly Queue<string> _iconOrder = new();
    private Task? _loading;

    public Task EnsureLoadedAsync() => _loading ??= Task.Run(Load);

    private void Load()
    {
        var found = new Dictionary<string, App>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                 })
        {
            try
            {
                // the localised folder names ("Программы") are junctions nobody may list: walking into one ended the
                // whole scan, quietly, so some programs were never found
                var walk = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var f in Directory.EnumerateFiles(root, "*.lnk", walk))
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("удал", StringComparison.OrdinalIgnoreCase)) continue;
                    found.TryAdd(name, new App(name, f));
                }
            }
            catch (Exception ex) { Log.Write("apps: start menu scan: " + ex.GetType().Name + ": " + ex.Message); }
        }
        AddStoreApps(found);
        _apps = found.Values.OrderBy(a => a.Name).ToList();
    }

    /// <summary>
    /// Everything in the Start menu's "All apps", including Microsoft Store apps that have no shortcut file
    /// (Calculator, Spotify from the Store…). They open through "shell:AppsFolder\&lt;app id&gt;".
    /// </summary>
    private static void AddStoreApps(Dictionary<string, App> found)
    {
        var t = new Thread(() =>
        {
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type == null) return;
                dynamic shell = Activator.CreateInstance(type)!;
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                foreach (dynamic item in folder.Items())
                {
                    string name = item.Name, id = item.Path;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("удал", StringComparison.OrdinalIgnoreCase)) continue;
                    lock (found) found.TryAdd(name, new App(name, @"shell:AppsFolder\" + id));
                }
            }
            catch (Exception ex) { Log.Write("apps: store apps scan: " + ex.GetType().Name + ": " + ex.Message); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join(TimeSpan.FromSeconds(15));
    }

    /// <summary>Display name of an indexed app (for Store apps, whose path is an id).</summary>
    public string? NameOf(string path) => _apps.FirstOrDefault(a => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase))?.Name;

    /// <summary>Best matches first: starts-with, then word-starts-with, then contains.</summary>
    public List<App> Search(string q, int max = 5)
    {
        q = q.Trim();
        if (q.Length == 0) return [];
        return _apps
            .Select(a => (App: a, Score: Score(a.Name, q)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.App.Name.Length)
            .Take(max).Select(x => x.App).ToList();
    }

    private static int Score(string name, string q)
    {
        if (name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.Split(' ', '-', '_').Any(w => w.StartsWith(q, StringComparison.OrdinalIgnoreCase))) return 2;
        return name.Contains(q, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    /// <summary>The program's own icon (bounded LRU/FIFO cache). Call on the UI thread.</summary>
    public ImageSource? Icon(string path)
    {
        if (_icons.TryGetValue(path, out var cached)) return cached;
        ImageSource? img = ShellIcons.Get(path, 32);
        if (img == null)
        {
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon != null)
                {
                    img = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    img.Freeze();
                }
            }
            catch { /* no icon: the generic one */ }
        }
        if (_icons.Count >= MaxCachedIcons && _iconOrder.TryDequeue(out var oldest))
            _icons.Remove(oldest);
        _icons[path] = img;
        _iconOrder.Enqueue(path);
        return img;
    }

    public static bool IsStoreApp(string path) => path.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase);

    public static void Launch(string path)
    {
        try
        {
            Process.Start(IsStoreApp(path)
                ? new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true }
                : new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Write("apps: launch: " + ex.GetType().Name + ": " + ex.Message); }
    }
}
