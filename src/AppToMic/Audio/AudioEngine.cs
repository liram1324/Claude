using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AppToMic.Audio;

/// <summary>
/// Mixes the microphone with the audio of selected apps and plays the result into an output device
/// (normally a virtual cable such as "CABLE Input"). Other programs then use the cable's other end
/// ("CABLE Output") as their microphone.
///
///   mic ─────────────► mic volume ─────────────┐
///   app 1 ─┐                                   ├─► mixer ─► clip ─► output device
///   app 2 ─┼─► apps mixer ─► on/off + volume ──┘
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private static readonly TimeSpan MaxBufferedLatency = TimeSpan.FromMilliseconds(150);

    private readonly object _sync = new();
    private readonly Dictionary<int, AppCapture> _captures = new();
    private readonly Dictionary<int, DateTime> _failedAt = new();
    private HashSet<string> _selectedApps = new(StringComparer.OrdinalIgnoreCase);

    private WasapiCapture? _micCapture;
    private WasapiOut? _player;
    private MixingSampleProvider? _appsMixer;
    private SmoothGainSampleProvider? _appsGain;
    private SmoothGainSampleProvider? _micGain;
    private PeakMeterSampleProvider? _micMeter;
    private PeakMeterSampleProvider? _appsMeter;
    private System.Threading.Timer? _refreshTimer;
    private int _sampleRate;
    private bool _running;

    private bool _active;
    private float _micVolume = 1f;
    private float _appsVolume = 0.8f;
    private bool _micEnabled = true;

    /// <summary>Raised from background threads with a user-facing message.</summary>
    public event Action<string>? Error;

    /// <summary>Raised from background threads when the set of captured processes changes.</summary>
    public event Action? CapturesChanged;

    public bool IsRunning => _running;

    /// <summary>When true, the selected apps are heard through the microphone.</summary>
    public bool Active
    {
        get => _active;
        set { _active = value; ApplyGains(); }
    }

    public float MicVolume
    {
        get => _micVolume;
        set { _micVolume = value; ApplyGains(); }
    }

    public float AppsVolume
    {
        get => _appsVolume;
        set { _appsVolume = value; ApplyGains(); }
    }

    public bool MicEnabled
    {
        get => _micEnabled;
        set { _micEnabled = value; ApplyGains(); }
    }

    public float TakeMicPeak() => _micMeter?.TakePeak() ?? 0;

    public float TakeAppsPeak() => _appsMeter?.TakePeak() ?? 0;

    public IReadOnlyList<(int ProcessId, string Name)> CapturedProcesses
    {
        get
        {
            lock (_sync)
                return _captures.Values.Select(c => (c.ProcessId, c.Name)).OrderBy(c => c.Name).ToList();
        }
    }

    public void SetSelectedApps(IEnumerable<string> processNames)
    {
        var names = new HashSet<string>(processNames.Select(ProcessTree.NormalizeName), StringComparer.OrdinalIgnoreCase);
        lock (_sync)
        {
            _selectedApps = names;
            _failedAt.Clear();
        }
        _refreshTimer?.Change(0, 2000);
    }

    /// <param name="micDeviceId">Capture device id, or null for none.</param>
    /// <param name="outputDeviceId">Render device id (virtual cable input).</param>
    public void Start(string? micDeviceId, string outputDeviceId)
    {
        Stop();
        lock (_sync)
        {
            using var enumerator = new MMDeviceEnumerator();
            var outputDevice = enumerator.GetDevice(outputDeviceId);
            _sampleRate = outputDevice.AudioClient.MixFormat.SampleRate;
            var mixFormat = WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, 2);

            var mainMixer = new MixingSampleProvider(mixFormat) { ReadFully = true };

            // Apps bus
            _appsMixer = new MixingSampleProvider(mixFormat) { ReadFully = true };
            _appsGain = new SmoothGainSampleProvider(_appsMixer, 0f);
            _appsMeter = new PeakMeterSampleProvider(_appsGain);
            mainMixer.AddMixerInput(_appsMeter);

            // Microphone
            if (!string.IsNullOrEmpty(micDeviceId))
            {
                var micDevice = enumerator.GetDevice(micDeviceId);
                _micCapture = new WasapiCapture(micDevice, true, 20);
                var micBuffer = new LiveBuffer(_micCapture.WaveFormat, MaxBufferedLatency);
                _micCapture.DataAvailable += (_, e) => micBuffer.Add(e.Buffer, e.BytesRecorded);
                _micCapture.RecordingStopped += (_, e) =>
                {
                    if (e.Exception != null) Error?.Invoke("המיקרופון הפסיק לעבוד: " + e.Exception.Message);
                };
                _micGain = new SmoothGainSampleProvider(SampleChain.ToMixerFormat(micBuffer, _sampleRate), 0f);
                _micMeter = new PeakMeterSampleProvider(_micGain);
                mainMixer.AddMixerInput(_micMeter);
            }

            _player = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 40);
            _player.Init(new SampleToWaveProvider(new ClipSampleProvider(mainMixer)));
            _player.PlaybackStopped += (_, e) =>
            {
                if (e.Exception != null) Error?.Invoke("התקן הפלט הפסיק לעבוד: " + e.Exception.Message);
            };

            ApplyGains();
            _player.Play();
            _micCapture?.StartRecording();
            _running = true;
            _refreshTimer = new System.Threading.Timer(_ => RefreshCaptures(), null, 0, 2000);
        }
    }

    public void Stop()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        lock (_sync)
        {
            _running = false;
            foreach (var capture in _captures.Values) capture.Dispose();
            _captures.Clear();
            _failedAt.Clear();

            try { _micCapture?.StopRecording(); } catch { /* device may be gone */ }
            _micCapture?.Dispose();
            _micCapture = null;

            try { _player?.Stop(); } catch { /* device may be gone */ }
            _player?.Dispose();
            _player = null;

            _appsMixer = null;
            _appsGain = null;
            _micGain = null;
            _micMeter = null;
            _appsMeter = null;
        }
        CapturesChanged?.Invoke();
    }

    private void ApplyGains()
    {
        if (_appsGain != null) _appsGain.Target = _active ? _appsVolume : 0f;
        if (_micGain != null) _micGain.Target = _micEnabled ? _micVolume : 0f;
    }

    /// <summary>Starts capturing newly launched selected apps and drops the ones that exited.</summary>
    private void RefreshCaptures()
    {
        bool changed = false;
        lock (_sync)
        {
            if (!_running || _appsMixer == null) return;

            var wanted = ProcessTree.FindRootProcesses(_selectedApps, Environment.ProcessId)
                .ToDictionary(p => p.Id);

            foreach (var pid in _captures.Keys.ToList())
            {
                if (wanted.TryGetValue(pid, out var info) && info.Name == _captures[pid].Name && !_captures[pid].Failed)
                    continue;
                _captures[pid].Dispose();
                _captures.Remove(pid);
                changed = true;
            }

            foreach (var process in wanted.Values)
            {
                if (_captures.ContainsKey(process.Id)) continue;
                if (_failedAt.TryGetValue(process.Id, out var failedAt) && DateTime.UtcNow - failedAt < TimeSpan.FromSeconds(15))
                    continue;
                try
                {
                    _captures[process.Id] = new AppCapture(process.Id, process.Name, _sampleRate, _appsMixer);
                    _failedAt.Remove(process.Id);
                    changed = true;
                }
                catch (Exception ex)
                {
                    _failedAt[process.Id] = DateTime.UtcNow;
                    Error?.Invoke($"לא ניתן לחבר את {process.Name}: {ex.Message}");
                }
            }
        }
        if (changed) CapturesChanged?.Invoke();
    }

    public void Dispose() => Stop();

    /// <summary>One captured process feeding the apps mixer.</summary>
    private sealed class AppCapture : IDisposable
    {
        private readonly ProcessLoopbackCapture _capture;
        private readonly MixingSampleProvider _mixer;
        private readonly ISampleProvider _input;

        public int ProcessId { get; }
        public string Name { get; }
        public bool Failed { get; private set; }

        public AppCapture(int processId, string name, int sampleRate, MixingSampleProvider mixer)
        {
            ProcessId = processId;
            Name = name;
            _mixer = mixer;
            _capture = ProcessLoopbackCapture.Create(processId, sampleRate);
            var buffer = new LiveBuffer(_capture.WaveFormat, MaxBufferedLatency);
            _capture.DataAvailable += (_, e) => buffer.Add(e.Buffer, e.BytesRecorded);
            _capture.Stopped += (_, e) => { if (e != null) Failed = true; };
            _input = SampleChain.ToMixerFormat(buffer, sampleRate);
            _mixer.AddMixerInput(_input);
            _capture.Start();
        }

        public void Dispose()
        {
            _mixer.RemoveMixerInput(_input);
            _capture.Dispose();
        }
    }
}
