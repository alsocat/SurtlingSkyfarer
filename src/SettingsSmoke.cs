using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace Wise.Skyfarer;

internal static class SettingsSmoke
{
    internal static IEnumerator Run(Aircraft plane, Player player, Action<bool, string> check)
    {
        check(!Jotunn.Managers.PrefabManager.Instance.GetPrefab("Karve").GetComponent<Piece>().m_canBeRemoved,
            "Vanilla Karve disables hammer removal (inherited cause confirmed)");
        check(plane.GetComponent<Piece>().m_canBeRemoved, "Skyfarer explicitly enables hammer removal");
        check(SkyfarerSettings.TryRecipe("Wood:12,Stone:3", out var parsed, out _) && parsed.Length == 2,
            "Custom recipe accepts different valid item materials");
        foreach (var invalid in new[] { "", "Wood:0", "Wood:-1", "Wood:10001", "Wood:1,Wood:2", "NotAnItem:2", "Wood:NaN", "Wood:2,", "Wood:1:2" })
            check(!SkyfarerSettings.TryRecipe(invalid, out _, out _), "Reject invalid recipe: " + invalid);
        string original = SkyfarerSettings.Recipe.Value;
        SkyfarerSettings.Recipe.Value = "Wood:12,Stone:3";
        check(AircraftFactory.Prefab.GetComponent<Piece>().m_resources.Length == 2 &&
            plane.GetComponent<Piece>().m_resources.Any(r => r.m_resItem.name == "Stone" && r.m_amount == 3),
            "Custom recipe updates hammer prefab and existing aircraft refunds");
        SkyfarerSettings.Recipe.Value = "Invalid:3";
        check(plane.GetComponent<Piece>().m_resources.Any(r => r.m_resItem.name == "Copper" && r.m_amount == 25),
            "Invalid configured recipe safely falls back to default costs");
        SkyfarerSettings.Recipe.Value = original;
        float speed = SkyfarerSettings.Speed.Value;
        SkyfarerSettings.Speed.Value = float.NaN;
        check(!float.IsNaN(SkyfarerSettings.TopSpeed) && SkyfarerSettings.TopSpeed >= 10 && SkyfarerSettings.TopSpeed <= 100,
            "Non-finite speed resolves to a finite allowed value");
        SkyfarerSettings.Speed.Value = 1000;
        check(SkyfarerSettings.TopSpeed == 100, "Speed upper limit is enforced");
        SkyfarerSettings.Speed.Value = 0;
        check(SkyfarerSettings.TopSpeed == 10, "Speed lower limit preserves takeoff capability");
        SkyfarerSettings.Speed.Value = 35;
        player.transform.position = plane.Seats[0].AttachPoint.position;
        plane.RequestSeat(0);
        yield return new WaitForSeconds(.3f);
        plane.Body.isKinematic = false;
        plane.SmokeDrive = true;
        float end = Time.time + 7;
        while (Time.time < end)
        {
            plane.SmokeInput = new Vector3(1, 0, 0);
            yield return new WaitForFixedUpdate();
        }
        check(plane.Body.linearVelocity.magnitude > 33 && plane.Body.linearVelocity.magnitude < 36,
            "Configured 35 m/s powered flight reaches " + plane.Body.linearVelocity.magnitude.ToString("F2") + " m/s");
        plane.SmokeInput = Vector3.zero;
        plane.SmokeDrive = false;
        plane.Body.isKinematic = true;
        player.StopDoodadControl();
        SkyfarerSettings.Speed.Value = speed;
    }
}
