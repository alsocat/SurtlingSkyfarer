using UnityEngine;
namespace Wise.Skyfarer;

internal static class Commands
{
    internal static void Register()
    {
        new Terminal.ConsoleCommand("skyfarer", "Show Skyfarer controls and test instructions", (Terminal.ConsoleEvent)(args =>
        {
            args.Context.AddString("Surtling Skyfarer — one pilot + four passenger seats.\nHammer > Transport near a workbench: 80 fine wood, 30 iron, 20 deer hide, 8 Surtling cores.\nShift/Ctrl throttle, W/S nose down/up, A/D roll, Q/E rudder, R reverse on ground, right mouse wheel brakes, Space leave seat.\nHold W/S for loops and A/D for rolls; release to hold attitude. Only Space exits the helm.\nThrottle stays set when you release the keys or leave the helm.\nLocal testing: devcommands, confirmcheats (if requested), then skyfarer_spawn.");
        }));
        new Terminal.ConsoleCommand("skyfarer_spawn", "Spawn a Skyfarer in front of you for local testing", (Terminal.ConsoleEvent)(args =>
        {
            var player = Player.m_localPlayer;
            if (!player || !ZNet.instance || !ZNet.instance.IsServer() || ZNet.instance.GetPeers().Count != 0)
            {
                args.Context.AddString("Use this test command in a local single-player world.");
                return;
            }
            Vector3 direction = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
            Vector3 pos = player.transform.position + direction * 10 + Vector3.up * 2;
            if (Physics.Raycast(pos + Vector3.up * 15, Vector3.down, out var hit, 50,
                LayerMask.GetMask("terrain", "static_solid", "piece"), QueryTriggerInteraction.Ignore)) pos.y = hit.point.y + 1.7f;
            var plane = Object.Instantiate(AircraftFactory.Prefab, pos, Quaternion.LookRotation(direction));
            plane.GetComponent<Piece>().SetCreator(player.GetPlayerID(), Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
            args.Context.AddString("Skyfarer spawned. Board using the port ladder; E on the front chair to pilot.\nHold Shift to increase throttle, then S to lift off. Ctrl reduces throttle; W lowers the nose. Four passenger seats sit behind the pilot.");
        }), isCheat: true, isNetwork: true, onlyServer: true);
    }
}
