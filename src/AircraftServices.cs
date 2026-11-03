using UnityEngine;
using HarmonyLib;

namespace Wise.Skyfarer;

internal class AircraftServices : MonoBehaviour
{
    private Aircraft plane;
    private bool menu;
    private static AircraftServices active;
    internal static bool IsOpen => active && active.menu;
    private GameObject wisp;
    internal Demister Mist { get; private set; }
    private void Awake() => plane = GetComponent<Aircraft>();
    internal void Open() { if (active && active != this) active.menu = false; menu = !menu; active = this; }
    private void Update()
    {
        if (menu && ZInput.GetKeyDown(KeyCode.Escape)) menu = false;
        var player = Player.m_localPlayer;
        if (menu && (!player || player.IsDead() || player.IsAttached() || Vector3.Distance(player.transform.position, transform.position) > 10)) menu = false;
        if (!plane || !plane.IsValid) return;
        var cargo = CargoLinks.Find(GetComponent<ZNetView>().GetZDO().GetString(CargoLinks.Key), true);
        bool enabled = cargo != null && cargo.GetBool(CargoUpgrades.WispKey);
        if (!enabled || wisp) return;
        wisp = new GameObject("Bound Skyfarer wisp");
        wisp.transform.SetParent(transform, false);
        wisp.transform.localPosition = new Vector3(0, 3.6f, 2.4f);
        var torch = Jotunn.Managers.PrefabManager.Instance.GetPrefab("piece_groundtorch_mist");
        var nativeMist = torch ? torch.GetComponentInChildren<Demister>(true) : null;
        if (nativeMist)
        {
            var clearing = Instantiate(nativeMist.gameObject, wisp.transform, false);
            clearing.transform.localPosition = Vector3.zero;
            clearing.SetActive(true);
            Mist = clearing.GetComponent<Demister>();
            Mist.m_forceField.endRange = 25;
        }
        else
        {
            Plugin.Log.LogError("Native wisp torch mist volume was not found.");
            Destroy(wisp);
            return;
        }
        var light = wisp.AddComponent<Light>();
        light.color = new Color(.25f, .7f, 1);
        light.intensity = 2;
        light.range = 12;
        var item = Jotunn.Managers.PrefabManager.Instance.GetPrefab("Wisp");
        var attach = item ? item.transform.Find("attach") : null;
        if (attach)
        {
            var glow = Instantiate(attach.gameObject, wisp.transform, false);
            glow.transform.localPosition = Vector3.zero;
            foreach (var collider in glow.GetComponentsInChildren<Collider>(true)) Destroy(collider);
        }
    }
    private void OnGUI()
    {
        if (!menu) return;
        float x = (Screen.width - 460) / 2f, y = (Screen.height - 260) / 2f;
        GUI.Box(new Rect(x, y, 460, 260), "Skyfarer services");
        GUI.Label(new Rect(x + 20, y + 30, 420, 40), "Place upgrade materials in cargo, then close the chest.\nEach cargo expansion costs 20 Fine wood + 10 Copper.");
        var cargo = GetComponent<CargoHold>().Cargo;
        if (GUI.Button(new Rect(x + 20, y + 80, 420, 32), "Expand cargo: 10 → 20 → 30 slots"))
            cargo?.GetComponent<CargoUpgrades>().Request(false);
        if (GUI.Button(new Rect(x + 20, y + 120, 420, 32), "Bind 1 Wisp: clear nearby Mistlands mist"))
            cargo?.GetComponent<CargoUpgrades>().Request(true);
        if (GUI.Button(new Rect(x + 20, y + 160, 420, 32), "Unstuck: lift and level the parked aircraft"))
        { plane.RequestUnstuck(); menu = false; }
        if (GUI.Button(new Rect(x + 20, y + 210, 420, 30), "Close")) menu = false;
    }
}

[HarmonyPatch(typeof(PlayerController), "TakeInput")]
internal static class ServiceMenuInput
{
    private static bool Prefix(ref bool __result)
    {
        if (!AircraftServices.IsOpen) return true;
        __result = false;
        return false;
    }
}
[HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
internal static class ServiceMenuCursor
{
    private static void Postfix()
    {
        if (!AircraftServices.IsOpen) return;
        ZCursor.LockState = CursorLockMode.None;
        ZCursor.Show();
    }
}
