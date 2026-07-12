using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentCleaner.Services;

public record CleanHistoryEntry(DateTime Date, long BytesFreed, int ItemsRemoved);

public class AppSettings
{
    // single shared instance, loaded from disk at startup.
    // NOTE: initialized in the static constructor (below), NOT with an inline "= Load()".
    // Static field initializers run in textual order, and Load() depends on the path fields
    // (PortablePath/SettingsFile/JsonOptions) declared further down. An inline initializer here
    // would run BEFORE those fields are set, making SettingsFile null and Load() silently return
    // empty defaults — which is exactly the bug that made saved settings (language!) never load.
    public static AppSettings Instance { get; private set; } = null!;

    // Portable mode: if settings.json sits next to the exe, use it instead of %AppData%.
    // Drop a settings.json next to FluentCleaner.exe and the app becomes fully portable.
    private static readonly string PortablePath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    private static readonly string RoamingPath  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluentCleaner", "settings.json");

    private static readonly string SettingsFile = File.Exists(PortablePath) ? PortablePath : RoamingPath;

    [JsonIgnore]
    public static bool IsPortable => SettingsFile == PortablePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    // Runs after ALL static field initializers above are set, so Load() sees valid paths.
    static AppSettings() => Instance = Load();

    // --- persisted settings -----------------------------------------------

    public string? CustomWinapp2Path { get; set; }
    public string? Theme             { get; set; }
    public HashSet<string> SelectedEntries { get; set; } = [];

    // which built-in databases to load on startup
    public bool EnableWinapp2 { get; set; } = true;
    public bool EnableWinapp3 { get; set; } = false;
    public bool EnableWinappx { get; set; } = true;   // AppX/bloatware database for terminal debloater
    public bool EnableCleanerML { get; set; } = true;  // BleachBit CleanerML XML database

    // post-clean commands;one per line; global on/off switch
    public bool   PostCleanEnabled  { get; set; } = false;
    public string PostCleanCommands { get; set; } = "";

    // global exclusions — paths that are NEVER cleaned regardless of what Winapp2.ini says
    // uses the same ExcludeKey syntax: PATH|%LocalAppData%\...\Service Worker
    public bool         GlobalExclusionsEnabled { get; set; } = false;
    public List<string> GlobalExclusions        { get; set; } = [];

    // backdrop style;terminal-only tweak, no Settings UI on purpose
    public string Backdrop { get; set; } = "mica";

    // remembered window size;restored on next launch
    public int WindowWidth  { get; set; } = 960;
    public int WindowHeight { get; set; } = 620;

    //Junk growth tracker;logged after every successful clean run
    public bool CleanHistoryEnabled { get; set; } = true;
    public List<CleanHistoryEntry> CleanHistory { get; set; } = [];

    // Groq API key for AI entry explanations; null = not configured
    public string? GroqApiKey { get; set; }

    // true once the user dismisses the startup donation tip
    public bool DonationDismissed { get; set; } = false;

    // UI language override; "" = follow Windows, "en-US" / "de-DE" = forced
    public string Language { get; set; } = "";


    // -----------------------------------------------------------------------

    // true only when CustomWinapp2Path points to a file that actually exists
    [JsonIgnore]
    public bool HasCustomPath =>
        !string.IsNullOrWhiteSpace(CustomWinapp2Path) && File.Exists(CustomWinapp2Path);

    // re-reads settings.json from disk and replaces the current instance;useful shit after external edits, called after the user saves changes in Settings
    public static void Reload() => Instance = Load();

    // yields all active database paths in load order (built-ins first, custom last)
    public IEnumerable<string> ResolveDatabasePaths()
    {
        if (EnableWinapp2)
        {
            var p = Path.Combine(AppContext.BaseDirectory, "Winapp2.ini");
            if (File.Exists(p)) yield return p;
        }
        if (EnableWinapp3)
        {
            var p = Path.Combine(AppContext.BaseDirectory, "Winapp3.ini");
            if (File.Exists(p)) yield return p;
        }
        if (EnableCleanerML)
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Cleaners");
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.xml"))
                    yield return f;
        }
        if (!string.IsNullOrWhiteSpace(CustomWinapp2Path) && File.Exists(CustomWinapp2Path))
            yield return CustomWinapp2Path;
    }

    // true when the path looks like a CleanerML XML file (vs Winapp2 INI)
    public static bool IsCleanerML(string path) =>
        path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    // returns the custom path if set and valid, otherwise falls back to the bundled Winapp2.ini
    public string ResolveWinapp2Path()
    {
        if (!string.IsNullOrWhiteSpace(CustomWinapp2Path) && File.Exists(CustomWinapp2Path))
            return CustomWinapp2Path;
        return Path.Combine(AppContext.BaseDirectory, "Winapp2.ini");
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { }
    }

    private static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new();
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), JsonOptions) ?? new();
            s.CustomWinapp2Path = NormalizePath(s.CustomWinapp2Path);
            return s;
        }
        catch { return new(); }  // corrupted file;just start fresh
    }

    // Export current settings to a file (for backup or sharing)
    public static void ExportTo(string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(Instance, JsonOptions));

    // Import settings from a file and replace the current instance
    public static void ImportFrom(string path)
    {
        var json = File.ReadAllText(path);
        var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new();
        s.CustomWinapp2Path = NormalizePath(s.CustomWinapp2Path);
        Instance = s;
        Instance.Save();
    }

    // strips quotes and expands %env% variables so paths from the JSON always work
    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var result = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
