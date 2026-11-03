using System;
using HarmonyLib;
using UnityEngine;

namespace Wise.Skyfarer;

// Payments and upgrades live on the inventory's owner, so opening a chest or
// handing the pilot seat to someone else cannot charge twice or lose a tier.
internal class CargoUpgrades : MonoBehaviour
{
    internal const string TierKey = "skyfarer.cargoTier";
    internal const string WispKey = "skyfarer.wisp";
    private ZNetView view;
    private Container container;
    private static readonly Func<Container, bool> Load = AccessTools.MethodDelegate<Func<Container, bool>>(AccessTools.Method(typeof(Container), "Load"));
    private static readonly Action<Container> Save = AccessTools.MethodDelegate<Action<Container>>(AccessTools.Method(typeof(Container), "Save"));
    internal int Tier => view && view.IsValid() ? Mathf.Clamp(view.GetZDO().GetInt(TierKey), 0, 2) : 0;
    private void Awake()
    {
        view = GetComponent<ZNetView>();
        container = GetComponent<Container>();
        if (!view.IsValid()) return;
        ApplySize();
        view.Register<long, bool>("Skyfarer.Upgrade", Upgrade);
        view.Register<string>("Skyfarer.ServiceReply", Reply);
    }
    private void Update() => ApplySize();
    internal void ApplySize()
    {
        if (!view) view = GetComponent<ZNetView>();
        if (!container) container = GetComponent<Container>();
        if (!view || !view.IsValid() || !container || container.GetInventory() == null) return;
        int height = 2 + Tier * 2;
        container.m_height = height;
        container.GetInventory().SetHeight(height);
    }
    internal void Request(bool wisp)
    {
        if (view && view.IsValid() && Player.m_localPlayer)
            view.InvokeRPC("Skyfarer.Upgrade", Player.m_localPlayer.GetPlayerID(), wisp);
    }
    private void Reply(long sender, string message)
    {
        if (view && view.IsValid() && sender == view.GetZDO().GetOwner() && Player.m_localPlayer)
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, message);
    }
    internal void Upgrade(long sender, long id, bool wisp)
    {
        if (!view || !view.IsValid() || !view.IsOwner()) return;
        var player = Player.GetPlayer(id);
        if (!player || player.IsDead() || player.GetOwner() != sender ||
            Vector3.Distance(player.transform.position, transform.position) > 10 ||
            !ServiceAccess.Allowed(transform.position, id)) return;
        string result = TryUpgrade(wisp);
        view.InvokeRPC(sender, "Skyfarer.ServiceReply", result);
    }
    internal string TryUpgrade(bool wisp)
    {
        if (!view || !view.IsValid() || !view.IsOwner()) return "Cargo is unavailable.";
        if (container.IsInUse()) return "Close the cargo chest before upgrading.";
        ApplySize();
        Load(container);
        if (wisp ? view.GetZDO().GetBool(WispKey) : Tier >= 2)
            return wisp ? "A wisp is already bound." : "Cargo is already at its maximum size.";
        var inventory = container.GetInventory();
        string first = ObjectDB.instance.GetItemPrefab(wisp ? "Wisp" : "FineWood").GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
        string copper = ObjectDB.instance.GetItemPrefab("Copper").GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
        int amount = wisp ? 1 : 20;
        if (inventory.CountItems(first) < amount || (!wisp && inventory.CountItems(copper) < 10))
            return wisp ? "Place 1 Wisp in cargo first." : "Place 20 Fine wood and 10 Copper in cargo first.";
        inventory.RemoveItem(first, amount);
        if (!wisp) inventory.RemoveItem(copper, 10);
        if (wisp) view.GetZDO().Set(WispKey, true);
        else view.GetZDO().Set(TierKey, Tier + 1);
        ApplySize();
        Save(container);
        ZDOMan.instance.ForceSendZDO(view.GetZDO().m_uid);
        return wisp ? "Wisp bound: mist clearing enabled." : "Cargo expanded to " + (10 + Tier * 10) + " slots.";
    }
}

// Container.Load discards slots beyond its current size. Set the saved size
// BEFORE its first load, including when an upgraded chest streams back in.
[HarmonyPatch(typeof(Container), "Load")]
internal static class CargoSizeBeforeLoad
{
    private static void Prefix(Container __instance)
    {
        var upgrades = __instance.GetComponent<CargoUpgrades>();
        if (upgrades) upgrades.ApplySize();
    }
}
