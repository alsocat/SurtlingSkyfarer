using UnityEngine;

namespace Wise.Skyfarer;

public partial class Aircraft
{
    private bool suspended, wasKinematic;
    private float unstuckTime = -100;
    internal bool StreamingSuspended => suspended;

    private void RegisterUtilityRpcs()
    {
        view.Register<long>("Skyfarer.Unstuck", RPC_Unstuck);
        view.Register<string>("Skyfarer.UtilityReply", (sender, message) =>
        {
            if (sender == view.GetZDO().GetOwner() && Player.m_localPlayer)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, message);
        });
    }
    // Distant network objects can outlive terrain colliders. Suspend both
    // gravity and contacts until the local zone and its objects are ready.
    internal bool SuspendForStreaming()
    {
        if (!IsValid || !view.IsOwner() || !ZNetScene.instance) return false;
        // Waiting on every ZDO in IsAreaReady can stall a moving aircraft as
        // new entities stream. The terrain zone is the required physics surface.
        if (!ZoneSystem.instance.IsZoneLoaded(ZoneSystem.GetZone(body.position)))
        {
            if (!suspended)
            {
                wasKinematic = body.isKinematic;
                if (!wasKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                body.isKinematic = true;
                suspended = true;
            }
            return true;
        }
        if (suspended) { body.isKinematic = wasKinematic; suspended = false; }
        return false;
    }
    internal void RequestUnstuck()
    {
        if (IsValid && Player.m_localPlayer) view.InvokeRPC("Skyfarer.Unstuck", Player.m_localPlayer.GetPlayerID());
    }
    private void RPC_Unstuck(long sender, long id)
    {
        if (!IsValid || !view.IsOwner()) return;
        var player = AuthenticatedPlayer(sender, id);
        if (!player || Vector3.Distance(player.transform.position, transform.position) > 10 || !ServiceAccess.Allowed(transform.position, id)) return;
        view.InvokeRPC(sender, "Skyfarer.UtilityReply", TryUnstuck());
    }
    internal string TryUnstuck()
    {
        if (!IsValid || !view.IsOwner()) return "Aircraft is unavailable.";
        if (HasRiders() || body.linearVelocity.magnitude > 2 || throttle > .01f) return "Park, stop the engine and have everyone step off first.";
        if (Time.time - unstuckTime < 10) return "Wait a few seconds before trying again.";
        if (!ZNetScene.instance.IsAreaReady(transform.position)) return "Wait for the nearby terrain to load.";
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Quaternion level = Quaternion.LookRotation(forward.sqrMagnitude > .01f ? forward : Vector3.forward, Vector3.up);
        int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "character", "character_net", "character_noenv");
        for (float height = 2.5f; height <= 8; height += .5f)
        {
            Vector3 target = transform.position + Vector3.up * height;
            // Conservative box encloses the entire level hull, wings and wheels.
            bool blocked = false;
            foreach (var hit in Physics.OverlapBox(target + Vector3.up * 1.1f, new Vector3(6.9f, 2.6f, 5.6f), level, mask, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform)) { blocked = true; break; }
            if (blocked) continue;
            body.position = target;
            body.rotation = level;
            transform.SetPositionAndRotation(target, level);
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            Physics.SyncTransforms();
            GetComponent<ZSyncTransform>().SyncNow();
            GetComponent<CargoHold>().SyncCargo();
            unstuckTime = Time.time;
            return "Aircraft lifted and levelled.";
        }
        return "No clear space above the plane within 8 metres. Clear the obstruction and try again.";
    }
}
