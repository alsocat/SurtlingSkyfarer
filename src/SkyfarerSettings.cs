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
    internal static ConfigEntry<bool> DurabilityEnabled;
    internal static ConfigEntry<int> MaxHealth;
    internal static float TopSpeed => float.IsNaN(Speed.Value) || float.IsInfinity(Speed.Value)
        ? 22f : Mathf.Clamp(Speed.Value, 10f, 100f);
    internal static void Initialize(ConfigFile config, string pluginPath)
    {
        // Keep the editable file beside the DLL. Jotunn identifies synchronized
        // files relative to BepInEx/config, so mirror values into the plugin's
        // standard config in memory, never an absolute-path custom sync file.
        string path = Path.Combine(Path.GetDirectoryName(pluginPath), "Skyfarer.cfg");
        bool create = !File.Exists(path);
        if (!create) UpgradeConfig(path);
        var source = new ConfigFile(path, false) { SaveOnConfigSet = false };
        var recipe = source.Bind("Aircraft", "Recipe", DefaultRecipe,
            "ItemPrefab:amount pairs separated by commas. Case-sensitive names; 1–16 items, amounts 1–10000. Example: Wood:80,Copper:25. Restart after editing. Refunds use the current recipe, including existing planes.");
        var speed = source.Bind("Aircraft", "TopSpeed", 22f,
            "Powered target speed in metres/second (10–100; default 22). Applies to flight and forward wheels. Reverse remains 4 m/s. Restart after editing. Server settings override client settings. Dedicated servers may struggle at 100 m/s; lower speed if you notice lag or rubber-banding.");
        var durability = source.Bind("Aircraft", "DurabilityEnabled", false,
            "False keeps the plane indestructible (default). True enables damage, destruction, and hammer repairs. Restart after editing; server settings apply to everyone.");
        var health = source.Bind("Aircraft", "MaxHealth", 1800,
            "Maximum plane health, 1–1000000 (default 1800). Existing planes preserve their health percentage when this changes. Restart after editing.");
        if (create) source.Save();
        config.SaveOnConfigSet = false;
        Recipe = config.Bind("Aircraft", "Recipe", DefaultRecipe, new ConfigDescription(
            "Comma-separated item prefab names and amounts (1–10000), e.g. Wood:80,Copper:25. Synced; server admins only. Refunds use the current recipe, including existing planes.",
            null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
        Speed = config.Bind("Aircraft", "Top speed", 22f, new ConfigDescription(
            "Powered target speed in metres/second, 10–100. Applies to flight and forward wheel drive. Reverse remains 4 m/s. Synced; server admins only.",
            new AcceptableValueRange<float>(10, 100), new ConfigurationManagerAttributes { IsAdminOnly = true }));
        DurabilityEnabled = config.Bind("Aircraft", "Durability enabled", false, new ConfigDescription(
            "Enable plane damage and destruction. False by default. Synced; server admins only.",
            null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
        MaxHealth = config.Bind("Aircraft", "Maximum health", 1800, new ConfigDescription(
            "Maximum plane health. Preserves existing health percentage. Synced; server admins only.",
            new AcceptableValueRange<int>(1, 1000000), new ConfigurationManagerAttributes { IsAdminOnly = true }));
        DurabilityEnabled.Value = durability.Value;
        MaxHealth.Value = health.Value;
        DurabilityEnabled.SettingChanged += (_, _) => AircraftDurability.ApplyAll();
        MaxHealth.SettingChanged += (_, _) => AircraftDurability.ApplyAll();
        SynchronizationManager.OnConfigurationSynchronized += (_, _) => AircraftDurability.ApplyAll();
        Recipe.Value = recipe.Value;
        Speed.Value = speed.Value;
        Recipe.SettingChanged += (_, _) => ApplyRecipe();
        SynchronizationManager.OnConfigurationSynchronized += (_, _) => ApplyRecipe();
        PieceManager.OnPiecesRegistered += ApplyRecipe;
    }

    // Add only missing fields to old configs; preserve comments and player edits.
    internal static void UpgradeConfig(string path)
    {
        var lines = new List<string>(File.ReadAllLines(path));
        int start = lines.FindIndex(line => line.Trim() == "[Aircraft]");
        if (start < 0) { lines.Add(""); lines.Add("[Aircraft]"); start = lines.Count - 1; }
        int end = start + 1;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (end < lines.Count && !lines[end].TrimStart().StartsWith("["))
        {
            string line = lines[end].Trim();
            int equals = line.IndexOf('=');
            if (!line.StartsWith("#") && equals > 0) keys.Add(line.Substring(0, equals).Trim());
            end++;
        }
        var additions = new List<string>();
        if (!keys.Contains("DurabilityEnabled")) additions.AddRange(new[] { "",
            "# False keeps the plane indestructible. True enables damage and hammer repairs.",
            "# Restart after editing; the server's settings apply to everyone.", "DurabilityEnabled = false" });
        if (!keys.Contains("MaxHealth")) additions.AddRange(new[] { "",
            "# Maximum plane health: 1–1000000. Existing planes keep their health percentage.",
            "# Restart after editing. Default: 1800.", "MaxHealth = 1800" });
        if (additions.Count == 0) return;
        lines.InsertRange(end, additions);
        File.WriteAllLines(path, lines);
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
