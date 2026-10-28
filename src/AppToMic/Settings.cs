using System.Text.Json;

namespace AppToMic;

public sealed class Settings
{
    public string? MicrophoneId { get; set; }
    public string? OutputId { get; set; }
    public List<string> SelectedApps { get; set; } = new();
    public int MicVolume { get; set; } = 100;
    public int AppsVolume { get; set; } = 80;
    public bool MicEnabled { get; set; } = true;

    /// <summary>System.Windows.Forms.Keys value (key + modifiers). Default: Ctrl+Alt+F9.</summary>
    public int Hotkey { get; set; } = (int)(Keys.Control | Keys.Alt | Keys.F9);

    /// <summary>Local port for Stream Deck "Website" actions and the --toggle command line.</summary>
    public int ControlPort { get; set; } = 8765;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppToMic", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            // Corrupt settings: start fresh.
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Not fatal.
        }
    }
}
