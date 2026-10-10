using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using Jotunn.Managers;
using UnityEngine;
using System.IO;

namespace Wise.Skyfarer;

internal static class SkyfarerSettings
{
    internal const string DefaultRecipe = "FineWood:80,Copper:25,DeerHide:20,SurtlingCore:8";
    internal static ConfigEntry<string> Recipe;
    internal static ConfigEntry<float> Speed;
    internal static float TopSpeed => float.IsNaN(Speed.Value) || float.IsInfinity(Speed.Value)
        ? 22f : Mathf.Clamp(Speed.Value, 10f, 100f);
    internal static void Initialize(ConfigFile config, string pluginPath)
    {
        // Keep the editable file beside the DLL. Jotunn identifies synchronized
        // files relative to BepInEx/config, so mirror values into the plugin's
        // standard config in memory, never an absolute-path custom sync file.
        string path = Path.Combine(Path.GetDirectoryName(pluginPath), "Skyfarer.cfg");
        bool create = !File.Exists(path);
        var source = new ConfigFile(path, false) { SaveOnConfigSet = false };
        var recipe = source.Bind("Aircraft", "Recipe", DefaultRecipe,
            "ItemPrefab:amount pairs separated by commas. Case-sensitive names; 1–16 items, amounts 1–10000. Example: Wood:80,Copper:25. Restart after editing. Refunds use the current recipe, including existing planes.");
        var speed = source.Bind("Aircraft", "TopSpeed", 22f,
            "Powered target speed in metres/second (10–100; default 22). Applies to flight and forward wheels. Reverse remains 4 m/s. Restart after editing. Server settings override client settings.");
        if (create) source.Save();
        config.SaveOnConfigSet = false;
        Recipe = config.Bind("Aircraft", "Recipe", DefaultRecipe, new ConfigDescription(
            "Comma-separated item prefab names and amounts (1–10000), e.g. Wood:80,Copper:25. Synced; server admins only. Refunds use the current recipe, including existing planes.",
            null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
        Speed = config.Bind("Aircraft", "Top speed", 22f, new ConfigDescription(
            "Powered target speed in metres/second, 10–100. Applies to flight and forward wheel drive. Reverse remains 4 m/s. Synced; server admins only.",
            new AcceptableValueRange<float>(10, 100), new ConfigurationManagerAttributes { IsAdminOnly = true }));
        Recipe.Value = recipe.Value;
        Speed.Value = speed.Value;
        Recipe.SettingChanged += (_, _) => ApplyRecipe();
        SynchronizationManager.OnConfigurationSynchronized += (_, _) => ApplyRecipe();
        PieceManager.OnPiecesRegistered += ApplyRecipe;
    }

    internal static bool TryRecipe(string text, out Piece.Requirement[] resources, out string error)
    {
        resources = null;
        error = "Use ItemPrefab:amount pairs, separated by commas (1–16 different items; amounts 1–10000).";
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] entries = text.Split(',');
        if (entries.Length > 16) return false;
        var list = new List<Piece.Requirement>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string entry in entries)
        {
            string[] pair = entry.Trim().Split(':');
            if (pair.Length != 2 || !int.TryParse(pair[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int amount)
                || amount < 1 || amount > 10000) return false;
            string name = pair[0].Trim();
            var prefab = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(name) : null;
            var item = prefab ? prefab.GetComponent<ItemDrop>() : null;
            if (!item || !names.Add(name))
            {
                error = "Unknown or duplicate item prefab: " + name + ". Names are case-sensitive (e.g. FineWood).";
                return false;
            }
            list.Add(new Piece.Requirement { m_resItem = item, m_amount = amount, m_recover = true });
        }
        resources = list.ToArray();
        error = "";
        return true;
    }

    internal static void ApplyRecipe()
    {
        if (!AircraftFactory.Prefab || !ObjectDB.instance || !ObjectDB.instance.GetItemPrefab("Wood")) return;
        if (!TryRecipe(Recipe.Value, out var resources, out string error))
        {
            Plugin.Log.LogWarning("Invalid Skyfarer recipe; using default recipe. " + error);
            if (!TryRecipe(DefaultRecipe, out resources, out _)) return;
        }
        AircraftFactory.Prefab.GetComponent<Piece>().m_resources = resources;
        foreach (var aircraft in UnityEngine.Object.FindObjectsByType<Aircraft>(FindObjectsSortMode.None))
            aircraft.GetComponent<Piece>().m_resources = resources;
    }

}
