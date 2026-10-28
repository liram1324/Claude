using System.Diagnostics;
using System.Drawing.Drawing2D;
using AppToMic.Audio;

namespace AppToMic;

public sealed class MainForm : Form
{
    private const string VbCableUrl = "https://vb-audio.com/Cable/";

    private static readonly Color OnColor = Color.FromArgb(220, 38, 38);
    private static readonly Color OffColor = Color.FromArgb(75, 85, 99);

    private readonly Settings _settings;
    private readonly string? _startupCommand;
    private readonly bool _startMinimized;
    private readonly AudioEngine _engine = new();
    private readonly GlobalHotkey _hotkey = new();
    private RemoteControl? _remote;

    private readonly Button _toggleButton = new();
    private readonly CheckedListBox _appsList = new();
    private readonly Button _refreshAppsButton = new();
    private readonly Label _capturedLabel = new();
    private readonly ComboBox _micCombo = new();
    private readonly ComboBox _outputCombo = new();
    private readonly Label _outputWarning = new();
    private readonly LinkLabel _vbCableLink = new();
    private readonly CheckBox _micEnabledCheck = new();
    private readonly TrackBar _micVolume = new();
    private readonly TrackBar _appsVolume = new();
    private readonly ProgressBar _micMeter = new();
    private readonly ProgressBar _appsMeter = new();
    private readonly HotkeyBox _hotkeyBox = new();
    private readonly TextBox _urlBox = new();
    private readonly CheckBox _autoStartCheck = new();
    private readonly Label _statusLabel = new();
    private readonly NotifyIcon _tray = new();
    private readonly System.Windows.Forms.Timer _meterTimer = new() { Interval = 60 };

    private readonly Icon _onIcon = MakeIcon(OnColor);
    private readonly Icon _offIcon = MakeIcon(OffColor);

    private bool _loadingUi;
    private bool _exiting;
    private bool _trayHintShown;

    public MainForm(Settings settings, string? startupCommand, bool startMinimized)
    {
        _settings = settings;
        _startupCommand = startupCommand;
        _startMinimized = startMinimized;

        Text = "App To Mic – אפליקציות למיקרופון";
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10f);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 720);
        Size = new Size(600, 820);
        Icon = _offIcon;

        BuildUi();
        BuildTray();

        _engine.Error += message => RunOnUi(() => SetStatus(message, true));
        _engine.CapturesChanged += () => RunOnUi(UpdateCapturedLabel);
        _hotkey.Pressed += () => SetActive(!_engine.Active);
        _meterTimer.Tick += (_, _) => UpdateMeters();
    }

    #region UI construction

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(12),
            AutoScroll = true,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Big on/off button
        _toggleButton.Dock = DockStyle.Top;
        _toggleButton.Height = 70;
        _toggleButton.FlatStyle = FlatStyle.Flat;
        _toggleButton.FlatAppearance.BorderSize = 0;
        _toggleButton.ForeColor = Color.White;
        _toggleButton.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
        _toggleButton.Cursor = Cursors.Hand;
        _toggleButton.Click += (_, _) => SetActive(!_engine.Active);
        root.Controls.Add(_toggleButton);

        root.Controls.Add(BuildAppsGroup());
        root.Controls.Add(BuildDevicesGroup());
        root.Controls.Add(BuildVolumeGroup());
        root.Controls.Add(BuildControlGroup());

        _statusLabel.AutoSize = true;
        _statusLabel.MaximumSize = new Size(540, 0);
        _statusLabel.Margin = new Padding(3, 8, 3, 3);
        root.Controls.Add(_statusLabel);

        for (int i = 0; i < root.Controls.Count; i++)
            root.RowStyles.Add(new RowStyle(i == 1 ? SizeType.Percent : SizeType.AutoSize, 100));

        Controls.Add(root);
    }

    private Control BuildAppsGroup()
    {
        var group = NewGroup("1. אילו אפליקציות יישמעו במיקרופון?");
        group.Dock = DockStyle.Fill;
        group.AutoSize = false;
        group.MinimumSize = new Size(0, 220);

        var layout = NewGrid(1);
        layout.Dock = DockStyle.Fill;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            Text = "סמן אפליקציות (Spotify, דפדפן, משחק...). 🔊 = משמיעה קול כרגע.",
            AutoSize = true,
        });

        _appsList.Dock = DockStyle.Fill;
        _appsList.CheckOnClick = true;
        _appsList.IntegralHeight = false;
        _appsList.ItemCheck += (_, e) => BeginInvoke(SaveSelectedApps);
        layout.Controls.Add(_appsList);

        _refreshAppsButton.Text = "רענן רשימה";
        _refreshAppsButton.AutoSize = true;
        _refreshAppsButton.Click += (_, _) => RefreshAppsList();
        layout.Controls.Add(_refreshAppsButton);

        _capturedLabel.AutoSize = true;
        _capturedLabel.ForeColor = Color.DimGray;
        layout.Controls.Add(_capturedLabel);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildDevicesGroup()
    {
        var group = NewGroup("2. התקנים");
        var layout = NewGrid(2);

        _micCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _micCombo.Dock = DockStyle.Fill;
        _micCombo.SelectedIndexChanged += (_, _) => OnDeviceChanged();
        AddRow(layout, "המיקרופון שלי:", _micCombo);

        _outputCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _outputCombo.Dock = DockStyle.Fill;
        _outputCombo.SelectedIndexChanged += (_, _) => OnDeviceChanged();
        AddRow(layout, "שלח אל (כבל וירטואלי):", _outputCombo);

        _outputWarning.AutoSize = true;
        _outputWarning.ForeColor = Color.DarkRed;
        _outputWarning.MaximumSize = new Size(500, 0);
        layout.Controls.Add(_outputWarning);
        layout.SetColumnSpan(_outputWarning, 2);

        _vbCableLink.Text = "אין כבל וירטואלי? הורד VB-Cable (חינם)";
        _vbCableLink.AutoSize = true;
        _vbCableLink.LinkClicked += (_, _) => OpenUrl(VbCableUrl);
        layout.Controls.Add(_vbCableLink);
        layout.SetColumnSpan(_vbCableLink, 2);

        var hint = new Label
        {
            Text = "בדיסקורד / זום / OBS / משחק – בחר כמיקרופון את \"CABLE Output\".",
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            ForeColor = Color.DimGray,
        };
        layout.Controls.Add(hint);
        layout.SetColumnSpan(hint, 2);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildVolumeGroup()
    {
        var group = NewGroup("3. עוצמות");
        var layout = NewGrid(3);
        layout.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 60);
        layout.ColumnStyles[2] = new ColumnStyle(SizeType.Percent, 40);

        _micEnabledCheck.Text = "הקול שלי";
        _micEnabledCheck.AutoSize = true;
        _micEnabledCheck.CheckedChanged += (_, _) =>
        {
            _engine.MicEnabled = _micEnabledCheck.Checked;
            SaveSettings(s => s.MicEnabled = _micEnabledCheck.Checked);
        };
        ConfigureVolume(_micVolume, 200, v =>
        {
            _engine.MicVolume = v / 100f;
            SaveSettings(s => s.MicVolume = v);
        });
        ConfigureMeter(_micMeter);
        layout.Controls.Add(_micEnabledCheck);
        layout.Controls.Add(_micVolume);
        layout.Controls.Add(_micMeter);

        ConfigureVolume(_appsVolume, 200, v =>
        {
            _engine.AppsVolume = v / 100f;
            SaveSettings(s => s.AppsVolume = v);
        });
        ConfigureMeter(_appsMeter);
        layout.Controls.Add(new Label { Text = "אפליקציות", AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Left });
        layout.Controls.Add(_appsVolume);
        layout.Controls.Add(_appsMeter);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildControlGroup()
    {
        var group = NewGroup("4. קיצור מקלדת / Stream Deck");
        var layout = NewGrid(2);

        _hotkeyBox.Dock = DockStyle.Fill;
        _hotkeyBox.ValueChanged += (_, _) => ApplyHotkey(_hotkeyBox.Value);
        AddRow(layout, "קיצור הפעלה/כיבוי:", _hotkeyBox);

        var urlPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _urlBox.ReadOnly = true;
        _urlBox.Width = 260;
        _urlBox.RightToLeft = RightToLeft.No;
        var copyButton = new Button { Text = "העתק", AutoSize = true };
        copyButton.Click += (_, _) => Clipboard.SetText(_urlBox.Text);
        urlPanel.Controls.Add(_urlBox);
        urlPanel.Controls.Add(copyButton);
        AddRow(layout, "כתובת לשליטה:", urlPanel);

        var help = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            ForeColor = Color.DimGray,
            Text = "ב-Stream Deck: גרור פעולת Hotkey ולחץ על הקיצור שלמעלה,\n" +
                   "או פעולת Website עם הכתובת שלמעלה וסמן \"GET request in background\".\n" +
                   "אפשר גם /on או /off במקום /toggle.",
        };
        layout.Controls.Add(help);
        layout.SetColumnSpan(help, 2);

        _autoStartCheck.Text = "הפעל אוטומטית עם Windows (ממוזער למגש)";
        _autoStartCheck.AutoSize = true;
        _autoStartCheck.CheckedChanged += (_, _) =>
        {
            if (!_loadingUi) AutoStart.Set(_autoStartCheck.Checked);
        };
        layout.Controls.Add(_autoStartCheck);
        layout.SetColumnSpan(_autoStartCheck, 2);

        group.Controls.Add(layout);
        return group;
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes };
        menu.Items.Add("הפעל / כבה", null, (_, _) => SetActive(!_engine.Active));
        menu.Items.Add("פתח חלון", null, (_, _) => ShowFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("יציאה", null, (_, _) => ExitApp());

        _tray.ContextMenuStrip = menu;
        _tray.Icon = _offIcon;
        _tray.Text = "App To Mic";
        _tray.Visible = true;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowFromTray();
        };
    }

    private static GroupBox NewGroup(string title) => new()
    {
        Text = title,
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(8),
        Margin = new Padding(0, 8, 0, 0),
    };

    private static TableLayoutPanel NewGrid(int columns)
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = columns,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int i = 1; i < columns; i++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / (columns - 1)));
        return grid;
    }

    private static void AddRow(TableLayoutPanel layout, string label, Control control)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Left });
        layout.Controls.Add(control);
    }

    private void ConfigureVolume(TrackBar bar, int max, Action<int> changed)
    {
        bar.Minimum = 0;
        bar.Maximum = max;
        bar.TickFrequency = 25;
        bar.Dock = DockStyle.Fill;
        bar.ValueChanged += (_, _) =>
        {
            if (!_loadingUi) changed(bar.Value);
        };
    }

    private static void ConfigureMeter(ProgressBar meter)
    {
        meter.Dock = DockStyle.Fill;
        meter.Maximum = 100;
        meter.Style = ProgressBarStyle.Continuous;
        meter.Margin = new Padding(3, 12, 3, 12);
    }

    #endregion

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _loadingUi = true;

        _micEnabledCheck.Checked = _settings.MicEnabled;
        _micVolume.Value = Math.Clamp(_settings.MicVolume, _micVolume.Minimum, _micVolume.Maximum);
        _appsVolume.Value = Math.Clamp(_settings.AppsVolume, _appsVolume.Minimum, _appsVolume.Maximum);
        _engine.MicEnabled = _settings.MicEnabled;
        _engine.MicVolume = _settings.MicVolume / 100f;
        _engine.AppsVolume = _settings.AppsVolume / 100f;
        _hotkeyBox.Value = (Keys)_settings.Hotkey;
        _urlBox.Text = $"http://127.0.0.1:{_settings.ControlPort}/toggle";
        _autoStartCheck.Checked = AutoStart.IsEnabled();

        LoadDevices();
        RefreshAppsList();
        _engine.SetSelectedApps(_settings.SelectedApps);
        _loadingUi = false;

        ApplyHotkey((Keys)_settings.Hotkey, save: false);
        StartRemoteControl();
        StartEngine();
        SetActive(false);
        _meterTimer.Start();

        if (_startupCommand != null) HandleCommand(_startupCommand);
        if (_startMinimized) BeginInvoke(Hide);
    }

    private void LoadDevices()
    {
        var mics = DeviceList.Microphones();
        var outputs = DeviceList.Outputs();

        _micCombo.Items.Clear();
        _micCombo.Items.Add(new AudioDevice("", "(ללא מיקרופון – רק אפליקציות)"));
        foreach (var mic in mics) _micCombo.Items.Add(mic);
        string? micId = _settings.MicrophoneId ?? DeviceList.DefaultMicrophoneId();
        _micCombo.SelectedItem = _micCombo.Items.Cast<AudioDevice>().FirstOrDefault(d => d.Id == micId) ?? _micCombo.Items[0];

        _outputCombo.Items.Clear();
        foreach (var output in outputs) _outputCombo.Items.Add(output);
        var selectedOutput = outputs.FirstOrDefault(d => d.Id == _settings.OutputId) ?? DeviceList.FindVirtualCableOutput(outputs);
        if (selectedOutput != null) _outputCombo.SelectedItem = selectedOutput;

        UpdateOutputWarning();
    }

    private void UpdateOutputWarning()
    {
        var output = _outputCombo.SelectedItem as AudioDevice;
        var mic = _micCombo.SelectedItem as AudioDevice;
        if (output == null)
            _outputWarning.Text = "⚠ לא נמצא כבל וירטואלי. התקן VB-Cable, הפעל מחדש את המחשב ואז את התוכנה.";
        else if (!DeviceList.IsVirtualCable(output.Name))
            _outputWarning.Text = "⚠ ההתקן שנבחר אינו כבל וירטואלי – הקול שלך יושמע ברמקולים ולא יגיע לשיחה.";
        else if (mic != null && mic.Id.Length > 0 && DeviceList.IsVirtualCable(mic.Name))
            _outputWarning.Text = "⚠ בחרת כבל וירטואלי כמיקרופון – בחר את המיקרופון האמיתי שלך.";
        else
            _outputWarning.Text = "";
        _outputWarning.Visible = _outputWarning.Text.Length > 0;
    }

    private void OnDeviceChanged()
    {
        if (_loadingUi) return;
        _settings.MicrophoneId = (_micCombo.SelectedItem as AudioDevice)?.Id;
        _settings.OutputId = (_outputCombo.SelectedItem as AudioDevice)?.Id;
        _settings.Save();
        UpdateOutputWarning();
        StartEngine();
    }

    private void StartEngine()
    {
        var output = _outputCombo.SelectedItem as AudioDevice;
        var mic = _micCombo.SelectedItem as AudioDevice;
        if (output == null)
        {
            _engine.Stop();
            SetStatus("בחר התקן פלט (כבל וירטואלי) כדי להתחיל.", true);
            return;
        }

        try
        {
            _engine.Start(string.IsNullOrEmpty(mic?.Id) ? null : mic.Id, output.Id);
            SetStatus("פועל. " + (_engine.Active ? "האפליקציות נשמעות במיקרופון." : "האפליקציות מושתקות."), false);
        }
        catch (Exception ex)
        {
            SetStatus("שגיאה בהפעלת השמע: " + ex.Message, true);
        }
    }

    private void RefreshAppsList()
    {
        bool wasLoading = _loadingUi;
        _loadingUi = true;
        var selected = new HashSet<string>(_settings.SelectedApps.Select(ProcessTree.NormalizeName));
        var withAudio = DeviceList.ProcessesWithAudio();
        int ownPid = Environment.ProcessId;

        var groups = new Dictionary<string, AppItem>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == ownPid || process.Id == 0) continue;
                string key = ProcessTree.NormalizeName(process.ProcessName);
                bool hasWindow = process.MainWindowHandle != IntPtr.Zero;
                bool hasAudio = withAudio.TryGetValue(process.Id, out bool playing);
                if (!hasWindow && !hasAudio && !selected.Contains(key)) continue;

                if (!groups.TryGetValue(key, out var item))
                    groups[key] = item = new AppItem(key);
                item.Running = true;
                item.Playing |= hasAudio && playing;
                if (string.IsNullOrWhiteSpace(item.Title) && hasWindow)
                    item.Title = process.MainWindowTitle;
            }
            catch
            {
                // Process exited or access denied.
            }
            finally
            {
                process.Dispose();
            }
        }
        foreach (var key in selected.Where(k => !groups.ContainsKey(k)))
            groups[key] = new AppItem(key);

        _appsList.BeginUpdate();
        _appsList.Items.Clear();
        foreach (var item in groups.Values
                     .OrderByDescending(i => selected.Contains(i.Key))
                     .ThenByDescending(i => i.Playing)
                     .ThenBy(i => i.Key))
        {
            _appsList.Items.Add(item, selected.Contains(item.Key));
        }
        _appsList.EndUpdate();
        _loadingUi = wasLoading;
        UpdateCapturedLabel();
    }

    private void SaveSelectedApps()
    {
        if (_loadingUi) return;
        var keys = _appsList.CheckedItems.Cast<AppItem>().Select(i => i.Key).ToList();
        _settings.SelectedApps = keys;
        _settings.Save();
        _engine.SetSelectedApps(keys);
        UpdateCapturedLabel();
    }

    private void UpdateCapturedLabel()
    {
        var captured = _engine.CapturedProcesses;
        _capturedLabel.Text = captured.Count == 0
            ? (_settings.SelectedApps.Count == 0 ? "לא נבחרו אפליקציות." : "ממתין שהאפליקציות שנבחרו ייפתחו...")
            : "מחובר ל: " + string.Join(", ", captured.Select(c => c.Name).Distinct());
    }

    private void ApplyHotkey(Keys keys, bool save = true)
    {
        bool ok = _hotkey.Register(keys);
        if (save) SaveSettings(s => s.Hotkey = (int)keys);
        if (!ok) SetStatus($"הקיצור {GlobalHotkey.Describe(keys)} תפוס על ידי תוכנה אחרת – בחר קיצור אחר.", true);
    }

    private void StartRemoteControl()
    {
        try
        {
            _remote = new RemoteControl(_settings.ControlPort, command =>
            {
                bool active = false;
                Invoke(() => active = HandleCommand(command));
                return active;
            });
        }
        catch (Exception ex)
        {
            SetStatus($"לא ניתן לפתוח את פורט השליטה {_settings.ControlPort}: {ex.Message}", true);
        }
    }

    /// <summary>Handles "toggle", "on", "off", "status", "show" and returns whether apps are now audible.</summary>
    public bool HandleCommand(string command)
    {
        switch (command)
        {
            case "toggle": SetActive(!_engine.Active); break;
            case "on": SetActive(true); break;
            case "off": SetActive(false); break;
            case "show": ShowFromTray(); break;
        }
        return _engine.Active;
    }

    private void SetActive(bool active)
    {
        _engine.Active = active;
        _toggleButton.Text = active ? "🔴  משדר למיקרופון – לחץ לכיבוי" : "⏸  כבוי – לחץ כדי שישמעו את האפליקציות";
        _toggleButton.BackColor = active ? OnColor : OffColor;
        _tray.Icon = active ? _onIcon : _offIcon;
        _tray.Text = active ? "App To Mic – משדר" : "App To Mic – כבוי";
        Icon = active ? _onIcon : _offIcon;
        if (_engine.IsRunning)
            SetStatus("פועל. " + (active ? "האפליקציות נשמעות במיקרופון." : "האפליקציות מושתקות."), false);
    }

    private void UpdateMeters()
    {
        _micMeter.Value = ToMeter(_engine.TakeMicPeak());
        _appsMeter.Value = ToMeter(_engine.TakeAppsPeak());
    }

    private static int ToMeter(float peak)
    {
        if (peak <= 0.001f) return 0;
        double db = 20 * Math.Log10(peak); // -60..0 dB → 0..100
        return (int)Math.Clamp((db + 60) / 60 * 100, 0, 100);
    }

    private void SetStatus(string text, bool isError)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = isError ? Color.DarkRed : Color.DarkGreen;
    }

    private void SaveSettings(Action<Settings> change)
    {
        if (_loadingUi) return;
        change(_settings);
        _settings.Save();
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { /* closing */ }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        RefreshAppsList();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            // Keep running in the tray so the hotkey / Stream Deck keep working.
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _tray.ShowBalloonTip(3000, "App To Mic ממשיך לפעול", "התוכנה במגש המערכת. לחיצה ימנית על האייקון → יציאה.", ToolTipIcon.Info);
                _trayHintShown = true;
            }
            return;
        }
        base.OnFormClosing(e);
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _meterTimer.Dispose();
            _remote?.Dispose();
            _hotkey.Dispose();
            _engine.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _onIcon.Dispose();
            _offIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* no browser */ }
    }

    private static Icon MakeIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 28, 28);
            // simple microphone glyph
            using var white = new SolidBrush(Color.White);
            using var pen = new Pen(Color.White, 2.5f);
            g.FillRectangle(white, 13, 7, 6, 12);
            g.FillEllipse(white, 13, 4, 6, 6);
            g.FillEllipse(white, 13, 16, 6, 6);
            g.DrawArc(pen, 9, 10, 14, 13, 0, 180);
            g.DrawLine(pen, 16, 23, 16, 27);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    private sealed class AppItem
    {
        public AppItem(string key) => Key = key;

        public string Key { get; }
        public string Title { get; set; } = "";
        public bool Running { get; set; }
        public bool Playing { get; set; }

        public override string ToString()
        {
            string prefix = Playing ? "🔊 " : "";
            string title = string.IsNullOrWhiteSpace(Title) ? Key : $"{Key}  –  {Truncate(Title, 45)}";
            return prefix + title + (Running ? "" : "  (לא פועל כרגע)");
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
    }
}
