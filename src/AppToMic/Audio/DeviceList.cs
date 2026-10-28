using NAudio.CoreAudioApi;

namespace AppToMic.Audio;

public sealed record AudioDevice(string Id, string Name)
{
    public override string ToString() => Name;
}

public static class DeviceList
{
    /// <summary>Names of known virtual audio cables; their "input" end is where we play the mix.</summary>
    private static readonly string[] VirtualCableHints = { "cable input", "vb-audio", "voicemeeter input", "virtual" };

    public static List<AudioDevice> Microphones() => List(DataFlow.Capture);

    public static List<AudioDevice> Outputs() => List(DataFlow.Render);

    public static string? DefaultMicrophoneId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return IsVirtualCable(device.FriendlyName) ? null : device.ID;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsVirtualCable(string name)
    {
        name = name.ToLowerInvariant();
        return VirtualCableHints.Any(name.Contains) || name.Contains("cable output");
    }

    /// <summary>Picks the virtual cable to play into, e.g. "CABLE Input (VB-Audio Virtual Cable)".</summary>
    public static AudioDevice? FindVirtualCableOutput(IEnumerable<AudioDevice> outputs) =>
        outputs.FirstOrDefault(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase))
        ?? outputs.FirstOrDefault(d => IsVirtualCable(d.Name));

    /// <summary>Process ids that currently have an audio session, and whether it is playing right now.</summary>
    public static Dictionary<int, bool> ProcessesWithAudio()
    {
        var result = new Dictionary<int, bool>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    int pid = (int)session.GetProcessID;
                    if (pid == 0) continue;
                    bool playing = session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive;
                    result[pid] = (result.TryGetValue(pid, out var p) && p) || playing;
                }
            }
        }
        catch
        {
            // Session enumeration is only a hint for the UI.
        }
        return result;
    }

    private static List<AudioDevice> List(DataFlow flow)
    {
        var result = new List<AudioDevice>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
                result.Add(new AudioDevice(device.ID, device.FriendlyName));
        }
        catch
        {
            // No audio subsystem available.
        }
        return result;
    }
}
