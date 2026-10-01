using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace Wise.Skyfarer;

// The playtest launcher uses its own character and save directory.
public class PlaytestWorld : MonoBehaviour
{
    private IEnumerator Start()
    {
        string saves = Environment.GetEnvironmentVariable("SKYFARER_TEST_SAVES");
        if (string.IsNullOrWhiteSpace(saves)) yield break;
        yield return new WaitForSecondsRealtime(3);
        Utils.SetSaveDataPath(saves);
        SaveSystem.ClearWorldListCache(true);
        var world = World.GetCreateWorld("SkyfarerFlightTest", FileHelpers.FileSource.Local);
        var profile = new PlayerProfile("SkyfarerPilot", FileHelpers.FileSource.Local);
        if (!profile.Load()) { profile.SetName("Skyfarer Pilot"); profile.m_firstSpawn = false; profile.Save(); }
        Game.SetProfile("SkyfarerPilot", FileHelpers.FileSource.Local);
        ZNet.SetServer(true, false, false, "Skyfarer Flight Test", "", world);
        ZNet.ResetServerHost();
        SystemResourceManager.FastLoadScene(FejdStartup.instance.m_mainScene);
        while (!ZNetScene.instance || !Player.m_localPlayer) yield return null;
        if (Player.m_localPlayer.InIntro()) Game.instance.SkipIntro();
        yield return new WaitForSeconds(3);
        while (!Player.m_localPlayer || Player.m_localPlayer.InIntro() || Player.m_localPlayer.IsTeleporting()) yield return null;
        var player = Player.m_localPlayer;
        player.SetGodMode(true);
        player.StopDoodadControl();
        Vector3 start = player.transform.position;
        float ground = 30f;
        if (Physics.Raycast(start + Vector3.up * 1000, Vector3.down, out var groundHit, 2000, LayerMask.GetMask("terrain")))
            ground = Mathf.Max(ground, groundHit.point.y);
        start.y = ground + 120f;
        Quaternion heading = Quaternion.Euler(0, player.transform.eulerAngles.y, 0);
        var plane = FindObjectsByType<Aircraft>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsValid);
        if (!plane)
            plane = Instantiate(AircraftFactory.Prefab, start, heading).GetComponent<Aircraft>();
        plane.GetComponent<ZNetView>().ClaimOwnership();
        plane.transform.SetPositionAndRotation(start, heading);
        plane.Body.position = start;
        plane.Body.rotation = heading;
        plane.Body.linearVelocity = Vector3.zero;
        plane.Body.angularVelocity = Vector3.zero;
        plane.GetComponent<WearNTear>().Repair();
        // Let ownership initialize before setting throttle and giving control to the pilot.
        yield return new WaitForFixedUpdate();
        player.transform.position = plane.Seats[0].AttachPoint.position;
        plane.RequestSeat(0);
        float deadline = Time.realtimeSinceStartup + 4;
        while (!plane.StartPlaytestCruise())
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                Plugin.Log.LogError("PLAYTEST_AIR_START_FAILED: pilot did not attach.");
                yield break;
            }
            yield return null;
        }
        player.Message(MessageHud.MessageType.Center, "Already airborne — full throttle\nShift/Ctrl: throttle, W/S: nose down/up, A/D: roll, Q/E: rudder. R reverses on land. Space leaves your seat; engine stops when empty.");
        Plugin.Log.LogInfo($"PLAYTEST_READY — airborne, pilot attached, height above launch ground={plane.transform.position.y - ground:F1} m, speed={plane.Body.linearVelocity.magnitude:F1} m/s, throttle={plane.Throttle:P0}.");
        yield return new WaitForSeconds(5);
        if (plane)
            Plugin.Log.LogInfo($"PLAYTEST_CRUISE — clearance={plane.Clearance:F1} m, speed={plane.Body.linearVelocity.magnitude:F1} m/s, throttle={plane.Throttle:P0}, attached={player.IsAttached()}.");
    }
}
