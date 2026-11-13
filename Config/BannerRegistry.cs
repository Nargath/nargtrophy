using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace NargTrophy.Config;

/// <summary>
/// Owns the banner list. The list (which banners exist) comes from the local banner files at startup and never
/// changes while the game runs, because each banner is a generated prefab. Values do change: when a file is
/// saved, and when a server sends its own files (server values win while connected).
///
/// Files (phase 4): <c>banners.yml</c> (main: defaults and vanilla banners) plus every <c>banners.*.yml</c> in the
/// same folder, e.g. one per mod, read in name order. Other names (like <c>suggested-banners.yml</c>) are ignored.
/// </summary>
internal static class BannerRegistry
{
    public const string FileName = "banners.yml";
    private const string ExtraPrefix = "banners.";
    private const string Extension = ".yml";
    private const float ReloadDelay = 0.5f;

    public static readonly List<BannerDefinition> All = new();
    private static readonly Dictionary<string, BannerDefinition> ById = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The local files as last read, main first. A server sends these to joining clients.</summary>
    public static IReadOnlyList<BannerFile> LocalFiles { get; private set; } = new List<BannerFile>();

    /// <summary>Creatures the local files leave without a banner on purpose (noBanner); used by nt_trophies.</summary>
    public static HashSet<string> NoBanner { get; private set; } = new();

    /// <summary>True while connected to a remote server whose values are in force.</summary>
    public static bool UsingServerValues { get; private set; }

    /// <summary>Raised after any value change (reload, sync, back to local).</summary>
    public static event Action? ValuesChanged;

    public static string Folder => Path.Combine(Paths.ConfigPath, Plugin.PluginName);
    public static string FilePath => Path.Combine(Folder, FileName);

    private static FileSystemWatcher? _watcher;
    private static volatile bool _fileChanged;
    private static float _reloadAt = -1f;

    public static void Init()
    {
        EnsureFile();
        LocalFiles = ReadFiles() ?? new List<BannerFile>();
        NoBanner = BannerYaml.ReadNoBanner(LocalFiles);
        List<BannerDefinition>? defs = BannerYaml.ParseAll(LocalFiles, "");
        if (defs == null)
        {
            Plugin.Log.LogError($"No banners loaded. Fix {FilePath} and restart.");
            return;
        }
        foreach (BannerDefinition def in defs)
        {
            All.Add(def);
            ById[def.Id] = def;
        }
        Plugin.Log.LogInfo($"Loaded {All.Count} banner definition(s) from {string.Join(", ", LocalFiles.Select(f => f.Name))}.");
        StartWatching();
    }

    public static bool TryGet(string id, out BannerDefinition def) => ById.TryGetValue(id, out def);

    /// <summary>True for banners.yml and banners.&lt;anything&gt;.yml.</summary>
    public static bool IsBannerFile(string name) =>
        string.Equals(name, FileName, StringComparison.OrdinalIgnoreCase) ||
        (name.StartsWith(ExtraPrefix, StringComparison.OrdinalIgnoreCase) &&
         name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) &&
         name.Length > ExtraPrefix.Length + Extension.Length);

    /// <summary>Called every frame from the plugin. Picks up file changes on the main thread, debounced.</summary>
    public static void Update()
    {
        if (_fileChanged)
        {
            _fileChanged = false;
            _reloadAt = Time.unscaledTime + ReloadDelay;
        }
        if (_reloadAt >= 0f && Time.unscaledTime >= _reloadAt)
        {
            _reloadAt = -1f;
            ReloadLocalFiles();
        }
    }

    /// <summary>Back to the local files' values. Called when a new connection starts (ZNet.Awake).</summary>
    public static void UseLocalValues()
    {
        UsingServerValues = false;
        List<BannerDefinition>? defs = BannerYaml.ParseAll(LocalFiles, "");
        if (defs != null) ApplyValues(defs, fromServer: false);
    }

    /// <summary>Applies the server's files. Banners the server lacks are switched off on this client.</summary>
    public static void UseServerValues(IReadOnlyList<BannerFile> files)
    {
        List<BannerDefinition>? defs = BannerYaml.ParseAll(files, "server ");
        if (defs == null)
        {
            Plugin.Log.LogError("The server's banner files could not be read. Keeping local values.");
            return;
        }
        UsingServerValues = true;
        ApplyValues(defs, fromServer: true);
    }

    private static void ReloadLocalFiles()
    {
        List<BannerFile>? files = ReadFiles();
        if (files == null) return;
        LocalFiles = files;
        NoBanner = BannerYaml.ReadNoBanner(files);

        if (UsingServerValues)
        {
            Plugin.Log.LogInfo("Banner files changed. Connected to a server, so its values stay in force until you disconnect.");
            return;
        }

        List<BannerDefinition>? defs = BannerYaml.ParseAll(files, "");
        if (defs == null) return;
        Plugin.Log.LogInfo("Banner files changed. Reloading values.");
        ApplyValues(defs, fromServer: false);
        Sync.BannerSync.SendToAllClients();
    }

    private static void ApplyValues(List<BannerDefinition> incoming, bool fromServer)
    {
        var incomingById = new Dictionary<string, BannerDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (BannerDefinition def in incoming) incomingById[def.Id] = def;
        string where = fromServer ? "the server's banner files" : "the banner files";

        foreach (BannerDefinition def in All)
        {
            if (!incomingById.TryGetValue(def.Id, out BannerDefinition other))
            {
                def.KnownToServer = !fromServer;
                if (fromServer)
                    Plugin.Log.LogWarning($"Banner '{def.Id}' is not in {where}. It does nothing on this server.");
                else
                    Plugin.Log.LogWarning($"Banner '{def.Id}' was removed from {where}. Restart to remove it fully; until then it does nothing.");
                if (!fromServer) def.Enabled = false;
                continue;
            }

            def.KnownToServer = true;
            if (!string.Equals(def.Creature, other.Creature, StringComparison.Ordinal) ||
                !string.Equals(def.Trophy, other.Trophy, StringComparison.Ordinal))
            {
                Plugin.Log.LogWarning($"Banner '{def.Id}' has a different creature or trophy in {where}. " +
                                      "Those are fixed at startup; only values were updated. Restart with matching files.");
            }
            if (other.Enabled && !def.Registered && !def.Enabled)
            {
                Plugin.Log.LogWarning($"Banner '{def.Id}' is enabled in {where} but was disabled at startup. Restart to build it.");
            }

            def.Enabled = other.Enabled;
            def.Permanent = other.Permanent;
            def.Portable = other.Portable;
        }

        foreach (BannerDefinition other in incoming)
        {
            if (ById.ContainsKey(other.Id)) continue;
            if (fromServer)
                Plugin.Log.LogError($"The server defines banner '{other.Id}', which this client doesn't have. " +
                                    "Copy the server's banner files and restart, or that banner won't work for you.");
            else
                Plugin.Log.LogWarning($"Banner '{other.Id}' was added to {where}. Restart to build it.");
        }

        ValuesChanged?.Invoke();
    }

    private static void EnsureFile()
    {
        string path = FilePath;
        if (File.Exists(path)) return;
        try
        {
            Directory.CreateDirectory(Folder);
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NargTrophy.banners.yml");
            if (stream == null)
            {
                Plugin.Log.LogError("Default banners.yml is missing from the plugin.");
                return;
            }
            using var reader = new StreamReader(stream);
            File.WriteAllText(path, reader.ReadToEnd());
            Plugin.Log.LogInfo($"Wrote default {FileName} to {path}.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not write default {FileName}: {e.Message}");
        }
    }

    /// <summary>banners.yml first, then the extra files in name order. Null if the main file can't be read.</summary>
    private static List<BannerFile>? ReadFiles()
    {
        var files = new List<BannerFile>();
        try
        {
            files.Add(new BannerFile(FileName, File.ReadAllText(FilePath)));
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not read {FilePath}: {e.Message}");
            return null;
        }

        IEnumerable<string> extras;
        try
        {
            extras = Directory.GetFiles(Folder, "*" + Extension)
                .Select(Path.GetFileName)
                .Where(name => IsBannerFile(name) && !string.Equals(name, FileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not list extra banner files in {Folder}: {e.Message}");
            return files;
        }

        foreach (string name in extras)
        {
            try
            {
                files.Add(new BannerFile(name, File.ReadAllText(Path.Combine(Folder, name))));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not read {name}: {e.Message}. Skipped.");
            }
        }
        return files;
    }

    private static void StartWatching()
    {
        try
        {
            _watcher = new FileSystemWatcher(Folder, "*" + Extension)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            // Raised on a worker thread: only set a flag, the main thread reloads. Other .yml files (such as the
            // suggestions nt_trophies writes) don't trigger a reload.
            _watcher.Changed += (_, e) => { if (IsBannerFile(e.Name)) _fileChanged = true; };
            _watcher.Created += (_, e) => { if (IsBannerFile(e.Name)) _fileChanged = true; };
            _watcher.Deleted += (_, e) => { if (IsBannerFile(e.Name)) _fileChanged = true; };
            _watcher.Renamed += (_, e) => { if (IsBannerFile(e.Name) || IsBannerFile(e.OldName)) _fileChanged = true; };
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not watch the banner files for changes ({e.Message}). Restart to apply edits.");
        }
    }

    public static void Shutdown()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
