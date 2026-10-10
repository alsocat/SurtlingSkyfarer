using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wise.Skyfarer;

// Opt-in developer test. Runs only with SKYFARER_SMOKE=1 and an explicit test directory.
public class RuntimeSmoke : MonoBehaviour
{
    private string output;
    private bool completed;
    private IEnumerator Start()
    {
        output = Environment.GetEnvironmentVariable("SKYFARER_TEST_OUTPUT");
        string saves = Environment.GetEnvironmentVariable("SKYFARER_TEST_SAVES");
        if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(saves))
        {
            Plugin.Log.LogError("Smoke test requires explicit output and test-save directories.");
            yield break;
        }
        Invoke(nameof(Timeout), 360);
        Check(AircraftFactory.Prefab.GetComponent<Aircraft>().Seats.Length == 5, "Five seats in prefab");
        Check(AircraftFactory.Prefab.GetComponentsInChildren<SkySeat>(true).Count(s => s.Index > 0) == 4, "Exactly four passenger seats");
        Check(AircraftFactory.Prefab.GetComponent<ZNetView>().m_persistent, "Persistent network object");
        Check(AircraftFactory.Prefab.GetComponent<LandingGear>().Wheels.Length == 4, "Four wagon wheels with suspension");
        yield return new WaitForSecondsRealtime(3);
        Utils.SetSaveDataPath(saves);
        SaveSystem.ClearWorldListCache(true);
        var world = Directory.Exists(Path.Combine(saves, "worlds_local", "SkyfarerSmoke")) || File.Exists(Path.Combine(saves, "worlds_local", "SkyfarerSmoke.fwl"))
            ? World.GetCreateWorld("SkyfarerSmoke", FileHelpers.FileSource.Local)
            : new World("SkyfarerSmoke", "skyfarer01") { m_fileSource = FileHelpers.FileSource.Local, m_needsDB = false };
        var profile = new PlayerProfile("SkyfarerSmoke", FileHelpers.FileSource.Local);
        profile.SetName("Skyfarer Test Pilot");
        profile.m_firstSpawn = false;
        profile.Save();
        Game.SetProfile("SkyfarerSmoke", FileHelpers.FileSource.Local);
        ZNet.SetServer(true, false, false, "Skyfarer local test", "", world);
        ZNet.ResetServerHost();
        SystemResourceManager.FastLoadScene(FejdStartup.instance.m_mainScene);
        while (!ZNetScene.instance || !Player.m_localPlayer) yield return null;
        if (Player.m_localPlayer.InIntro()) Game.instance.SkipIntro();
        yield return new WaitForSecondsRealtime(2);
        while (!Player.m_localPlayer || Player.m_localPlayer.InIntro() || Player.m_localPlayer.IsTeleporting()) yield return null;
        var player = Player.m_localPlayer;
        player.SetGodMode(true);
        // A headless renderer may cull the wake-up animation. InventoryGui
        // intentionally closes during cutscenes, so finish spawning before UI tests.
        player.GetComponentInChildren<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;
        float wakeDeadline = Time.time + 15;
        while (player.InCutscene() && Time.time < wakeDeadline) yield return null;
        Check(!player.InCutscene(), "Test character finishes its spawn cutscene before interaction checks");
        if (Environment.GetEnvironmentVariable("SKYFARER_CONTROL_RELOAD") == "1")
        {
            // The landing rollout can end outside the spawn area's streamed
            // objects. Locate the persisted ZDO first, then load its actual area.
            var persisted = new System.Collections.Generic.List<ZDO>();
            int searchIndex = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(AircraftFactory.PrefabName, persisted, ref searchIndex))
                yield return null;
            Check(persisted.Count > 0, "Saved aircraft exists in the chunked world database");
            var data = persisted.FirstOrDefault(z => Mathf.Abs(z.GetFloat("skyfarer.throttle".GetStableHashCode()) - .57f) < .001f);
            Check(data != null, "World database stores the aircraft's 57 percent throttle");
            Plugin.Log.LogInfo($"RELOAD_LOCATE spawn={player.transform.position} aircraft={data.GetPosition()}");
            var reloadBody = player.GetComponent<Rigidbody>();
            player.transform.position = reloadBody.position = data.GetPosition() + new Vector3(30, 3, 0);
            reloadBody.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            player.GetComponent<ZSyncTransform>().SyncNow();
            ZNet.instance.SetReferencePosition(player.transform.position);
            float loadDeadline = Time.time + 20;
            while (!FindObjectsByType<Aircraft>(FindObjectsSortMode.None).Any(p => p.IsValid) && Time.time < loadDeadline)
                yield return null;
            var saved = FindObjectsByType<Aircraft>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsValid);
            Check(saved && saved.IsValid, "Saved aircraft restored after streaming its area");
            Check(saved && Mathf.Abs(saved.Throttle - .57f) < .001f, "Saved partial throttle restored after restarting the game");
            Check(saved.Occupant(0) == 0, "Reloaded aircraft retains an empty pilot seat");
            yield return CargoSmoke.Reload(saved, Check);
            if (Environment.GetEnvironmentVariable("SKYFARER_SETTINGS_RELOAD") == "1")
            {
                Check(SkyfarerSettings.TopSpeed == 31, "Restart loads 31 m/s from the config beside the DLL");
                var costs = saved.GetComponent<Piece>().m_resources;
                Check(costs.Length == 2 && costs.Any(r => r.m_resItem.name == "Wood" && r.m_amount == 12)
                    && costs.Any(r => r.m_resItem.name == "Stone" && r.m_amount == 3),
                    "Restart applies edited material recipe to a saved aircraft");
            }
            // Keep the moving aircraft streamed while waiting for ownership and
            // its six-second seat lease. A higher configured speed can carry it
            // beyond the original, stationary test observer's active area.
            float cutoffDeadline = Time.time + 20;
            float diagnosticTime = 0;
            while (saved && saved.IsValid && (saved.HasRiders() || saved.Throttle != 0) && Time.time < cutoffDeadline)
            {
                player.transform.position = reloadBody.position = saved.transform.position + new Vector3(30, 3, 0);
                reloadBody.linearVelocity = Vector3.zero;
                Physics.SyncTransforms();
                player.GetComponent<ZSyncTransform>().SyncNow();
                ZNet.instance.SetReferencePosition(player.transform.position);
                if (Time.time >= diagnosticTime)
                {
                    diagnosticTime = Time.time + 1;
                    Plugin.Log.LogInfo($"RELOAD_CUTOFF owner={saved.GetComponent<ZNetView>().IsOwner()} riders={saved.HasRiders()} throttle={saved.Throttle} position={saved.transform.position}");
                }
                yield return null;
            }
            Check(saved && saved.IsValid && !saved.HasRiders() && saved.Throttle == 0,
                "Reloaded aircraft cuts power after its absent passenger lease expires");
            completed = true;
            File.AppendAllText(output, "SKYFARER_SMOKE_PASS\n");
            Application.Quit();
            yield break;
        }
        Vector3 spawn = player.transform.position + new Vector3(0, 80, 8);
        var root = Instantiate(AircraftFactory.Prefab, spawn, Quaternion.identity);
        var plane = root.GetComponent<Aircraft>();
        yield return new WaitForFixedUpdate();
        Check(plane.IsValid, "Spawned aircraft has a live ZDO");
        Check(root.GetComponent<ZNetView>().GetZDO().GetPrefab() == AircraftFactory.PrefabName.GetStableHashCode(), "Prefab ID registered for saving");
        Check(ObjectDB.instance.GetItemPrefab("Hammer").GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces.m_pieces.Contains(AircraftFactory.Prefab), "Crafting recipe registered with the hammer");
        plane.Body.isKinematic = true;
        var recipe = AircraftFactory.Prefab.GetComponent<Piece>().m_resources;
        Check(recipe.Any(r => r.m_resItem.name == "Copper" && r.m_amount == 25) && !recipe.Any(r => r.m_resItem.name == "Iron"),
            "Recipe requires 25 Copper and no Iron");
        yield return CargoSmoke.Run(plane, player, Check);
        yield return SettingsSmoke.Run(plane, player, Check);
        OwnershipSmoke.Run(plane, player, Check);
        player.transform.position = plane.Seats[0].AttachPoint.position;
        plane.RequestSeat(0);
        yield return new WaitForSecondsRealtime(.5f);
        Check(ReferenceEquals(player.GetDoodadController(), plane.Seats[0]) && player.IsAttached(), "Pilot seat request, RPC reply and attachment");
        Check(plane.Occupant(0) == player.GetPlayerID(), "Pilot reservation stored in ZDO");
        player.StopDoodadControl();
        yield return new WaitForSecondsRealtime(.2f);
        Check(plane.Occupant(0) == 0, "Pilot release clears reservation");
        for (int i = 1; i <= 4; i++)
        {
            player.transform.position = plane.Seats[i].AttachPoint.position;
            plane.RequestSeat(i);
            yield return new WaitForSecondsRealtime(.2f);
            Check(ReferenceEquals(player.GetDoodadController(), plane.Seats[i]) && player.IsAttached(), "Passenger seat " + i + " accepts and attaches player");
            player.StopDoodadControl();
            yield return new WaitForSecondsRealtime(.1f);
            Check(plane.Occupant(i) == 0, "Passenger seat " + i + " releases");
        }
        player.transform.position = plane.Seats[0].AttachPoint.position;
        plane.RequestSeat(0);
        yield return new WaitForSecondsRealtime(.3f);
        plane.Body.isKinematic = false;
        plane.Body.linearVelocity = Vector3.forward * 14;
        float startY = plane.transform.position.y;
        plane.SmokeDrive = true;
        yield return FlyPitch(plane, 1, 15, 5);
        Plugin.Log.LogInfo($"FLIGHT_DIAGNOSTIC owner={root.GetComponent<ZNetView>().IsOwner()} kinematic={plane.Body.isKinematic} shipEnabled={root.GetComponent<Ship>().enabled} controllerEnabled={plane.enabled} throttle={plane.Throttle} velocity={plane.Body.linearVelocity} position={plane.transform.position} rotation={plane.transform.eulerAngles} mass={plane.Body.mass} damping={plane.Body.linearDamping} clearance={plane.Clearance}");
        Check(plane && plane.transform.position.y > startY + 5, "Powered flight gains altitude: " + (plane.transform.position.y - startY).ToString("F2") + " m");
        Check(plane.Body.linearVelocity.magnitude > 12 && plane.Body.linearVelocity.magnitude < 30, "Flight speed bounded: " + plane.Body.linearVelocity.magnitude.ToString("F2") + " m/s");
        Check(Vector3.Distance(player.transform.position, plane.Seats[0].AttachPoint.position) < .6f, "Pilot stays attached during moving flight");
        // The original defect only appeared once WearNTear's 30-second grace expired.
        yield return FlyPitch(plane, 1, 0, 40);
        Check(plane && plane.IsValid, "Aircraft survives 45 seconds of continuous airborne flight");
        var cargo = plane.GetComponent<CargoHold>().Cargo;
        Check(cargo && Vector3.Distance(cargo.transform.position, plane.transform.TransformPoint(CargoHold.Offset)) < .5f &&
            cargo.Container.GetInventory().CountItems("$item_wood") == 7, "Cargo follows the aircraft in flight and retains its contents");
        var wear = plane.GetComponent<WearNTear>();
        var view = plane.GetComponent<ZNetView>();
        Check(view.GetZDO().GetFloat(ZDOVars.s_health, wear.m_health) == wear.m_health,
            "Aircraft stays at full health after the building-wear grace period");
        bool damaged = wear.ApplyDamage(100000);
        yield return null;
        Check(!damaged && plane && view.GetZDO().GetFloat(ZDOVars.s_health, wear.m_health) == wear.m_health,
            "Direct lethal environmental damage is rejected");
        var hit = new HitData { m_point = plane.transform.position, m_hitType = HitData.HitType.Impact };
        hit.m_damage.m_blunt = 100000;
        hit.m_damage.m_fire = 100000;
        wear.Damage(hit);
        yield return new WaitForSeconds(.2f);
        Check(plane && view.GetZDO().GetFloat(ZDOVars.s_health, wear.m_health) == wear.m_health,
            "Lethal physical and fire damage through the network RPC is rejected");
        wear.UpdateWear(Time.time + 120);
        yield return null;
        Check(plane && view.GetZDO().GetFloat(ZDOVars.s_health, wear.m_health) == wear.m_health,
            "Unsupported aircraft survives the native building-wear update");
        var ordinary = Instantiate(Jotunn.Managers.PrefabManager.Instance.GetPrefab("wood_floor"),
            plane.transform.position + Vector3.right * 30, Quaternion.identity);
        bool ordinaryDamaged = ordinary.GetComponent<WearNTear>().ApplyDamage(100000);
        yield return null;
        Check(ordinaryDamaged && !ordinary, "Ordinary building pieces still take lethal damage");
        yield return ControlSmoke.Run(plane, player, Check);
        // Check takeoff from a level test runway using the real game's physics.
        plane.Body.isKinematic = true;
        Vector3 runwayStart = spawn + new Vector3(0, 60, 0);
        var runway = GameObject.CreatePrimitive(PrimitiveType.Cube);
        runway.name = "Skyfarer smoke-test runway";
        runway.layer = LayerMask.NameToLayer("static_solid");
        runway.transform.position = runwayStart + new Vector3(0, -2.05f, 200);
        runway.transform.localScale = new Vector3(800, 1, 800);
        plane.transform.SetPositionAndRotation(runwayStart, Quaternion.identity);
        plane.Body.position = runwayStart;
        plane.Body.rotation = Quaternion.identity;
        player.transform.position = plane.Seats[0].AttachPoint.position;
        // This synthetic jump crosses many streamed world sectors. Flush both
        // physics/network positions and the streaming reference before yielding,
        // so scene cleanup cannot unload the player at its previous ZDO position.
        var playerBody = player.GetComponent<Rigidbody>();
        playerBody.position = player.transform.position;
        playerBody.rotation = player.transform.rotation;
        playerBody.linearVelocity = Vector3.zero;
        playerBody.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
        plane.GetComponent<ZSyncTransform>().SyncNow();
        plane.GetComponent<CargoHold>().SyncCargo();
        player.GetComponent<ZSyncTransform>().SyncNow();
        ZNet.instance.SetReferencePosition(player.transform.position);
        plane.RequestSeat(0);
        yield return new WaitForSeconds(.3f);
        Check(player && player == Player.m_localPlayer && player.IsAttached(), "Test pilot remains loaded after transfer to the runway");
        plane.Body.isKinematic = false;
        plane.Body.linearVelocity = Vector3.zero;
        plane.Body.angularVelocity = Vector3.zero;
        plane.SmokeDrive = true;
        plane.SmokeInput = Vector3.zero;
        yield return new WaitForSeconds(2);
        Plugin.Log.LogInfo($"GEAR_SETTLE position={plane.transform.position} velocity={plane.Body.linearVelocity} grounded={plane.Gear.GroundedCount}");
        Check(plane.Gear.GroundedCount == 4, "All four wagon wheels support the stationary aircraft");
        // Reverse through the normal input RPC, with an idle engine setting.
        var testController = player.GetComponent<PlayerController>();
        testController.enabled = false;
        plane.SmokeDrive = false;
        Vector3 reverseStart = plane.Body.position;
        yield return ControlSmoke.Drive(plane, Vector3.zero, false, false, 4, false, 0, true);
        Check(Vector3.Dot(plane.Body.position - reverseStart, plane.transform.forward) < -5 &&
            Vector3.Dot(plane.Body.linearVelocity, plane.transform.forward) < -2 && plane.Gear.GroundedCount >= 2,
            "R backs up on the wagon wheels at zero throttle");
        Check(plane.Throttle == 0, "Reverse does not alter the persistent throttle setting");
        float reverseHeading = plane.transform.eulerAngles.y;
        yield return ControlSmoke.Drive(plane, Vector3.right, false, false, 2, false, 0, true);
        Check(Mathf.Abs(Mathf.DeltaAngle(reverseHeading, plane.transform.eulerAngles.y)) > 4,
            "Front wheels steer while reversing");
        yield return ControlSmoke.Drive(plane, Vector3.zero, false, false, 3, true, 0, true);
        Check(plane.Body.linearVelocity.magnitude < 1 && plane.Gear.Braking,
            "Wheel brakes override reverse thrust");
        yield return ControlSmoke.Drive(plane, Vector3.zero, false, false, 1);
        Check(plane.Body.linearVelocity.magnitude < 1 && plane.Throttle == 0,
            "Releasing reverse at zero throttle leaves the aircraft parked");
        testController.enabled = true;
        plane.SmokeDrive = true;
        plane.SmokeReverse = false;
        plane.SmokeRudder = 0;
        Vector3 taxiStart = plane.transform.position;
        float wheelSpin = plane.Gear.SpinDegrees;
        plane.SmokeInput = new Vector3(.35f, 0, 0);
        yield return new WaitForSeconds(7);
        Plugin.Log.LogInfo($"GEAR_TAXI position={plane.transform.position} velocity={plane.Body.linearVelocity} grounded={plane.Gear.GroundedCount} spin={plane.Gear.SpinDegrees - wheelSpin} throttle={plane.Throttle} torque={plane.Gear.Wheels[0].motorTorque} brake={plane.Gear.Wheels[0].brakeTorque} rpm={plane.Gear.Wheels[0].rpm}");
        Check(Vector3.Distance(taxiStart, plane.transform.position) > 15 && plane.Gear.GroundedCount >= 2,
            "Aircraft taxis on its wheels without taking off");
        Check(plane.Gear.SpinDegrees - wheelSpin > 360, "Wagon wheels rotate while rolling");
        float headingBefore = plane.transform.eulerAngles.y;
        plane.SmokeInput = new Vector3(.35f, .4f, 0);
        yield return new WaitForSeconds(2);
        Check(Mathf.Abs(Mathf.DeltaAngle(headingBefore, plane.transform.eulerAngles.y)) > 4 && plane.Gear.Wheels[0].steerAngle > 0,
            "Front wheels steer the aircraft during taxiing");
        Plugin.Log.LogInfo($"GEAR_STEER headingBefore={headingBefore} headingAfter={plane.transform.eulerAngles.y} position={plane.transform.position}");
        plane.SmokeInput = Vector3.zero;
        yield return new WaitForSeconds(4);
        Plugin.Log.LogInfo($"GEAR_BRAKE speed={plane.Body.linearVelocity.magnitude} grounded={plane.Gear.GroundedCount}");
        Check(plane.Body.linearVelocity.magnitude < 1 && plane.Gear.GroundedCount >= 2, "Wheel brakes stop the aircraft on land");
        float takeoffY = plane.transform.position.y;
        plane.SmokeInput = new Vector3(1, 0, 1);
        yield return FlyPitch(plane, 1, 15, 9);
        Check(plane.transform.position.y > takeoffY + 5 && plane.Gear.GroundedCount == 0,
            "Rolling takeoff from rest: " + (plane.transform.position.y - takeoffY).ToString("F2") + " m gained");
        plane.SmokeInput = new Vector3(.45f, 0, -1);
        Plugin.Log.LogInfo($"GEAR_APPROACH position={plane.transform.position} velocity={plane.Body.linearVelocity} heading={plane.transform.eulerAngles.y} runway={runway.transform.position}");
        float landingDeadline = Time.time + 30;
        while (plane.Gear.GroundedCount < 2 && Time.time < landingDeadline)
        {
            SetPitch(plane, .3f, plane.Clearance < 3 ? 0 : -10);
            yield return new WaitForFixedUpdate();
        }
        Plugin.Log.LogInfo($"GEAR_TOUCHDOWN position={plane.transform.position} velocity={plane.Body.linearVelocity} grounded={plane.Gear.GroundedCount} runway={runway.transform.position}");
        Check(plane.Gear.GroundedCount >= 2, "Aircraft descends and lands on the wagon wheels");
        plane.SmokeInput = Vector3.zero;
        yield return new WaitForSeconds(4);
        Check(plane.Body.linearVelocity.magnitude < 1 && plane.Gear.GroundedCount >= 2, "Landing rollout brakes to a stable stop");
        Check(view.GetZDO().GetFloat(ZDOVars.s_health, wear.m_health) == wear.m_health, "Aircraft remains indestructible after landing");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Skyfarer impact-test wall";
        wall.layer = LayerMask.NameToLayer("static_solid");
        Vector3 crashHeading = Vector3.ProjectOnPlane(plane.transform.forward, Vector3.up).normalized;
        wall.transform.position = plane.transform.position + crashHeading * 12;
        wall.transform.rotation = Quaternion.LookRotation(crashHeading, Vector3.up);
        wall.transform.localScale = new Vector3(60, 100, 2);
        plane.SmokeInput = new Vector3(1, 0, 0);
        plane.Body.linearVelocity = crashHeading * 22;
        Physics.SyncTransforms();
        Plugin.Log.LogInfo($"IMPACT_TEST_SETUP plane={plane.transform.position} wall={wall.transform.position} heading={crashHeading} velocity={plane.Body.linearVelocity}");
        int contactsBefore = plane.SmokeContacts;
        yield return new WaitForSeconds(4);
        Plugin.Log.LogInfo($"IMPACT_TEST_RESULT plane={plane.transform.position} wall={wall.transform.position} velocity={plane.Body.linearVelocity} contacts={plane.SmokeContacts - contactsBefore}");
        Check(plane && plane.SmokeContacts > contactsBefore, "Aircraft physically hits the test wall at flight speed");
        Check(view.GetZDO().GetFloat(ZDOVars.s_health, wear.m_health) == wear.m_health,
            "Aircraft stays at full health after a real collision");
        Destroy(wall);
        player.StopDoodadControl();
        plane.Body.isKinematic = true;
        player.transform.position = plane.Seats[1].AttachPoint.position;
        plane.RequestSeat(1);
        yield return new WaitForSeconds(.2f);
        Check(player.IsAttached() && plane.Occupant(1) == player.GetPlayerID(), "Passenger remains aboard for the saved-throttle check");
        plane.SmokeInput = new Vector3(.57f, 0, 0);
        yield return new WaitForSeconds(.1f);
        plane.SmokeDrive = false;
        Check(Mathf.Abs(plane.Throttle - .57f) < .001f, "Partial throttle retained for the save/reload check");
        var finalCargo = plane.GetComponent<CargoHold>().Cargo;
        Check(finalCargo && finalCargo.Container.GetInventory().CountItems("$item_wood") == 7 &&
            Vector3.Distance(finalCargo.GetComponent<ZNetView>().GetZDO().GetPosition(), plane.transform.TransformPoint(CargoHold.Offset)) < .5f,
            "Cargo contents and saved streaming position stay with the plane through the full flight suite");
        DateTime saveStarted = DateTime.UtcNow;
        ZNet.instance.Save(true);
        string worldPath = Path.Combine(saves, "worlds_local", "SkyfarerSmoke");
        Check(Directory.Exists(worldPath) && Directory.GetFiles(worldPath, "*.db2").Any(p => File.GetLastWriteTimeUtc(p) >= saveStarted.AddSeconds(-1))
            && Directory.GetFiles(worldPath, "*.ok").Length > 0, "World database saved with aircraft (Valheim chunk format)");
        completed = true;
        File.AppendAllText(output, "SKYFARER_SMOKE_PASS\n");
        Plugin.Log.LogInfo("SKYFARER_SMOKE_PASS");
        Application.Quit();
    }
    private static void SetPitch(Aircraft plane, float throttle, float degrees)
    {
        float current = Mathf.Asin(Mathf.Clamp(plane.transform.forward.y, -1, 1)) * Mathf.Rad2Deg;
        plane.SmokeInput = new Vector3(throttle, 0, Mathf.Clamp((degrees - current) * .15f, -1, 1));
    }
    private static IEnumerator FlyPitch(Aircraft plane, float throttle, float degrees, float duration)
    {
        float until = Time.time + duration;
        while (Time.time < until)
        {
            SetPitch(plane, throttle, degrees);
            yield return new WaitForFixedUpdate();
        }
    }
    private void Check(bool condition, string label)
    {
        File.AppendAllText(output, (condition ? "PASS " : "FAIL ") + label + "\n");
        if (!condition) { Plugin.Log.LogError("SMOKE FAIL: " + label); Application.Quit(); throw new InvalidOperationException(label); }
        Plugin.Log.LogInfo("SMOKE PASS: " + label);
    }
    private void Timeout()
    {
        if (!completed) { File.AppendAllText(output, "FAIL Smoke test timeout\n"); Application.Quit(); }
    }
    private void Update()
    {
        Application.runInBackground = true;
        if (Game.instance) Game.Unpause();
    }
}
