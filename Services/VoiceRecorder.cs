using System.Diagnostics;
using System.IO;
using NAudio.Wave;

namespace DynamicIsland.Services;

/// <summary>
/// Built-in voice recorder using NAudio WinMM (48 kHz 16-bit mono PCM WAV).
/// Saves recordings into Documents\LeFleur Atoll\Voice with pause/resume support and live peak level.
/// </summary>
public sealed class VoiceRecorder : IDisposable
{
    private WaveIn? _waveIn;
    private WaveFileWriter? _writer;
    private readonly object _lock = new();
    private readonly Stopwatch _activeWatch = new();
    private volatile float _peak;

    public bool IsRecording { get; private set; }
    public bool IsPaused { get; private set; }
    public string? CurrentPath { get; private set; }
    public TimeSpan Elapsed => _activeWatch.Elapsed;
    public float Peak => IsPaused ? 0f : _peak;

    public event Action<string?, string?>? Finished;

    internal static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "LeFleur Atoll",
        "Voice");

    public static string BuildFilePath(string? folder = null, DateTime? now = null)
    {
        var dir = folder ?? DefaultFolder;
        Directory.CreateDirectory(dir);
        var stamp = (now ?? DateTime.Now).ToString("yyyy-MM-dd HH-mm-ss");
        return Path.Combine(dir, $"{L.T("Запись")} {stamp}.wav");
    }

    public bool Start(out string? error, string? folder = null)
    {
        error = null;
        if (IsRecording) return true;
        if (WaveIn.DeviceCount == 0)
        {
            error = L.T("Микрофон не найден");
            return false;
        }

        string path = BuildFilePath(folder);
        try
        {
            var format = new WaveFormat(48000, 16, 1);
            var waveIn = new WaveIn { WaveFormat = format, BufferMilliseconds = 50 };
            var writer = new WaveFileWriter(path, format);
            waveIn.DataAvailable += OnDataAvailable;
            waveIn.RecordingStopped += OnRecordingStopped;

            lock (_lock)
            {
                _waveIn = waveIn;
                _writer = writer;
                CurrentPath = path;
                _peak = 0;
                IsPaused = false;
                IsRecording = true;
                _activeWatch.Restart();
            }

            waveIn.StartRecording();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Cleanup(deleteEmpty: true);
            return false;
        }
    }

    public void Pause()
    {
        if (!IsRecording || IsPaused) return;
        IsPaused = true;
        _activeWatch.Stop();
        _peak = 0;
    }

    public void Resume()
    {
        if (!IsRecording || !IsPaused) return;
        IsPaused = false;
        _activeWatch.Start();
    }

    public void Stop()
    {
        if (!IsRecording) return;
        IsPaused = false;
        _activeWatch.Stop();
        try { _waveIn?.StopRecording(); }
        catch (Exception ex) { Log.Write("voice rec stop: " + ex.Message); }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (IsPaused || e.BytesRecorded < 2) return;
        float max = 0;
        for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
        {
            short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            float abs = Math.Abs(sample / 32768f);
            if (abs > max) max = abs;
        }
        _peak = max;

        lock (_lock)
        {
            try { _writer?.Write(e.Buffer, 0, e.BytesRecorded); }
            catch (Exception ex) { Log.Write("voice rec write: " + ex.Message); }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        string? saved = CurrentPath;
        string? err = e.Exception?.Message;
        Cleanup(deleteEmpty: err != null);
        Finished?.Invoke(err == null ? saved : null, err);
    }

    private void Cleanup(bool deleteEmpty)
    {
        WaveIn? waveIn;
        WaveFileWriter? writer;
        string? path;
        lock (_lock)
        {
            waveIn = _waveIn;
            writer = _writer;
            path = CurrentPath;
            _waveIn = null;
            _writer = null;
            IsRecording = false;
            IsPaused = false;
            _peak = 0;
            _activeWatch.Stop();
        }
        try { if (waveIn != null) { waveIn.DataAvailable -= OnDataAvailable; waveIn.RecordingStopped -= OnRecordingStopped; waveIn.Dispose(); } } catch { }
        try { writer?.Dispose(); } catch { }
        if (deleteEmpty && !string.IsNullOrEmpty(path))
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    public void Dispose()
    {
        if (IsRecording) Stop();
        Cleanup(deleteEmpty: false);
    }
}
