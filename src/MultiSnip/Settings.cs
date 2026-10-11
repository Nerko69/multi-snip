using System.Text.Json;

namespace MultiSnip;

/// <summary>User preferences, stored in %APPDATA%\MultiSnip\settings.json.</summary>
public sealed class Settings
{
    public CombineLayout Layout { get; set; } = CombineLayout.Vertical;
    public int GridColumns { get; set; } = 2;
    public CombineAlignment Alignment { get; set; } = CombineAlignment.Start;
    public int Spacing { get; set; } = 10;
    public int Margin { get; set; } = 0;
    public int BackgroundArgb { get; set; } = Color.White.ToArgb();
    public bool TransparentBackground { get; set; }
    public bool Border { get; set; }
    public int BorderArgb { get; set; } = Color.FromArgb(200, 200, 200).ToArgb();
    public int BorderWidth { get; set; } = 1;
    public bool MatchSize { get; set; }
    public int DelaySeconds { get; set; }
    public bool KeepSnipping { get; set; }
    public bool FitPreview { get; set; } = true;

    /// <summary>Global snip hotkey (key code + Control/Alt/Shift flags); Keys.None turns it off.</summary>
    public Keys SnipHotkey { get; set; } = Hotkey.Default;
    public string? LastSaveFolder { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiSnip", "settings.json");

    public CombineOptions ToCombineOptions() => new()
    {
        Layout = Layout,
        GridColumns = GridColumns,
        Alignment = Alignment,
        Spacing = Spacing,
        Margin = Margin,
        Background = Color.FromArgb(BackgroundArgb),
        TransparentBackground = TransparentBackground,
        Border = Border,
        BorderColor = Color.FromArgb(BorderArgb),
        BorderWidth = BorderWidth,
        MatchSize = MatchSize,
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            // corrupt or unreadable settings: fall back to defaults
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // settings are a convenience; never crash because of them
        }
    }
}
