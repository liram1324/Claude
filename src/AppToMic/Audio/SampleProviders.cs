using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AppToMic.Audio;

/// <summary>Helpers to bring any captured stream to the mixer's format (float, stereo, mixer rate).</summary>
public static class SampleChain
{
    public static ISampleProvider ToMixerFormat(IWaveProvider source, int sampleRate)
    {
        ISampleProvider samples = source.ToSampleProvider();
        samples = samples.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(samples),
            2 => samples,
            _ => new FirstTwoChannelsSampleProvider(samples),
        };
        if (samples.WaveFormat.SampleRate != sampleRate)
            samples = new WdlResamplingSampleProvider(samples, sampleRate);
        return samples;
    }
}

/// <summary>Keeps only the first two channels of a multi-channel stream.</summary>
public sealed class FirstTwoChannelsSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private float[] _buffer = Array.Empty<float>();

    public FirstTwoChannelsSampleProvider(ISampleProvider source)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / 2;
        int needed = frames * _sourceChannels;
        if (_buffer.Length < needed) _buffer = new float[needed];
        int read = _source.Read(_buffer, 0, needed);
        int framesRead = read / _sourceChannels;
        for (int i = 0; i < framesRead; i++)
        {
            buffer[offset + i * 2] = _buffer[i * _sourceChannels];
            buffer[offset + i * 2 + 1] = _buffer[i * _sourceChannels + 1];
        }
        return framesRead * 2;
    }
}

/// <summary>Volume control that ramps smoothly to the target gain to avoid clicks when toggling.</summary>
public sealed class SmoothGainSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float _step;
    private float _current;

    public SmoothGainSampleProvider(ISampleProvider source, float initialGain, double rampSeconds = 0.05)
    {
        _source = source;
        _current = initialGain;
        Target = initialGain;
        _step = (float)(1.0 / Math.Max(1, source.WaveFormat.SampleRate * rampSeconds));
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public volatile float Target;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        int channels = WaveFormat.Channels;
        float target = Target;
        for (int i = 0; i < read; i += channels)
        {
            if (_current < target) _current = Math.Min(target, _current + _step);
            else if (_current > target) _current = Math.Max(target, _current - _step);
            for (int c = 0; c < channels && i + c < read; c++)
                buffer[offset + i + c] *= _current;
        }
        return read;
    }
}

/// <summary>Remembers the peak level since the last time it was read (for the UI meters).</summary>
public sealed class PeakMeterSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float _peak;

    public PeakMeterSampleProvider(ISampleProvider source) => _source = source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float TakePeak() => Interlocked.Exchange(ref _peak, 0f);

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        float max = 0;
        for (int i = 0; i < read; i++)
        {
            float v = Math.Abs(buffer[offset + i]);
            if (v > max) max = v;
        }
        if (max > _peak) _peak = max;
        return read;
    }
}

/// <summary>Hard clips the final mix to [-1, 1] so loud apps + voice don't wrap around.</summary>
public sealed class ClipSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;

    public ClipSampleProvider(ISampleProvider source) => _source = source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        for (int i = 0; i < read; i++)
            buffer[offset + i] = Math.Clamp(buffer[offset + i], -1f, 1f);
        return read;
    }
}

/// <summary>
/// A buffer between a capture thread and the mixer. Always returns full reads (silence when empty)
/// and drops old audio when it falls too far behind so latency never builds up.
/// </summary>
public sealed class LiveBuffer : IWaveProvider
{
    private readonly BufferedWaveProvider _buffer;
    private readonly TimeSpan _maxLatency;

    public LiveBuffer(WaveFormat format, TimeSpan maxLatency)
    {
        _maxLatency = maxLatency;
        _buffer = new BufferedWaveProvider(format)
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };
    }

    public WaveFormat WaveFormat => _buffer.WaveFormat;

    public void Add(byte[] data, int count)
    {
        if (_buffer.BufferedDuration > _maxLatency)
            _buffer.ClearBuffer();
        _buffer.AddSamples(data, 0, count);
    }

    public int Read(byte[] buffer, int offset, int count) => _buffer.Read(buffer, offset, count);
}
