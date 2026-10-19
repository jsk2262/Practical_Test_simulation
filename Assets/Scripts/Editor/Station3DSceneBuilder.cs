using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Kitchen > Build 3D Station Scene - makes Assets/Scenes/PracticalStation3D.unity:
/// a copy of SampleScene (so every interaction, the plan UI, hand wash etc. keep
/// working unchanged) whose flat background art is swapped for the Blender-made
/// stainless exam station (Assets/Models/CookingStation_Wide.fbx) and whose
/// ingredient quads are swapped for the Blender ingredient models.
///
/// The game still plays on the screen (XY) plane under the orthographic front
/// camera, so the station is tilted back (TiltDegrees) to show its counter top
/// like the old art did, pushed behind all gameplay objects, and the burner /
/// faucet / knob interactables are moved onto the model's parts (keeping their
/// own depth). Old background renderers are hidden but their colliders stay,
/// since "click empty counter to set down" relies on hitting them.
///
/// Ingredient meshes are re-baked to the unit size the old quads had (pieces
/// from cutting are laid out assuming that), tilted the same way as the
/// station, so each IngredientItem keeps its transform and size. Re-running
/// rebuilds the scene from SampleScene again.
/// </summary>
public static class Station3DSceneBuilder
{
    private const string SourceScene = "Assets/Scenes/SampleScene.unity";
    private const string TargetScene = "Assets/Scenes/PracticalStation3D.unity";
    private const string StationFbx = "Assets/Models/CookingStation_Wide.fbx";
    private const string IngredientDir = "Assets/Models/Ingredients";
    private const string GeneratedDir = "Assets/Models/Generated";

    private const float TiltDegrees = 41f;   // counter top shown like the old art
    private const float MetersToUnits = 8.46f; // sink (x = -0.65 m) lands on the scene's sink (x = -5.5)

    private static readonly string[] HiddenBackground =
    {
        "CounterBody", "Sink", "StoveBody", "Shelf", "SinkBasin", "Faucet", "Faucet_Model",
        "BurnerR_Model(broken)", "BurnerR_Model (real)", "Burner_L", "Burner_R", "Floor",
    };

    // scene ingredient name fragment -> Blender model (Ingredient_Oniion keeps its own onion + cut half)
    private static readonly (string key, string model)[] IngredientModels =
    {
        ("당근", "Carrot"), ("오이", "Cucumber"), ("목이", "WoodEar"), ("대파", "GreenOnion"),
        ("완두콩", "GreenPeas"), ("돼지고기", "MincedMeat"), ("돼지등심", "Beef"), ("Tofu", "Tofu"),
        ("고추_", "Chili"), ("마늘", "Garlic"), ("생강", "Ginger"), ("달걀", "Egg"),
    };

    [MenuItem("Tools/Kitchen/Build 3D Station Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Directory.CreateDirectory(GeneratedDir);

        AssetDatabase.DeleteAsset(TargetScene);
        if (!AssetDatabase.CopyAsset(SourceScene, TargetScene)) { Debug.LogError("[Station3D] copy failed"); return; }
        EditorSceneManager.OpenScene(TargetScene);

        foreach (string n in HiddenBackground)
        {
            GameObject go = Find(n);
            if (go == null) continue;
            Renderer r = go.GetComponent<Renderer>();
            if (r != null) r.enabled = false;
        }
        DisableHiddenBackgroundColliders(EditorSceneManager.GetActiveScene());

        Transform station = PlaceStation();
        if (station == null) return;
        MoveInteractablesOntoModel(station);
        SetupFaucetLever(EditorSceneManager.GetActiveScene());
        int swapped = SwapIngredients();

        // the wall sat at z = 2, in front of most of the tilted station - send it behind
        GameObject wall = Find("Background_Wall");
        if (wall != null)
        {
            Bounds sb = Bounds(station);
            Vector3 p = wall.transform.position;
            wall.transform.position = new Vector3(p.x, p.y, sb.max.z + 1f);
            wall.transform.localScale = new Vector3(40f, 24f, 1f);
        }

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AddToBuildSettings();
        Debug.Log($"[Station3D] Built {TargetScene}: station placed, {swapped} ingredients swapped to 3D.");
    }

    // ---------------- station ----------------

    private static Transform PlaceStation()
    {
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(StationFbx);
        if (fbx == null) { Debug.LogError($"[Station3D] {StationFbx} missing"); return null; }

        var root = new GameObject("Station3D").transform;
        var model = ((GameObject)PrefabUtility.InstantiatePrefab(fbx, root)).transform;
        PrefabUtility.UnpackPrefabInstance(model.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        ApplyStationMaterials(model);

        // Front of the station (handle bar side) must face the camera (-Z).
        Vector3 handle = Center(model, "FrontHandleBar"), body = Center(model, "Body");
        if (handle.z > body.z) model.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.localRotation;

        // Tilt the top toward the camera, scale to scene units.
        Quaternion tilt = Quaternion.Euler(TiltDegrees, 0f, 0f);
        if ((tilt * Vector3.up).z > 0f) tilt = Quaternion.Euler(-TiltDegrees, 0f, 0f);
        root.rotation = tilt;
        root.localScale = Vector3.one * MetersToUnits;

        // Sink basin over the scene's sink; whole model behind every gameplay object.
        Vector3 sinkModel = Center(model, "Sink_Bottom");
        GameObject sink = Find("Sink");
        Vector3 sinkScene = sink != null ? sink.GetComponent<Collider>().bounds.center : new Vector3(-5.5f, -0.93f, 0f);
        root.position += new Vector3(-Center(model, "Body").x, sinkScene.y - sinkModel.y, 0f);
        float minZ = Bounds(root).min.z;
        root.position += new Vector3(0f, 0f, 0.6f - minZ);
        return root;
    }

    private static void ApplyStationMaterials(Transform model)
    {
        var made = new Dictionary<string, Material>();
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string n = mats[i].name;
                if (!made.TryGetValue(n, out Material m))
                {
                    m = LitMaterial("Station_" + n, mats[i].color, Metallic(n), Smoothness(n));
                    made[n] = m;
                }
                mats[i] = m;
            }
            r.sharedMaterials = mats;
        }
    }

    private static float Metallic(string n) =>
        n.Contains("Steel") || n.Contains("Chrome") || n.Contains("Brass") ? 0.85f : n.Contains("Burner") ? 0.4f : 0f;

    private static float Smoothness(string n) =>
        n.Contains("Chrome") ? 0.9f : n.Contains("Steel") ? 0.62f : n.Contains("Brass") ? 0.6f : 0.3f;

    private static Material LitMaterial(string name, Color color, float metallic, float smoothness)
    {
        string path = $"{GeneratedDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Burners, flames, faucet/water and the gas knob go where the model
    /// draws them (x/y only - their depth is part of the gameplay layering).</summary>
    private static void MoveInteractablesOntoModel(Transform station)
    {
        MoveXY("Burner_L", Center(station, "Burner1_Base"));
        MoveXY("Flame_L", Center(station, "Burner1_Head"));
        MoveXY("Burner_R", Center(station, "Burner2_Base"));
        MoveXY("Flame_R", Center(station, "Burner2_Head"));
        MoveXY("StoveHandle", Center(station, "GasKnob2"));

        Vector3 spout = Center(station, "Faucet_Spout");
        MoveXY("Faucet", spout);

        // Stream hangs from the nozzle; the hand wash origin follows the stream.
        Bounds nozzle = RendererBounds(station, "Faucet_Nozzle");
        GameObject stream = Find("WaterStream");
        if (stream != null)
        {
            Vector3 top = new Vector3(nozzle.center.x, nozzle.min.y, 0f);
            Vector3 before = stream.transform.position;
            float half = stream.transform.lossyScale.y * 0.5f;
            MoveXY("WaterStream", top - new Vector3(0f, half, 0f));
            Vector3 delta = stream.transform.position - before;
            GameObject wash = Find("WashOrigin");
            if (wash != null) wash.transform.position += new Vector3(delta.x, delta.y, 0f);
            GameObject label = Find("HandWashStatus");
            if (label != null) label.transform.position += new Vector3(delta.x, delta.y, 0f);
        }
    }

    // ---------------- ingredients ----------------

    private static int SwapIngredients()
    {
        int count = 0;
        foreach (IngredientItem item in Object.FindObjectsByType<IngredientItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string model = null;
            foreach (var (key, m) in IngredientModels)
                if (item.name.Contains(key)) { model = m; break; }
            if (model == null) continue;

            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{IngredientDir}/{model}.fbx");
            MeshRenderer src = fbx != null ? fbx.GetComponentInChildren<MeshRenderer>() : null;
            if (src == null) { Debug.LogWarning($"[Station3D] {model}.fbx missing"); continue; }

            // On-screen size the old quad art had (its longest side).
            Bounds old = item.GetComponent<Renderer>().bounds;
            float target = Mathf.Max(old.size.x, old.size.y) * 1.15f;

            Mesh mesh = UnitMesh(src.GetComponent<MeshFilter>().sharedMesh, model, src.transform);
            item.GetComponent<MeshFilter>().sharedMesh = mesh;

            var mats = new Material[src.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                Material s = src.sharedMaterials[i];
                mats[i] = LitMaterial("Ingredient_" + s.name, s.color, 0f, s.name.Contains("Chili") || s.name.Contains("Egg") ? 0.55f : 0.35f);
            }
            item.GetComponent<MeshRenderer>().sharedMaterials = mats;

            // Uniform scale (the quads' non-uniform one would squash the 3D shape)
            // so the model's longest side matches the old art's.
            Transform t = item.transform;
            float parent = t.parent != null ? Mathf.Abs(t.parent.lossyScale.x) : 1f;
            t.localScale = Vector3.one * (target / Mathf.Max(parent, 0.0001f));

            FitBoxCollider(item.gameObject, mesh);
            EditorUtility.SetDirty(item.gameObject);
            count++;
        }
        return count;
    }

    // ---------------- 지급량 원재료 (raw portions as supplied at the exam) ----------------

    private const string PortionDir = "Assets/Models/Ingredients/Portions";
    /// <summary>World units per real meter for the portions - all to one scale,
    /// a bit under the station's own (MetersToUnits) so small pieces stay clickable.</summary>
    private const float PortionUnitsPerMeter = 7f;

    private static readonly (string key, string model)[] PortionModels =
    {
        ("당근", "Carrot_Portion"), ("오이", "Cucumber_Portion"), ("Oniion", "Onion_Portion"), ("Onion", "Onion_Portion"),
        ("대파", "GreenOnion_Portion"), ("고추_", "Chili_Portion"), ("마늘", "Garlic_Portion"), ("생강", "Ginger_Portion"),
        ("Tofu", "Tofu_Portion"), ("돼지등심", "Pork_Portion"), ("돼지고기", "Minced_Portion"),
    };

    /// <summary>The raw ingredients on the tray become the exam's supplied
    /// portions (당근 30g 토막, 오이 1/4, 양파 1/4, 대파 흰부분 6cm, 고추 1/2 (가로),
    /// 마늘 2쪽, 생강 5g, 두부 150g, 돼지등심 200g, 다진고기 50g) - Blender models
    /// with textured cut faces, all at one real-world scale.</summary>
    [MenuItem("Tools/Kitchen/Swap To Raw Portions (지급량)")]
    private static void SwapToPortions()
    {
        var log = new System.Text.StringBuilder();
        foreach (IngredientItem item in Object.FindObjectsByType<IngredientItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string model = null;
            foreach (var (key, m) in PortionModels)
                if (item.name.Contains(key)) { model = m; break; }
            if (model == null) continue;

            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{PortionDir}/{model}.fbx");
            MeshRenderer src = fbx != null ? fbx.GetComponentInChildren<MeshRenderer>() : null;
            if (src == null) { Debug.LogWarning($"[Station3D] {model}.fbx missing"); continue; }
            Mesh source = src.GetComponent<MeshFilter>().sharedMesh;

            Undo.RecordObject(item.transform, "portion");
            Mesh mesh = UnitMesh(source, model, src.transform);
            item.GetComponent<MeshFilter>().sharedMesh = mesh;

            var mats = new Material[src.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                Material s = src.sharedMaterials[i];
                string baseName = s.name.StartsWith("P_") ? s.name.Substring(2) : s.name;
                string texName = baseName == "Tofu" ? "Tofu_Surface" : baseName;
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PortionDir}/Textures/{texName}.png");
                bool shiny = baseName.Contains("Chili") || baseName.Contains("Onion_Cut") || baseName.Contains("Cucumber");
                Material m = LitMaterial("Portion_" + baseName, tex != null ? Color.white : s.color, 0f, shiny ? 0.55f : 0.3f);
                m.SetTexture("_BaseMap", tex);
                mats[i] = m;
            }
            item.GetComponent<MeshRenderer>().sharedMaterials = mats;

            // Real size: the model's longest on-screen side in meters, to scale.
            Vector3[] v = source.vertices;
            Quaternion rot = Quaternion.Euler(-TiltDegrees, 0f, 0f) * src.transform.localRotation;
            var b = new Bounds(rot * Vector3.Scale(v[0], src.transform.localScale), Vector3.zero);
            foreach (Vector3 p in v) b.Encapsulate(rot * Vector3.Scale(p, src.transform.localScale));
            float meters = Mathf.Max(b.size.x, b.size.y);
            Transform t = item.transform;
            float parent = t.parent != null ? Mathf.Abs(t.parent.lossyScale.x) : 1f;
            t.rotation = Quaternion.identity; // the mesh carries the counter's tilt itself
            t.localScale = Vector3.one * (meters * PortionUnitsPerMeter / Mathf.Max(parent, 0.0001f));

            FitBoxCollider(item.gameObject, mesh);
            EditorUtility.SetDirty(item.gameObject);
            log.Append($"{item.name}->{model} ({meters * 100f:F1}cm)  ");
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Station3D] portions: " + log);
    }

    /// <summary>Where each raw ingredient sits on the prepped plate, as an offset
    /// (world units) from the plate's middle - rows from back to front, so the
    /// real-size portions don't pile up on each other.</summary>
    private static readonly (string key, Vector2 offset)[] PlateLayout =
    {
        ("오이", new Vector2(-0.3f, 0.5f)), ("돼지등심", new Vector2(0.52f, 0.46f)),
        ("Oniion", new Vector2(-0.6f, 0.2f)), ("Onion", new Vector2(-0.6f, 0.2f)), ("Tofu", new Vector2(-0.12f, 0.2f)),
        ("돼지고기", new Vector2(0.3f, 0.2f)), ("Egg", new Vector2(0.68f, 0.17f)),
        ("당근", new Vector2(-0.68f, -0.08f)), ("대파_Demo (1)", new Vector2(0.06f, -0.08f)), ("대파", new Vector2(-0.33f, -0.08f)),
        ("고추_", new Vector2(0.52f, -0.08f)),
        ("마늘", new Vector2(-0.45f, -0.3f)), ("생강", new Vector2(-0.18f, -0.3f)), ("Pea", new Vector2(0.1f, -0.3f)),
        ("WoodEar", new Vector2(0.4f, -0.3f)),
    };

    /// <summary>Lays the raw ingredients out on the prepped plate (PlateLayout)
    /// and sets each down onto the plate's (tilted) surface - by its own mesh,
    /// so nothing sinks into the plate or floats above it.</summary>
    [MenuItem("Tools/Kitchen/Arrange Raw Portions On Plate")]
    private static void ArrangePortionsOnPlate()
    {
        GameObject plate = Find("Plate_Model_Prepped");
        Renderer pr = plate != null ? plate.GetComponent<Renderer>() : null;
        if (pr == null) { Debug.LogWarning("[Station3D] Plate_Model_Prepped not found"); return; }
        Vector3 c = pr.bounds.center;
        float SurfaceZ(float y) => c.z + HandCarry.CounterSlope * (y - c.y);

        var log = new System.Text.StringBuilder();
        foreach (IngredientItem item in Object.FindObjectsByType<IngredientItem>(FindObjectsSortMode.None))
        {
            Vector2? off = null;
            foreach (var (key, o) in PlateLayout)
                if (item.name.Contains(key)) { off = o; break; }
            if (off == null) continue;
            Transform t = item.transform;
            Undo.RecordObject(t, "arrange");

            // Center the visible model (not the pivot) on its spot.
            Renderer r = item.GetComponent<Renderer>();
            Vector3 vis = r != null ? r.bounds.center : t.position;
            t.position += new Vector3(c.x + off.Value.x - vis.x, c.y + off.Value.y - vis.y, 0f);

            // Rest on the surface: the deepest point of the mesh just touches it.
            MeshFilter mf = item.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                float dz = float.MaxValue;
                foreach (Vector3 v in mf.sharedMesh.vertices)
                {
                    Vector3 w = t.TransformPoint(v);
                    dz = Mathf.Min(dz, SurfaceZ(w.y) - w.z);
                }
                t.position += new Vector3(0f, 0f, dz - 0.01f);
            }
            log.Append($"{item.name} {t.position:F2}  ");
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Station3D] arranged: " + log);
    }

    /// <summary>One box over the whole model - a MeshCollider on gappy shapes
    /// (the loose peas, thin green onion) lets clicks fall through the gaps
    /// to the bowl behind, so the ingredient can't be picked up.</summary>
    private static void FitBoxCollider(GameObject go, Mesh mesh)
    {
        // Box first: Interactable requires a Collider, so the last one can't go.
        BoxCollider box = go.GetComponent<BoxCollider>();
        if (box == null) box = go.AddComponent<BoxCollider>();
        box.center = mesh.bounds.center;
        box.size = mesh.bounds.size;
        foreach (Collider c in go.GetComponents<Collider>())
            if (c != box) Object.DestroyImmediate(c);
    }

    /// <summary>Applies the latest fixes to the open scene and SampleScene
    /// without rebuilding (keeps hand edits): box colliders on the 3D
    /// ingredients, no hand-wash status text.</summary>
    [MenuItem("Tools/Kitchen/Apply Ingredient + Hand Wash Fixes")]
    public static void ApplyFixes()
    {
        var active = EditorSceneManager.GetActiveScene();
        Fix(active);

        if (active.path != SourceScene)
        {
            var sample = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Additive);
            Fix(sample);
            EditorSceneManager.SaveScene(sample);
            EditorSceneManager.CloseScene(sample, true);
        }
        Debug.Log($"[Station3D] Fixes applied - save {active.name} to keep them.");
    }

    private static void Fix(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (IngredientItem item in root.GetComponentsInChildren<IngredientItem>(true))
            {
                Mesh mesh = item.GetComponent<MeshFilter>().sharedMesh;
                if (mesh != null && mesh.name.EndsWith("_Unit")) FitBoxCollider(item.gameObject, mesh);
            }
            foreach (HandWashSequence seq in root.GetComponentsInChildren<HandWashSequence>(true))
            {
                if (seq.statusLabel != null) Object.DestroyImmediate(seq.statusLabel.gameObject);
                seq.statusLabel = null;
                if (seq.soapVisual != null) seq.soapVisual.transform.localScale = Vector3.one * HandWashSetup.FoamScale;
                EditorUtility.SetDirty(seq);
            }
        }
        SetupFaucetLever(scene);
        DisableHiddenBackgroundColliders(scene);
        SetupTissueBoxTowel(scene);
        ConfigureTowelDraining(scene);
        SetFlag(scene, new[] { "Pan_01a", "Wok" }, typeof(MovableBowl), "oilForFrying");      // half full of oil for frying
        ConfigurePeaBlanching(scene);
        RepairKnifeMesh(scene);
        SetupHangingRacks(scene);
        TameBurnerFlames(scene);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    /// <summary>The burners' click areas and flames drifted off the model's
    /// burners (the left one by ~2 units), so the flame showed up beside the
    /// burner instead of under the pot. Puts them back on Burner1/Burner2 of
    /// the station model (x/y only; depth is gameplay layering). Undoable.</summary>
    [MenuItem("Tools/Kitchen/Snap Burners To Model")]
    private static void SnapBurnersToModel()
    {
        var scene = EditorSceneManager.GetActiveScene();
        Transform station = FindStation(scene);
        if (station == null) { Debug.LogError("[Station3D] no Station3D in the open scene"); return; }
        foreach (string n in new[] { "Burner_L", "Flame_L", "Burner_R", "Flame_R" })
        {
            GameObject go = Find(n);
            if (go != null) Undo.RecordObject(go.transform, "Snap Burners");
        }
        MoveXY("Burner_L", Center(station, "Burner1_Base"));
        MoveXY("Flame_L", Center(station, "Burner1_Head"));
        MoveXY("Burner_R", Center(station, "Burner2_Base"));
        MoveXY("Flame_R", Center(station, "Burner2_Head"));
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Station3D] burners snapped: L={Find("Burner_L").transform.position:F2} R={Find("Burner_R").transform.position:F2}");
    }

    /// <summary>Blue gas flames around each burner head, one ring per flame
    /// level (child of that level's effect, so the burner's own on/off/level
    /// switching shows the right one): little camera-facing tongues standing
    /// up from the head's rim along the counter's up, blue at the base fading
    /// out at the tip, additive, no light cast. Re-running replaces them.</summary>
    [MenuItem("Tools/Kitchen/Add Gas Flame Rings")]
    private static void AddGasFlameRings()
    {
        var scene = EditorSceneManager.GetActiveScene();
        Transform station = FindStation(scene);
        if (station == null) { Debug.LogError("[Station3D] no Station3D in the open scene"); return; }

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Material mat = AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedDir}/GasFlame.mat");
        if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, $"{GeneratedDir}/GasFlame.mat"); }
        mat.shader = sh;
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 2f);                       // additive
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite", 0);
        mat.SetFloat("_Cull", 0f);
        mat.SetColor("_BaseColor", Color.white);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);

        var heights = new System.Collections.Generic.Dictionary<string, float> { { "flameWeak", 0.08f }, { "flameMedium", 0.24f }, { "flameStrong", 0.45f } };
        int made = 0;
        foreach (var (burnerName, headName) in new[] { ("Burner_L", "Burner1_Head"), ("Burner_R", "Burner2_Head") })
        {
            GameObject bgo = Find(burnerName);
            StoveBurner burner = bgo != null ? bgo.GetComponent<StoveBurner>() : null;
            Bounds head = RendererBounds(station, headName);
            if (burner == null || head.size == Vector3.zero) continue;

            // Head's top rim: the ring sits at its radius, on the counter's plane.
            Vector3 up = station.up, right = station.right, fwd = station.forward;
            float radius = head.extents.x > 0.01f ? head.extents.x : 0.3f;
            Vector3 center = head.center + up * (head.extents.y * 0.25f);

            var so = new SerializedObject(burner);
            foreach (var kv in heights)
            {
                GameObject level = so.FindProperty(kv.Key).objectReferenceValue as GameObject;
                if (level == null) continue;
                Transform old = level.transform.Find("GasFlame");
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

                var ring = new GameObject("GasFlame");
                Undo.RegisterCreatedObjectUndo(ring, "Gas Flame Ring");
                ring.transform.SetParent(level.transform, false);
                ring.transform.SetPositionAndRotation(center, station.rotation); // pivot on the ring's plane: the flicker grows tongues from their base
                Vector3 ls = level.transform.lossyScale;
                ring.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
                ring.AddComponent<MeshFilter>().sharedMesh = FlameRingMesh($"{burnerName}_{kv.Key}", ring.transform, center, radius, kv.Value, up, right, fwd);
                var mr = ring.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                ring.AddComponent<GasFlameFlicker>();
                made++;
            }
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Station3D] {made} gas flame ring(s) added - save the scene.");
    }

    private static Mesh FlameRingMesh(string name, Transform ring, Vector3 center, float radius, float height, Vector3 up, Vector3 right, Vector3 fwd)
    {
        const int tongues = 16;
        float width = 2f * Mathf.PI * radius / tongues * 0.75f;
        Color baseCol = new Color(0.25f, 0.45f, 1f, 0.95f), tipCol = new Color(0.55f, 0.75f, 1f, 0f);
        var v = new System.Collections.Generic.List<Vector3>();
        var c = new System.Collections.Generic.List<Color>();
        var tri = new System.Collections.Generic.List<int>();
        for (int i = 0; i < tongues; i++)
        {
            float a = i * Mathf.PI * 2f / tongues;
            Vector3 b = center + (right * Mathf.Cos(a) + fwd * Mathf.Sin(a)) * radius + Vector3.back * 0.03f; // a hair toward the camera
            float h = height * (0.8f + 0.4f * Mathf.PerlinNoise(i * 0.7f, 0.3f));
            int k = v.Count;
            v.Add(ring.InverseTransformPoint(b - Vector3.right * width * 0.5f)); c.Add(baseCol);
            v.Add(ring.InverseTransformPoint(b + Vector3.right * width * 0.5f)); c.Add(baseCol);
            v.Add(ring.InverseTransformPoint(b + up * h)); c.Add(tipCol);
            tri.Add(k); tri.Add(k + 2); tri.Add(k + 1);
        }
        string path = $"{GeneratedDir}/GasFlame_{name}.asset";
        Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (m == null) { m = new Mesh(); AssetDatabase.CreateAsset(m, path); }
        m.Clear();
        m.name = "GasFlame_" + name;
        m.SetVertices(v);
        m.SetColors(c);
        m.SetTriangles(tri, 0);
        m.RecalculateBounds();
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Lets MovableBowl.BodyCenterWorld read the long-handled pots/pans'
    /// meshes (Read/Write on their model import), so it can tell their round
    /// body (the wider end) from the handle - some models' pivots sit at the
    /// handle's end, and guessing by pivot put the handle over the burner.</summary>
    [MenuItem("Tools/Kitchen/Make Pan Meshes Readable")]
    private static void MakePanMeshesReadable()
    {
        int n = 0;
        foreach (MovableBowl b in Object.FindObjectsByType<MovableBowl>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!b.HoldsByHandle && b.GetComponent<WokDragHandler>() == null) continue;
            Mesh m = b.GetComponent<MeshFilter>()?.sharedMesh;
            string path = m != null ? AssetDatabase.GetAssetPath(m) : null;
            if (string.IsNullOrEmpty(path) || !(AssetImporter.GetAtPath(path) is ModelImporter imp) || imp.isReadable) continue;
            imp.isReadable = true;
            imp.SaveAndReimport();
            Debug.Log($"[Station3D] {b.name}: mesh readable ({path})");
            n++;
        }
        Debug.Log($"[Station3D] {n} pot/pan model(s) made readable.");
    }

    [MenuItem("Tools/Kitchen/Tame Burner Flames")]
    private static void TameBurnerFlamesMenu()
    {
        var scene = EditorSceneManager.GetActiveScene();
        TameBurnerFlames(scene);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    /// <summary>The burner flame VFX (a campfire-style pack) was far too loud
    /// for a gas ring: per-particle lights threw a big orange glow over the pot
    /// and counter, plus ash, sparks and heat-shimmer. Those are switched off and
    /// the flames themselves made smaller. Done once per effect (marked by name).</summary>
    private static void TameBurnerFlames(UnityEngine.SceneManagement.Scene scene)
    {
        const string Tamed = " (tamed)";
        int n = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (StoveBurner b in root.GetComponentsInChildren<StoveBurner>(true))
            {
                var so = new SerializedObject(b);
                foreach (string f in new[] { "flameWeak", "flameMedium", "flameStrong" })
                {
                    GameObject fx = so.FindProperty(f).objectReferenceValue as GameObject;
                    if (fx == null || fx.name.EndsWith(Tamed)) continue;
                    foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        Undo.RecordObject(ps, "Tame Burner Flames");
                        var lights = ps.lights;
                        lights.enabled = false;                       // the orange glow on everything around
                        string pn = ps.name.ToLower();
                        if (pn.Contains("ash") || pn.Contains("sparkle") || pn.Contains("distortion") || pn == "light" || pn == "lights")
                        {
                            Undo.RecordObject(ps.gameObject, "Tame Burner Flames");
                            ps.gameObject.SetActive(false);
                            continue;
                        }
                        var main = ps.main;
                        main.startSizeMultiplier *= 0.7f;
                    }
                    foreach (Light l in fx.GetComponentsInChildren<Light>(true)) { Undo.RecordObject(l, "Tame Burner Flames"); l.intensity *= 0.3f; }
                    Undo.RecordObject(fx, "Tame Burner Flames");
                    fx.name += Tamed;
                    n++;
                }
            }
        if (n > 0) Debug.Log($"[Station3D] {n} burner flame effect(s) toned down ({scene.name})");
    }

    /// <summary>Long wooden frying chopsticks and two strainers (체망 소/대) from
    /// Assets/Models/Utensils, real size in scene units, filed with the other
    /// cooking utensils; they start the exam hanging on the racks (chopsticks
    /// on HangingRack, strainers on HangingRack2). Safe to re-run.</summary>
    [MenuItem("Tools/Kitchen/Add Frying Chopsticks + Strainers")]
    private static void AddChopsticksAndStrainers()
    {
        var scene = EditorSceneManager.GetActiveScene();
        SetupHangingRacks(scene); // racks need their HangingRack first
        Transform group = null;
        HangingRack rack1 = null, rack2 = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Utensilholde" && group == null) group = t.parent;
                if (t.name == "HangingRack") rack1 = t.GetComponent<HangingRack>();
                if (t.name == "HangingRack2") rack2 = t.GetComponent<HangingRack>();
            }
        if (rack1 == null || rack2 == null) { Debug.LogError("[Station3D] HangingRack / HangingRack2 not found"); return; }

        Material mesh = LitMaterial("Utensil_StrainerMesh", new Color(0.72f, 0.73f, 0.75f, 0.45f), 0.9f, 0.6f);
        MakeTransparent(mesh);

        Carryable chop = AddUtensil(scene, "FryingChopsticks", typeof(FryingChopsticks), group, rack1.transform.position + Vector3.down * 1.5f, null, null);
        Carryable small = AddUtensil(scene, "Strainer_Small", typeof(Strainer), group, rack2.transform.position + Vector3.down * 1.5f, "Strainer_Mesh", mesh);
        Carryable large = AddUtensil(scene, "Strainer_Large", typeof(Strainer), group, rack2.transform.position + Vector3.down * 1.5f + Vector3.right * 0.8f, "Strainer_Mesh", mesh);

        SetStartItems(rack1, chop);
        SetStartItems(rack2, small, large);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[Station3D] frying chopsticks + 2 strainers added, hanging on the racks at start - save the scene.");
    }

    /// <summary>Scene units per meter for hand utensils: the scene's own utensils
    /// (whisk, spatula...) are sized at about half of MetersToUnits, so these match them.</summary>
    private const float UtensilUnits = 4.2f;

    private static Carryable AddUtensil(UnityEngine.SceneManagement.Scene scene, string model, System.Type type, Transform group,
                                        Vector3 position, string swapMaterialName, Material swapTo)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == model && t.GetComponent(type) != null)
                {
                    t.localScale = Vector3.one * UtensilUnits; // re-run: keep the size in line
                    return (Carryable)t.GetComponent(type);
                }

        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Utensils/{model}.fbx");
        if (fbx == null) { Debug.LogError($"[Station3D] {model}.fbx missing"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx, group);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        Undo.RegisterCreatedObjectUndo(go, "Add Utensil");
        go.name = model;
        go.transform.position = position;
        go.transform.localScale = go.transform.localScale * UtensilUnits;

        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                mats[i] = swapTo != null && mats[i].name == swapMaterialName ? swapTo
                    : LitMaterial("Utensil_" + mats[i].name, mats[i].color, mats[i].name.Contains("Rim") || mats[i].name.Contains("Mesh") ? 0.9f : 0f,
                                  mats[i].name.Contains("Rim") ? 0.8f : 0.4f);
            }
            r.sharedMaterials = mats;

            BoxCollider bc = go.AddComponent<BoxCollider>();
            Bounds lb = go.GetComponent<MeshFilter>().sharedMesh.bounds;
            bc.center = lb.center;
            bc.size = new Vector3(Mathf.Max(lb.size.x, 0.02f), lb.size.y, Mathf.Max(lb.size.z, 0.02f));
        }
        go.AddComponent(type);
        return (Carryable)go.GetComponent(type);
    }

    private const string WireMeshTexture = GeneratedDir + "/StrainerWireMesh.png";

    /// <summary>The strainers' basket as real wire mesh: a thin square wire grid
    /// (finer than the utensil holder's holes) with the gaps cut out, both
    /// faces drawn, silver metal. Replaces the old flat see-through grey.</summary>
    [MenuItem("Tools/Kitchen/Strainer Wire Mesh Material")]
    private static void StrainerWireMesh()
    {
        Material m = WireMeshMaterial();
        int n = 0;
        foreach (Strainer s in Object.FindObjectsByType<Strainer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Renderer r = s.GetComponent<Renderer>();
            if (r == null) continue;
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && (mats[i].name.Contains("StrainerMesh") || mats[i].name.Contains("Strainer_Mesh") || mats[i].name.Contains("WireMesh")))
                    mats[i] = m;
            Undo.RecordObject(r, "Strainer Wire Mesh");
            r.sharedMaterials = mats;
            n++;
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[Station3D] wire mesh on {n} strainer(s) - save the scene.");
    }

    private static Material WireMeshMaterial()
    {
        // 64px tile: an 8x8 grid of 8px cells, wires 2px wide (alpha = wire).
        const int size = 64, cell = 8, wire = 2;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool onWire = x % cell < wire || y % cell < wire;
                tex.SetPixel(x, y, onWire ? Color.white : new Color(1f, 1f, 1f, 0f));
            }
        tex.Apply();
        File.WriteAllBytes(WireMeshTexture, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(WireMeshTexture);

        var imp = (TextureImporter)AssetImporter.GetAtPath(WireMeshTexture);
        imp.alphaIsTransparency = true;
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.mipmapEnabled = true;
        imp.mipMapsPreserveCoverage = true;      // thin wires don't fade out at a distance
        imp.alphaTestReferenceValue = 0.5f;
        imp.filterMode = FilterMode.Bilinear;
        imp.SaveAndReimport();

        Material m = LitMaterial("Utensil_StrainerWireMesh", new Color(0.78f, 0.79f, 0.81f, 1f), 0.85f, 0.6f);
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(WireMeshTexture));
        m.SetTextureScale("_BaseMap", new Vector2(14f, 7f)); // cells across the basket
        m.SetFloat("_Surface", 0f);
        m.SetFloat("_AlphaClip", 1f);
        m.SetFloat("_Cutoff", 0.5f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.SetFloat("_Cull", 0f);                 // inside and outside of the basket
        m.SetOverrideTag("RenderType", "TransparentCutout");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
        return m;
    }

    private static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
    }

    private static void SetStartItems(HangingRack rack, params Carryable[] items)
    {
        var so = new SerializedObject(rack);
        SerializedProperty p = so.FindProperty("startItems");
        p.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        so.ApplyModifiedProperties();
    }

    [MenuItem("Tools/Kitchen/Setup Hanging Racks")]
    private static void SetupHangingRacksMenu()
    {
        var scene = EditorSceneManager.GetActiveScene();
        SetupHangingRacks(scene);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    /// <summary>HangingRack / HangingRack2 (wall rails) become utensil hooks:
    /// a click/drop area over the rail and the space below it where things
    /// hang, plus the HangingRack component. Safe to re-run.</summary>
    private static void SetupHangingRacks(UnityEngine.SceneManagement.Scene scene)
    {
        const float hangDepth = 1.3f; // how far below the rail the click area reaches
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("HangingRack")) continue;
                Renderer r = t.GetComponent<Renderer>();
                if (r == null) continue;

                BoxCollider bc = t.GetComponent<BoxCollider>();
                if (bc == null) bc = Undo.AddComponent<BoxCollider>(t.gameObject);
                Bounds w = r.bounds;
                Vector3 worldCenter = new Vector3(w.center.x, w.center.y - hangDepth * 0.5f, w.center.z);
                Vector3 worldSize = new Vector3(w.size.x, w.size.y + hangDepth, Mathf.Max(w.size.z, 0.3f));
                bc.center = t.InverseTransformPoint(worldCenter);
                Vector3 ls = t.InverseTransformVector(worldSize);
                bc.size = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));

                if (t.GetComponent<HangingRack>() == null) Undo.AddComponent<HangingRack>(t.gameObject);
                EditorUtility.SetDirty(t.gameObject);
                Debug.Log($"[Station3D] {t.name}: utensil hooks ready ({scene.name})");
            }
    }

    private const string KnifeFbx = "Assets/NEW/ChineseCleaver.fbx";

    /// <summary>The knife's mesh asset was deleted from the project, leaving the
    /// knife invisible (only its handle overlay drew). Same model, re-supplied as
    /// Assets/NEW/ChineseCleaver.fbx: its mesh goes back in, transform untouched
    /// (the knife's 150x scale fits it as before). Materials only if missing.</summary>
    private static void RepairKnifeMesh(UnityEngine.SceneManagement.Scene scene)
    {
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(KnifeFbx);
        MeshFilter srcMf = fbx != null ? fbx.GetComponentInChildren<MeshFilter>(true) : null;
        if (srcMf == null) return;
        Renderer srcRend = srcMf.GetComponent<Renderer>();

        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (ToolItem t in root.GetComponentsInChildren<ToolItem>(true))
            {
                if (t.Kind != ToolKind.Knife) continue;
                MeshFilter mf = t.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh != null) continue;

                Undo.RecordObject(mf, "Repair Knife Mesh");
                mf.sharedMesh = srcMf.sharedMesh;
                if (t.TryGetComponent(out MeshCollider mc)) { Undo.RecordObject(mc, "Repair Knife Mesh"); mc.sharedMesh = srcMf.sharedMesh; }
                Renderer r = t.GetComponent<Renderer>();
                if (r != null && (r.sharedMaterial == null)) r.sharedMaterials = srcRend.sharedMaterials;
                EditorUtility.SetDirty(t.gameObject);
                Debug.Log($"[Station3D] {t.name}: knife mesh restored from {KnifeFbx} ({scene.name})");
            }
    }

    /// <summary>Green peas: blanched in boiling water, deeper green at 10 s,
    /// overcooked olive-brown at 40 s.</summary>
    /// <summary>Real exam heating: the pot's 0.5-1 L of water boils in 5 min on a
    /// strong flame (3.5 with the lid), the wok's ~1 L of oil reaches frying heat
    /// in 7 min (the scene kept the old 15 s / 8 s test values).</summary>
    [MenuItem("Tools/Kitchen/Set Real Heating Times (water 5 min, oil 7 min)")]
    private static void SetRealHeatingTimes()
    {
        int n = 0;
        foreach (MovableBowl b in Object.FindObjectsByType<MovableBowl>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(b);
            so.FindProperty("timeToBoilNoLid").floatValue = 300f;
            so.FindProperty("timeToBoilWithLid").floatValue = 210f;
            so.FindProperty("timeToHeatOil").floatValue = 180f;
            so.ApplyModifiedProperties();
            n++;
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[Station3D] heating times set on {n} vessels: water 300 s (lid 210 s), oil 420 s");
    }

    /// <summary>두부 데치기 (마파두부): tofu in a pot of boiling water firms up
    /// - done after 15 s, a little creamier; overdone (rubbery, yellowed) past 60 s.
    /// It had no cooking set up at all, so it never changed in the water.</summary>
    [MenuItem("Tools/Kitchen/Setup Tofu Blanching")]
    private static void SetupTofuBlanching()
    {
        int n = 0;
        foreach (IngredientItem ing in Object.FindObjectsByType<IngredientItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!ing.name.Contains("Tofu") && !ing.name.Contains("두부")) continue;
            CookableItem c = ing.GetComponent<CookableItem>() ?? Undo.AddComponent<CookableItem>(ing.gameObject);
            var so = new SerializedObject(c);
            so.FindProperty("process").enumValueIndex = (int)CookProcess.Blanch;
            so.FindProperty("timeToCooked").floatValue = 15f;
            so.FindProperty("timeToOvercooked").floatValue = 60f;
            so.FindProperty("doneColor").colorValue = new Color(0.97f, 0.94f, 0.82f, 1f);
            so.FindProperty("overcookedColor").colorValue = new Color(0.88f, 0.80f, 0.55f, 1f);
            so.ApplyModifiedProperties();
            n++;
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[Station3D] tofu blanching set on {n} item(s)");
    }

    private static void ConfigurePeaBlanching(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (CookableItem c in root.GetComponentsInChildren<CookableItem>(true))
            {
                if (!c.name.Contains("완두콩") && !c.name.ToLower().Contains("pea")) continue;
                var so = new SerializedObject(c);
                so.FindProperty("process").enumValueIndex = (int)CookProcess.Blanch;
                so.FindProperty("timeToCooked").floatValue = 10f;
                so.FindProperty("timeToOvercooked").floatValue = 40f;
                so.FindProperty("doneColor").colorValue = new Color(0.12f, 0.42f, 0.07f, 1f);
                so.ApplyModifiedProperties();
                Debug.Log($"[Station3D] {c.name}: blanch 10 s done, 40 s overcooked ({scene.name})");
            }
    }

    /// <summary>Brings the cabinet body (Body + Body_Top* frame around the
    /// sink recess) in the open scene up to date with the re-exported FBX,
    /// without rebuilding the scene (so every other hand edit stays). The
    /// scene's station is an unpacked copy: meshes follow the FBX, but each
    /// part's own transform and any new parts don't.</summary>
    [MenuItem("Tools/Kitchen/Update Station Body (Sink Recess)")]
    private static void UpdateStationBody()
    {
        var scene = EditorSceneManager.GetActiveScene();
        Transform station = FindStation(scene);
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(StationFbx);
        if (station == null || fbx == null) { Debug.LogError("[Station3D] needs Station3D in the open scene and " + StationFbx); return; }

        Transform sceneBody = null, assetBody = null;
        foreach (Transform t in station.GetComponentsInChildren<Transform>(true)) if (t.name == "Body") sceneBody = t;
        foreach (Transform t in fbx.GetComponentsInChildren<Transform>(true)) if (t.name == "Body") assetBody = t;
        if (sceneBody == null || assetBody == null) { Debug.LogError("[Station3D] Body not found"); return; }

        Material[] mats = sceneBody.GetComponent<Renderer>().sharedMaterials;
        int updated = 0;
        foreach (Transform part in assetBody.parent)
        {
            if (!part.name.StartsWith("Body")) continue;
            Transform target = sceneBody.parent.Find(part.name);
            if (target == null)
            {
                target = Object.Instantiate(part.gameObject, sceneBody.parent).transform;
                target.name = part.name;
                Undo.RegisterCreatedObjectUndo(target.gameObject, "Update Station Body");
                target.GetComponent<Renderer>().sharedMaterials = mats;
            }
            else Undo.RecordObject(target, "Update Station Body");
            target.localPosition = part.localPosition;
            target.localRotation = part.localRotation;
            target.localScale = part.localScale;
            updated++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Station3D] Station body updated ({updated} parts) - the sink is recessed now. Save the scene.");
    }

    /// <summary>The old flat art hidden in the 3D scene (floor, counter body,
    /// stove body...) kept invisible colliders that swallowed clicks meant for
    /// whatever sits behind them - e.g. the pots on the lower shelf, behind the
    /// floor quad. Ones with no interaction of their own are switched off
    /// (a click on empty space still sets the held item down without them);
    /// the sink, faucet and burners keep theirs. Only where the 3D station exists.</summary>
    private static void DisableHiddenBackgroundColliders(UnityEngine.SceneManagement.Scene scene)
    {
        bool has3D = false;
        has3D = FindStation(scene) != null;
        if (!has3D) return;

        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (System.Array.IndexOf(HiddenBackground, t.name) < 0) continue;
                if (t.GetComponent<Interactable>() != null) continue;
                Renderer r = t.GetComponent<Renderer>();
                if (r != null && r.enabled) continue; // still shown - leave it as it is
                foreach (Collider c in t.GetComponents<Collider>())
                {
                    if (!c.enabled) continue;
                    c.enabled = false;
                    EditorUtility.SetDirty(c);
                    Debug.Log($"[Station3D] collider off: {t.name} ({scene.name})");
                }
            }
    }

    /// <summary>The TissueBox placed in the scene (from Assets/NEW) becomes the
    /// kitchen towel: clicking it pulls one sheet into the hand. The old towel
    /// roll (Tool_KitchenTowel) is hidden and only used as the sheet's template.
    /// Skipped in a scene without a TissueBox.</summary>
    [MenuItem("Tools/Kitchen/Relink Tissue Box Towel")]
    private static void RelinkTissueBox()
    {
        var scene = EditorSceneManager.GetActiveScene();
        EnsureTowelTemplate(scene);
        SetupTissueBoxTowel(scene);
        KitchenTowelDispenser d = Object.FindFirstObjectByType<KitchenTowelDispenser>(FindObjectsInactive.Include);
        Object t = d != null ? new SerializedObject(d).FindProperty("sheetTemplate").objectReferenceValue : null;
        Debug.Log($"[Station3D] tissue box {(d ? d.name : "NONE")} template={(t ? t.name : "NONE")}");
        foreach (ToolItem ti in Object.FindObjectsByType<ToolItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (ti.IsTowelKind || ti.name.ToLower().Contains("towel"))
                Debug.Log($"[Station3D] towel-like tool: {ti.name} kind={ti.Kind} active={ti.gameObject.activeSelf} parent={ti.transform.parent?.name}");
        EditorSceneManager.MarkSceneDirty(scene);
    }

    /// <summary>The towel-sheet template (Tool_KitchenTowel, hidden) was gone from
    /// the scene, so the tissue box had nothing to pull a sheet from. Rebuilt
    /// like the original: a small white roll, a PaperTowel ToolItem.</summary>
    private static void EnsureTowelTemplate(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (ToolItem t in root.GetComponentsInChildren<ToolItem>(true))
                if (t.name == "Tool_KitchenTowel") return;

        GameObject box = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (box == null && t.name.StartsWith("TissueBox")) box = t.gameObject;

        const string matPath = "Assets/Generated/KitchenTowelSheet.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.97f, 0.97f, 0.95f) };
            AssetDatabase.CreateAsset(mat, matPath);
        }

        GameObject towel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        towel.name = "Tool_KitchenTowel";
        Object.DestroyImmediate(towel.GetComponent<Collider>());
        towel.AddComponent<BoxCollider>();
        towel.GetComponent<Renderer>().sharedMaterial = mat;
        if (box != null)
        {
            towel.transform.SetParent(box.transform.parent, false);
            towel.transform.position = box.transform.position;
        }
        towel.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        towel.transform.localScale = Vector3.one * 0.35f;
        ToolItem tool = towel.AddComponent<ToolItem>();
        var so = new SerializedObject(tool);
        so.FindProperty("kind").enumValueIndex = (int)ToolKind.PaperTowel;
        so.FindProperty("handHoldOffset").vector3Value = new Vector3(0f, 0.35f, -0.25f);
        so.ApplyModifiedProperties();
        Undo.RegisterCreatedObjectUndo(towel, "Kitchen towel template");
        Debug.Log("[Station3D] rebuilt the missing Tool_KitchenTowel (towel sheet template)");
    }

    private static void SetupTissueBoxTowel(UnityEngine.SceneManagement.Scene scene)
    {
        GameObject box = null;
        ToolItem template = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (box == null && t.name.StartsWith("TissueBox")) box = t.gameObject;
                if (template == null && t.name == "Tool_KitchenTowel") template = t.GetComponent<ToolItem>();
            }
        if (box == null || template == null) return;

        if (box.GetComponent<Collider>() == null)
        {
            // Box collider over the whole model, in the box's own space.
            BoxCollider bc = box.AddComponent<BoxCollider>();
            Renderer[] rs = box.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                Bounds wb = rs[0].bounds;
                foreach (Renderer r in rs) wb.Encapsulate(r.bounds);
                Vector3 c = box.transform.InverseTransformPoint(wb.center);
                Vector3 s = box.transform.InverseTransformVector(wb.size);
                bc.center = c;
                bc.size = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            }
        }

        KitchenTowelDispenser dispenser = box.GetComponent<KitchenTowelDispenser>();
        if (dispenser == null) dispenser = box.AddComponent<KitchenTowelDispenser>();
        var so = new SerializedObject(dispenser);
        so.FindProperty("sheetTemplate").objectReferenceValue = template;
        so.ApplyModifiedProperties();

        template.gameObject.SetActive(false);
        EditorUtility.SetDirty(box);
        Debug.Log($"[Station3D] {box.name} is the kitchen towel now (sheets from {template.name}, hidden) - {scene.name}");
    }

    /// <summary>Meat (blood) and tofu (water) have to drain on a spread paper
    /// towel before going in the wok/pan - IngredientItem.drainOnTowel.</summary>
    private static void ConfigureTowelDraining(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (IngredientItem item in root.GetComponentsInChildren<IngredientItem>(true))
            {
                MeshFilter mf = item.GetComponent<MeshFilter>();
                string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "";
                string n = item.name + " " + item.IngredientName + " " + mesh;
                IngredientItem.DrainNeed need =
                    n.Contains("돼지고기") || n.Contains("돼지등심") || n.Contains("Beef") || n.Contains("MincedMeat") || n.Contains("다진고기")
                        ? IngredientItem.DrainNeed.Blood
                    : n.Contains("Tofu") || n.Contains("두부") ? IngredientItem.DrainNeed.Moisture
                    : IngredientItem.DrainNeed.None;
                if (need == IngredientItem.DrainNeed.None) continue;

                var so = new SerializedObject(item);
                so.FindProperty("drainOnTowel").enumValueIndex = (int)need;
                so.ApplyModifiedProperties();
                Debug.Log($"[Station3D] {item.name}: drains {need} on a paper towel ({scene.name})");
            }
    }

    private static void SetFlag(UnityEngine.SceneManagement.Scene scene, string[] objectNames, System.Type type, string field)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Component c in root.GetComponentsInChildren(type, true))
            {
                if (System.Array.IndexOf(objectNames, c.name) < 0) continue;
                var so = new SerializedObject(c);
                so.FindProperty(field).boolValue = true;
                so.ApplyModifiedProperties();
                return;
            }
        Debug.LogWarning($"[Station3D] {string.Join("/", objectNames)} ({type.Name}) not found in {scene.name}");
    }

    /// <summary>The model's faucet lever becomes the water control: turned on
    /// a pivot at its back end by FaucetLever (down = running, up = off), with
    /// the WaterTap's own click collider moved over it. Only for a scene with
    /// the 3D station; safe to re-run.</summary>
    /// <summary>The 3D station's root wherever it sits (it may be filed under a
    /// hierarchy group by Tools > Kitchen > Organize Hierarchy).</summary>
    private static Transform FindStation(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "Station3D") return t;
        return null;
    }

    [MenuItem("Tools/Kitchen/Setup Faucet Lever")]
    private static void SetupFaucetLeverMenu() => SetupFaucetLever(EditorSceneManager.GetActiveScene());

    private static void SetupFaucetLever(UnityEngine.SceneManagement.Scene scene)
    {
        Transform station = FindStation(scene);
        WaterTap tap = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (tap == null) tap = root.GetComponentInChildren<WaterTap>(true);
        if (station == null || tap == null) return;

        MeshRenderer lever = null, faucetBase = null;
        foreach (MeshRenderer r in station.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.name == "Faucet_Lever") lever = r;
            if (r.name == "Faucet_Base") faucetBase = r;
        }
        if (lever == null || faucetBase == null) { Debug.LogWarning("[Station3D] Faucet_Lever/Faucet_Base not in the model"); return; }

        // Lever's long axis (world) and its back end - the end at the faucet body.
        Bounds lb = lever.GetComponent<MeshFilter>().sharedMesh.bounds;
        Vector3 best = Vector3.right;
        float bestLen = 0f;
        foreach (Vector3 a in new[] { Vector3.right, Vector3.up, Vector3.forward })
        {
            float len = lever.transform.TransformVector(Vector3.Scale(a, lb.extents)).magnitude;
            if (len > bestLen) { bestLen = len; best = a; }
        }
        Vector3 e1 = lever.transform.TransformPoint(lb.center + Vector3.Scale(best, lb.extents));
        Vector3 e2 = lever.transform.TransformPoint(lb.center - Vector3.Scale(best, lb.extents));
        Vector3 baseC = faucetBase.bounds.center;
        Vector3 back = (e1 - baseC).sqrMagnitude < (e2 - baseC).sqrMagnitude ? e1 : e2;
        Vector3 tip = back == e1 ? e2 : e1;

        FaucetLever handle = lever.GetComponentInParent<FaucetLever>();
        Transform pivot;
        if (handle == null)
        {
            pivot = new GameObject("Faucet_LeverPivot").transform;
            pivot.SetParent(lever.transform.parent, false);
            pivot.SetPositionAndRotation(back, station.rotation);
            lever.transform.SetParent(pivot, true);
            handle = pivot.gameObject.AddComponent<FaucetLever>();
        }
        else pivot = handle.transform;

        // Raised/lowered = turned about the axis across the lever, level with
        // the counter (perpendicular to both the lever and the counter's up).
        Vector3 axisWorld = Vector3.Cross(station.up, (tip - back).normalized).normalized;
        Vector3 lifted = back + Quaternion.AngleAxis(30f, axisWorld) * (tip - back);
        float off = Vector3.Dot(lifted - tip, station.up) > 0f ? 30f : -30f;

        var so = new SerializedObject(handle);
        so.FindProperty("tap").objectReferenceValue = tap;
        so.FindProperty("axis").vector3Value = pivot.InverseTransformDirection(axisWorld);
        so.FindProperty("onAngle").floatValue = 0f;
        so.FindProperty("offAngle").floatValue = off;
        so.ApplyModifiedProperties();

        // The tap's click area goes over the lever (filling uses the stream, not this).
        Bounds b = lever.bounds;
        Transform t = tap.transform;
        t.position = new Vector3(b.center.x, b.center.y, t.position.z);
        t.rotation = Quaternion.identity;
        t.localScale = new Vector3(Mathf.Max(b.size.x, 0.6f), Mathf.Max(b.size.y, 0.45f), 1f);
        EditorUtility.SetDirty(t);
        Debug.Log($"[Station3D] Faucet lever ready ({scene.name}) - the tap is now clicked at the lever.");
    }

    /// <summary>Copy of the Blender mesh: tilted like the station, centered,
    /// fitted to the 1x1 (XY) footprint the old quads had.</summary>
    private static Mesh UnitMesh(Mesh source, string model, Transform importNode)
    {
        string path = $"{GeneratedDir}/{model}_Unit.asset";
        Quaternion rot = Quaternion.Euler(-TiltDegrees, 0f, 0f) * importNode.localRotation;
        Vector3[] v = source.vertices;
        Vector3[] n = source.normals;
        for (int i = 0; i < v.Length; i++)
        {
            v[i] = rot * Vector3.Scale(v[i], importNode.localScale);
            if (n.Length > i) n[i] = (rot * n[i]).normalized;
        }
        var b = new Bounds(v[0], Vector3.zero);
        foreach (Vector3 p in v) b.Encapsulate(p);
        float k = 1f / Mathf.Max(b.size.x, b.size.y);
        for (int i = 0; i < v.Length; i++) v[i] = (v[i] - b.center) * k;

        Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (m == null) { m = new Mesh(); AssetDatabase.CreateAsset(m, path); }
        m.Clear();
        m.name = model + "_Unit";
        m.vertices = v;
        if (n.Length == v.Length) m.normals = n;
        m.uv = source.uv;
        m.subMeshCount = source.subMeshCount;
        for (int s = 0; s < source.subMeshCount; s++) m.SetTriangles(source.GetTriangles(s), s);
        if (n.Length != v.Length) m.RecalculateNormals();
        m.RecalculateBounds();
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------------- helpers ----------------

    private static GameObject Find(string name)
    {
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == name) return root;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
        }
        return null;
    }

    private static void MoveXY(string name, Vector3 target)
    {
        GameObject go = Find(name);
        if (go == null) { Debug.LogWarning($"[Station3D] {name} not found"); return; }
        Vector3 p = go.transform.position;
        go.transform.position = new Vector3(target.x, target.y, p.z);
    }

    private static Bounds RendererBounds(Transform root, string child)
    {
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            if (r.name == child) return r.bounds;
        Debug.LogWarning($"[Station3D] model part {child} not found");
        return new Bounds(root.position, Vector3.zero);
    }

    private static Vector3 Center(Transform root, string child) => RendererBounds(root, child).center;

    private static Bounds Bounds(Transform root)
    {
        Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    private static void AddToBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == TargetScene)) return;
        scenes.Add(new EditorBuildSettingsScene(TargetScene, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
