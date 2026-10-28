using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace AppToMic.Audio;

/// <summary>
/// Captures the audio output of a single process (and its child processes) using the
/// WASAPI "process loopback" virtual device (Windows 10 2004+ / Windows 11).
/// </summary>
public sealed class ProcessLoopbackCapture : IDisposable
{
    private const string VirtualAudioDeviceProcessLoopback = "VAD\\Process_Loopback";
    private static readonly Guid IidIAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

    private readonly AudioClient _audioClient;
    private readonly EventWaitHandle _frameEvent = new(false, EventResetMode.AutoReset);
    private Thread? _thread;
    private volatile bool _stopRequested;
    private bool _disposed;

    public int ProcessId { get; }
    public WaveFormat WaveFormat { get; }

    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<Exception?>? Stopped;

    private ProcessLoopbackCapture(int processId, AudioClient audioClient, WaveFormat format)
    {
        ProcessId = processId;
        _audioClient = audioClient;
        WaveFormat = format;
    }

    /// <summary>
    /// Activates a loopback client for the given process. Blocks until activation completes.
    /// Call from an MTA thread (thread pool / background thread).
    /// </summary>
    public static ProcessLoopbackCapture Create(int processId, int sampleRate)
    {
        // Prefer 32-bit float; fall back to 16-bit PCM which is what Microsoft's sample uses.
        var formats = new[]
        {
            WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2),
            new WaveFormat(sampleRate, 16, 2),
        };

        Exception? last = null;
        foreach (var format in formats)
        {
            AudioClient? client = null;
            try
            {
                client = new AudioClient(Activate(processId));
                client.Initialize(
                    AudioClientShareMode.Shared,
                    AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback |
                    AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality,
                    200_000, // 20ms, in 100ns units
                    0,
                    format,
                    Guid.Empty);
                return new ProcessLoopbackCapture(processId, client, format);
            }
            catch (Exception ex)
            {
                last = ex;
                client?.Dispose();
            }
        }

        throw new InvalidOperationException(
            $"לא ניתן להקליט את תהליך {processId}. נדרש Windows 10 גרסה 2004 ומעלה או Windows 11.", last);
    }

    public void Start()
    {
        if (_thread != null) return;
        _audioClient.SetEventHandle(_frameEvent.SafeWaitHandle.DangerousGetHandle());
        _stopRequested = false;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = $"Loopback {ProcessId}" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Stop()
    {
        _stopRequested = true;
        _frameEvent.Set();
        if (_thread != null && _thread != Thread.CurrentThread)
            _thread.Join(1000);
        _thread = null;
    }

    private void CaptureLoop()
    {
        Exception? error = null;
        try
        {
            var captureClient = _audioClient.AudioCaptureClient;
            int bytesPerFrame = WaveFormat.BlockAlign;
            var buffer = new byte[WaveFormat.AverageBytesPerSecond / 10];

            _audioClient.Start();
            while (!_stopRequested)
            {
                _frameEvent.WaitOne(100);
                if (_stopRequested) break;

                int packetSize = captureClient.GetNextPacketSize();
                while (packetSize != 0 && !_stopRequested)
                {
                    IntPtr data = captureClient.GetBuffer(out int frames, out AudioClientBufferFlags flags);
                    int bytes = frames * bytesPerFrame;
                    if (buffer.Length < bytes) buffer = new byte[bytes];

                    if ((flags & AudioClientBufferFlags.Silent) != 0)
                        Array.Clear(buffer, 0, bytes);
                    else
                        Marshal.Copy(data, buffer, 0, bytes);

                    captureClient.ReleaseBuffer(frames);

                    if (bytes > 0)
                        DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytes));

                    packetSize = captureClient.GetNextPacketSize();
                }
            }
            _audioClient.Stop();
        }
        catch (Exception ex)
        {
            error = ex;
        }
        Stopped?.Invoke(this, error);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _audioClient.Dispose();
        _frameEvent.Dispose();
    }

    private static IAudioClient Activate(int processId)
    {
        var activationParams = new AudioClientActivationParamsNative
        {
            ActivationType = 1, // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
            TargetProcessId = (uint)processId,
            ProcessLoopbackMode = 0, // PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE
        };

        int paramsSize = Marshal.SizeOf<AudioClientActivationParamsNative>();
        IntPtr paramsPtr = Marshal.AllocHGlobal(paramsSize);
        IntPtr propVariantPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariantBlob>());
        var handler = new ActivationCompletionHandler();
        bool completed = false;
        try
        {
            Marshal.StructureToPtr(activationParams, paramsPtr, false);
            var propVariant = new PropVariantBlob
            {
                vt = 65, // VT_BLOB
                cbSize = (uint)paramsSize,
                pBlobData = paramsPtr,
            };
            Marshal.StructureToPtr(propVariant, propVariantPtr, false);

            var iid = IidIAudioClient;
            ActivateAudioInterfaceAsync(VirtualAudioDeviceProcessLoopback, ref iid, propVariantPtr, handler,
                out IActivateAudioInterfaceAsyncOperation operation);

            completed = handler.Completed.Wait(TimeSpan.FromSeconds(5));
            if (!completed)
                throw new TimeoutException("הפעלת הקלטת התהליך לא הסתיימה בזמן.");

            operation.GetActivateResult(out int hr, out object activated);
            Marshal.ThrowExceptionForHR(hr);
            return (IAudioClient)activated;
        }
        finally
        {
            // If activation never completed the OS may still read the params; leak them rather than crash.
            if (completed)
            {
                Marshal.FreeHGlobal(paramsPtr);
                Marshal.FreeHGlobal(propVariantPtr);
            }
        }
    }

    #region Native interop

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParamsNative
    {
        public int ActivationType;
        public uint TargetProcessId;
        public int ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariantBlob
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public uint cbSize;
        [FieldOffset(8 + 8)] public IntPtr pBlobData; // x64 layout (the app is built for x64)
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [In] ref Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    #endregion
}

// COM types must be public and COM-visible so the runtime can hand the callback to Windows.

[ComImport, ComVisible(true), Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IActivateAudioInterfaceCompletionHandler
{
    void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
}

[ComImport, ComVisible(true), Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IActivateAudioInterfaceAsyncOperation
{
    void GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
}

/// <summary>Marker interface: tells COM the completion handler can be called from any thread.</summary>
[ComImport, ComVisible(true), Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAgileObject
{
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ActivationCompletionHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
{
    public ManualResetEventSlim Completed { get; } = new(false);

    public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation) => Completed.Set();
}
