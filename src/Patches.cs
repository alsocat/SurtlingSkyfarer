using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;

namespace Wise.Skyfarer;

// A cloned Karve still schedules UpdateOwner every two seconds. Its boat
// passenger list does not reliably include SkySeat pilots; aircraft ownership
// is granted by Aircraft.RPC_SeatRequest instead. Ordinary ships keep vanilla logic.
[HarmonyPatch(typeof(Ship), "UpdateOwner")]
internal static class AircraftOwnerPatch
{
    private static bool Prefix(Ship __instance) => !__instance.GetComponent<Aircraft>();
}

[HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
internal static class FlightPhysicsPatch
{
    private static bool Prefix(Ship __instance, float fixedDeltaTime)
    {
        var plane = __instance.GetComponent<Aircraft>();
        if (!plane) return true;
        return false;
    }
}

// Space/Jump is the only manual exit from the helm. Ctrl controls throttle;
// attacks and dodges must not trigger vanilla's vehicle dismount behavior.
[HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
internal static class PilotControlPatch
{
    private static void Prefix(Player __instance, ref bool crouch, ref bool attack, ref bool attackHold,
        ref bool secondaryAttack, ref bool secondaryAttackHold, ref bool dodge, ref bool autoRun)
    {
        if (!(__instance.GetDoodadController() is SkySeat seat) || seat.Index != 0) return;
        crouch = attack = attackHold = secondaryAttack = secondaryAttackHold = dodge = autoRun = false;
    }
}

// E still boards normally. Once piloting, raw E is rudder and the Use action
// must not interact with nearby objects or release the vehicle controls.
[HarmonyPatch]
internal static class PilotUsePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetButton));
        yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetButtonDown));
        yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetButtonUp));
    }

    private static bool Prefix(string name, ref bool __result)
    {
        if ((name != "Use" && name != "JoyUse" && name != "Hide") || !Player.m_localPlayer ||
            !(Player.m_localPlayer.GetDoodadController() is SkySeat seat) || seat.Index != 0) return true;
        __result = false;
        return false;
    }
}

// Native UpdateAttach already follows the full seat rotation. Boat steering's
// extra world-up rotation would fight it and becomes singular during loops.
[HarmonyPatch(typeof(Player), "UpdateDoodadControls")]
internal static class AircraftSeatRotationPatch
{
    private static bool Prefix(Player __instance, float ___m_maxInteractDistance)
    {
        if (!(__instance.GetDoodadController() is SkySeat seat)) return true;
        if (!seat.IsValid() || Vector3.Distance(seat.GetPosition(), __instance.transform.position) > ___m_maxInteractDistance)
            __instance.StopDoodadControl();
        return false;
    }
}

// Keep collision physics and deliberate hammer removal, but reject all health damage.
// RPC_Damage covers attacks/impacts; ApplyDamage also covers direct environmental wear.
[HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
internal static class AircraftHitImmunityPatch
{
    private static bool Prefix(WearNTear __instance) => !__instance.GetComponent<Aircraft>();
}

[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.ApplyDamage))]
internal static class AircraftWearImmunityPatch
{
    private static bool Prefix(WearNTear __instance, ref bool __result)
    {
        if (!__instance.GetComponent<Aircraft>()) return true;
        __result = false;
        return false;
    }
}
