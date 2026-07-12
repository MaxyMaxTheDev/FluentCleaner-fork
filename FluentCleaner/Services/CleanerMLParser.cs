using System.Xml.Linq;
using FluentCleaner.Models;

namespace FluentCleaner.Services;

// Parses BleachBit's CleanerML XML format into CleanerEntry objects.
// CleanerML uses <cleaner> → <option> → <action> hierarchy with search="..." and command="..." attributes.
// Variables like $$base$$ and $$profile$$ are resolved from <var> elements inside each <cleaner>.
public class CleanerMLParser
{
    public List<CleanerEntry> Parse(string content)
    {
        var doc = XDocument.Parse(content);
        var entries = new List<CleanerEntry>();

        foreach (var cleaner in doc.Descendants("cleaner"))
        {
            var os = cleaner.Attribute("os")?.Value;
            if (os is not null && !os.Equals("windows", StringComparison.OrdinalIgnoreCase))
                continue;

            var vars = ParseVars(cleaner);
            var label = cleaner.Element("label")?.Value?.Trim() ?? "";
            var description = cleaner.Element("description")?.Value?.Trim();
            var running = ParseRunning(cleaner);

            foreach (var option in cleaner.Elements("option"))
            {
                var optLabel = option.Element("label")?.Value?.Trim() ?? "";
                var optDesc = option.Element("description")?.Value?.Trim();
                var warning = option.Element("warning")?.Value?.Trim();

                var entry = new CleanerEntry
                {
                    Name = string.IsNullOrEmpty(optLabel) ? label : $"{label} - {optLabel}",
                    Warning = warning,
                    Default = true
                };

                if (running is not null)
                    entry.DetectFiles.Add(running);

                if (description is not null || optDesc is not null)
                    entry.Section = label;

                bool hasActions = false;
                foreach (var action in option.Elements("action"))
                {
                    var actionOs = action.Attribute("os")?.Value;
                    if (actionOs is not null && !actionOs.Equals("windows", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var command = action.Attribute("command")?.Value ?? "";
                    var search  = action.Attribute("search")?.Value ?? "";
                    var path    = action.Attribute("path")?.Value ?? "";
                    var name    = action.Attribute("name")?.Value;

                    path = ResolveVars(path, vars);

                    switch (command)
                    {
                        case "delete":
                        case "truncate":
                            AddFileActions(entry, search, path);
                            hasActions = true;
                            break;
                        case "winreg":
                            AddRegAction(entry, path, name);
                            hasActions = true;
                            break;
                    }
                }

                if (hasActions)
                    entries.Add(entry);
            }
        }

        return entries;
    }

    public async Task<List<CleanerEntry>> ParseFileAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        return Parse(content);
    }

    private static Dictionary<string, string> ParseVars(XElement cleaner)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var varEl in cleaner.Elements("var"))
        {
            var name = varEl.Attribute("name")?.Value;
            if (name is null) continue;

            var values = varEl.Elements("value")
                .Where(v =>
                {
                    var vOs = v.Attribute("os")?.Value;
                    return vOs is null || vOs.Equals("windows", StringComparison.OrdinalIgnoreCase);
                })
                .Select(v => v.Value?.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();

            if (values.Count > 0)
                vars[$"$$${name}$$"] = values[0];
        }
        return vars;
    }

    private static string? ParseRunning(XElement cleaner)
    {
        foreach (var running in cleaner.Elements("running"))
        {
            var type = running.Attribute("type")?.Value;
            var os   = running.Attribute("os")?.Value;

            if (os is not null && !os.Equals("windows", StringComparison.OrdinalIgnoreCase))
                continue;

            if (type == "exe")
                return running.Value?.Trim();
        }
        return null;
    }

    private static string ResolveVars(string path, Dictionary<string, string> vars)
    {
        foreach (var (key, value) in vars)
            path = path.Replace(key, value, StringComparison.OrdinalIgnoreCase);
        return path;
    }

    private static void AddFileActions(CleanerEntry entry, string search, string path)
    {
        switch (search)
        {
            case "file":
                var dir  = Path.GetDirectoryName(path);
                var file = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(file))
                {
                    entry.FileKeys.Add(new FileKeyEntry
                    {
                        Path    = dir,
                        Pattern = file.Contains('*') || file.Contains('?') ? file : "*.*"
                    });
                }
                else
                {
                    entry.FileKeys.Add(new FileKeyEntry { Path = path, Pattern = "*.*" });
                }
                break;

            case "glob":
                var globDir = Path.GetDirectoryName(path);
                var globPat = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(globDir))
                    entry.FileKeys.Add(new FileKeyEntry
                    {
                        Path    = globDir,
                        Pattern = string.IsNullOrEmpty(globPat) ? "*.*" : globPat
                    });
                break;

            case "walk.files":
            case "walk.all":
            case "walk.top":
            case "deep":
                entry.FileKeys.Add(new FileKeyEntry
                {
                    Path    = path,
                    Pattern = "*.*",
                    Flag    = FileKeyFlag.Recurse
                });
                break;
        }
    }

    private static void AddRegAction(CleanerEntry entry, string path, string? name)
    {
        if (string.IsNullOrEmpty(path)) return;
        entry.RegKeys.Add(new RegKeyEntry
        {
            KeyPath   = path,
            ValueName = name
        });
    }
}
