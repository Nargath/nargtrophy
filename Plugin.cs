using BepInEx;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using NargTrophy.Commands;
using NargTrophy.Banners;
using NargTrophy.Config;
using NargTrophy.Sync;

namespace NargTrophy;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid)]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
[SynchronizationMode(AdminOnlyStrictness.IfOnServer)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGUID = "com.nargath.nargtrophy";
    public const string PluginName = "NargTrophy";
    public const string PluginVersion = "0.1.1";

    internal static Plugin Instance { get; private set; } = null!;
    internal static BepInEx.Logging.ManualLogSource Log => Instance.Logger;

    private Harmony? _harmony;

    private void Awake()
    {
        Instance = this;

        ModConfig.Bind(Config);
        Translations.Init();
        BannerRegistry.Init();
        BannerSync.Init();
        BannerPieces.Init();
        CommandManager.Instance.AddConsoleCommand(new TrophyDumpCommand());
        CommandManager.Instance.AddConsoleCommand(new RemountCommand());
        CommandManager.Instance.AddConsoleCommand(new StandardsCommand());

        _harmony = new Harmony(PluginGUID);
        _harmony.PatchAll();

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
    }

    private void Update()
    {
        BannerRegistry.Update();
        BannerAura.Update();
    }

    private void OnDestroy()
    {
        BannerRegistry.Shutdown();
        _harmony?.UnpatchSelf();
    }
}
