namespace FluentCleaner.Tools;

public enum ToolsCategory
{
    All,
    System,
    Privacy,
    Network,
    Apps,
    Debloat
}

public class ScriptMeta
{
    public string Description { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public ToolsCategory Category { get; set; } = ToolsCategory.All;
    public bool UseConsole { get; set; }
    public bool UseLog { get; set; }
    public bool SupportsInput { get; set; }
    public string InputPlaceholder { get; set; } = "";
    public string PoweredByText { get; set; } = "";
    public string PoweredByUrl { get; set; } = "";
}

public class ToolsDefinition
{
    public string Title { get; set; }
    public string Icon { get; set; }
    public string ScriptPath { get; set; }
    public string Description { get; set; }
    public List<string> Options { get; set; }
    public ToolsCategory Category { get; set; }
    public bool UseConsole { get; set; }
    public bool UseLog { get; set; }
    public bool SupportsInput { get; set; }
    public string InputPlaceholder { get; set; }
    public string? PoweredByText { get; set; }
    public string? PoweredByUrl { get; set; }

    public ToolsDefinition(string title, string icon, string scriptPath, ScriptMeta meta)
    {
        Title = title;
        Icon = icon;
        ScriptPath = scriptPath;
        Description = meta.Description;
        Options = meta.Options;
        Category = meta.Category;
        UseConsole = meta.UseConsole;
        UseLog = meta.UseLog;
        SupportsInput = meta.SupportsInput;
        InputPlaceholder = meta.InputPlaceholder;
        PoweredByText = string.IsNullOrWhiteSpace(meta.PoweredByText) ? null : meta.PoweredByText;
        PoweredByUrl = string.IsNullOrWhiteSpace(meta.PoweredByUrl) ? null : meta.PoweredByUrl;
    }
}
