using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Wise.Skyfarer;

// Opt-in, synchronous regression fixtures in the disposable smoke world.
// Peer IDs are simulated here: this is not a multi-client network test.
internal static class OwnershipSmoke
{
    internal static void Run(Aircraft plane, Player player, Action<bool, string> check)
    {
        var state = Traverse.Create(plane);
        var zdo = plane.GetComponent<ZNetView>().GetZDO();
        var playerZdo = player.GetComponent<ZNetView>().GetZDO();
        long owner = zdo.GetOwner(), playerOwner = playerZdo.GetOwner(), id = player.GetPlayerID();
        long peer = owner + 12345;
        Vector3 position = plane.Body.position, playerPosition = player.transform.position;
        Quaternion rotation = plane.Body.rotation;
        bool kinematic = plane.Body.isKinematic;
        var heartbeat = state.Field("lastHeartbeat").GetValue<float[]>();
        void Call(string method, params object[] args) => AccessTools.Method(typeof(Aircraft), method).Invoke(plane, args);
        void Audit()
        {
            state.Field("auditTime").SetValue(0f);
            plane.PhysicsStep(Time.fixedDeltaTime);
        }
        try
        {
            var updateOwner = AccessTools.Method(typeof(Ship), "UpdateOwner");
            check(Harmony.GetPatchInfo(updateOwner).Prefixes.Any(p => p.owner == Plugin.Guid &&
                p.PatchMethod.DeclaringType == typeof(AircraftOwnerPatch)), "Aircraft owner guard is installed on Ship.UpdateOwner");
            var prefix = AccessTools.Method(typeof(AircraftOwnerPatch), "Prefix");
            check(!(bool)prefix.Invoke(null, new object[] { plane.GetComponent<Ship>() }),
                "Vanilla owner reassignment is suppressed on aircraft");
            check((bool)prefix.Invoke(null, new object[] { Jotunn.Managers.PrefabManager.Instance.GetPrefab("Karve").GetComponent<Ship>() }),
                "Ordinary Karves retain vanilla ownership logic");
            updateOwner.Invoke(plane.GetComponent<Ship>(), null);
            check(zdo.GetOwner() == owner, "Aircraft owner stays unchanged when vanilla UpdateOwner executes");

            player.transform.position = plane.Seats[0].AttachPoint.position + Vector3.right * 10;
            Call("RPC_SeatRequest", playerOwner, id, 0, 101);
            check(plane.Occupant(0) == 0, "Initial boarding still rejects players more than four metres away");
            player.transform.position = plane.Seats[0].AttachPoint.position;
            Call("RPC_SeatRequest", peer, id, 0, 102);
            check(plane.Occupant(0) == 0 && zdo.GetOwner() == owner, "Unauthenticated boarding cannot claim the pilot seat or aircraft");

            // Change only the test player's authenticated peer identity, without
            // yielding a frame or allowing other game systems to observe it.
            playerZdo.SetOwner(peer);
            player.transform.position = plane.Seats[1].AttachPoint.position;
            Call("RPC_SeatRequest", peer, id, 1, 103);
            check(plane.Occupant(1) == id && zdo.GetOwner() == owner, "Passenger boarding reserves the seat without taking aircraft ownership");
            Call("RPC_SeatRelease", peer, id, 1);
            plane.Body.isKinematic = false;
            plane.transform.position = plane.Body.position = position + Vector3.forward * .5f;
            plane.transform.rotation = plane.Body.rotation = Quaternion.Euler(8, 23, 17);
            plane.Body.linearVelocity = new Vector3(3, 4, 19);
            plane.Body.angularVelocity = new Vector3(.1f, .2f, .3f);
            Physics.SyncTransforms();
            player.transform.position = plane.Seats[0].AttachPoint.position;
            Call("RPC_SeatRequest", peer, id, 0, 104);
            check(plane.Occupant(0) == id && zdo.GetOwner() == peer, "Authenticated pilot boarding transfers aircraft ownership to the pilot peer");
            check(Vector3.Distance(zdo.GetPosition(), plane.Body.position) < .001f &&
                Quaternion.Angle(zdo.GetRotation(), plane.Body.rotation) < .01f &&
                Vector3.Distance(zdo.GetVec3(ZDOVars.s_bodyVelHash, Vector3.zero), plane.Body.linearVelocity) < .001f &&
                Vector3.Distance(zdo.GetVec3(ZDOVars.s_bodyAVelHash, Vector3.zero), plane.Body.angularVelocity) < .001f,
                "Ownership handoff publishes the latest position, rotation and both velocities");
            zdo.SetOwner(owner);
            playerZdo.SetOwner(playerOwner);
            plane.Body.isKinematic = true;
            plane.PhysicsStep(Time.fixedDeltaTime);

            state.Field("pendingOwner").SetValue(peer);
            state.Field("pendingRequest").SetValue(105);
            state.Field("pendingSeat").SetValue(0);
            state.Field("pendingUntil").SetValue(Time.time + 4);
            player.transform.position = plane.Seats[0].AttachPoint.position + Vector3.right * 30;
            Call("RPC_SeatReply", owner, 0, 105, true, .42f);
            check(!player.IsAttached(), "Seat replies from a peer other than the recorded grantor are ignored");
            Call("RPC_SeatReply", peer, 0, 104, true, .42f);
            check(!player.IsAttached() && plane.Occupant(0) == id, "Stale seat replies cannot attach or release a newer reservation");
            Call("RPC_SeatReply", peer, 0, 105, true, .42f);
            check(player.IsAttached() && ReferenceEquals(player.GetDoodadController(), plane.Seats[0]),
                "Previous owner's matching grant attaches after ownership changes, despite movement during the reply");
            player.StopDoodadControl();

            for (int i = 0; i < 5; i++)
            {
                zdo.Set(Aircraft.SeatKeys[i], id);
                player.transform.position = plane.Seats[i].AttachPoint.position + Vector3.right * 50;
                heartbeat[i] = Time.time - 7;
                Call("RPC_Heartbeat", playerOwner, id, i);
                Audit();
                check(plane.Occupant(i) == id && Time.time - heartbeat[i] < .01f,
                    "Authenticated heartbeat keeps seat " + i + " occupied with 50 metres of position lag");
                heartbeat[i] = Time.time - 7;
                Call("RPC_Heartbeat", peer, id, i);
                Audit();
                check(plane.Occupant(i) == 0, "Seat " + i + " expires after missing heartbeats; spoofed heartbeats cannot renew it");
            }
            zdo.Set(Aircraft.SeatKeys[1], id + 123456);
            heartbeat[1] = Time.time;
            Audit();
            check(plane.Occupant(1) != 0, "Temporarily unstreamed seated player gets heartbeat grace after handoff");
            heartbeat[1] = Time.time - 7;
            Audit();
            check(plane.Occupant(1) == 0, "Disconnected or missing player reservation still expires");
        }
        finally
        {
            zdo.SetOwner(owner);
            playerZdo.SetOwner(playerOwner);
            player.StopDoodadControl();
            for (int i = 0; i < 5; i++) zdo.Set(Aircraft.SeatKeys[i], 0L);
            state.Field("pendingSeat").SetValue(-1);
            plane.Body.isKinematic = false;
            plane.transform.position = plane.Body.position = position;
            plane.transform.rotation = plane.Body.rotation = rotation;
            plane.Body.linearVelocity = plane.Body.angularVelocity = Vector3.zero;
            plane.Body.isKinematic = kinematic;
            player.transform.position = player.GetComponent<Rigidbody>().position = playerPosition;
            Physics.SyncTransforms();
            plane.GetComponent<ZSyncTransform>().SyncNow();
        }
    }
}
