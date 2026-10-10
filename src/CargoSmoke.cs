using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Wise.Skyfarer;

internal static class CargoSmoke
{
    internal static void AddWood(Container container, int count, bool marker = false)
    {
        var prefab = ObjectDB.instance.GetItemPrefab("Wood");
        var item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
        item.m_dropPrefab = prefab;
        item.m_stack = count;
        if (marker) item.m_customData["skyfarer.smoke.cargo"] = "drop-test";
        container.GetInventory().AddItem(item);
    }

    internal static IEnumerator Run(Aircraft plane, Player player, Action<bool, string> check)
    {
        var hold = plane.GetComponent<CargoHold>();
        float deadline = Time.time + 5;
        while (!hold.Cargo && Time.time < deadline) yield return null;
        check(hold.Cargo, "Aircraft creates its persistent cargo chest");
        var cargo = hold.Cargo;
        var container = cargo.Container;
        var inventory = container.GetInventory();
        var cargoView = cargo.GetComponent<ZNetView>();
        var planeView = plane.GetComponent<ZNetView>();
        var vanilla = Jotunn.Managers.PrefabManager.Instance.GetPrefab("piece_chest_wood").GetComponent<Container>();
        check(inventory.GetWidth() == vanilla.m_width && inventory.GetHeight() == vanilla.m_height &&
            inventory.GetWidth() * inventory.GetHeight() == 10, "Cargo has a normal wooden chest's ten slots");
        check(cargoView.GetZDO().m_uid != planeView.GetZDO().m_uid &&
            cargoView.GetZDO().GetString(CargoLinks.Key).Length == 32 &&
            cargoView.GetZDO().GetString(CargoLinks.Key) == planeView.GetZDO().GetString(CargoLinks.Key),
            "Cargo and aircraft have separate network IDs and share a stable saved link");
        var collider = cargo.GetComponentsInChildren<Collider>().First();
        Physics.SyncTransforms();
        check(Physics.RaycastAll(collider.bounds.center + Vector3.right * 2, Vector3.left, 3,
            LayerMask.GetMask("piece", "Default", "static_solid")).Any(hit => hit.collider == collider),
            "Cargo collider is reachable by the game's default interaction ray query");
        AddWood(container, 7);
        check(inventory.CountItems("$item_wood") == 7 && cargoView.GetZDO().GetByteArray(ZDOVars.s_items) != null,
            "Putting items in cargo writes its persistent inventory");
        player.transform.position = player.GetComponent<Rigidbody>().position = cargo.transform.position + Vector3.right;
        player.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
        player.GetComponent<ZSyncTransform>().SyncNow();
        ZNet.instance.SetReferencePosition(player.transform.position);
        container.Interact(player, false, false);
        Plugin.Log.LogInfo($"CARGO_UI_OPEN visible={InventoryGui.IsVisible()} inUse={container.IsInUse()} open={InventoryGui.instance.IsContainerOpen()} owner={container.IsOwner()} distance={Vector3.Distance(player.transform.position, cargo.transform.position)} active={InventoryGui.instance.isActiveAndEnabled} cutscene={player.InCutscene()} teleport={player.IsTeleporting()} components={string.Join(",", cargo.GetComponents<Component>().Select(c => c.GetType().Name))}");
        yield return new WaitForSeconds(.2f);
        Plugin.Log.LogInfo($"CARGO_UI_AFTER visible={InventoryGui.IsVisible()} inUse={container.IsInUse()} open={InventoryGui.instance.IsContainerOpen()} owner={container.IsOwner()} distance={Vector3.Distance(player.transform.position, cargo.transform.position)} active={InventoryGui.instance.isActiveAndEnabled} cutscene={player.InCutscene()} teleport={player.IsTeleporting()}");
        check(InventoryGui.IsVisible() && container.IsInUse(), "Cargo opens in the normal chest inventory UI");
        InventoryGui.instance.Hide();
        container.SetInUse(false);
        long owner = planeView.GetZDO().GetOwner();
        long cargoOwner = cargoView.GetZDO().GetOwner();
        try
        {
            foreach (string rpc in new[] { "RPC_RequestOpen", "RPC_RequestStack" })
            {
                cargoView.GetZDO().SetOwner(cargoOwner);
                AccessTools.Method(typeof(Container), rpc).Invoke(container, new object[] { owner + 789, player.GetPlayerID() });
                check(cargoView.GetZDO().GetOwner() == owner + 789 && planeView.GetZDO().GetOwner() == owner,
                    rpc + " transfers only cargo ownership and leaves the pilot's aircraft ownership intact");
            }
        }
        finally { cargoView.GetZDO().SetOwner(cargoOwner); }
        // Keep the seven wood in this chest for the separate process reload.

        var doomed = UnityEngine.Object.Instantiate(AircraftFactory.Prefab,
            plane.transform.position + Vector3.right * 60, Quaternion.identity).GetComponent<Aircraft>();
        doomed.Body.isKinematic = true;
        var doomedHold = doomed.GetComponent<CargoHold>();
        deadline = Time.time + 5;
        while (!doomedHold.Cargo && Time.time < deadline) yield return null;
        check(doomedHold.Cargo, "Second aircraft receives a distinct cargo chest");
        var droppedCargo = doomedHold.Cargo;
        AddWood(droppedCargo.Container, 3, true);
        var piece = doomed.GetComponent<Piece>();
        piece.SetCreator(player.GetPlayerID(), Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
        var camera = GameCamera.instance.transform;
        Vector3 cameraPosition = camera.position;
        Quaternion cameraRotation = camera.rotation;
        Vector3 aim = doomed.transform.position + Vector3.up * .2f;
        player.transform.position = player.GetComponent<Rigidbody>().position = aim + Vector3.right * 4;
        camera.position = aim + Vector3.right * 4;
        camera.LookAt(aim);
        Physics.SyncTransforms();
        var remove = AccessTools.Method(typeof(Player), "RemovePiece");
        check(!(bool)AccessTools.Method(typeof(Player), "CheckCanRemovePiece").Invoke(player, new object[] { piece }),
            "Hammer retains the nearby-workbench requirement");
        var bench = UnityEngine.Object.Instantiate(Jotunn.Managers.PrefabManager.Instance.GetPrefab("piece_workbench"),
            aim + Vector3.right * 8, Quaternion.identity);
        yield return null; // CraftingStation registers its range in Start.
        camera.position = aim + Vector3.right * 4;
        camera.LookAt(aim);
        Physics.SyncTransforms();
        check((bool)AccessTools.Method(typeof(Player), "CheckCanRemovePiece").Invoke(player, new object[] { piece }),
            "Workbench becomes available after its Start lifecycle");
        check(Physics.Raycast(camera.position, camera.forward, out var target, 50,
            Traverse.Create(player).Field("m_removeRayMask").GetValue<int>()) &&
            target.collider.GetComponentInParent<Piece>() == piece, "Actual hammer ray targets the aircraft hull");
        check(Vector3.Distance(target.point, player.m_eye.position) < player.m_maxPlaceDistance,
            "Hammer target is within the player's build reach");
        check(!Location.IsInsideNoBuildLocation(piece.transform.position), "Hammer fixture is outside protected world locations");
        check(PrivateArea.CheckAccess(piece.transform.position), "Hammer fixture permits ward access");
        check(piece.CanBeRemoved(), "Empty aircraft permits removal through native occupancy check");
        piece.m_canBeRemoved = false;
        check(!(bool)remove.Invoke(player, null) && doomed, "Hammer reproduces rejection with inherited Karve removal flag");
        piece.m_canBeRemoved = true;
        check((bool)remove.Invoke(player, null), "Actual Player.RemovePiece hammer path dismantles empty aircraft near workbench");
        camera.SetPositionAndRotation(cameraPosition, cameraRotation);
        ZNetScene.instance.Destroy(bench);
        yield return new WaitForSeconds(1.5f);
        check(!doomed && !droppedCargo && UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None)
            .Any(drop => drop.m_itemData.m_stack == 3 && drop.m_itemData.m_customData.ContainsKey("skyfarer.smoke.cargo")),
            "Hammer removal drops stored cargo and removes the linked chest");
    }

    internal static IEnumerator Reload(Aircraft plane, Action<bool, string> check)
    {
        var hold = plane.GetComponent<CargoHold>();
        float deadline = Time.time + 8;
        while ((!hold.Cargo || hold.Cargo.Container.GetInventory().CountItems("$item_wood") != 7) && Time.time < deadline)
            yield return null;
        check(hold.Cargo && hold.Cargo.Container.GetInventory().CountItems("$item_wood") == 7,
            "Cargo contents survive a full game restart and world reload");
        check(UnityEngine.Object.FindObjectsByType<SkyCargo>(FindObjectsSortMode.None).Count(c =>
            c.GetComponent<ZNetView>().GetZDO().GetString(CargoLinks.Key) == plane.GetComponent<ZNetView>().GetZDO().GetString(CargoLinks.Key)) == 1,
            "Reload reconnects the existing cargo chest without creating a duplicate");
        check(Vector3.Distance(hold.Cargo.transform.position, plane.transform.TransformPoint(CargoHold.Offset)) < .5f,
            "Reloaded cargo follows its aircraft after world object IDs change");
    }
}
