using System.Runtime.InteropServices;

namespace AppToMic;

/// <summary>A system-wide keyboard shortcut (works while other apps/games are focused, and from a Stream Deck "Hotkey" action).</summary>
public sealed class GlobalHotkey : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000;
    private const int HotkeyId = 0xA2D1;

    private bool _registered;

    public event Action? Pressed;

    public GlobalHotkey() => CreateHandle(new CreateParams());

    /// <returns>false if the shortcut is already taken by another program.</returns>
    public bool Register(Keys keys)
    {
        Unregister();
        var key = keys & Keys.KeyCode;
        if (key == Keys.None) return true;

        uint modifiers = ModNoRepeat;
        if (keys.HasFlag(Keys.Alt)) modifiers |= ModAlt;
        if (keys.HasFlag(Keys.Control)) modifiers |= ModControl;
        if (keys.HasFlag(Keys.Shift)) modifiers |= ModShift;

        _registered = RegisterHotKey(Handle, HotkeyId, modifiers, (uint)key);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered) UnregisterHotKey(Handle, HotkeyId);
        _registered = false;
    }

    public static string Describe(Keys keys)
    {
        var key = keys & Keys.KeyCode;
        if (key == Keys.None) return "ללא";
        var parts = new List<string>();
        if (keys.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (keys.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (keys.HasFlag(Keys.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
            Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
