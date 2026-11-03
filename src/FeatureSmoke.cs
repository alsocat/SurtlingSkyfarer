using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Wise.Skyfarer;

internal static class FeatureSmoke
{
    private static void Move(Player player, Vector3 point)
    {
        var rb = player.GetComponent<Rigidbody>();
        player.transform.position = rb.position = point;
        rb.linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
        player.GetComponent<ZSyncTransform>().SyncNow();
        ZNet.instance.SetReferencePosition(point);
    }
    private static void Add(Container cargo, string prefab, int count)
    {
        var obj = ObjectDB.instance.GetItemPrefab(prefab);
        var item = obj.GetComponent<ItemDrop>().m_itemData.Clone();
        item.m_dropPrefab = obj;
        item.m_stack = count;
        cargo.GetInventory().AddItem(item);
    }
    private static void Invoke(Aircraft plane, string method, params object[] args) => AccessTools.Method(typeof(Aircraft), method).Invoke(plane, args);
    internal static IEnumerator Run(Player player, Action<bool, string> check)
    {
        if (Environment.GetEnvironmentVariable("SKYFARER_CONTROL_RELOAD") == "1")
        {
            var records = new System.Collections.Generic.List<ZDO>();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(AircraftFactory.PrefabName, records, ref index)) yield return null;
            var data = records.Single(z => z.GetBool("skyfarer.featureTest"));
            Move(player, data.GetPosition() + Vector3.right * 20);
            float until = Time.time + 25;
            while (!ZNetScene.instance.FindInstance(data.m_uid) && Time.time < until) yield return null;
            var restored = ZNetScene.instance.FindInstance(data.m_uid).GetComponent<Aircraft>();
            yield return CheckCargo(restored, check);
            check(restored.IsValid, "Upgraded aircraft persists across a full process restart");
            yield break;
        }
        player.GetComponent<PlayerController>().enabled = false;
        var start = player.transform.position + Vector3.up * 70;
        var plane = UnityEngine.Object.Instantiate(AircraftFactory.Prefab, start, Quaternion.identity).GetComponent<Aircraft>();
        plane.Body.isKinematic = true;
        Move(player, start + Vector3.right * 20);
        float deadline = Time.time + 15;
        while ((!plane.GetComponent<CargoHold>().Cargo || !ZNetScene.instance.IsAreaReady(start)) && Time.time < deadline) yield return null;
        var view = plane.GetComponent<ZNetView>();
        var cargo = plane.GetComponent<CargoHold>().Cargo;
        var upgrade = cargo.GetComponent<CargoUpgrades>();
        check(!view.GetZDO().Distant && !cargo.GetComponent<ZNetView>().GetZDO().Distant, "Hull and cargo do not stream as distant physics objects");
        var mesh = plane.GetComponentsInChildren<MeshFilter>().First(m => m.name.StartsWith("Stitched wing")).sharedMesh;
        check(Vector3.Distance(mesh.vertices[0], mesh.vertices[5]) > .01f, "Front and back wing surfaces are separated to prevent depth fighting");
        var material = plane.GetComponentsInChildren<MeshRenderer>().First(m => m.name.StartsWith("Stitched wing")).sharedMaterial;
        check(material.shader && !material.shader.name.Contains("Particle") && (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null || material.shader.isSupported), "Cloth uses a supported item material shader: " + material.shader.name);
        var ward = UnityEngine.Object.Instantiate(Jotunn.Managers.PrefabManager.Instance.GetPrefab("guard_stone"), start + Vector3.right * 5, Quaternion.identity);
        ward.GetComponent<Piece>().SetCreator(player.GetPlayerID(), Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
        ward.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_enabled, true);
        check(ServiceAccess.Allowed(start, player.GetPlayerID()), "Service access recognizes the requesting ward owner");
        check(!ServiceAccess.Allowed(start, player.GetPlayerID() + 123), "Service access rejects an unpermitted requester even on the owner's client");
        AccessTools.Method(typeof(PrivateArea), "AddPermitted").Invoke(ward.GetComponent<PrivateArea>(), new object[] { player.GetPlayerID() + 123, "Test guest" });
        check(ServiceAccess.Allowed(start, player.GetPlayerID() + 123), "Service access recognizes explicitly permitted guests");
        ZNetScene.instance.Destroy(ward);
        yield return null;
        check(upgrade.TryUpgrade(false).StartsWith("Place") && upgrade.Tier == 0, "Storage upgrade requires materials");
        Add(cargo.Container, "FineWood", 40); Add(cargo.Container, "Copper", 20); Add(cargo.Container, "Wisp", 1);
        upgrade.Upgrade(view.GetZDO().GetOwner() + 100, player.GetPlayerID(), false);
        check(upgrade.Tier == 0, "Unauthenticated upgrade RPC cannot spend cargo");
        cargo.Container.SetInUse(true);
        check(upgrade.TryUpgrade(false).StartsWith("Close") && upgrade.Tier == 0, "Open cargo cannot be resized during an inventory operation");
        cargo.Container.SetInUse(false);
        Move(player, cargo.transform.position + Vector3.right * 2);
        upgrade.Request(false);
        yield return null;
        check(upgrade.Tier == 1 && cargo.Container.GetInventory().GetHeight() == 4, "Authenticated upgrade grows cargo to 20 slots");
        upgrade.Request(false);
        yield return null;
        check(upgrade.Tier == 2 && cargo.Container.GetInventory().GetHeight() == 6, "Second upgrade grows cargo to 30 slots");
        check(cargo.Container.GetInventory().CountItems("$item_finewood") == 0 && cargo.Container.GetInventory().CountItems("$item_copper") == 0, "Two expansions consume exactly 40 Fine wood and 20 Copper");
        check(upgrade.TryUpgrade(false).Contains("maximum"), "Further storage expansion cannot charge materials");
        upgrade.Request(true);
        yield return new WaitForSeconds(.2f);
        check(cargo.GetComponent<ZNetView>().GetZDO().GetBool(CargoUpgrades.WispKey) && cargo.Container.GetInventory().CountItems("$item_wisp") == 0, "Binding a wisp consumes one item and persists on the cargo record");
        var mist = plane.GetComponent<AircraftServices>().Mist;
        check(mist && Demister.GetDemisters().Contains(mist) && mist.m_forceField.endRange == 35, "Bound wisp registers a larger native 35 metre mist-clearing volume");
        Vector3 localCentre = plane.transform.InverseTransformPoint(mist.transform.position);
        check(Mathf.Abs(localCentre.z - 10.6f) < .05f,
            "Wisp clearing centre is about 20 feet beyond the nose, while visible wisp stays aboard");
        var particleMist = UnityEngine.Object.FindFirstObjectByType<ParticleMist>();
        check(particleMist, "Native particle mist system is available for clearing checks");
        var inside = AccessTools.Method(typeof(ParticleMist), "InsideDemister");
        check((bool)inside.Invoke(particleMist, new object[] { plane.transform.TransformPoint(new Vector3(0, 3.6f, 40)) }),
            "Native mist system clears well ahead of the plane before it arrives");
        check(!(bool)inside.Invoke(particleMist, new object[] { plane.transform.TransformPoint(new Vector3(0, 3.6f, -40)) }),
            "Expanded mist clearing favours the front rather than the rear");
        Quaternion previousRotation = plane.transform.rotation;
        plane.transform.rotation = plane.Body.rotation = Quaternion.Euler(0, 90, 0);
        check((bool)inside.Invoke(particleMist, new object[] { plane.transform.TransformPoint(new Vector3(0, 3.6f, 40)) }),
            "Forward mist clearing turns with the aircraft");
        plane.transform.rotation = plane.Body.rotation = previousRotation;
        Add(cargo.Container, "Coal", 3);
        var coal = cargo.Container.GetInventory().GetAllItems().Single(i => i.m_shared.m_name == "$item_coal");
        coal.m_gridPos = new Vector2i(4, 5);
        AccessTools.Method(typeof(Container), "Save").Invoke(cargo.Container, null);
        Move(player, plane.Seats[0].AttachPoint.position);
        plane.RequestSeat(0);
        yield return new WaitForSeconds(.2f);
        check(plane.TryUnstuck().StartsWith("Park"), "Unstuck refuses an occupied aircraft");
        player.StopDoodadControl();
        Move(player, plane.transform.position + Vector3.right * 20);
        plane.Body.isKinematic = true;
        yield return new WaitForSeconds(1);
        Vector3 before = plane.transform.position;
        var obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstruction.layer = LayerMask.NameToLayer("static_solid");
        obstruction.transform.position = before + Vector3.up * 6;
        obstruction.transform.localScale = new Vector3(40, 12, 40);
        Physics.SyncTransforms();
        check(plane.TryUnstuck().StartsWith("No clear") && plane.transform.position == before,
            "Unstuck refuses to move the aircraft into an overhead obstruction");
        UnityEngine.Object.Destroy(obstruction);
        yield return null;
        Physics.SyncTransforms();
        check(plane.TryUnstuck().StartsWith("Aircraft lifted") && plane.transform.position.y >= before.y + 2.5f, "Unstuck lifts and levels an empty parked aircraft");
        check(plane.TryUnstuck().StartsWith("Wait"), "Unstuck has a repeat-use cooldown");
        // Park at terrain height, unload the whole sector, then stream it twice.
        Vector3 parked = new Vector3(start.x, ZoneSystem.instance.GetGroundHeight(start) + 3, start.z);
        plane.transform.position = plane.Body.position = parked;
        plane.GetComponent<ZSyncTransform>().SyncNow();
        plane.GetComponent<CargoHold>().SyncCargo();
        view.GetZDO().Set("skyfarer.featureTest", true);
        var id = view.GetZDO().m_uid;
        for (int cycle = 0; cycle < 2; cycle++)
        {
            Move(player, parked + new Vector3(1500, 150, 0));
            deadline = Time.time + 20;
            while (ZNetScene.instance.FindInstance(id) && Time.time < deadline) yield return null;
            check(!ZNetScene.instance.FindInstance(id), "Leaving the area unloads the aircraft instance, cycle " + cycle);
            var data = ZDOMan.instance.GetZDO(id);
            check(data != null && Vector3.Distance(data.GetPosition(), parked) < 5, "Unloaded aircraft remains saved at its parked location, cycle " + cycle);
            Move(player, parked + Vector3.right * 20 + Vector3.up * 5);
            deadline = Time.time + 25;
            while (!ZNetScene.instance.FindInstance(id) && Time.time < deadline) yield return null;
            plane = ZNetScene.instance.FindInstance(id).GetComponent<Aircraft>();
            plane.Body.isKinematic = true;
            yield return CheckCargo(plane, check);
            parked = plane.transform.position;
        }
        // Exercise actual buoyancy and reverse over generated ocean terrain.
        Vector3 ocean = parked;
        bool foundOcean = false;
        for (int i = 1; i <= 80; i++)
        {
            var candidate = parked + new Vector3(i * 80, 0, i * 37);
            if (WorldGenerator.instance.GetHeight(candidate) < 15) { ocean = candidate; foundOcean = true; break; }
        }
        check(foundOcean, "Located an ocean test site");
        ocean.y = 30.45f;
        plane.transform.position = plane.Body.position = ocean;
        plane.transform.rotation = plane.Body.rotation = Quaternion.identity;
        plane.GetComponent<ZSyncTransform>().SyncNow();
        plane.GetComponent<CargoHold>().SyncCargo();
        Move(player, ocean + Vector3.up * 3);
        deadline = Time.time + 35;
        while (!ZNetScene.instance.IsAreaReady(ocean) && Time.time < deadline) yield return null;
        check(ZNetScene.instance.IsAreaReady(ocean), "Ocean terrain and objects are ready before water physics test");
        Move(player, plane.Seats[0].AttachPoint.position);
        plane.RequestSeat(0);
        yield return new WaitForSeconds(.3f);
        plane.Body.isKinematic = false;
        plane.Body.linearVelocity = Vector3.zero;
        Vector3 reverseStart = plane.transform.position;
        yield return ControlSmoke.Drive(plane, Vector3.zero, false, false, 4, false, 0, true);
        Plugin.Log.LogInfo($"WATER_REVERSE start={reverseStart} end={plane.transform.position} velocity={plane.Body.linearVelocity} pilot={plane.Occupant(0)} attached={player.IsAttached()} frozen={plane.StreamingSuspended} kinematic={plane.Body.isKinematic}");
        check(plane.transform.position.z < reverseStart.z - 5 && plane.Gear.GroundedCount == 0,
            "R reverses a floating aircraft on actual water at zero throttle");
        check(Mathf.Abs(plane.transform.position.y - 30) < 3, "Aircraft remains afloat while reversing");
        yield return ControlSmoke.Drive(plane, Vector3.zero, false, false, 2, true, 0, true);
        check(Vector3.ProjectOnPlane(plane.Body.linearVelocity, Vector3.up).magnitude < .5f,
            "Brakes stop water reverse");
        player.StopDoodadControl();
        Move(player, plane.transform.position + Vector3.right * 20);
        plane.Body.isKinematic = true;
        yield return new WaitForSeconds(1);
        ZNet.instance.Save(true);
        check(true, "World save completed with upgraded cargo and a bound wisp");
    }
    private static IEnumerator CheckCargo(Aircraft plane, Action<bool, string> check)
    {
        float deadline = Time.time + 12;
        var hold = plane.GetComponent<CargoHold>();
        while ((!hold.Cargo || hold.Cargo.Container.GetInventory().CountItems("$item_coal") != 3) && Time.time < deadline) yield return null;
        var inventory = hold.Cargo.Container.GetInventory();
        check(inventory.GetHeight() == 6 && inventory.GetAllItems().Any(i => i.m_shared.m_name == "$item_coal" && i.m_gridPos == new Vector2i(4, 5)), "Expanded last-row cargo survives streaming/reload without loss");
        deadline = Time.time + 3;
        while (!plane.GetComponent<AircraftServices>().Mist && Time.time < deadline) yield return null;
        var mist = plane.GetComponent<AircraftServices>().Mist;
        check(mist && mist.m_forceField.endRange == 35 &&
            Mathf.Abs(plane.transform.InverseTransformPoint(mist.transform.position).z - 10.6f) < .05f,
            "Saved wisp returns with the expanded forward clearing area");
    }
}
