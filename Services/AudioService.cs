using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace DynamicIsland.Services;

/// <summary>
/// System audio level (for the liquid wave) and per-app volume of the current player.
/// Must be used from the UI thread.
/// </summary>
public sealed class AudioService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _device;
    private DateTime _deviceAt;

    private MMDevice? Device
    {
        get
        {
            // default device can change (headphones plugged in) - refresh every few seconds. Each ask is a new COM
            // object with its own session manager, which registers a callback with Windows and is never freed unless
            // disposed: one a second piled up (3,265 after an hour, the memory growing past 1 GB in 2.0.0)
            if (_device == null || (DateTime.Now - _deviceAt).TotalSeconds > 5)
            {
                _deviceAt = DateTime.Now;
                try
                {
                    var d = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (_device != null && d.ID == _device.ID) d.Dispose();
                    else { Forget(); _device = d; }
                }
                catch { Forget(); }
            }
            return _device;
        }
    }

    /// <summary>The device went away or changed: free it and everything taken from it.</summary>
    private void Forget()
    {
        _meter = null; _meterDevice = null;
        _found = null; _foundFor = "";
        var d = _device;
        _device = null;
        try { d?.Dispose(); } catch (Exception ex) { Log.Error("audio: release device", ex); }
    }

    private AudioMeterInformation? _meter;
    private MMDevice? _meterDevice;
    private float _peak;
    private long _peakAt;

    /// <summary>
    /// The sound level now (0…1). Read many times a frame (the wave, the character, the outline), so the
    /// meter object is kept (asking the device for a new one each time was costly) and the value is reused
    /// for 15 ms.
    /// </summary>
    public float Peak
    {
        get
        {
            long now = Environment.TickCount64;
            if (now - _peakAt < 15) return _peak;
            _peakAt = now;
            try
            {
                var d = Device;
                if (d == null) return _peak = 0;
                if (!ReferenceEquals(d, _meterDevice) || _meter == null) { _meter = d.AudioMeterInformation; _meterDevice = d; }
                return _peak = _meter.MasterPeakValue;
            }
            catch { Forget(); return _peak = 0; }
        }
    }

    private string _foundFor = "";
    private AudioSessionControl? _found;
    private long _foundAt;

    /// <summary>
    /// Finds the audio session of the player app (by its media id: a Store app's package family, a program's
    /// exe; see <see cref="AudioMatch"/>); null = use master volume. Kept for two seconds: a mouse wheel asks
    /// many times a second.
    /// </summary>
    private AudioSessionControl? FindSession(string appId)
    {
        var device = Device;
        if (device == null || string.IsNullOrWhiteSpace(appId)) return null;
        long now = Environment.TickCount64;
        if (appId == _foundFor && now - _foundAt < 2000) return _found;

        // browsers have several sessions: of the best-matching ones, take the one that is actually making sound
        AudioSessionControl? best = null;
        int bestScore = 0;
        float bestPeak = -1;
        try
        {
            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if (s.IsSystemSoundsSession) continue;
                int pid = (int)s.GetProcessID;
                string name;
                try { using var p = Process.GetProcessById(pid); name = p.ProcessName; }
                catch { continue; }
                int score = AudioMatch.Score(appId, name, Native.PackageFamilyOf(pid));
                if (score == 0 || score < bestScore) continue;
                float peak = 0;
                try { peak = s.AudioMeterInformation.MasterPeakValue; } catch { /* the session went away */ }
                if (s.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive) peak += 0.001f;
                if (score > bestScore || peak > bestPeak) { best = s; bestScore = score; bestPeak = peak; }
            }
        }
        catch (Exception e) { Log.Write("audio sessions: " + e.Message); }
        _foundFor = appId; _found = best; _foundAt = now;
        return best;
    }

    private static List<string> Tokens(string appId)
    {
        var raw = appId.ToLowerInvariant().Replace(".exe", "");
        return raw.Split(['.', '_', '!', ' ', '-', '\\', '/'], StringSplitOptions.RemoveEmptyEntries)
                  .Where(t => t.Length >= 4 && t != "microsoft" && t != "app" && !t.All(Uri.IsHexDigit))
                  .ToList();
    }

    public (float Volume, bool PerApp) GetVolume(string appId)
    {
        try
        {
            var s = FindSession(appId);
            if (s != null) return (s.SimpleAudioVolume.Volume, true);
            return (Device?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0, false);
        }
        catch { return (0, false); }
    }

    /// <summary>App's own output: is it there, is it muted, how loud right now.</summary>
    public (bool Found, bool Muted, float Peak) AppState(string app)
    {
        try
        {
            var s = FindSession(app);
            if (s == null) return (false, false, 0);
            return (true, s.SimpleAudioVolume.Mute, s.AudioMeterInformation.MasterPeakValue);
        }
        catch { return (false, false, 0); }
    }

    public bool ToggleAppMute(string app)
    {
        try
        {
            var s = FindSession(app);
            if (s == null) return false;
            s.SimpleAudioVolume.Mute = !s.SimpleAudioVolume.Mute;
            return s.SimpleAudioVolume.Mute;
        }
        catch { return false; }
    }

    private readonly List<MMDevice> _mics = [];
    private readonly HashSet<string> _mutedByUs = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _micAt;

    private void ForgetMics()
    {
        foreach (var m in _mics)
            try { m.Dispose(); } catch (Exception ex) { Log.Error("audio: release mic", ex); }
        _mics.Clear();
    }

    /// <summary>
    /// All active capture endpoints (cached for 5 s, disposed on refresh like <see cref="Device"/>).
    /// Windows and apps like Discord often use different mics for Default Multimedia vs Communications
    /// or a user-picked device, so muting all active capture devices ensures the active mic actually mutes.
    /// </summary>
    private IReadOnlyList<MMDevice> CaptureDevices(bool forceRefresh = false)
    {
        if (!forceRefresh && _mics.Count > 0 && (DateTime.Now - _micAt).TotalSeconds <= 5) return _mics;
        _micAt = DateTime.Now;
        ForgetMics();
        try
        {
            foreach (var d in _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                _mics.Add(d);
        }
        catch { ForgetMics(); }
        return _mics;
    }

    /// <summary>
    /// Processes that have a live microphone stream open right now (any input device).
    /// This is the real signal for "Discord is in a voice channel".
    /// </summary>
    public HashSet<string> AppsRecording()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var dev in _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            using (dev)   // a new object each time, with its own session manager: freed here (it piled up every few seconds)
            {
                try
                {
                    var mgr = dev.AudioSessionManager;
                    mgr.RefreshSessions();
                    var sessions = mgr.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var s = sessions[i];
                        if (s.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive) continue;
                        try { using var p = Process.GetProcessById((int)s.GetProcessID); names.Add(p.ProcessName); } catch { /* exited meanwhile */ }
                    }
                }
                catch { /* the device went away */ }
            }
        }
        catch (Exception ex) { Log.Error("audio: recording apps", ex); }
        return names;
    }

    /// <summary>True when all active capture devices are muted; setting mutes/unmutes every active mic (Discord, Zoom, games…).</summary>
    public bool? MicMuted
    {
        get
        {
            try
            {
                var mics = CaptureDevices();
                if (mics.Count == 0) return null;
                foreach (var m in mics)
                    if (!m.AudioEndpointVolume.Mute) return false;
                return true;
            }
            catch { ForgetMics(); return null; }
        }
        set
        {
            if (!value.HasValue) return;
            try
            {
                var mics = CaptureDevices(forceRefresh: true);
                if (value.Value)
                {
                    foreach (var m in mics)
                    {
                        try
                        {
                            if (!m.AudioEndpointVolume.Mute)
                            {
                                m.AudioEndpointVolume.Mute = true;
                                _mutedByUs.Add(m.ID);
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    bool selective = _mutedByUs.Count > 0;
                    foreach (var m in mics)
                    {
                        try
                        {
                            if (!selective || _mutedByUs.Contains(m.ID))
                                m.AudioEndpointVolume.Mute = false;
                        }
                        catch { }
                    }
                    _mutedByUs.Clear();
                }
            }
            catch (Exception ex) { Log.Write("audio: mic mute: " + ex.GetType().Name + ": " + ex.Message); }
        }
    }

    public float MicPeak
    {
        get
        {
            try
            {
                float max = 0;
                foreach (var m in CaptureDevices())
                {
                    float p = m.AudioMeterInformation.MasterPeakValue;
                    if (p > max) max = p;
                }
                return max;
            }
            catch { ForgetMics(); return 0; }
        }
    }

    public sealed record AppSound(string Name, float Volume, bool Muted);

    private IEnumerable<(string Name, AudioSessionControl Session)> LiveSessions()
    {
        var device = Device;
        if (device == null) yield break;
        SessionCollection sessions;
        try
        {
            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            sessions = manager.Sessions;
        }
        catch { yield break; }
        int self = Environment.ProcessId;
        for (int i = 0; i < sessions.Count; i++)
        {
            AudioSessionControl s;
            string name;
            try
            {
                s = sessions[i];
                if (s.IsSystemSoundsSession || s.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) continue;
                int pid = (int)s.GetProcessID;
                if (pid == 0 || pid == self) continue;
                using var p = Process.GetProcessById(pid);
                name = p.ProcessName;
            }
            catch { continue; }
            yield return (name, s);
        }
    }

    /// <summary>Programs with their own sound right now (one row per program, at most eight).</summary>
    public List<AppSound> AppSounds()
    {
        var result = new List<AppSound>();
        try
        {
            foreach (var (name, s) in LiveSessions())
            {
                if (result.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(new AppSound(name, s.SimpleAudioVolume.Volume, s.SimpleAudioVolume.Mute));
                if (result.Count >= 8) break;
            }
        }
        catch (Exception ex) { Log.Error("audio: app sounds", ex); }
        return result;
    }

    public void SetAppVolume(string name, float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        try
        {
            foreach (var (n, s) in LiveSessions())
                if (n.Equals(name, StringComparison.OrdinalIgnoreCase)) s.SimpleAudioVolume.Volume = value;
        }
        catch (Exception ex) { Log.Write("audio: app volume: " + ex.GetType().Name + ": " + ex.Message); }
    }

    public void ToggleAppSoundMute(string name)
    {
        try
        {
            bool? to = null;
            foreach (var (n, s) in LiveSessions())
            {
                if (!n.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                to ??= !s.SimpleAudioVolume.Mute;
                s.SimpleAudioVolume.Mute = to.Value;
            }
        }
        catch (Exception ex) { Log.Write("audio: app mute: " + ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>The volume of the speakers (0..1); -1 when there are none.</summary>
    public float Master
    {
        get { try { return Device?.AudioEndpointVolume.MasterVolumeLevelScalar ?? -1; } catch { return -1; } }
        set { try { if (Device != null) Device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f); } catch (Exception ex) { Log.Write("audio: master volume: " + ex.GetType().Name + ": " + ex.Message); } }
    }

    public bool MasterMuted
    {
        get { try { return Device?.AudioEndpointVolume.Mute ?? false; } catch { return false; } }
        set { try { if (Device != null) Device.AudioEndpointVolume.Mute = value; } catch (Exception ex) { Log.Write("audio: master mute: " + ex.GetType().Name + ": " + ex.Message); } }
    }

    public void SetVolume(string appId, float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        try
        {
            var s = FindSession(appId);
            if (s != null) s.SimpleAudioVolume.Volume = value;
            else if (Device != null) Device.AudioEndpointVolume.MasterVolumeLevelScalar = value;
        }
        catch (Exception ex) { Log.Write("audio: set volume: " + ex.GetType().Name + ": " + ex.Message); }
    }
}
