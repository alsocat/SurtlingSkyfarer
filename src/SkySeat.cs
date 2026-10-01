using UnityEngine;
using System;
using HarmonyLib;

namespace Wise.Skyfarer;

public class SkySeat : MonoBehaviour, Hoverable, Interactable, IDoodadController
{
    private static readonly Func<PlayerController, bool, bool> TakeInput = AccessTools.MethodDelegate<Func<PlayerController, bool, bool>>(
        AccessTools.Method(typeof(PlayerController), "TakeInput"));
    private static readonly Func<PlayerController, bool> InInventory = AccessTools.MethodDelegate<Func<PlayerController, bool>>(
        AccessTools.Method(typeof(PlayerController), "InInventoryEtc"));
    public int Index;
    public Aircraft Aircraft;
    public Transform AttachPoint;
    public string GetHoverName() => Index == 0 ? "Skyfarer helm" : "Passenger seat " + Index + " / 4";
    public float GetHoverOffset() => .5f;
    public string GetHoverText()
    {
        string state = Aircraft && Aircraft.Occupant(Index) != 0 ? "Occupied" : "[<color=yellow><b>$KEY_Use</b></color>] " + (Index == 0 ? "Pilot" : "Sit");
        return Localization.instance.Localize(GetHoverName() + "\n" + state);
    }
    public bool Interact(Humanoid human, bool hold, bool alt)
    {
        if (hold || !(human is Player player) || player != Player.m_localPlayer) return false;
        if (Vector3.Distance(player.transform.position, AttachPoint.position) > 3f || player.IsEncumbered()) return false;
        if (player.IsAttached()) { player.Message(MessageHud.MessageType.Center, "Leave your current seat first."); return false; }
        Aircraft.RequestSeat(Index);
        return true;
    }
    public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    public bool IsValid() => this && Aircraft && Aircraft.IsValid;
    public Component GetControlledComponent() => Aircraft;
    public Vector3 GetPosition() => AttachPoint.position;
    public void ApplyControlls(Vector3 moveDir, Vector3 lookDir, bool run, bool autoRun, bool block)
    {
        if (Index != 0) return;
        var controller = Player.m_localPlayer ? Player.m_localPlayer.GetComponent<PlayerController>() : null;
        bool allowed = controller && TakeInput(controller, false) && !InInventory(controller) && !Hud.InRadial();
        // Read held actions directly: vanilla suppresses Run when W/S are released.
        bool up = allowed && (ZInput.GetButton("Run") || ZInput.GetButton("JoyRun"));
        bool down = allowed && (ZInput.GetButton("Crouch") || ZInput.GetButton("JoyCrouch"));
        float rudder = allowed ? (ZInput.GetKey(KeyCode.E) ? 1 : 0) - (ZInput.GetKey(KeyCode.Q) ? 1 : 0) : 0;
        Aircraft.LocalInput(allowed ? moveDir : Vector3.zero, up, down, allowed && block, rudder, allowed && ZInput.GetKey(KeyCode.R));
    }
    public void OnUseStop(Player player)
    {
        if (Aircraft) Aircraft.ReleaseSeat(Index, player.GetPlayerID());
        player.AttachStop();
        Vector3 heading = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up);
        if (heading.sqrMagnitude < .01f) heading = Vector3.forward;
        player.transform.rotation = Quaternion.LookRotation(heading, Vector3.up);
    }
    internal void Attach(Player player)
    {
        player.StartDoodadControl(this);
        // Ignore the airframe's colliders while seated to avoid fighting its rigidbody.
        player.AttachStart(AttachPoint, Aircraft.gameObject, false, false, true, "attach_chair", new Vector3(Index % 2 == 0 ? .65f : -.65f, .4f, -.3f));
        player.Message(MessageHud.MessageType.Center, Index == 0
            ? "Shift/Ctrl: throttle  ·  W/S: nose down/up  ·  A/D: roll  ·  Q/E: rudder\nR: ground reverse  ·  Right mouse: brakes  ·  Space: leave seat; engine stops when empty"
            : "Passenger " + Index + " / 4  ·  Space: leave seat");
    }
}
