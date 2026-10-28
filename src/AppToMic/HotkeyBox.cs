namespace AppToMic;

/// <summary>Text box that records a keyboard shortcut when the user presses it.</summary>
public sealed class HotkeyBox : TextBox
{
    private Keys _value;

    public event EventHandler? ValueChanged;

    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        Cursor = Cursors.Hand;
        RightToLeft = RightToLeft.No;
        TextAlign = HorizontalAlignment.Center;
    }

    public Keys Value
    {
        get => _value;
        set
        {
            _value = value;
            Text = GlobalHotkey.Describe(value);
        }
    }

    protected override void OnPreviewKeyDown(PreviewKeyDownEventArgs e)
    {
        e.IsInputKey = true;
        base.OnPreviewKeyDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        if (e.KeyCode is Keys.Back or Keys.Delete or Keys.Escape)
        {
            Value = Keys.None;
            ValueChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return; // wait for the actual key

        Value = e.KeyCode | e.Modifiers;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
