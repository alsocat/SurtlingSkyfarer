using UnityEngine;

namespace Wise.Skyfarer;

internal static class AircraftDurability
{
    internal const float LegacyMaxHealth = 1800f;
    internal static readonly int MaxHealthKey = "skyfarer.maxHealth".GetStableHashCode();

    internal static void Apply(Aircraft aircraft)
    {
        var wear = aircraft.GetComponent<WearNTear>();
        float maximum = SkyfarerSettings.MaxHealth.Value;
        bool enabled = SkyfarerSettings.DurabilityEnabled.Value;
        wear.m_health = maximum;
        // A flying vehicle must never require building supports or a roof.
        wear.m_noSupportWear = wear.m_noRoofWear = false;
        wear.m_burnable = enabled;
        wear.m_ashDamageImmune = wear.m_snowDamageImmune = !enabled;
        foreach (var impact in aircraft.GetComponentsInChildren<ImpactEffect>(true))
        {
            impact.m_damageToSelf = enabled;
            impact.m_hitDestroyChance = 0; // Damage must go through networked health.
        }
        var view = aircraft.GetComponent<ZNetView>();
        if (!view || !view.IsValid() || !view.IsOwner()) return;
        var data = view.GetZDO();
        float oldMaximum = data.GetFloat(MaxHealthKey, LegacyMaxHealth);
        if (data.GetFloat(MaxHealthKey, 0) == maximum) return;
        if (float.IsNaN(oldMaximum) || float.IsInfinity(oldMaximum) || oldMaximum <= 0) oldMaximum = LegacyMaxHealth;
        float health = data.GetFloat(ZDOVars.s_health, oldMaximum);
        if (float.IsNaN(health) || float.IsInfinity(health)) health = oldMaximum;
        float adjusted = Mathf.Clamp01(health / oldMaximum) * maximum;
        data.Set(MaxHealthKey, maximum);
        data.Set(ZDOVars.s_health, adjusted);
        view.InvokeRPC(ZNetView.Everybody, "RPC_HealthChanged", adjusted);
    }

    internal static void ApplyAll()
    {
        if (AircraftFactory.Prefab) AircraftFactory.Prefab.GetComponent<WearNTear>().m_health = SkyfarerSettings.MaxHealth.Value;
        foreach (var aircraft in Object.FindObjectsByType<Aircraft>(FindObjectsSortMode.None)) Apply(aircraft);
    }
}
