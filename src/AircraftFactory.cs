using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Wise.Skyfarer;

internal static class AircraftFactory
{
    internal const string PrefabName = "Wise_SurtlingSkyfarer";
    internal static GameObject Prefab;
    private static Material wood, iron, cloth, redCloth;

    internal static void Register()
    {
        var pm = PrefabManager.Instance;
        CargoHold.Register();
        var karve = pm.GetPrefab("Karve");
        var root = pm.CreateClonedPrefab(PrefabName, karve);
        root.SetActive(false);
        Prefab = root;
        wood = pm.GetPrefab("wood_beam").transform.Find("new").GetComponent<Renderer>().sharedMaterial;
        iron = new Material(wood) { name = "Skyfarer forged fittings" };
        iron.SetColor("_Color", new Color(0.23f, 0.24f, 0.23f));
        var sail = karve.GetComponentsInChildren<SkinnedMeshRenderer>(true).First();
        // Reuse the installed game's cloth texture with its ordinary piece shader.
        cloth = new Material(wood) { name = "Skyfarer sailcloth" };
        cloth.mainTexture = sail.sharedMaterial.mainTexture;
        cloth.SetColor("_Color", new Color(0.91f, 0.82f, 0.64f));
        redCloth = new Material(cloth) { name = "Skyfarer oxblood cloth" };
        redCloth.SetColor("_Color", new Color(0.51f, 0.13f, 0.09f));

        Object.DestroyImmediate(root.GetComponent<LODGroup>());
        Disable(root, "ship/mast", "ship/ropes", "SurfaceEffects", "ashdamageeffects", "piece_chest");
        foreach (var c in root.GetComponentsInChildren<Chair>(true)) c.gameObject.SetActive(false);
        var ship = root.GetComponent<Ship>();
        ship.enabled = true;
        ship.m_shipControlls.gameObject.SetActive(false);
        ship.m_hasSail = false;
        // Only the onboard trigger contributes to the ship's passenger list.
        ship.m_floatCollider.enabled = false;
        var onboard = root.transform.Find("OnboardTrigger");
        onboard.localPosition = new Vector3(0, 1.1f, 0);
        onboard.localScale = new Vector3(3.4f, 3.8f, 9f);

        var piece = root.GetComponent<Piece>();
        piece.m_name = "Surtling Skyfarer";
        piece.m_description = "A core-fired skyboat with woven wings. One pilot and four passengers.";
        piece.m_canBeRemoved = true; // Karve disables hammer dismantling by default.
        piece.m_waterPiece = false;
        piece.m_groundOnly = false;
        piece.m_noInWater = false;
        piece.m_notOnTiltingSurface = true;
        piece.m_usage = Piece.UsageTagFlags.Transport;
        var wear = root.GetComponent<WearNTear>();
        wear.m_health = SkyfarerSettings.MaxHealth.Value;
        // These mean "wear when there is no roof/support", not immunity.
        wear.m_noRoofWear = false;
        wear.m_noSupportWear = false;
        wear.m_ashDamageImmune = true;
        wear.m_snowDamageImmune = true;
        wear.m_burnable = false;
        wear.m_staticPosition = false;
        wear.m_supports = false;
        foreach (var impact in root.GetComponentsInChildren<ImpactEffect>(true))
        {
            impact.m_damageToSelf = false;
            impact.m_hitDestroyChance = 0;
        }
        var body = root.GetComponent<Rigidbody>();
        body.mass = 700;
        body.centerOfMass = new Vector3(0, -0.2f, 0);
        body.linearDamping = 0.02f;
        body.angularDamping = 3;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var view = root.GetComponent<ZNetView>();
        view.m_persistent = true;
        view.m_distant = true;
        root.GetComponent<ZSyncTransform>().m_syncBodyVelocity = true;

        var frame = Child(root.transform, "Skyfarer airframe", Vector3.zero);
        // Cloth wings taper and sweep backwards; ribs remain visible on both faces.
        for (int side = -1; side <= 1; side += 2)
        {
            for (int bay = 0; bay < 5; bay++)
            {
                float x0 = 1.05f + bay * 1.12f, x1 = x0 + 1.12f;
                Vector3 a = WingPoint(side, x0, false), b = WingPoint(side, x1, false);
                Vector3 c = WingPoint(side, x1, true), d = WingPoint(side, x0, true);
                Panel(frame, "Stitched wing " + side + " " + bay, a, b, c, d, bay == 3 ? redCloth : cloth, 0.10f);
                Beam(frame, "Leading spar", a, b, 0.11f, wood);
                Beam(frame, "Trailing spar", d, c, 0.08f, wood);
                Beam(frame, "Wing rib", a, d, 0.065f, wood);
                if (bay == 4) Beam(frame, "Wingtip rib", b, c, 0.07f, wood);
            }
            Beam(frame, "Wing brace", new Vector3(side * 1.1f, 0.4f, 0.4f), WingPoint(side, 4.1f, false), 0.09f, wood);
            Beam(frame, "Aft wing brace", new Vector3(side * 1.05f, 0.4f, -1.5f), WingPoint(side, 4.1f, true), 0.075f, wood);
            var wingCollider = Child(frame, "Wing collision", new Vector3(side * 3.7f, 2.85f, -.25f)).gameObject.AddComponent<BoxCollider>();
            wingCollider.size = new Vector3(5.1f, .14f, 2.1f);
            Beam(frame, "Cabane strut", new Vector3(side * 0.9f, 0.45f, 1.3f), new Vector3(side * 1.05f, 2.65f, 1.6f), 0.12f, wood);
            Panel(frame, "Tailplane", new Vector3(0, 1.6f, -3.4f), new Vector3(side * 2.5f, 1.65f, -4.1f),
                new Vector3(side * 2.2f, 1.65f, -5f), new Vector3(0, 1.6f, -4.8f), redCloth, 0.02f);
            Beam(frame, "Tail spar", new Vector3(0, 1.6f, -3.4f), new Vector3(side * 2.5f, 1.65f, -4.1f), 0.09f, wood);
        }
        Beam(frame, "Tail spine", new Vector3(0, 0.6f, -2.8f), new Vector3(0, 1.6f, -5f), 0.18f, wood);
        Panel(frame, "Raven tail fin", new Vector3(0, 1.6f, -3.5f), new Vector3(0, 3.25f, -4.4f),
            new Vector3(0, 2.7f, -5.1f), new Vector3(0, 1.6f, -5f), cloth, 0);
        Beam(frame, "Fin spar", new Vector3(0, 1.6f, -3.5f), new Vector3(0, 3.25f, -4.4f), 0.10f, wood);

        var engine = Child(frame, "Surtling engine", new Vector3(0, 1.2f, 3.6f));
        Cube(engine, "Core housing", Vector3.zero, new Vector3(0.82f, 0.82f, 1.3f), iron, false);
        var core = Object.Instantiate(pm.GetPrefab("SurtlingCore").transform.Find("attach").gameObject, engine, false);
        core.name = "Bound Surtling core";
        core.transform.localPosition = new Vector3(0, 0.53f, 0);
        core.transform.localScale = Vector3.one * 2.3f;
        foreach (var c in core.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var lod in core.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
        var prop = Child(engine, "Propeller", new Vector3(0, 0, 0.85f));
        for (int blade = 0; blade < 4; blade++)
        {
            var arm = Child(prop, "Carved propeller blade", Vector3.zero);
            arm.localRotation = Quaternion.Euler(0, 0, blade * 90);
            Panel(arm, "Blade", new Vector3(-0.12f, 0.18f, 0), new Vector3(0.09f, 1.35f, 0.04f),
                new Vector3(0.32f, 1.25f, 0.04f), new Vector3(0.14f, 0.18f, 0), wood, 0);
        }
        Cube(prop, "Iron hub", Vector3.zero, Vector3.one * 0.30f, iron, false);

        var aircraft = root.AddComponent<Aircraft>();
        root.AddComponent<CargoHold>();
        aircraft.Gear = AddWagonWheels(root, frame);
        aircraft.Propeller = prop;
        aircraft.EngineLight = core.GetComponentInChildren<Light>();
        aircraft.Seats = new SkySeat[5];
        Vector3[] positions = { new(0, 0.15f, 2.1f), new(-0.62f, 0.10f, 0.55f), new(0.62f, 0.10f, 0.55f), new(-0.62f, 0.10f, -1.25f), new(0.62f, 0.10f, -1.25f) };
        for (int i = 0; i < positions.Length; i++)
        {
            var seatRoot = Child(root.transform, i == 0 ? "Pilot seat" : "Passenger seat " + i, positions[i]);
            var seat = seatRoot.gameObject.AddComponent<SkySeat>();
            seat.Index = i;
            seat.Aircraft = aircraft;
            seat.AttachPoint = Child(seatRoot, "SkyfarerAttach" + i, new Vector3(0, 0.1f, 0));
            var visual = Object.Instantiate(pm.GetPrefab("piece_chair").transform.Find("New").gameObject, seatRoot, false);
            foreach (var lod in visual.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            var collider = seatRoot.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, 0.42f, 0);
            collider.size = new Vector3(0.55f, 0.8f, 0.55f);
            aircraft.Seats[i] = seat;
        }
        // A handhold at the helm visually separates it from the passenger chairs.
        Beam(frame, "Pilot tiller", new Vector3(0, 0.45f, 2.7f), new Vector3(0, 1.0f, 2.55f), 0.08f, iron);
        Beam(frame, "Pilot tiller grip", new Vector3(-0.3f, 1.0f, 2.55f), new Vector3(0.3f, 1.0f, 2.55f), 0.09f, wood);
        // Preserve vanilla trigger layers so onboard volumes do not block E/hover rays.
        SetLayer(frame, root.layer);
        foreach (var seat in aircraft.Seats) SetLayer(seat.transform, root.layer);

        if (!PieceManager.Instance.AddPiece(new CustomPiece(root, false, new PieceConfig
        {
            PieceTable = "Hammer", Category = "Misc", CraftingStation = "piece_workbench",
            Requirements = new[] { new RequirementConfig("FineWood", 80, recover: true), new RequirementConfig("Copper", 25, recover: true),
                new RequirementConfig("DeerHide", 20, recover: true), new RequirementConfig("SurtlingCore", 8, recover: true) }
        }))) throw new InvalidOperationException("Could not register Skyfarer build piece.");
        root.SetActive(true); // PrefabManager's inactive parent keeps the template dormant.
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            string preview = Environment.GetEnvironmentVariable("SKYFARER_PREVIEW");
            int size = string.IsNullOrEmpty(preview) ? 128 : 1024;
            var sprite = RenderManager.Instance.Render(new RenderManager.RenderRequest(root)
            {
                Width = size, Height = size, Rotation = RenderManager.IsometricRotation, ParticleSimulationTime = -1
            });
            if (sprite)
            {
                piece.m_icon = sprite;
                if (!string.IsNullOrEmpty(preview)) File.WriteAllBytes(preview, sprite.texture.EncodeToPNG());
            }
        }
        Plugin.Log.LogInfo("Skyfarer registered: 1 pilot, 4 passenger seats, 13.3 m wingspan.");
    }

    private static Vector3 WingPoint(int side, float x, bool trailing)
    {
        float t = (x - 1.05f) / 5.6f;
        return new Vector3(side * x, 2.65f + t * 0.40f, (trailing ? -1.4f : 1.6f) - t * (trailing ? 0.2f : 1.15f));
    }

    private static LandingGear AddWagonWheels(GameObject root, Transform frame)
    {
        var gear = root.AddComponent<LandingGear>();
        gear.Wheels = new WheelCollider[4];
        gear.Visuals = new Transform[4];
        for (int axle = 0; axle < 2; axle++)
        {
            float z = axle == 0 ? 2.3f : -2.5f;
            Beam(frame, "Wagon axle", new Vector3(-1.6f, -.67f, z), new Vector3(1.6f, -.67f, z), .18f, iron);
            for (int side = 0; side < 2; side++)
            {
                int index = axle * 2 + side;
                float x = side == 0 ? -1.6f : 1.6f;
                Beam(frame, "Axle support", new Vector3(x * .6f, .1f, z), new Vector3(x * .85f, -.65f, z), .15f, wood);
                for (int leaf = 0; leaf < 3; leaf++)
                    Cube(frame, "Laminated spring", new Vector3(x * .83f, -.36f - leaf * .06f, z), new Vector3(.15f, .05f, 1.1f - leaf * .2f), iron, false);
                var mount = Child(frame, "Wheel suspension " + index, new Vector3(x, -.5f, z));
                var wheel = mount.gameObject.AddComponent<WheelCollider>();
                wheel.radius = LandingGear.Radius;
                wheel.mass = 25;
                wheel.suspensionDistance = .32f;
                wheel.suspensionSpring = new JointSpring { spring = 40000, damper = 4200, targetPosition = .5f };
                wheel.forceAppPointDistance = .45f;
                wheel.wheelDampingRate = .1f;
                wheel.forwardFriction = new WheelFrictionCurve { extremumSlip = .4f, extremumValue = 1, asymptoteSlip = .8f, asymptoteValue = .5f, stiffness = 1 };
                wheel.sidewaysFriction = new WheelFrictionCurve { extremumSlip = .2f, extremumValue = 1, asymptoteSlip = .5f, asymptoteValue = .75f, stiffness = 1.1f };
                gear.Wheels[index] = wheel;
                var visual = Child(frame, (axle == 0 ? "Front" : "Rear") + " wagon wheel " + side, new Vector3(x, -.66f, z));
                Ring(visual, "Wooden felloe", .57f, .69f, .26f, wood);
                Ring(visual, "Forged iron tire", .685f, LandingGear.Radius, .28f, iron);
                for (int spoke = 0; spoke < 10; spoke++)
                {
                    float angle = spoke * Mathf.PI * 2 / 10;
                    Vector3 direction = new Vector3(0, Mathf.Cos(angle), Mathf.Sin(angle));
                    Beam(visual, "Oak spoke", direction * .12f, direction * .6f, .07f, wood);
                }
                Cylinder(visual, "Wooden hub", Vector3.zero, new Vector3(.29f, .22f, .29f), wood);
                for (int cap = -1; cap <= 1; cap += 2)
                    Cylinder(visual, "Iron hub cap", new Vector3(cap * .22f, 0, 0), new Vector3(.32f, .025f, .32f), iron);
                gear.Visuals[index] = visual;
            }
        }
        return gear;
    }

    private static void Cylinder(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        obj.transform.SetParent(parent, false);
        obj.name = name;
        obj.transform.localPosition = position;
        obj.transform.localRotation = Quaternion.Euler(0, 0, 90);
        obj.transform.localScale = scale;
        Object.DestroyImmediate(obj.GetComponent<Collider>());
        obj.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static void Ring(Transform parent, string name, float inner, float outer, float width, Material material)
    {
        const int segments = 32;
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        for (int segment = 0; segment < segments; segment++)
        {
            float a = segment * Mathf.PI * 2 / segments, b = (segment + 1) * Mathf.PI * 2 / segments;
            Vector3 Point(float x, float r, float theta) => new Vector3(x, Mathf.Cos(theta) * r, Mathf.Sin(theta) * r);
            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
            {
                int first = vertices.Count;
                vertices.AddRange(new[] { p0, p1, p2, p3 });
                uv.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }
            float half = width / 2;
            Quad(Point(-half, outer, a), Point(-half, outer, b), Point(half, outer, b), Point(half, outer, a));
            Quad(Point(half, inner, a), Point(half, inner, b), Point(-half, inner, b), Point(-half, inner, a));
            Quad(Point(half, inner, a), Point(half, outer, a), Point(half, outer, b), Point(half, inner, b));
            Quad(Point(-half, inner, b), Point(-half, outer, b), Point(-half, outer, a), Point(-half, inner, a));
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.colors = Enumerable.Repeat(Color.white, vertices.Count).ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var obj = Child(parent, name, Vector3.zero).gameObject;
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        obj.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    internal static Transform Child(Transform parent, string name, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        return go.transform;
    }

    private static void Disable(GameObject root, params string[] paths)
    {
        foreach (var path in paths) root.transform.Find(path)?.gameObject.SetActive(false);
    }

    private static void Cube(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collision)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position;
        obj.transform.localScale = size;
        obj.GetComponent<Renderer>().sharedMaterial = material;
        if (!collision) Object.DestroyImmediate(obj.GetComponent<Collider>());
        else obj.GetComponent<Collider>().sharedMaterial = new PhysicsMaterial("Skyfarer skid")
        {
            dynamicFriction = .10f, staticFriction = .12f, frictionCombine = PhysicsMaterialCombine.Minimum,
            bounciness = 0, bounceCombine = PhysicsMaterialCombine.Minimum
        };
    }

    private static void Beam(Transform parent, string name, Vector3 a, Vector3 b, float width, Material material, bool collision = false)
    {
        var pivot = Child(parent, name, (a + b) * 0.5f);
        pivot.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
        Cube(pivot, "Timber", Vector3.zero, new Vector3(width, (b - a).magnitude, width), material, collision);
    }

    private static void Panel(Transform parent, string name, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Material material, float camber)
    {
        var obj = Child(parent, name, Vector3.zero).gameObject;
        Vector3 mid = (a + b + c + d) / 4 + Vector3.up * camber;
        var mesh = new Mesh { name = name };
        var v = new[] { a, b, c, d, mid };
        // Duplicate vertices so the back face has its own normals.
        mesh.vertices = v.Concat(v).ToArray();
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(.5f, .5f),
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(.5f, .5f) };
        mesh.triangles = new[] { 0,1,4, 1,2,4, 2,3,4, 3,0,4, 5,9,6, 6,9,7, 7,9,8, 8,9,5 };
        mesh.colors = Enumerable.Repeat(Color.white, 10).ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        obj.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform child in t) SetLayer(child, layer);
    }
}
