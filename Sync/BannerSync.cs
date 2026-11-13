using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using NargTrophy.Config;

namespace NargTrophy.Sync;

/// <summary>
/// Sends the server's banner files (banners.yml and every banners.*.yml) to clients: once when they join (before
/// they load into the world) and again whenever one of the server's files is saved. Clients keep their own banner
/// list (prefabs are built at startup) and take the server's values. See the Open question on list mismatch in
/// docs/04_Technical.md; this is option (a): lists must match, values sync, mismatches are logged.
/// </summary>
internal static class BannerSync
{
    private static CustomRPC? _rpc;

    public static void Init()
    {
        _rpc = NetworkManager.Instance.AddRPC("NargTrophy_Banners", OnServerReceive, OnClientReceive);
        SynchronizationManager.Instance.AddInitialSynchronization(_rpc, () => Package(BannerRegistry.LocalFiles));
    }

    /// <summary>Pushes the server's current files to every connected client. Does nothing on a client.</summary>
    public static void SendToAllClients()
    {
        if (_rpc == null || ZNet.instance == null || !ZNet.instance.IsServer()) return;
        if (ZNet.instance.m_peers.Count == 0) return;
        _rpc.SendPackage(ZNet.instance.m_peers, Package(BannerRegistry.LocalFiles));
    }

    private static ZPackage Package(IReadOnlyList<BannerFile> files)
    {
        var package = new ZPackage();
        package.Write(files.Count);
        foreach (BannerFile file in files)
        {
            package.Write(file.Name);
            package.Write(file.Text);
        }
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
        int count = package.ReadInt();
        var files = new List<BannerFile>(count);
        for (int i = 0; i < count; i++) files.Add(new BannerFile(package.ReadString(), package.ReadString()));
        Plugin.Log.LogInfo($"Received {count} banner file(s) from the server.");
        BannerRegistry.UseServerValues(files);
        yield break;
    }

    /// <summary>Every new session starts from the local files; a server then overrides them during login.</summary>
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
    private static class ResetOnNewSession
    {
        private static void Postfix() => BannerRegistry.UseLocalValues();
    }
}
