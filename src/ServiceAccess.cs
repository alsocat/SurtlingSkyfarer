using System;
using HarmonyLib;
using UnityEngine;

namespace Wise.Skyfarer;

internal static class ServiceAccess
{
    private static readonly Func<PrivateArea, long, bool> Permitted =
        AccessTools.MethodDelegate<Func<PrivateArea, long, bool>>(AccessTools.Method(typeof(PrivateArea), "IsPermitted"));

    // Native CheckAccess uses Player.m_localPlayer. An owner RPC may run on a
    // dedicated server, or on a different player's client: check the requester.
    internal static bool Allowed(Vector3 point, long playerId)
    {
        bool protectedArea = false;
        foreach (var area in UnityEngine.Object.FindObjectsByType<PrivateArea>(FindObjectsSortMode.None))
        {
            var view = area.GetComponent<ZNetView>();
            if (!view || !view.IsValid() || !view.GetZDO().GetBool(ZDOVars.s_enabled) ||
                Utils.DistanceXZ(area.transform.position, point) >= area.m_radius) continue;
            protectedArea = true;
            if (area.GetComponent<Piece>().GetCreator() == playerId || Permitted(area, playerId)) return true;
        }
        return !protectedArea;
    }
}
