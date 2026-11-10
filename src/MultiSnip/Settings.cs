using System.Text.Json;

namespace MultiSnip;

/// <summary>User preferences, stored in %APPDATA%\MultiSnip\settings.json.</summary>
public sealed class Settings
{
    public CombineLayout Layout { get; set; } = CombineLayout.Vertical;
    public int GridColumns { get; set; } // 0 = automatic
    public CombineAlignment Alignment { get; set; } = CombineAlignment.Start;
    public int Spacing { get; set; } = 10;
    public int Margin { get; set; } = 0;
    public int BackgroundArgb { get; set; } = Color.White.ToArgb();
    public bool TransparentBackground { get; set; }
    public bool Border { get; set; }
    public int BorderArgb { get; set; } = Color.FromArgb(200, 200, 200).ToArgb();
    public int BorderWidth { get; set; } = 1;
    public bool MatchSize { get; set; }
    public int OutputScale { get; set; } = 100;
    public int DelaySeconds { get; set; }
    public bool KeepSnipping { get; set; }
    public bool ScrollToTopFirst { get; set; } = true;

    // Drawing tools
    public int ToolArgb { get; set; } = Color.FromArgb(232, 17, 35).ToArgb();
    public float ToolWidth { get; set; } = 3;
    public float ToolFontSize { get; set; } = 24;
    public bool ToolFilled { get; set; }
    public bool ToolBold { get; set; } = true;
    public int[]? CustomColors { get; set; }

    // Saving
    /// <summary>Add "_yyyy-MM_dd-HH-mm-ss" to new file names.</summary>
    public bool AddTimestamp { get; set; } = true;
    public List<string> RecentFiles { get; set; } = new();
    public List<string> RecentFolders { get; set; } = new();

    /// <summary>Global snip hotkey (key code + Control/Alt/Shift flags); Keys.None turns it off.</summary>
    public Keys SnipHotkey { get; set; } = Hotkey.Default;
    public string? LastSaveFolder { get; set; }

    /// <summary>Used by the self-test so it never touches the real settings or open tabs.</summary>
    public static string? AppDataOverride { get; set; }

    public static string AppDataFolder => AppDataOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiSnip");

    /// <summary>Open tabs are kept here between runs.</summary>
    public static string SessionFolder => Path.Combine(AppDataFolder, "Session");

    private static string FilePath => Path.Combine(AppDataFolder, "settings.json");

    /// <summary>Remembers a tab's options as the defaults for new tabs.</summary>
    public void RememberOptions(CombineOptions o)
    {
        Layout = o.Layout;
        GridColumns = o.GridColumns;
        Alignment = o.Alignment;
        Spacing = o.Spacing;
        Margin = o.Margin;
        BackgroundArgb = o.Background.ToArgb();
        TransparentBackground = o.TransparentBackground;
        Border = o.Border;
        BorderArgb = o.BorderColor.ToArgb();
        BorderWidth = o.BorderWidth;
        MatchSize = o.MatchSize;
        OutputScale = o.OutputScale;
    }

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
        OutputScale = OutputScale is > 0 and <= 100 ? OutputScale : 100,
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
