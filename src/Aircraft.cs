using System;
using UnityEngine;

namespace Wise.Skyfarer;

public class Aircraft : MonoBehaviour
{
    public SkySeat[] Seats;
    public Transform Propeller;
    public Light EngineLight;
    public LandingGear Gear;
    internal static readonly int[] SeatKeys = { "skyfarer.pilot".GetStableHashCode(), "skyfarer.passenger1".GetStableHashCode(),
        "skyfarer.passenger2".GetStableHashCode(), "skyfarer.passenger3".GetStableHashCode(), "skyfarer.passenger4".GetStableHashCode() };
    private static readonly int ThrottleKey = "skyfarer.throttle".GetStableHashCode();
    private ZNetView view;
    private Rigidbody body;
    private Ship ship;
    private WaterVolume water;
    private readonly float[] lastHeartbeat = new float[5];
    private float throttle, roll, climb, rudder, lastInput, localThrottle, inputTime, heartbeatTime, auditTime, pendingUntil;
    private int pendingSeat = -1, pendingRequest;
    private long pendingOwner;
    private float emptySince = -1;
    private bool wasOwner, brake, reverse;
    private float clearance = 100;
    private readonly RaycastHit[] groundHits = new RaycastHit[24];
    private GUIStyle titleStyle, textStyle;
    internal bool SmokeDrive;
    internal Vector3 SmokeInput = new Vector3(1, 0, 1);
    internal float SmokeRudder;
    internal bool SmokeReverse;
    internal float Rudder => rudder;
    internal int SmokeContacts;
    public bool IsValid => view && view.IsValid();
    internal float Throttle => throttle;
    internal Vector3 ControlState => new Vector3(throttle, roll, climb);
    internal float Clearance => clearance;
    internal Rigidbody Body => body;

    internal bool StartPlaytestCruise()
    {
        var player = Player.m_localPlayer;
        if (!IsValid || !view.IsOwner() || !player || Occupant(0) != player.GetPlayerID() ||
            !(player.GetDoodadController() is SkySeat seat) || seat.Aircraft != this || seat.Index != 0 || !player.IsAttached())
            return false;
        localThrottle = throttle = 1;
        view.GetZDO().Set(ThrottleKey, throttle);
        roll = climb = rudder = 0;
        lastInput = Time.time;
        inputTime = 0;
        body.linearVelocity = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized * 22f;
        body.angularVelocity = Vector3.zero;
        return true;
    }

    private void Awake()
    {
        view = GetComponent<ZNetView>();
        body = GetComponent<Rigidbody>();
        ship = GetComponent<Ship>();
        if (!IsValid) return;
        view.Register<long, int, int>("Skyfarer.SeatRequest", RPC_SeatRequest);
        view.Register<int, int, bool, float>("Skyfarer.SeatReply", RPC_SeatReply);
        view.Register<long, int>("Skyfarer.SeatRelease", RPC_SeatRelease);
        view.Register<long, int>("Skyfarer.Heartbeat", RPC_Heartbeat);
        view.Register<long, Vector3, float, bool, bool>("Skyfarer.Input", RPC_Input);
        throttle = StoredThrottle();
        body.centerOfMass = new Vector3(0, -.2f, 0);
    }

    public long Occupant(int index) => IsValid && index >= 0 && index < 5 ? view.GetZDO().GetLong(SeatKeys[index]) : 0;

    internal void RequestSeat(int index)
    {
        if (!IsValid || !Player.m_localPlayer || index < 0 || index >= 5) return;
        if (pendingSeat >= 0 && Time.time <= pendingUntil) return;
        pendingSeat = index;
        pendingUntil = Time.time + 4;
        pendingOwner = view.GetZDO().GetOwner();
        pendingRequest++;
        view.InvokeRPC(pendingOwner, "Skyfarer.SeatRequest", Player.m_localPlayer.GetPlayerID(), index, pendingRequest);
    }

    private Player AuthenticatedPlayer(long sender, long id)
    {
        var player = Player.GetPlayer(id);
        return player && player.GetOwner() == sender && !player.IsDead() ? player : null;
    }

    private void RPC_SeatRequest(long sender, long id, int index, int request)
    {
        if (!IsValid || !view.IsOwner() || index < 0 || index >= 5) return;
        var player = AuthenticatedPlayer(sender, id);
        if (!player || Vector3.Distance(player.transform.position, Seats[index].AttachPoint.position) > 4f) return;
        bool granted = Occupant(index) == 0 || Occupant(index) == id;
        for (int i = 0; i < 5; i++) if (i != index && Occupant(i) == id) granted = false;
        if (granted)
        {
            view.GetZDO().Set(SeatKeys[index], id);
            lastHeartbeat[index] = Time.time;
            if (index == 0 && view.GetZDO().GetOwner() != sender)
            {
                // Publish the latest pose and velocity before the pilot starts
                // simulating. Passengers never take ownership through seating.
                GetComponent<ZSyncTransform>().SyncNow();
                view.GetZDO().SetOwner(sender);
                wasOwner = false;
                ZDOMan.instance.ForceSendZDO(view.GetZDO().m_uid);
            }
        }
        view.InvokeRPC(sender, "Skyfarer.SeatReply", index, request, granted, StoredThrottle());
    }

    private void RPC_SeatReply(long sender, int index, int request, bool granted, float currentThrottle)
    {
        // The grantor may already have transferred ownership to us. Authenticate
        // against this request, independently of ZDO/RPC delivery order.
        if (!IsValid || !Player.m_localPlayer || index < 0 || index >= 5 ||
            sender != pendingOwner || index != pendingSeat || request != pendingRequest) return;
        var player = Player.m_localPlayer;
        pendingSeat = -1;
        if (Time.time > pendingUntil || player.IsAttached() || player.IsDead())
        {
            if (granted) ReleaseSeat(index, player.GetPlayerID());
            return;
        }
        if (granted)
        {
            // The seat reply carries the owner's setting even if ZDO replication is behind.
            localThrottle = Finite(currentThrottle) ? Mathf.Clamp01(currentThrottle) : StoredThrottle();
            inputTime = 0;
            Seats[index].Attach(player);
        }
        else player.Message(MessageHud.MessageType.Center, "That seat is occupied.");
    }

    internal void ReleaseSeat(int index, long id)
    {
        if (IsValid) view.InvokeRPC("Skyfarer.SeatRelease", id, index);
    }

    private void RPC_SeatRelease(long sender, long id, int index)
    {
        if (!IsValid || !view.IsOwner() || index < 0 || index >= 5 || Occupant(index) != id) return;
        var player = Player.GetPlayer(id);
        if (player && player.GetOwner() == sender) ClearSeat(index);
    }

    private void ClearSeat(int index)
    {
        view.GetZDO().Set(SeatKeys[index], 0L);
        if (index == 0) { roll = climb = rudder = 0; brake = reverse = false; }
    }

    private void RPC_Heartbeat(long sender, long id, int index)
    {
        if (!IsValid || !view.IsOwner() || index < 0 || index >= 5 || Occupant(index) != id) return;
        var player = AuthenticatedPlayer(sender, id);
        // Attached players' synced positions can trail a fast aircraft. The
        // authenticated seat heartbeat, not that lagged position, renews its lease.
        if (player) lastHeartbeat[index] = Time.time;
    }

    private float StoredThrottle()
    {
        float power = IsValid ? view.GetZDO().GetFloat(ThrottleKey) : 0;
        return Finite(power) ? Mathf.Clamp01(power) : 0;
    }

    internal void LocalInput(Vector3 move, bool throttleUp, bool throttleDown, bool wheelBrake = false, float rudderInput = 0, bool wheelReverse = false)
    {
        if (!Player.m_localPlayer || !IsValid || Occupant(0) != Player.m_localPlayer.GetPlayerID()) return;
        float change = (throttleUp ? 1 : 0) - (throttleDown ? 1 : 0);
        localThrottle = Mathf.Clamp01(localThrottle + change * Time.fixedDeltaTime * .38f);
        if (Time.time < inputTime) return;
        inputTime = Time.time + .05f;
        var input = new Vector3(localThrottle, Mathf.Clamp(move.x, -1, 1), Mathf.Clamp(-move.z, -1, 1));
        view.InvokeRPC("Skyfarer.Input", Player.m_localPlayer.GetPlayerID(), input, rudderInput, wheelBrake, wheelReverse);
    }

    private void RPC_Input(long sender, long id, Vector3 input, float rudderInput, bool wheelBrake, bool wheelReverse)
    {
        if (!IsValid || !view.IsOwner() || Occupant(0) != id || !AuthenticatedPlayer(sender, id)) return;
        if (!Finite(input.x) || !Finite(input.y) || !Finite(input.z) || !Finite(rudderInput)) return;
        throttle = Mathf.Clamp01(input.x);
        roll = Mathf.Clamp(input.y, -1, 1);
        rudder = Mathf.Clamp(rudderInput, -1, 1);
        climb = Mathf.Clamp(input.z, -1, 1);
        brake = wheelBrake;
        reverse = wheelReverse;
        // Store immediately so seat changes and ownership handoffs see the latest setting.
        view.GetZDO().Set(ThrottleKey, throttle);
        lastInput = Time.time;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal bool HasRiders()
    {
        // Seat leases tolerate network lag; the native onboard trigger also
        // counts people standing on the deck without occupying a chair.
        for (int i = 0; i < 5; i++) if (Occupant(i) != 0) return true;
        foreach (var player in Player.GetAllPlayers())
            if (player && !player.IsDead() && player.GetOwner() != 0 && ship.IsPlayerInBoat(player)) return true;
        return false;
    }

    private void Update()
    {
        if (!IsValid) return;
        float power = view.IsOwner() ? throttle : view.GetZDO().GetFloat(ThrottleKey);
        if (Propeller) Propeller.Rotate(0, 0, (50 + power * 1200) * Time.deltaTime, Space.Self);
        if (EngineLight) EngineLight.intensity = .7f + power * 1.8f;
        var player = Player.m_localPlayer;
        if (player && player.GetDoodadController() is SkySeat seat && seat.Aircraft == this)
        {
            if (Time.time >= heartbeatTime)
            {
                heartbeatTime = Time.time + 1;
                view.InvokeRPC("Skyfarer.Heartbeat", player.GetPlayerID(), seat.Index);
            }
            if (!player.IsAttached() || player.IsDead()) player.StopDoodadControl();
        }
    }

    private void FixedUpdate() => PhysicsStep(Time.fixedDeltaTime);

    private void OnCollisionEnter(Collision collision)
    {
        if (SmokeDrive)
        {
            SmokeContacts++;
            Plugin.Log.LogInfo("FLIGHT_CONTACT " + collision.gameObject.name + " velocity=" + collision.relativeVelocity);
        }
    }

    internal void PhysicsStep(float dt)
    {
        if (!IsValid || !view.IsOwner()) { wasOwner = false; return; }
        if (!wasOwner)
        {
            GetComponent<ZSyncTransform>().SyncNow();
            wasOwner = true;
            emptySince = -1;
            for (int i = 0; i < 5; i++) lastHeartbeat[i] = Time.time;
            throttle = StoredThrottle();
            roll = climb = rudder = 0;
            brake = reverse = false;
        }
        if (Time.time >= auditTime)
        {
            auditTime = Time.time + .5f;
            for (int i = 0; i < 5; i++)
            {
                if (Occupant(i) == 0) continue;
                var p = Player.GetPlayer(Occupant(i));
                // Allow briefly unstreamed player objects after a handoff, but
                // still reclaim dead/disconnected seats after their lease expires.
                if ((p && p.IsDead()) || Time.time - lastHeartbeat[i] > 6) ClearSeat(i);
            }
        }
        if (SmokeDrive) { throttle = SmokeInput.x; roll = SmokeInput.y; climb = SmokeInput.z; rudder = SmokeRudder; brake = false; reverse = SmokeReverse; }
        else if (Occupant(0) == 0 || Time.time - lastInput > 1.5f) { roll = climb = rudder = 0; brake = reverse = false; }
        if (HasRiders()) emptySince = -1;
        else
        {
            // Allow the native trigger a few physics frames to recognize a
            // rider stepping from a chair onto the deck before cutting power.
            if (emptySince < 0) emptySince = Time.time;
            if (Time.time - emptySince >= .5f)
            {
                // Store zero so returning cannot restart the engine by itself.
                throttle = localThrottle = roll = climb = rudder = 0;
                brake = reverse = false;
            }
        }
        view.GetZDO().Set(ThrottleKey, throttle);
        if (body.isKinematic) return;
        Gear.Step(Mathf.Clamp(roll + rudder, -1, 1), throttle, brake, reverse);
        // A wing strike or inverted crash must not snap the airframe upright.
        bool grounded = Gear.GroundedCount > 0 && Vector3.Dot(transform.up, Gear.GroundNormal) > .5f;

        Vector3 pos = body.position;
        Vector3 velocity = body.linearVelocity;
        float waterLevel = Floating.GetWaterLevel(pos, ref water);
        float surface = waterLevel;
        int count = Physics.RaycastNonAlloc(pos + Vector3.up * .2f, Vector3.down, groundHits, 220f,
            LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain"), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (groundHits[i].collider.attachedRigidbody != body && groundHits[i].point.y <= pos.y + .15f)
                surface = Mathf.Max(surface, groundHits[i].point.y);
        clearance = Mathf.Max(0, pos.y - surface - 1f);

        Vector3 forward = body.rotation * Vector3.forward;
        float speed = velocity.magnitude;
        float authority = Mathf.Clamp01(speed / 7f);
        float support = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(3, 8, speed));
        // The Surtling core provides forgiving support even while inverted. Thrust
        // follows the full forward vector, including straight up/down through a loop.
        // No Euler-angle targets, attitude clamps, or altitude ceiling in flight.
        if (grounded && (climb <= 0 || reverse)) support = 0;
        float gravity = Mathf.Max(.1f, -Physics.gravity.y);
        float verticalAccel = gravity * support;
        Vector3 desired = forward * (throttle * SkyfarerSettings.TopSpeed);
        if (throttle < .15f) desired += Vector3.down * 2.5f;
        if (!grounded)
            body.AddForce(Vector3.ClampMagnitude((desired - velocity) * 2.5f, 40f),
                ForceMode.Acceleration);
        else if (climb > 0 && !reverse)
            verticalAccel += climb * 6f * authority;

        // Keep the original floating hull and a gentle terminal fall when unpowered.
        float submerged = waterLevel + .45f - pos.y;
        if (submerged > 0) verticalAccel = Mathf.Max(verticalAccel, gravity + submerged * 12f - velocity.y * 4f);
        if (throttle < .15f && velocity.y < -8)
            verticalAccel = Mathf.Max(verticalAccel, gravity + (-8 - velocity.y) * 2);
        body.AddForce(Vector3.up * Mathf.Clamp(verticalAccel, -5, gravity + 18), ForceMode.Acceleration);

        if (grounded)
        {
            Vector3 tangent = Vector3.ProjectOnPlane(forward, Gear.GroundNormal).normalized;
            if (tangent.sqrMagnitude > .01f)
            {
                Quaternion target = Quaternion.LookRotation(tangent, Gear.GroundNormal)
                    * Quaternion.Euler(-Mathf.Max(0, climb) * 4 * authority, 0, 0);
                float forwardSpeed = Vector3.Dot(velocity, tangent);
                float turnRate = Mathf.Clamp(forwardSpeed / 4.8f
                    * Mathf.Tan(Gear.Wheels[0].steerAngle * Mathf.Deg2Rad) * Mathf.Rad2Deg, -30, 30);
                body.MoveRotation(Quaternion.AngleAxis(turnRate * dt, Gear.GroundNormal)
                    * Quaternion.Slerp(body.rotation, target, 3.5f * dt));
            }
        }
        else
        {
            // Local axes keep every control correct past vertical and upside down.
            // Neutral input holds the current attitude instead of leveling the plane.
            Vector3 rates = new Vector3(-climb * 45f, rudder * 28f, -roll * 85f) * authority;
            if (rates.sqrMagnitude > .0001f)
                body.MoveRotation(body.rotation * Quaternion.AngleAxis(rates.magnitude * dt, rates.normalized));
        }
        body.angularVelocity *= Mathf.Clamp01(1 - 5 * dt);
    }

    private void OnGUI()
    {
        var player = Player.m_localPlayer;
        if (!player || !(player.GetDoodadController() is SkySeat seat) || seat.Aircraft != this || !IsValid) return;
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = new Color(1f, .79f, .43f);
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            textStyle.normal.textColor = new Color(.96f, .91f, .80f);
        }
        float x = (Screen.width - 690) / 2f, y = Screen.height - 180;
        GUI.Box(new Rect(x, y, 690, 140), GUIContent.none);
        GUI.Label(new Rect(x, y + 5, 690, 27), "SURTLING SKYFARER", titleStyle);
        int passengers = 0;
        for (int i = 1; i < 5; i++) if (Occupant(i) != 0) passengers++;
        float power = view.IsOwner() ? throttle : view.GetZDO().GetFloat(ThrottleKey);
        GUI.Label(new Rect(x, y + 33, 690, 24), $"{(seat.Index == 0 ? "PILOT" : "PASSENGER")}   •   Throttle {power:P0}   •   Speed {body.linearVelocity.magnitude:F0} m/s   •   Passengers {passengers}/4", textStyle);
        GUI.Label(new Rect(x, y + 59, 690, 23), seat.Index == 0 ? "Shift / Ctrl  throttle     W / S  nose down / up     A / D  roll" : "Throttle stays set while someone remains aboard.", textStyle);
        GUI.Label(new Rect(x, y + 82, 690, 22), (Gear.GroundedCount > 0 ? "A / D or Q / E  wheel steering     •     S  lift off" : "Q / E  rudder     •     Hold pitch / roll for full rotations"), textStyle);
        GUI.Label(new Rect(x, y + 105, 690, 22), "R  reverse on ground     •     Right mouse  brake     •     Space  leave seat", textStyle);
    }
}
