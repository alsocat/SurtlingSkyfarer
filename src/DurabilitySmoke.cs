using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Wise.Skyfarer;

internal static class DurabilitySmoke
{
    internal static float Health(Aircraft aircraft) => aircraft.GetComponent<ZNetView>().GetZDO()
        .GetFloat(ZDOVars.s_health, aircraft.GetComponent<WearNTear>().m_health);

    internal static IEnumerator Run(Aircraft main, Action<bool, string> check)
    {
        check(!SkyfarerSettings.DurabilityEnabled.Value && SkyfarerSettings.MaxHealth.Value == 1800,
            "Durability defaults to disabled with 1800 maximum health");
        string migration = Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("SKYFARER_TEST_OUTPUT")), "old-config.cfg");
        File.WriteAllText(migration, "# Keep this comment\n[Aircraft]\nTopSpeed = 35\nRecipe = Wood:12\n\n[Other]\nKeep = yes\n");
        SkyfarerSettings.UpgradeConfig(migration);
        string migrated = File.ReadAllText(migration);
        check(migrated.Contains("TopSpeed = 35") && migrated.Contains("Recipe = Wood:12") && migrated.Contains("# Keep this comment")
            && migrated.IndexOf("MaxHealth = 1800") < migrated.IndexOf("[Other]"),
            "Old config gains durability fields without losing settings, comments, or other sections");
        SkyfarerSettings.UpgradeConfig(migration);
        check(File.ReadAllText(migration) == migrated, "Config upgrade is idempotent");
        SkyfarerSettings.MaxHealth.Value = 0;
        check(SkyfarerSettings.MaxHealth.Value == 1, "Maximum health rejects zero/negative values");
        SkyfarerSettings.MaxHealth.Value = 2000000;
        check(SkyfarerSettings.MaxHealth.Value == 1000000, "Maximum health enforces its upper bound");
        SkyfarerSettings.MaxHealth.Value = 3200;
        var plane = UnityEngine.Object.Instantiate(AircraftFactory.Prefab, main.transform.position + Vector3.right * 60,
            Quaternion.identity).GetComponent<Aircraft>();
        plane.Body.isKinematic = true;
        var wear = plane.GetComponent<WearNTear>();
        var data = plane.GetComponent<ZNetView>().GetZDO();
        check(wear.m_health == 3200 && Health(plane) == 3200, "New aircraft starts at the configured maximum health");
        check(!wear.ApplyDamage(800) && Health(plane) == 3200, "Disabled durability ignores damage at a custom maximum health");
        SkyfarerSettings.DurabilityEnabled.Value = true;
        check(wear.ApplyDamage(800) && Health(plane) == 2400, "Enabled durability deducts real health");
        SkyfarerSettings.MaxHealth.Value = 6400;
        check(wear.m_health == 6400 && Health(plane) == 4800, "Changing maximum health preserves 75 percent condition");
        AircraftDurability.Apply(plane);
        check(Health(plane) == 4800, "Repeated settings application does not heal or rescale twice");
        // Model an existing pre-update aircraft with no saved maximum-health key.
        data.Set(AircraftDurability.MaxHealthKey, 1800f);
        data.Set(ZDOVars.s_health, 900f);
        AircraftDurability.Apply(plane);
        check(Health(plane) == 3200, "Legacy 1800-health aircraft preserves its half-health condition");
        check(wear.Repair() && Health(plane) == 6400, "Native hammer repair restores the configured maximum");
        var hit = new HitData { m_point = plane.transform.position };
        hit.m_damage.m_blunt = 100;
        wear.Damage(hit);
        yield return new WaitForSeconds(.1f);
        check(Health(plane) < 6400 && Health(plane) > 0, "Enabled network damage RPC reduces aircraft health");
        SkyfarerSettings.DurabilityEnabled.Value = false;
        float before = Health(plane);
        wear.Damage(hit);
        check(!wear.ApplyDamage(100000), "Disabling durability blocks direct damage again");
        yield return new WaitForSeconds(.1f);
        check(Health(plane) == before, "Disabling durability blocks network damage without healing");
        SkyfarerSettings.DurabilityEnabled.Value = true;
        check(!wear.m_noSupportWear && !wear.m_noRoofWear, "Durable aircraft still does not require building supports or a roof");
        var impacts = plane.GetComponentsInChildren<ImpactEffect>(true);
        check(impacts.Length > 0 && impacts.All(i => i.m_damageToSelf && i.m_hitDestroyChance == 0),
            "Enabled durability routes collision damage through networked health");
        wear.UpdateWear(Time.time + 120);
        check(Health(plane) == before, "Unsupported flight does not cause structural wear with durability enabled");
        // Exercise real contact at flight speed against a temporary solid wall.
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.layer = LayerMask.NameToLayer("static_solid");
        wall.transform.position = plane.transform.position + Vector3.forward * 6;
        wall.transform.localScale = new Vector3(25, 20, 1);
        Physics.SyncTransforms();
        plane.Body.isKinematic = false;
        plane.Body.linearVelocity = Vector3.forward * 30;
        float deadline = Time.time + 2;
        while (Health(plane) == before && Time.time < deadline) yield return new WaitForFixedUpdate();
        plane.Body.isKinematic = true;
        UnityEngine.Object.Destroy(wall);
        check(Health(plane) < before, "Real collision at flight speed damages a durable aircraft");
        var hold = plane.GetComponent<CargoHold>();
        deadline = Time.time + 5;
        while (!hold.Cargo && Time.time < deadline) yield return null;
        check(hold.Cargo, "Durable aircraft retains its cargo chest");
        var cargo = hold.Cargo;
        CargoSmoke.AddWood(cargo.Container, 4, true);
        wear.ApplyDamage(1000000);
        yield return new WaitForSeconds(1.5f);
        check(!plane && !cargo && UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None)
            .Any(d => d.m_itemData.m_stack == 4 && d.m_itemData.m_customData.ContainsKey("skyfarer.smoke.cargo")),
            "Lethal damage destroys the aircraft and drops its stored cargo");
        SkyfarerSettings.DurabilityEnabled.Value = false;
        SkyfarerSettings.MaxHealth.Value = 1800;
    }

    internal static void PrepareSave(Aircraft plane, Action<bool, string> check)
    {
        SkyfarerSettings.DurabilityEnabled.Value = true;
        plane.GetComponent<WearNTear>().ApplyDamage(900);
        SkyfarerSettings.DurabilityEnabled.Value = false;
        check(Health(plane) == 900, "Saved aircraft has half health for durability reload validation");
    }

    internal static void Reload(Aircraft plane, Action<bool, string> check)
    {
        check(SkyfarerSettings.DurabilityEnabled.Value && SkyfarerSettings.MaxHealth.Value == 3600,
            "Restart loads enabled durability and 3600 max health from adjacent config");
        check(plane.GetComponent<WearNTear>().m_health == 3600 && Mathf.Abs(Health(plane) - 1800) < .1f,
            "Saved half-health aircraft remains half healthy after configured maximum changes");
        check(plane.GetComponent<WearNTear>().ApplyDamage(100) && Mathf.Abs(Health(plane) - 1700) < .1f,
            "Durability loaded from config allows damage after restart");
    }
}
