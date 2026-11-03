using UnityEngine;

namespace Wise.Skyfarer;

public partial class Aircraft
{
    private const string VtolKey = "skyfarer.vtol";
    private bool suspended, wasKinematic;
    private float unstuckTime = -100, vtolInputTime;
    private Vector3 vtolInput;
    private int lastVtolToggleFrame = -1;
    internal bool VtolActive => IsValid && SkyfarerSettings.VtolEnabled.Value && view.GetZDO().GetBool(VtolKey);
    internal bool StreamingSuspended => suspended;

    private void RegisterUtilityRpcs()
    {
        view.Register<long>("Skyfarer.Vtol", RPC_Vtol);
        view.Register<long, Vector3>("Skyfarer.VtolInput", RPC_VtolInput);
        view.Register<long>("Skyfarer.Unstuck", RPC_Unstuck);
        view.Register<string>("Skyfarer.UtilityReply", (sender, message) =>
        {
            if (sender == view.GetZDO().GetOwner() && Player.m_localPlayer)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, message);
        });
    }
    internal void ToggleVtol()
    {
        if (!IsValid || !Player.m_localPlayer || lastVtolToggleFrame == Time.frameCount) return;
        lastVtolToggleFrame = Time.frameCount;
        view.InvokeRPC("Skyfarer.Vtol", Player.m_localPlayer.GetPlayerID());
    }
    private void RPC_Vtol(long sender, long id)
    {
        if (!IsValid || !view.IsOwner() || Occupant(0) != id || !AuthenticatedPlayer(sender, id)) return;
        if (!SkyfarerSettings.VtolEnabled.Value) { view.InvokeRPC(sender, "Skyfarer.UtilityReply", "VTOL is disabled in the server config."); return; }
        bool active = !VtolActive;
        view.GetZDO().Set(VtolKey, active);
        throttle = localThrottle = 0;
        view.GetZDO().Set(ThrottleKey, 0f);
        vtolInput = Vector3.zero;
        vtolInputTime = Time.time;
        view.InvokeRPC(sender, "Skyfarer.UtilityReply", active ? "VTOL: Shift/Ctrl ascend/descend, W/S forward/back, A/D turn. V returns to flight." : "Flight mode: Shift increases throttle.");
    }
    private void LocalVtolInput(Vector3 move, bool up, bool down, float yaw)
    {
        if (Time.time < inputTime) return;
        inputTime = Time.time + .05f;
        view.InvokeRPC("Skyfarer.VtolInput", Player.m_localPlayer.GetPlayerID(),
            new Vector3(Mathf.Clamp(move.x + yaw, -1, 1), (up ? 1 : 0) - (down ? 1 : 0), Mathf.Clamp(move.z, -1, 1)));
    }
    private void RPC_VtolInput(long sender, long id, Vector3 input)
    {
        if (!IsValid || !view.IsOwner() || !VtolActive || Occupant(0) != id || !AuthenticatedPlayer(sender, id) ||
            !Finite(input.x) || !Finite(input.y) || !Finite(input.z)) return;
        vtolInput = new Vector3(Mathf.Clamp(input.x, -1, 1), Mathf.Clamp(input.y, -1, 1), Mathf.Clamp(input.z, -1, 1));
        vtolInputTime = Time.time;
    }
    private void StepVtol(float dt)
    {
        if (Time.time - vtolInputTime > 1.5f) vtolInput = Vector3.zero;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
        Vector3 desired = forward * vtolInput.z * 5f + Vector3.up * vtolInput.y * 4f;
        body.AddForce(-Physics.gravity + Vector3.ClampMagnitude((desired - body.linearVelocity) * 3f, 18), ForceMode.Acceleration);
        Quaternion level = Quaternion.LookRotation(forward, Vector3.up);
        body.MoveRotation(Quaternion.AngleAxis(vtolInput.x * 35 * dt, Vector3.up) * Quaternion.Slerp(body.rotation, level, dt * 3));
        body.angularVelocity = Vector3.zero;
        Gear.Step(0, 0, true, false);
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
            view.GetZDO().Set(VtolKey, false);
            Physics.SyncTransforms();
            GetComponent<ZSyncTransform>().SyncNow();
            GetComponent<CargoHold>().SyncCargo();
            unstuckTime = Time.time;
            return "Aircraft lifted and levelled.";
        }
        return "No clear space above the plane within 8 metres. Clear the obstruction and try again.";
    }
}
