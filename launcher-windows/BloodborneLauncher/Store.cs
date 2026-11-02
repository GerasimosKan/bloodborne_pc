// SPDX-FileCopyrightText: Copyright 2026 bbport contributors
// SPDX-License-Identifier: GPL-2.0-or-later
// The launcher's data: the settings schema (launcher/settings_schema.json, shared with the Linux
// launcher), the launcher's settings.json (%APPDATA%\bbport-launcher, read by bbport-run.exe --play)
// and bbport.ini (the game's settings, which its in-game menu edits too).
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BloodborneLauncher;

public sealed class Store
{
    /// The package root: the launcher's folder, or the nearest one above it holding bbport-run.exe.
    public static readonly string PackageDir = FindPackageDir();
    public static readonly string DataDir = Environment.GetEnvironmentVariable("BB_DATA_DIR") is { Length: > 0 } dir
        ? dir : PackageDir;
    // %APPDATA% as bbport_launcher_win.py reads it (os.environ), so both find the same file.
    public static readonly string ConfigFile = Path.Combine(
        Environment.GetEnvironmentVariable("APPDATA") is { Length: > 0 } appData
            ? appData : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "bbport-launcher", "settings.json");

    private static string FindPackageDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "bbport-run.exe")))
                return dir.FullName.TrimEnd('\\', '/');
        return AppContext.BaseDirectory.TrimEnd('\\', '/');
    }
    public static readonly string IniFile = Path.Combine(DataDir, "bbport.ini");

    public JsonObject Schema { get; }
    public JsonObject Settings { get; private set; } = new();
    private readonly Dictionary<string, string> ini = new(StringComparer.Ordinal);
    private readonly HashSet<string> iniChanged = new(StringComparer.Ordinal);

    public Store()
    {
        using var stream = typeof(Store).Assembly.GetManifestResourceStream("settings_schema.json")
            ?? throw new InvalidOperationException("settings_schema.json is not embedded");
        Schema = JsonNode.Parse(stream)!.AsObject();
        Load();
    }

    public void Load()
    {
        try
        {
            Settings = JsonNode.Parse(File.ReadAllText(ConfigFile, Encoding.UTF8))?.AsObject() ?? new JsonObject();
        }
        catch (Exception)
        {
            // A damaged settings file falls back to the copy kept by the previous save.
            try
            {
                Settings = JsonNode.Parse(File.ReadAllText(ConfigFile + ".bak", Encoding.UTF8))?.AsObject() ?? new JsonObject();
                File.Copy(ConfigFile + ".bak", ConfigFile, true); // so the next save keeps this good copy as .bak, not the damaged file
            }
            catch (Exception)
            {
                Settings = new JsonObject();
            }
        }
        ini.Clear();
        iniChanged.Clear();
        try
        {
            foreach (var line in File.ReadAllLines(IniFile, Encoding.UTF8))
            {
                var at = line.IndexOf('=');
                if (at > 0 && !line.TrimStart().StartsWith('#'))
                    ini[line[..at].Trim()] = line[(at + 1)..].Trim();
            }
        }
        catch (IOException)
        {
        }
    }

    /// The items of every section shown on Windows.
    public IEnumerable<JsonObject> Items => Schema["sections"]!.AsArray()
        .SelectMany(section => section!["items"]!.AsArray()).Select(item => item!.AsObject()).Where(OnWindows);

    public static bool OnWindows(JsonObject item) =>
        item["platforms"] is not JsonArray platforms || platforms.Any(p => (string?)p == "windows");

    public JsonObject? Item(string key) => Items.FirstOrDefault(item => (string?)item["key"] == key);

    // --- values as text: bools "1"/"0", numbers with a dot -------------------------------------

    public string Get(string key)
    {
        var item = Item(key);
        var store = (string?)item?["store"] ?? "settings";
        if (store == "ini")
            return ini.TryGetValue(key, out var value) ? value : DefaultText(item, key);
        var node = Settings[key];
        if (node is null)
            return DefaultText(item, key);
        return node.GetValueKind() switch
        {
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            JsonValueKind.Number => node.GetValue<double>().ToString(CultureInfo.InvariantCulture),
            _ => node.ToString(),
        };
    }

    /// A setting changed (its key): cards that depend on another one follow it (the upscaler's files).
    public event Action<string>? Changed;

    public void Set(string key, string value)
    {
        SetValue(key, value);
        Changed?.Invoke(key);
    }

    private void SetValue(string key, string value)
    {
        var item = Item(key);
        if (((string?)item?["store"] ?? "settings") == "ini")
        {
            if (Get(key) != value || !ini.ContainsKey(key))
            {
                ini[key] = value;
                iniChanged.Add(key);
            }
            return;
        }
        Settings[key] = (string?)item?["type"] switch
        {
            "bool" => JsonValue.Create(value == "1"),
            "int" => JsonValue.Create(int.TryParse(value, out var number) ? number : 0),
            _ => JsonValue.Create(value),
        };
    }

    /// An ini key the schema does not describe (frame_generation_multiplier).
    public string GetIni(string key, string fallback) => ini.TryGetValue(key, out var value) ? value : fallback;

    public void SetIni(string key, string value)
    {
        if (GetIni(key, "\0") != value)
        {
            ini[key] = value;
            iniChanged.Add(key);
        }
    }

    private static string DefaultText(JsonObject? item, string key)
    {
        if (key == "language")
            return "1"; // English, as this launcher always was
        var node = item?["default"];
        if (node is null)
            return "";
        return node.GetValueKind() switch
        {
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            JsonValueKind.Number when (string?)item!["type"] == "float" =>
                node.GetValue<double>().ToString("0.00", CultureInfo.InvariantCulture),
            JsonValueKind.Number => node.GetValue<double>().ToString(CultureInfo.InvariantCulture),
            _ => node.ToString(),
        };
    }

    public void Save()
    {
        // Every setting shown, defaults included: bbport-run.exe --play reads the same file.
        foreach (var item in Items)
        {
            var key = (string)item["key"]!;
            var type = (string?)item["type"];
            if ((string?)item["store"] == "settings" && type != "special" && Settings[key] is null)
                Set(key, Get(key));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
        WriteSafely(ConfigFile, Settings.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
        SaveIni();
    }

    /// Writes the changed keys into bbport.ini; its other lines (the in-game menu's) stay.
    private void SaveIni()
    {
        if (iniChanged.Count == 0)
            return;
        List<string> lines;
        try
        {
            lines = File.ReadAllLines(IniFile, Encoding.UTF8).ToList();
        }
        catch (IOException)
        {
            lines = new List<string> { "# bbport settings (in-game menu: Insert / L3+R3)" };
        }
        var left = new HashSet<string>(iniChanged, StringComparer.Ordinal);
        for (var i = 0; i < lines.Count; ++i)
        {
            var at = lines[i].IndexOf('=');
            if (at <= 0 || lines[i].TrimStart().StartsWith('#'))
                continue;
            var key = lines[i][..at].Trim();
            if (left.Remove(key))
                lines[i] = $"{key}={ini[key]}";
        }
        lines.AddRange(iniChanged.Where(left.Contains).Select(key => $"{key}={ini[key]}"));
        Directory.CreateDirectory(Path.GetDirectoryName(IniFile)!);
        WriteSafely(IniFile, string.Join("\n", lines) + "\n");
        iniChanged.Clear();
    }

    // Write beside the target and swap it in, so a crash mid-save never truncates the file;
    // the previous version is kept as .bak.
    internal static void WriteSafely(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        if (File.Exists(path))
            File.Replace(temp, path, path + ".bak");
        else
            File.Move(temp, path);
    }
}
