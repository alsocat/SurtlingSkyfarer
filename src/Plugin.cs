using System;
using System.Collections;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using Jotunn.Managers;
using Jotunn.Utils;
using HarmonyLib;
using UnityEngine;

namespace Wise.Skyfarer;

[BepInPlugin(Guid, "Surtling Skyfarer", Version)]
[BepInDependency(Jotunn.Main.ModGuid)]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "wise.valheim.skyfarer";
    public const string Version = "0.1.6";
    internal static ManualLogSource Log;

    private void Awake()
    {
        Log = Logger;
        SkyfarerSettings.Initialize(Config, Info.Location);
        string testSaves = Environment.GetEnvironmentVariable("SKYFARER_TEST_SAVES");
        if ((Environment.GetEnvironmentVariable("SKYFARER_SMOKE") == "1" || Environment.GetEnvironmentVariable("SKYFARER_PLAYTEST") == "1")
            && !string.IsNullOrWhiteSpace(testSaves))
            Utils.SetSaveDataPath(testSaves); // Set before the main menu builds its save-file cache.
        PrefabManager.OnVanillaPrefabsAvailable += OnPrefabs;
        new Harmony(Guid).PatchAll();
        Commands.Register();
        Logger.LogInfo("Surtling Skyfarer loaded. Pilot + four passengers.");
        Logger.LogInfo("World gravity: " + Physics.gravity);
    }

    private void OnPrefabs()
    {
        PrefabManager.OnVanillaPrefabsAvailable -= OnPrefabs;
        AircraftFactory.Register();
        if (Environment.GetEnvironmentVariable("SKYFARER_SMOKE") == "1")
            gameObject.AddComponent<RuntimeSmoke>();
        else if (Environment.GetEnvironmentVariable("SKYFARER_PLAYTEST") == "1")
            gameObject.AddComponent<PlaytestWorld>();
        if (Environment.GetEnvironmentVariable("SKYFARER_PROBE") == "1")
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (string name in new[] { "Karve", "VikingShip", "piece_chair", "wood_beam", "wood_floor", "SurtlingCore", "DeerHide" })
            {
                var prefab = PrefabManager.Instance.GetPrefab(name);
                lines.Add("PREFAB " + name + " " + (prefab ? "FOUND" : "MISSING"));
                if (!prefab) continue;
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                {
                    string path = t.name;
                    for (var p = t.parent; p && p != prefab.transform; p = p.parent) path = p.name + "/" + path;
                    lines.Add(path + " pos=" + t.localPosition + " scale=" + t.localScale + " rot=" + t.localEulerAngles +
                        " components=" + string.Join(",", t.GetComponents<Component>().Where(c => c).Select(c => c.GetType().Name)));
                    var mr = t.GetComponent<MeshRenderer>();
                    if (mr) lines.Add(" materials=" + string.Join(",", mr.sharedMaterials.Select(m => m ? m.name + ":" + m.shader.name : "null")));
                    var bc = t.GetComponent<BoxCollider>();
                    if (bc) lines.Add(" box=" + bc.size + " center=" + bc.center + " trigger=" + bc.isTrigger);
                }
            }
            File.WriteAllLines(Environment.GetEnvironmentVariable("SKYFARER_PROBE_OUTPUT"), lines);
            Logger.LogInfo("SKYFARER_PROBE_PASS");
            StartCoroutine(QuitProbe());
        }
    }

    private IEnumerator QuitProbe()
    {
        yield return new WaitForSecondsRealtime(2);
        Application.Quit();
    }
}
