using System;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Wise.Skyfarer;

// Cargo uses its own ZDO: vanilla Container transfers its owner on open/stack/
// take-all, which must never transfer ownership of the aircraft rigidbody.
internal class CargoHold : MonoBehaviour
{
    internal const string PrefabName = "Wise_SkyfarerCargo";
    internal static readonly Vector3 Offset = new Vector3(0, .12f, -2.65f);
    private static GameObject prefab;
    private ZNetView view;
    internal SkyCargo Cargo;

    internal static void Register()
    {
        var pm = PrefabManager.Instance;
        prefab = pm.CreateClonedPrefab(PrefabName, "piece_chest_wood");
        prefab.SetActive(false);
        // This is an aircraft fitting, not a separately buildable/destructible piece.
        Object.DestroyImmediate(prefab.GetComponent<WearNTear>());
        Object.DestroyImmediate(prefab.GetComponent<Piece>());
        var sync = prefab.GetComponent<ZSyncTransform>();
        if (sync) Object.DestroyImmediate(sync);
        var nv = prefab.GetComponent<ZNetView>();
        nv.m_persistent = nv.m_distant = true;
        var container = prefab.GetComponent<Container>();
        container.m_name = "Skyfarer cargo";
        container.m_checkGuardStone = false;
        container.m_privacy = Container.PrivacySetting.Public;
        container.m_rootObjectOverride = null;
        container.m_autoDestroyEmpty = false;
        // Preserve the vanilla wooden chest's width, height, UI and lid visuals.
        prefab.transform.localScale = Vector3.one * .65f;
        // Trigger colliders retain E interaction without adding a second moving
        // solid body that could push the aircraft or its passengers.
        foreach (var collider in prefab.GetComponentsInChildren<Collider>(true)) collider.isTrigger = true;
        prefab.AddComponent<SkyCargo>();
        pm.AddPrefab(new CustomPrefab(prefab, false));
        prefab.SetActive(true);
    }

    private void Awake() => view = GetComponent<ZNetView>();
    private void Start() => InvokeRepeating(nameof(EnsureCargo), 0, .5f);

    private void EnsureCargo()
    {
        if (!view || !view.IsValid() || Cargo) return;
        string link = view.GetZDO().GetString(CargoLinks.Key);
        if (link.Length > 0)
        {
            SyncCargo();
            return; // A linked inventory may still be streaming; never replace it.
        }
        if (!view.IsOwner()) return;
        var root = Instantiate(prefab, transform.TransformPoint(Offset), transform.rotation);
        Cargo = root.GetComponent<SkyCargo>();
        var cargoView = root.GetComponent<ZNetView>();
        link = Guid.NewGuid().ToString("N");
        cargoView.GetZDO().Set(CargoLinks.Key, link);
        view.GetZDO().Set(CargoLinks.Key, link);
        CargoLinks.Register(cargoView.GetZDO());
        CargoLinks.Register(view.GetZDO());
        ZDOMan.instance.ForceSendZDO(cargoView.GetZDO().m_uid);
        ZDOMan.instance.ForceSendZDO(view.GetZDO().m_uid);
    }

    private void LateUpdate() => SyncCargo();

    internal void SyncCargo()
    {
        if (!view || !view.IsValid()) return;
        var data = CargoLinks.Find(view.GetZDO().GetString(CargoLinks.Key), true);
        if (data == null) return;
        Vector3 position = transform.TransformPoint(Offset);
        // Keep each peer's streaming position with the hull, even if the chest
        // instance is temporarily unloaded. SetPosition only publishes changes
        // for the owner; inventory ownership is never claimed here.
        data.SetPosition(position);
        if (data.IsOwner()) data.SetRotation(transform.rotation);
        if (!Cargo)
        {
            var instance = ZNetScene.instance.FindInstance(data.m_uid);
            if (instance) Cargo = instance.GetComponent<SkyCargo>();
        }
        if (Cargo) Cargo.transform.SetPositionAndRotation(position, transform.rotation);
    }
}

internal class SkyCargo : MonoBehaviour
{
    private static readonly Action<Container> DropContents = AccessTools.MethodDelegate<Action<Container>>(
        AccessTools.Method(typeof(Container), "OnDestroyed"));
    private static readonly Func<Container, bool> LoadContents = AccessTools.MethodDelegate<Func<Container, bool>>(
        AccessTools.Method(typeof(Container), "Load"));
    private ZNetView view;
    private ZDOMan manager;
    private bool released;
    private float missingSince = -1;
    internal Container Container { get; private set; }

    private void Awake()
    {
        view = GetComponent<ZNetView>();
        Container = GetComponent<Container>();
        if (!view.IsValid()) return;
        manager = ZDOMan.instance;
        manager.m_onZDODestroyed += ParentDestroyed;
    }

    private void OnDestroy()
    {
        if (manager != null) manager.m_onZDODestroyed -= ParentDestroyed;
    }

    private void LateUpdate()
    {
        if (!view || !view.IsValid() || released) return;
        string link = view.GetZDO().GetString(CargoLinks.Key);
        if (link.Length == 0) return;
        var parent = CargoLinks.Find(link, false);
        if (parent != null)
        {
            missingSince = -1;
            var plane = ZNetScene.instance.FindInstance(parent.m_uid);
            Quaternion rotation = plane ? plane.transform.rotation : parent.GetRotation();
            Vector3 position = plane ? plane.transform.TransformPoint(CargoHold.Offset)
                : parent.GetPosition() + rotation * CargoHold.Offset;
            transform.SetPositionAndRotation(position, rotation);
            view.GetZDO().SetPosition(position);
            if (view.IsOwner()) view.GetZDO().SetRotation(rotation);
        }
        else if (ZNet.instance.IsServer())
        {
            // Server has the complete world database. Recover contents of an
            // orphan loaded after its aircraft was dismantled while unloaded.
            if (missingSince < 0) missingSince = Time.time;
            if (Time.time - missingSince > 10 && view.IsOwner()) Release();
        }
    }

    private void ParentDestroyed(ZDO data)
    {
        if (view && view.IsValid() && data.GetPrefab() == AircraftFactory.PrefabName.GetStableHashCode() &&
            data.GetString(CargoLinks.Key).Length > 0 && data.GetString(CargoLinks.Key) == view.GetZDO().GetString(CargoLinks.Key) &&
            view.IsOwner()) Release();
    }

    private void Release()
    {
        if (released) return;
        released = true;
        LoadContents(Container);
        DropContents(Container);
        view.Destroy();
    }
}
