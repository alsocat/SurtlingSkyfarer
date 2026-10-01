using System;
using System.Collections;
using UnityEngine;

namespace Wise.Skyfarer;

// Only called by the opt-in isolated-world harness. All maneuvers use the
// authenticated pilot RPC and actual Unity rigidbody simulation.
internal static class AcrobaticSmoke
{
    internal static IEnumerator Run(Aircraft plane, Player player, Action<bool, string> check)
    {
        Vector3 center = plane.Body.position + Vector3.up * 140;
        float power = plane.Throttle;
        yield return Rotations(plane, player, center, Vector3.back, Vector3.left, "S backloops", true, check);
        yield return Rotations(plane, player, center, Vector3.forward, Vector3.right, "W forward loops", true, check);
        yield return Rotations(plane, player, center, Vector3.left, Vector3.forward, "A left rolls", false, check);
        yield return Rotations(plane, player, center, Vector3.right, Vector3.back, "D right rolls", false, check);

        foreach (int direction in new[] { -1, 1 })
        {
            Reset(plane, player, center);
            yield return ControlSmoke.Drive(plane, Vector3.zero, false, false, 1, false, direction);
            float yaw = Mathf.DeltaAngle(0, plane.Body.rotation.eulerAngles.y);
            check(yaw * direction > 15 && Vector3.Dot(plane.transform.up, Vector3.up) > .99f,
                (direction < 0 ? "Q" : "E") + " rudder turns independently without rolling or ejecting the pilot");
        }
        Reset(plane, player, center);
        yield return ControlSmoke.Drive(plane, new Vector3(.7f, 0, -.7f), false, false, 2, false, .4f);
        check(Quaternion.Angle(Quaternion.identity, plane.Body.rotation) > 80 && player.IsAttached(),
            "Combined pitch, roll and rudder can perform a compound maneuver");

        Reset(plane, player, center);
        yield return ControlSmoke.Drive(plane, Vector3.zero, false, false, 1, false, 0, true);
        check(Vector3.Dot(plane.Body.linearVelocity, plane.transform.forward) > 12 && plane.Gear.GroundedCount == 0,
            "Ground reverse does not reverse thrust in flight");
        check(Mathf.Abs(plane.Throttle - power) < .001f, "Aerobatics and reverse input preserve the selected throttle");
        yield return ControlSmoke.Drive(plane, Vector3.zero, false, false, .2f);
    }

    private static IEnumerator Rotations(Aircraft plane, Player player, Vector3 center, Vector3 input,
        Vector3 axis, string label, bool pitch, Action<bool, string> check)
    {
        Reset(plane, player, center);
        Quaternion previous = plane.Body.rotation;
        float total = 0, minY = center.y, maxY = center.y, maxSeatDistance = 0, maxSeatAngle = 0;
        bool inverted = false, vertical = false;
        float deadline = Time.time + 24;
        while (total < 730 && Time.time < deadline)
        {
            plane.LocalInput(input, false, false);
            yield return new WaitForFixedUpdate();
            Quaternion current = plane.Body.rotation;
            Quaternion delta = Quaternion.Inverse(previous) * current;
            delta.ToAngleAxis(out float degrees, out Vector3 stepAxis);
            if (degrees > 180) degrees -= 360;
            if (degrees > .0001f) total += Vector3.Dot(stepAxis, axis) * degrees;
            previous = current;
            minY = Mathf.Min(minY, plane.Body.position.y);
            maxY = Mathf.Max(maxY, plane.Body.position.y);
            inverted |= Vector3.Dot(current * Vector3.up, Vector3.up) < -.95f;
            vertical |= Mathf.Abs((current * Vector3.forward).y) > .98f;
            maxSeatDistance = Mathf.Max(maxSeatDistance, Vector3.Distance(player.transform.position, plane.Seats[0].AttachPoint.position));
            maxSeatAngle = Mathf.Max(maxSeatAngle, Quaternion.Angle(player.transform.rotation, plane.Seats[0].AttachPoint.rotation));
        }
        Plugin.Log.LogInfo($"ACRO {label}: rotation={total:F1} altitudeSpan={maxY-minY:F1} seatDistance={maxSeatDistance:F2} seatAngle={maxSeatAngle:F2}");
        check(total >= 720 && inverted && (!pitch || vertical), label + " complete two continuous rotations through inverted flight");
        check(player.IsAttached() && plane.Occupant(0) == player.GetPlayerID() && maxSeatDistance < .8f && maxSeatAngle < 6,
            label + " keep the pilot attached and aligned with the seat");
        if (pitch) check(maxY - minY > 15, label + " bend the flight path into a loop");
        else check(Vector3.Dot(plane.transform.forward, Vector3.forward) > .99f, label + " do not change heading");

        yield return ControlSmoke.Drive(plane, Vector3.zero, false, false, .2f);
        Quaternion held = plane.Body.rotation;
        yield return ControlSmoke.Drive(plane, Vector3.zero, false, false, .7f);
        check(Quaternion.Angle(held, plane.Body.rotation) < 2, label + " stop rotating when released and hold attitude");
    }

    private static void Reset(Aircraft plane, Player player, Vector3 position)
    {
        plane.transform.SetPositionAndRotation(position, Quaternion.identity);
        plane.Body.position = position;
        plane.Body.rotation = Quaternion.identity;
        plane.Body.linearVelocity = Vector3.forward * (plane.Throttle * 22);
        plane.Body.angularVelocity = Vector3.zero;
        player.transform.SetPositionAndRotation(plane.Seats[0].AttachPoint.position, plane.Seats[0].AttachPoint.rotation);
        Physics.SyncTransforms();
    }
}
