using System.Runtime.InteropServices;

namespace AppToMic.Audio;

/// <summary>Snapshot of running processes with their parent ids.</summary>
public static class ProcessTree
{
    public readonly record struct ProcessInfo(int Id, int ParentId, string Name);

    /// <summary>Normalizes "Chrome.exe" / "chrome" to "chrome".</summary>
    public static string NormalizeName(string name)
    {
        name = name.Trim().ToLowerInvariant();
        return name.EndsWith(".exe") ? name[..^4] : name;
    }

    public static Dictionary<int, ProcessInfo> Snapshot()
    {
        var result = new Dictionary<int, ProcessInfo>();
        IntPtr snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == InvalidHandleValue) return result;
        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32FirstW(snapshot, ref entry)) return result;
            do
            {
                int id = (int)entry.th32ProcessID;
                result[id] = new ProcessInfo(id, (int)entry.th32ParentProcessID, NormalizeName(entry.szExeFile));
            } while (Process32NextW(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return result;
    }

    /// <summary>
    /// Returns the top-most processes of the selected apps. Loopback capture of a process includes its
    /// child processes (e.g. a browser's audio service), so capturing only the roots avoids hearing audio twice.
    /// </summary>
    public static List<ProcessInfo> FindRootProcesses(IReadOnlySet<string> names, int excludeProcessId)
    {
        var all = Snapshot();
        var roots = new List<ProcessInfo>();
        foreach (var process in all.Values)
        {
            if (process.Id == excludeProcessId || process.Id == 0 || !names.Contains(process.Name))
                continue;
            if (!HasSelectedAncestor(process, all, names))
                roots.Add(process);
        }
        return roots;
    }

    private static bool HasSelectedAncestor(ProcessInfo process, Dictionary<int, ProcessInfo> all, IReadOnlySet<string> names)
    {
        var visited = new HashSet<int> { process.Id };
        int parentId = process.ParentId;
        for (int depth = 0; depth < 64 && parentId != 0 && visited.Add(parentId); depth++)
        {
            if (!all.TryGetValue(parentId, out var parent)) return false;
            if (names.Contains(parent.Name)) return true;
            parentId = parent.ParentId;
        }
        return false;
    }

    private const uint Th32csSnapProcess = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
