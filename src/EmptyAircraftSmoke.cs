using System;
using System.Collections;
using UnityEngine;

namespace Wise.Skyfarer;

internal static class EmptyAircraftSmoke
{
    internal static IEnumerator Run(Aircraft plane, Player player, Action<bool, string> check)
    {
        plane.Body.isKinematic = true;
        plane.transform.rotation = plane.Body.rotation = Quaternion.identity;
        var playerBody = player.GetComponent<Rigidbody>();
        player.transform.position = playerBody.position = plane.Seats[0].AttachPoint.position;
        Physics.SyncTransforms();
        plane.RequestSeat(0);
        yield return new WaitForSeconds(.2f);
        yield return ControlSmoke.Drive(plane, Vector3.zero, true, false, 1.6f);
        float power = plane.Throttle;
        check(power > .5f, "Empty-aircraft test starts with a running engine and seated pilot");
        player.StopDoodadControl();
        // Keep a real, unseated player inside the native onboard trigger long
        // enough for Unity's trigger callbacks, without falling off the test hull.
        float until = Time.time + 1;
        while (Time.time < until)
        {
            player.transform.position = playerBody.position = plane.transform.TransformPoint(new Vector3(0, 1, 0));
            playerBody.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
        }
        check(!player.IsAttached() && plane.Occupant(0) == 0 && plane.GetComponent<Ship>().IsPlayerInBoat(player),
            "Unseated player standing on deck is counted by the native onboard trigger");
        check(plane.HasRiders() && Mathf.Abs(plane.Throttle - power) < .001f,
            "Throttle stays set while the last rider stands on deck");

        Vector3 start = plane.Body.position;
        player.transform.position = playerBody.position = start + new Vector3(35, 0, 0);
        playerBody.linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
        player.GetComponent<ZSyncTransform>().SyncNow();
        ZNet.instance.SetReferencePosition(player.transform.position);
        plane.Body.isKinematic = false;
        plane.Body.linearVelocity = Vector3.forward * 18;
        plane.Body.angularVelocity = Vector3.zero;
        yield return new WaitForSeconds(3);
        check(!plane.HasRiders() && plane.Throttle == 0 &&
            plane.GetComponent<ZNetView>().GetZDO().GetFloat("skyfarer.throttle".GetStableHashCode()) == 0,
            "Last rider leaving cuts the engine and stores zero throttle");
        check(Vector3.ProjectOnPlane(plane.Body.linearVelocity, Vector3.up).magnitude < 1 && plane.Body.position.y < start.y - 2,
            "An abandoned airborne plane slows down and descends instead of flying away");

        plane.Body.isKinematic = true;
        player.transform.position = playerBody.position = plane.Seats[0].AttachPoint.position;
        Physics.SyncTransforms();
        player.GetComponent<ZSyncTransform>().SyncNow();
        ZNet.instance.SetReferencePosition(player.transform.position);
        plane.RequestSeat(0);
        yield return new WaitForSeconds(.2f);
        yield return ControlSmoke.Drive(plane, Vector3.zero, false, false, .3f);
        check(player.IsAttached() && plane.Throttle == 0, "Reboarding an abandoned aircraft leaves its engine off");
        player.StopDoodadControl();
    }
}
