using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Wise.Skyfarer;

// World loading reassigns ZDO IDs. A shared saved GUID survives that remapping
// without consuming the native connection slot used by attached passengers.
internal static class CargoLinks
{
    internal const string Key = "skyfarer.cargoLink";
    private static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> Records =
        AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");
    private static readonly Dictionary<string, ZDOID> Planes = new Dictionary<string, ZDOID>();
    private static readonly Dictionary<string, ZDOID> Chests = new Dictionary<string, ZDOID>();
    private static ZDOMan manager;
    private static float nextScan;

    private static void ResetSession()
    {
        if (ReferenceEquals(manager, ZDOMan.instance)) return;
        manager = ZDOMan.instance;
        Planes.Clear();
        Chests.Clear();
        nextScan = 0;
    }

    internal static void Register(ZDO data)
    {
        ResetSession();
        string link = data.GetString(Key);
        if (link.Length == 0) return;
        if (data.GetPrefab() == AircraftFactory.PrefabName.GetStableHashCode()) Planes[link] = data.m_uid;
        else if (data.GetPrefab() == CargoHold.PrefabName.GetStableHashCode()) Chests[link] = data.m_uid;
    }

    internal static ZDO Find(string link, bool cargo)
    {
        ResetSession();
        if (manager == null || string.IsNullOrEmpty(link)) return null;
        var index = cargo ? Chests : Planes;
        var found = Lookup(index, link, cargo);
        if (found != null) return found;
        // Scan only on a cache miss, shared by all fittings, at most every two
        // seconds. The server sees the whole save; clients see streamed records.
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 2;
            foreach (var data in Records(manager).Values) Register(data);
            found = Lookup(index, link, cargo);
        }
        return found;
    }

    private static ZDO Lookup(Dictionary<string, ZDOID> index, string link, bool cargo)
    {
        if (!index.TryGetValue(link, out var id)) return null;
        var data = manager.GetZDO(id);
        int prefab = (cargo ? CargoHold.PrefabName : AircraftFactory.PrefabName).GetStableHashCode();
        // ZDO objects are pooled; validate the key and prefab on every lookup.
        return data != null && data.IsValid() && data.GetPrefab() == prefab && data.GetString(Key) == link ? data : null;
    }
}
