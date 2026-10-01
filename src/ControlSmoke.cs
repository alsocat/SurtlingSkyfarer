using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace Wise.Skyfarer;

// Runs only from the opt-in smoke harness in its isolated world.
internal static class ControlSmoke
{
    internal static IEnumerator Run(Aircraft plane, Player player, Action<bool, string> check)
    {
        var controller = player.GetComponent<PlayerController>();
        controller.enabled = false;
        plane.SmokeDrive = false;
        plane.Body.isKinematic = true;
        player.StopDoodadControl();
        player.transform.position = plane.Seats[0].AttachPoint.position;
        plane.RequestSeat(0);
        yield return new WaitForSeconds(.2f);

        yield return Drive(plane, Vector3.zero, false, true, 3.2f);
        check(plane.Throttle == 0, "Ctrl throttle-down reaches and clamps at zero");
        yield return Drive(plane, Vector3.zero, true, false, 3.2f);
        check(plane.Throttle == 1, "Shift throttle-up reaches full power without W/S");
        yield return Drive(plane, Vector3.zero, false, true, .85f);
        yield return Drive(plane, Vector3.zero, false, false, .15f);
        float held = plane.Throttle;
        check(held > .55f && held < .8f, "Ctrl selects a partial throttle: " + held.ToString("P1"));
        yield return Drive(plane, Vector3.zero, false, false, 2);
        check(Mathf.Abs(plane.Throttle - held) < .001f, "Releasing throttle keys holds the selected setting");
        yield return Drive(plane, Vector3.zero, true, true, .4f);
        check(Mathf.Abs(plane.Throttle - held) < .001f, "Shift and Ctrl together leave throttle unchanged");

        player.SetControls(Vector3.zero, false, false, false, false, false, false, false, true, false, false);
        check(!Traverse.Create(player).Field("m_crouchToggled").GetValue<bool>(), "Ctrl at the helm does not toggle character crouching");
        player.SetControls(Vector3.zero, true, true, true, true, false, false, false, true, false, true, true);
        check(ReferenceEquals(player.GetDoodadController(), plane.Seats[0]) && player.IsAttached(),
            "Attack, secondary attack, dodge, crouch and autorun do not eject the pilot");
        CheckUseAction(check, false);
        yield return KeyboardSmoke.Run(plane, player, check);
        // Exercise the actual physics while driving the normal pilot RPC inputs.
        // Start each pitch-direction check level with safe clearance.
        Vector3 flightStart = plane.transform.position;
        flightStart.y -= Mathf.Max(0, plane.Clearance - 90);
        plane.transform.position = plane.Body.position = flightStart;
        plane.transform.rotation = plane.Body.rotation = Quaternion.identity;
        player.transform.position = plane.Seats[0].AttachPoint.position;
        Physics.SyncTransforms();
        plane.Body.isKinematic = false;
        plane.Body.linearVelocity = Vector3.ProjectOnPlane(plane.transform.forward, Vector3.up).normalized * (held * 22);
        plane.Body.angularVelocity = Vector3.zero;
        float downStart = plane.transform.position.y;
        yield return Drive(plane, Vector3.forward, false, false, 1);
        Plugin.Log.LogInfo($"PITCH_DOWN startY={downStart} endY={plane.transform.position.y} forward={plane.transform.forward} clearance={plane.Clearance} input={plane.ControlState}");
        check(plane.transform.position.y < downStart - 1 && plane.transform.forward.y < -.04f,
            "W pitches the nose down and descends");
        check(Mathf.Abs(plane.Throttle - held) < .001f, "W changes pitch without changing throttle");
        plane.transform.rotation = plane.Body.rotation = Quaternion.identity;
        plane.Body.linearVelocity = Vector3.forward * (held * 22);
        Physics.SyncTransforms();
        float upStart = plane.transform.position.y;
        yield return Drive(plane, Vector3.back, false, false, 1);
        Plugin.Log.LogInfo($"PITCH_UP startY={upStart} endY={plane.transform.position.y} forward={plane.transform.forward} clearance={plane.Clearance} input={plane.ControlState}");
        check(plane.transform.position.y > upStart + 1 && plane.transform.forward.y > .04f,
            "S pulls the nose up and climbs");
        check(Mathf.Abs(plane.Throttle - held) < .001f, "S changes pitch without changing throttle");

        yield return AcrobaticSmoke.Run(plane, player, check);

        yield return Drive(plane, new Vector3(.4f, 0, -.5f), false, false, .2f);
        yield return new WaitForSeconds(2);
        check(Mathf.Abs(plane.Throttle - held) < .001f && plane.ControlState.y == 0 && plane.ControlState.z == 0 && plane.Rudder == 0,
            "Stale pilot input centers steering and pitch while holding throttle");

        plane.Body.isKinematic = true;
        var view = plane.GetComponent<ZNetView>();
        long owner = view.GetZDO().GetOwner();
        view.GetZDO().SetOwner(owner + 1);
        plane.PhysicsStep(Time.fixedDeltaTime);
        view.GetZDO().SetOwner(owner);
        plane.PhysicsStep(Time.fixedDeltaTime);
        check(Mathf.Abs(plane.Throttle - held) < .001f, "Ownership reacquisition restores the stored throttle");
        plane.Body.isKinematic = false;

        yield return Drive(plane, new Vector3(.5f, 0, -.5f), false, false, .2f);
        player.SetControls(Vector3.zero, false, false, false, false, false, false, true, false, false, false);
        check(plane.Occupant(0) == 0 && plane.ControlState.y == 0 && plane.ControlState.z == 0 && plane.Rudder == 0,
            "Space leaves the pilot seat and releases pitch, roll and rudder");
        player.transform.position = plane.Seats[1].AttachPoint.position;
        plane.RequestSeat(1);
        yield return new WaitForSeconds(3);
        check(Mathf.Abs(plane.Throttle - held) < .001f && plane.Body.linearVelocity.magnitude > 8,
            "Aircraft keeps flying at the set throttle with an empty pilot seat");
        check(plane.Occupant(1) == player.GetPlayerID() && player.IsAttached(), "Former pilot can ride as a passenger during powered flight");
        CheckUseAction(check, true);
        // The preceding stale-input check leaves the plane pitched and banked.
        // Start this separate maneuver level and measure inversion, rather than
        // assuming a timed 170-degree roll from an arbitrary attitude is inverted.
        Plugin.Log.LogInfo($"PASSENGER_ROLL_START rotation={plane.Body.rotation.eulerAngles} attached={player.IsAttached()}");
        plane.transform.rotation = plane.Body.rotation = Quaternion.identity;
        plane.Body.linearVelocity = Vector3.forward * (held * 22);
        plane.Body.angularVelocity = Vector3.zero;
        player.transform.SetPositionAndRotation(plane.Seats[1].AttachPoint.position, plane.Seats[1].AttachPoint.rotation);
        Physics.SyncTransforms();
        plane.SmokeDrive = true;
        plane.SmokeInput = new Vector3(held, 1, 0);
        float inversionDeadline = Time.time + 5;
        while (plane.transform.up.y > -.98f && Time.time < inversionDeadline)
            yield return new WaitForFixedUpdate();
        plane.SmokeDrive = false;
        yield return new WaitForSeconds(.2f);
        Plugin.Log.LogInfo($"PASSENGER_INVERTED up={plane.transform.up} attached={player.IsAttached()} occupant={plane.Occupant(1)} player={player.GetPlayerID()} seatAngle={Quaternion.Angle(player.transform.rotation, plane.Seats[1].AttachPoint.rotation):F2}");
        check(player.IsAttached() && plane.Occupant(1) == player.GetPlayerID() &&
            plane.transform.up.y < -.8f && Quaternion.Angle(player.transform.rotation, plane.Seats[1].AttachPoint.rotation) < 6,
            "Passenger stays attached and aligned during inverted flight");
        yield return Drive(plane, Vector3.forward, true, false, .5f);
        check(Mathf.Abs(plane.Throttle - held) < .001f && plane.ControlState.z == 0 && plane.Rudder == 0,
            "Passenger input cannot change throttle or pitch");

        player.StopDoodadControl();
        player.transform.position = plane.Seats[0].AttachPoint.position;
        plane.RequestSeat(0);
        yield return new WaitForSeconds(.2f);
        yield return Drive(plane, Vector3.zero, false, false, .3f);
        check(Mathf.Abs(plane.Throttle - held) < .001f, "Reseating at the helm preserves the existing throttle");
        yield return Drive(plane, Vector3.zero, false, false, .2f, true);
        check(plane.Gear.Braking && Mathf.Abs(plane.Throttle - held) < .001f,
            "Right mouse applies wheel brakes without changing pitch or throttle");
        yield return Drive(plane, Vector3.zero, false, true, 2.5f);
        check(plane.Throttle == 0, "Ctrl can stop the engine after reboarding");
        player.StopDoodadControl();
        yield return new WaitForSeconds(.2f);
        check(plane.Throttle == 0, "Zero throttle remains zero after leaving the seat");
        yield return EmptyAircraftSmoke.Run(plane, player, check);
        controller.enabled = true;
    }

    private static void CheckUseAction(Action<bool, string> check, bool available)
    {
        // Drive the game's real Use state so this checks both Harmony targets,
        // rather than merely observing a false result with no key pressed.
        var button = ZInput.instance.GetButtonDef("Use");
        string[] names = { "m_heldDynamic", "m_heldFixed", "m_pressedDynamic", "m_pressedFixed" };
        foreach (string name in names) Traverse.Create(button).Field(name).SetValue(true);
        try
        {
            check(ZInput.GetButton("Use") == available && ZInput.GetButtonDown("Use") == available,
                available ? "E / Use remains available outside the pilot seat" : "E / Use is suppressed while piloting, including held interaction");
        }
        finally { button.ResetState(); }
    }

    internal static IEnumerator Drive(Aircraft plane, Vector3 move, bool up, bool down, float duration, bool brake = false, float rudder = 0, bool reverse = false)
    {
        float until = Time.time + duration;
        while (Time.time < until)
        {
            plane.LocalInput(move, up, down, brake, rudder, reverse);
            yield return new WaitForFixedUpdate();
        }
    }
}
