using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace TmSync.Manager;

/// <summary>
/// Locates, loads and saves the shared TmSync appsettings.json so the GUI edits the same
/// configuration the Windows service and CLI use. Unknown properties in the file are preserved.
/// </summary>
public sealed class ConfigService
{
    private static readonly string RememberFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TmSync", "manager.lastconfig");

    public string ConfigPath { get; private set; } = "";
    public JsonNode Root { get; private set; } = new JsonObject();

    /// <summary>Finds a config file: TMSYNC_CONFIG env var, exe directory, or the last used path.</summary>
    public static string? LocateDefault()
    {
        var env = Environment.GetEnvironmentVariable("TMSYNC_CONFIG");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

        var local = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(local)) return local;

        if (File.Exists(RememberFile))
        {
            var remembered = File.ReadAllText(RememberFile).Trim();
            if (File.Exists(remembered)) return remembered;
        }
        return null;
    }

    public void Load(string path)
    {
        ConfigPath = Path.GetFullPath(path);
        Root = File.Exists(ConfigPath)
            ? JsonNode.Parse(File.ReadAllText(ConfigPath)) ?? new JsonObject()
            : new JsonObject();

        Directory.CreateDirectory(Path.GetDirectoryName(RememberFile)!);
        File.WriteAllText(RememberFile, ConfigPath);
    }

    public void Save()
    {
        File.WriteAllText(ConfigPath, Root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public IConfigurationRoot BuildConfiguration() =>
        new ConfigurationBuilder().AddJsonFile(ConfigPath, optional: false, reloadOnChange: false).Build();

    public T GetValue<T>(string[] path, T fallback)
    {
        JsonNode? node = Root;
        foreach (var segment in path)
        {
            node = node?[segment];
            if (node is null) return fallback;
        }
        try
        {
            return node!.GetValue<T>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return fallback;
        }
    }

    public void Set(string[] path, JsonNode? value)
    {
        JsonNode node = Root;
        for (var i = 0; i < path.Length - 1; i++)
        {
            if (node[path[i]] is not JsonObject child)
            {
                child = new JsonObject();
                node[path[i]] = child;
            }
            node = child;
        }
        node[path[^1]] = value;
    }

    /// <summary>State DB path from config, resolved relative to the config file's directory.</summary>
    public string ResolveStateDbPath()
    {
        var raw = GetValue(["Sync", "StateDatabasePath"], "tmsync-state.db");
        return Path.IsPathRooted(raw)
            ? raw
            : Path.Combine(Path.GetDirectoryName(ConfigPath)!, raw);
    }
}
