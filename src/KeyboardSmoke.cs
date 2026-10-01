using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Wise.Skyfarer;

internal static class KeyboardSmoke
{
    // A temporary Unity keyboard exercises the real key reads in SkySeat. It
    // exists only in the isolated smoke world and does not inject desktop input.
    internal static IEnumerator Run(Aircraft plane, Player player, Action<bool, string> check)
    {
        Keyboard original = Keyboard.current;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>("SkyfarerSmokeKeyboard");
        keyboard.MakeCurrent();
        float power = plane.Throttle;
        try
        {
            foreach (var key in new[] { Key.Q, Key.E, Key.R })
            {
                InputState.Change(keyboard, new KeyboardState(key));
                float until = Time.time + .2f;
                while (Time.time < until)
                {
                    plane.Seats[0].ApplyControlls(Vector3.zero, Vector3.forward, false, false, false);
                    yield return new WaitForFixedUpdate();
                }
                if (key == Key.R)
                    check(Traverse.Create(plane).Field("reverse").GetValue<bool>(), "R key requests ground reverse through the pilot controller");
                else
                    check(Mathf.Abs(plane.Rudder - (key == Key.E ? 1 : -1)) < .001f,
                        key + " key reaches the rudder through the pilot controller");
                check(player.IsAttached() && ReferenceEquals(player.GetDoodadController(), plane.Seats[0]),
                    key + " key leaves the pilot seated");
            }
            InputState.Change(keyboard, new KeyboardState(Key.Q, Key.E));
            float bothUntil = Time.time + .2f;
            while (Time.time < bothUntil)
            {
                plane.Seats[0].ApplyControlls(Vector3.zero, Vector3.forward, false, false, false);
                yield return new WaitForFixedUpdate();
            }
            check(plane.Rudder == 0 && Mathf.Abs(plane.Throttle - power) < .001f,
                "Q and E together center the rudder without changing throttle");
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
            if (original != null && original.added) original.MakeCurrent();
        }
        yield return ControlSmoke.Drive(plane, Vector3.zero, false, false, .2f);
    }
}
