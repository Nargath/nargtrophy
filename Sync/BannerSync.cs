using System.Collections;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using NargTrophy.Config;

namespace NargTrophy.Sync;

/// <summary>
/// Sends the server's <c>banners.yml</c> to clients: once when they join (before they load into the world)
/// and again whenever the server's file is saved. Clients keep their own banner list (prefabs are built at
/// startup) and take the server's values. See the Open question on list mismatch in docs/04_Technical.md;
/// this is option (a): lists must match, values sync, mismatches are logged.
/// </summary>
internal static class BannerSync
{
    private static CustomRPC? _rpc;

    public static void Init()
    {
        _rpc = NetworkManager.Instance.AddRPC("NargTrophy_Banners", OnServerReceive, OnClientReceive);
        SynchronizationManager.Instance.AddInitialSynchronization(_rpc, () => Package(BannerRegistry.LocalText));
    }

    /// <summary>Pushes the server's current file to every connected client. Does nothing on a client.</summary>
    public static void SendToAllClients()
    {
        if (_rpc == null || ZNet.instance == null || !ZNet.instance.IsServer()) return;
        if (ZNet.instance.m_peers.Count == 0) return;
        _rpc.SendPackage(ZNet.instance.m_peers, Package(BannerRegistry.LocalText));
    }

    private static ZPackage Package(string text)
    {
        var package = new ZPackage();
        package.Write(text);
        return package;
    }

    // Clients never send banner data to the server.
    private static IEnumerator OnServerReceive(long sender, ZPackage package)
    {
        yield break;
    }

    private static IEnumerator OnClientReceive(long sender, ZPackage package)
    {
        if (ZNet.instance != null && ZNet.instance.IsServer()) yield break;
        string text = package.ReadString();
        Plugin.Log.LogInfo("Received banner values from the server.");
        BannerRegistry.UseServerValues(text);
        yield break;
    }

    /// <summary>Every new session starts from the local file; a server then overrides it during login.</summary>
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
    private static class ResetOnNewSession
    {
        private static void Postfix() => BannerRegistry.UseLocalValues();
    }
}
