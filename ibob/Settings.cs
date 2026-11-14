using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Ibob;

// Everything you can change in the Settings window, saved to %LOCALAPPDATA%\ibob\settings.json.
public class Settings
{
    public bool StartWithWindows { get; set; } = true;
    public string PillSize { get; set; } = "Medium";       // Small, Medium or Large
    public string Screen { get; set; } = "";                // monitor's device name; empty = main monitor
    public List<string> HiddenPages { get; set; } = new();  // page names, e.g. "Music", "Focus"
    public bool AutoShelf { get; set; } = true;             // copied files/images land on the shelf
    public bool TextToShelf { get; set; } = true;           // copied text lands on the shelf too
    public bool ClipboardHistory { get; set; } = true;      // keep a searchable history of copied text
    public bool Sounds { get; set; } = true;                // chime for reminders and timers

    static readonly string SettingsFile = Path.Combine(App.DataFolder, "settings.json");

    // Collapsed pill size in DIPs for each choice.
    public (double Width, double Height) PillDimensions => PillSize switch
    {
        "Small" => (70, 18),
        "Large" => (100, 26),
        _ => (84, 22),
    };

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile)) ?? new(); }
        catch { return FirstRun(); }
    }

    // Before settings.json existed, turning Auto off left an "auto-shelf-off" marker file: carry that choice over.
    static Settings FirstRun()
    {
        var settings = new Settings();
        string oldMarker = Path.Combine(App.DataFolder, "auto-shelf-off");
        if (File.Exists(oldMarker)) { settings.AutoShelf = false; File.Delete(oldMarker); }
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(App.DataFolder);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
