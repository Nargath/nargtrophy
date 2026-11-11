using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace NargTrophy.Config;

/// <summary>
/// Owns the banner list. The list (which banners exist) comes from the local <c>banners.yml</c> at startup and
/// never changes while the game runs, because each banner is a generated prefab. Values do change: when the
/// file is saved, and when a server sends its own file (server values win while connected).
/// </summary>
internal static class BannerRegistry
{
    public const string FileName = "banners.yml";
    private const float ReloadDelay = 0.5f;

    public static readonly List<BannerDefinition> All = new();
    private static readonly Dictionary<string, BannerDefinition> ById = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The local file's text as last read. A server sends this to joining clients.</summary>
    public static string LocalText { get; private set; } = "";

    /// <summary>True while connected to a remote server whose values are in force.</summary>
    public static bool UsingServerValues { get; private set; }

    /// <summary>Raised after any value change (reload, sync, back to local).</summary>
    public static event Action? ValuesChanged;

    public static string FilePath => Path.Combine(Path.Combine(Paths.ConfigPath, Plugin.PluginName), FileName);

    private static FileSystemWatcher? _watcher;
    private static volatile bool _fileChanged;
    private static float _reloadAt = -1f;

    public static void Init()
    {
        EnsureFile();
        LocalText = ReadFile() ?? "";
        List<BannerDefinition>? defs = BannerYaml.Parse(LocalText, FileName);
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
        Plugin.Log.LogInfo($"Loaded {All.Count} banner definition(s) from {FileName}.");
        StartWatching();
    }

    public static bool TryGet(string id, out BannerDefinition def) => ById.TryGetValue(id, out def);

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
            ReloadLocalFile();
        }
    }

    /// <summary>Back to the local file's values. Called when a new connection starts (ZNet.Awake).</summary>
    public static void UseLocalValues()
    {
        UsingServerValues = false;
        List<BannerDefinition>? defs = BannerYaml.Parse(LocalText, FileName);
        if (defs != null) ApplyValues(defs, FileName, fromServer: false);
    }

    /// <summary>Applies the server's file. Banners the server lacks are switched off on this client.</summary>
    public static void UseServerValues(string text)
    {
        List<BannerDefinition>? defs = BannerYaml.Parse(text, "server " + FileName);
        if (defs == null)
        {
            Plugin.Log.LogError("The server's banners.yml could not be read. Keeping local values.");
            return;
        }
        UsingServerValues = true;
        ApplyValues(defs, "server " + FileName, fromServer: true);
    }

    private static void ReloadLocalFile()
    {
        string? text = ReadFile();
        if (text == null) return;
        LocalText = text;

        if (UsingServerValues)
        {
            Plugin.Log.LogInfo($"{FileName} changed. Connected to a server, so its values stay in force until you disconnect.");
            return;
        }

        List<BannerDefinition>? defs = BannerYaml.Parse(text, FileName);
        if (defs == null) return;
        Plugin.Log.LogInfo($"{FileName} changed. Reloading values.");
        ApplyValues(defs, FileName, fromServer: false);
        Sync.BannerSync.SendToAllClients();
    }

    private static void ApplyValues(List<BannerDefinition> incoming, string source, bool fromServer)
    {
        var incomingById = new Dictionary<string, BannerDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (BannerDefinition def in incoming) incomingById[def.Id] = def;

        foreach (BannerDefinition def in All)
        {
            if (!incomingById.TryGetValue(def.Id, out BannerDefinition other))
            {
                def.KnownToServer = !fromServer;
                if (fromServer)
                    Plugin.Log.LogWarning($"Banner '{def.Id}' is not in the server's {FileName}. It does nothing on this server.");
                else
                    Plugin.Log.LogWarning($"Banner '{def.Id}' was removed from {FileName}. Restart to remove it fully; until then it does nothing.");
                if (!fromServer) def.Enabled = false;
                continue;
            }

            def.KnownToServer = true;
            if (!string.Equals(def.Creature, other.Creature, StringComparison.Ordinal) ||
                !string.Equals(def.Trophy, other.Trophy, StringComparison.Ordinal))
            {
                Plugin.Log.LogWarning($"Banner '{def.Id}' has a different creature or trophy in {source}. " +
                                      "Those are fixed at startup; only values were updated. Restart with matching files.");
            }
            if (other.Enabled && !def.Registered && !def.Enabled)
            {
                Plugin.Log.LogWarning($"Banner '{def.Id}' is enabled in {source} but was disabled at startup. Restart to build it.");
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
                                    $"Copy the server's {FileName} and restart, or that banner won't work for you.");
            else
                Plugin.Log.LogWarning($"Banner '{other.Id}' was added to {FileName}. Restart to build it.");
        }

        ValuesChanged?.Invoke();
    }

    private static void EnsureFile()
    {
        string path = FilePath;
        if (File.Exists(path)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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

    private static string? ReadFile()
    {
        try
        {
            return File.ReadAllText(FilePath);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not read {FilePath}: {e.Message}");
            return null;
        }
    }

    private static void StartWatching()
    {
        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(FilePath)!, FileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            // Raised on a worker thread: only set a flag, the main thread reloads.
            _watcher.Changed += (_, _) => _fileChanged = true;
            _watcher.Created += (_, _) => _fileChanged = true;
            _watcher.Renamed += (_, _) => _fileChanged = true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not watch {FileName} for changes ({e.Message}). Restart to apply edits.");
        }
    }

    public static void Shutdown()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
