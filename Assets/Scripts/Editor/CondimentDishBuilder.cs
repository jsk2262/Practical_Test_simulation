using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the 종지 dishes for the seasonings that aren't in a shelf bottle
/// (CondimentDish): 감자전분, 고춧가루, 겨자 / 두반장, 춘장, 굴소스, 케첩. The old
/// Plate_RedChiliPowder / Plate_Doubanjiang (whose click box sat apart from
/// what showed on screen) are switched off. Each dish is one object - model,
/// collider and content together - placed on free spots of the counter.
/// </summary>
public static class CondimentDishBuilder
{
    private static readonly (string name, string ko)[] Dishes =
    {
        ("PotatoStarch", "감자전분"), ("RedChiliPowder", "고춧가루"), ("Mustard", "겨자"),
        ("Doubanjiang", "두반장"), ("Chunjang", "춘장"), ("OysterSauce", "굴소스"), ("Ketchup", "케첩"),
    };

    private const string ModelPath = "Assets/Mugs, Bowls and Plates/Models/plate02.fbx";
    private const float DishScale = 1.1f;

    [MenuItem("Tools/Kitchen/Build Condiment Dishes")]
    private static void Build()
    {
        Mesh mesh = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ModelPath)) if (o is Mesh m) { mesh = m; break; }
        // The project's own (URP) plate02 material - the one inside the .fbx is a
        // built-in Standard one, which renders magenta here.
        Material mat = null;
        foreach (string guid in AssetDatabase.FindAssets("plate02 t:Material"))
        {
            mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (mat != null) break;
        }
        GameObject oldChili = FindIncludingInactive("Plate_RedChiliPowder");
        if (mesh == null) { Debug.LogError("[Condiments] plate02 mesh not found"); return; }

        Quaternion rot = oldChili != null ? oldChili.transform.rotation : Quaternion.Euler(321f, 0f, 0f);
        foreach (string old in new[] { "Plate_RedChiliPowder ", "Plate_RedChiliPowder", "Plate_Doubanjiang ", "Plate_Doubanjiang" })
        {
            GameObject go = GameObject.Find(old);
            if (go != null) { Undo.RecordObject(go, "Hide old dish"); go.SetActive(false); }
        }

        GameObject sauces = GameObject.Find("Sauces");
        GameObject existing = GameObject.Find("CondimentDishes");
        if (existing != null) Undo.DestroyObjectImmediate(existing);
        GameObject root = new GameObject("CondimentDishes");
        Undo.RegisterCreatedObjectUndo(root, "Build Condiment Dishes");
        if (sauces != null) root.transform.SetParent(sauces.transform, false);

        // Content frame from the model itself: its thinnest axis is the depth (up).
        Bounds lb = mesh.bounds;
        Vector3 size = lb.size;
        int up = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
        int a = (up + 1) % 3, b = (up + 2) % 3;
        Vector3 upLocal = Vector3.zero; upLocal[up] = 1f;
        // The open side faces the camera-ish (up and toward it) in the resting pose.
        if (Vector3.Dot(rot * upLocal, new Vector3(0f, 0.6f, -1f)) < 0f) upLocal = -upLocal;
        float radius = Mathf.Min(size[a], size[b]) * 0.5f * 0.7f;
        Vector3 fillCenter = lb.center + upLocal * size[up] * 0.1f;

        // The exam sheets list what's supplied (SauceShelfController hides the
        // rest): 마파두부 uses 두반장·고춧가루(고추기름)·감자전분, 탕수육 감자전분.
        var required = new HashSet<string>();
        foreach (ExamPaperFlip page in Object.FindObjectsByType<ExamPaperFlip>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string[] add = page.DishName.Contains("마파") ? new[] { "두반장", "고춧가루", "감자전분" }
                         : page.DishName.Contains("탕수") ? new[] { "감자전분" } : new string[0];
            var pso = new SerializedObject(page);
            SerializedProperty list = pso.FindProperty("requiredSeasonings");
            foreach (string s in add)
            {
                bool has = false;
                for (int k = 0; k < list.arraySize; k++) if (list.GetArrayElementAtIndex(k).stringValue == s) has = true;
                if (has) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).stringValue = s;
            }
            pso.ApplyModifiedProperties();
            foreach (string s in page.RequiredSeasonings) required.Add(s);
            Debug.Log($"[Condiments] {page.DishName}: {string.Join(",", page.RequiredSeasonings)}");
        }

        // Today's dishes first, so they sit together on the nearest free spots.
        var ordered = new List<(string name, string ko)>(Dishes);
        ordered.Sort((x, y) => required.Contains(y.ko).CompareTo(required.Contains(x.ko)));

        Physics.SyncTransforms();
        spotsRoot = root.transform;
        List<Vector2> spots = FreeSpots(Dishes.Length, mesh.bounds.size.x * DishScale);
        for (int i = 0; i < Dishes.Length; i++)
        {
            var (en, ko) = ordered[i];
            GameObject dish = new GameObject("Dish_" + en);
            dish.transform.SetParent(root.transform, false);
            Vector2 p = i < spots.Count ? spots[i] : new Vector2(-1f + i * 0.6f, 2.2f);
            dish.transform.position = new Vector3(p.x, p.y, CounterZ(p.y));
            dish.transform.rotation = rot;
            dish.transform.localScale = Vector3.one * DishScale;
            dish.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = dish.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            BoxCollider box = dish.AddComponent<BoxCollider>();
            box.center = lb.center;
            box.size = lb.size;

            CondimentDish cd = dish.AddComponent<CondimentDish>();
            var so = new SerializedObject(cd);
            so.FindProperty("seasoningName").stringValue = ko;
            so.FindProperty("fillRadius").floatValue = radius;
            so.FindProperty("fillLocalCenter").vector3Value = fillCenter;
            so.FindProperty("fillLocalUp").vector3Value = upLocal;
            so.FindProperty("isMustard").boolValue = ko == "겨자";
            so.ApplyModifiedProperties();

            // A name label under the dish, like the shelf bottles have.
            GameObject label = new GameObject("Label_" + en);
            label.transform.SetParent(dish.transform, false); // hidden along with its dish
            label.transform.position = dish.transform.position + new Vector3(0f, -0.38f, -0.3f);
            label.transform.rotation = Quaternion.identity;
            label.transform.localScale = Vector3.one / DishScale;
            TextMesh tm = label.AddComponent<TextMesh>();
            tm.text = ko;
            tm.fontSize = 40;
            tm.characterSize = 0.035f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(0.15f, 0.15f, 0.15f);
            Undo.RegisterCreatedObjectUndo(dish, "dish");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[Condiments] material {(mat ? mat.name + "/" + mat.shader.name + " " + AssetDatabase.GetAssetPath(mat) : "none")}");
        Debug.Log($"[Condiments] built {Dishes.Length} dishes at {string.Join(" ", spots)} - save the scene.");
    }

    /// <summary>The measuring cup as a water vessel (MovableBowl that holds
    /// water, carried by its handle): filled under the faucet like a bowl, and
    /// the measuring spoon dips one spoonful of water out of it (겨자 개기).
    /// Its model was a bare child of an empty object - the model itself
    /// becomes the cup, so its click box sits right where it shows.</summary>
    [MenuItem("Tools/Kitchen/Setup Measuring Cup (water)")]
    private static void SetupMeasuringCup()
    {
        GameObject root = FindIncludingInactive("measuringCup");
        if (root == null) { Debug.LogError("[Condiments] measuringCup not found"); return; }
        GameObject cup = root;
        if (root.GetComponent<Renderer>() == null)
        {
            Renderer child = root.GetComponentInChildren<Renderer>(true);
            if (child == null) { Debug.LogError("[Condiments] measuringCup has no model"); return; }
            cup = child.gameObject;
            Undo.SetTransformParent(cup.transform, root.transform.parent, "Measuring cup");
            cup.name = "measuringCup";
            if (root.transform.childCount == 0) Undo.DestroyObjectImmediate(root);
            else root.name = "measuringCup_old";
        }

        Bounds lb = cup.GetComponent<Renderer>().localBounds;
        foreach (Collider c in cup.GetComponents<Collider>()) Undo.DestroyObjectImmediate(c);
        BoxCollider box = Undo.AddComponent<BoxCollider>(cup);
        box.center = lb.center;
        box.size = lb.size;

        MovableBowl bowl = cup.GetComponent<MovableBowl>() ?? Undo.AddComponent<MovableBowl>(cup);
        var so = new SerializedObject(bowl);
        so.FindProperty("canHoldWater").boolValue = true;
        so.FindProperty("holdByHandle").boolValue = true;
        so.ApplyModifiedProperties();

        MeshFilter mf = cup.GetComponent<MeshFilter>();
        EditorSceneManager.MarkSceneDirty(cup.scene);
        Debug.Log($"[Condiments] measuringCup ready at {cup.transform.position:F2} bounds={cup.GetComponent<Renderer>().bounds.size:F2} readable={(mf && mf.sharedMesh ? mf.sharedMesh.isReadable : false)} - save the scene.");
    }

    /// <summary>The 종지 as bowlSize1 bowls (that model, its material, size and
    /// resting tilt), left where they are on the counter - only their depth
    /// follows the tilted counter top.</summary>
    [MenuItem("Tools/Kitchen/Condiment Dishes As bowlSize1")]
    private static void UseBowlSize1()
    {
        GameObject src = FindIncludingInactive("bowlSize1");
        Renderer srcRend = src != null ? src.GetComponentInChildren<Renderer>(true) : null;
        MeshFilter srcMf = srcRend != null ? srcRend.GetComponent<MeshFilter>() : null;
        if (srcMf == null || srcMf.sharedMesh == null) { Debug.LogError("[Condiments] bowlSize1 model not found"); return; }

        var log = new System.Text.StringBuilder();
        foreach (CondimentDish dish in Object.FindObjectsByType<CondimentDish>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.RecordObject(dish.transform, "bowlSize1 dish");
            ApplyModel(dish, srcRend, srcMf.sharedMesh);
            Vector3 p = dish.transform.position;
            float lift = src.transform.position.z - HandCarry.CounterZ(src.transform.position.y);
            dish.transform.position = new Vector3(p.x, p.y, HandCarry.CounterZ(p.y) + lift);
            log.Append($"{dish.name}{(dish.gameObject.activeSelf ? "" : "(hidden)")} {dish.transform.position:F2}  ");
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Condiments] now bowlSize1: " + log);
    }

    /// <summary>Gives a 종지 the model of srcRend (mesh, material, world tilt and
    /// size), its click box, the content frame inside it and its label below.</summary>
    private static void ApplyModel(CondimentDish dish, Renderer srcRend, Mesh mesh)
    {
        GameObject go = dish.gameObject;
        MeshFilter mf = go.GetComponent<MeshFilter>() ?? Undo.AddComponent<MeshFilter>(go);
        MeshRenderer mr = go.GetComponent<MeshRenderer>() ?? Undo.AddComponent<MeshRenderer>(go);
        Undo.RecordObject(mf, "model"); Undo.RecordObject(mr, "model");
        mf.sharedMesh = mesh;
        mr.sharedMaterials = srcRend.sharedMaterials;

        Transform t = go.transform;
        t.rotation = srcRend.transform.rotation;
        Vector3 ps = t.parent != null ? t.parent.lossyScale : Vector3.one;
        Vector3 ws = srcRend.transform.lossyScale;
        t.localScale = new Vector3(ws.x / ps.x, ws.y / ps.y, ws.z / ps.z);

        Bounds lb = mesh.bounds;
        BoxCollider box = go.GetComponent<BoxCollider>() ?? Undo.AddComponent<BoxCollider>(go);
        Undo.RecordObject(box, "model");
        box.center = lb.center;
        box.size = lb.size;

        Vector3 size = lb.size;
        int up = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
        int a = (up + 1) % 3, b = (up + 2) % 3;
        // A bowl is about as deep as it is wide: its up is the axis that points
        // most up/toward the camera in its resting pose, not just the thinnest.
        Vector3 view = new Vector3(0f, 0.6f, -1f);
        float best = float.MinValue;
        for (int i = 0; i < 3; i++)
        {
            Vector3 axis = Vector3.zero; axis[i] = 1f;
            float d = Mathf.Abs(Vector3.Dot(t.rotation * axis, view.normalized));
            if (d > best) { best = d; up = i; }
        }
        a = (up + 1) % 3; b = (up + 2) % 3;
        Vector3 upLocal = Vector3.zero; upLocal[up] = 1f;
        if (Vector3.Dot(t.rotation * upLocal, view) < 0f) upLocal = -upLocal;
        var so = new SerializedObject(dish);
        so.FindProperty("fillRadius").floatValue = Mathf.Min(size[a], size[b]) * 0.5f * 0.75f;
        so.FindProperty("fillLocalCenter").vector3Value = lb.center + upLocal * size[up] * 0.25f;
        so.FindProperty("fillLocalUp").vector3Value = upLocal;
        so.ApplyModifiedProperties();

        Transform label = null;
        foreach (Transform c in t) if (c.name.StartsWith("Label_")) label = c;
        if (label != null)
        {
            Undo.RecordObject(label, "label");
            Bounds wb = mr.bounds;
            label.position = new Vector3(wb.center.x, wb.min.y - 0.12f, wb.min.z - 0.3f);
            label.rotation = Quaternion.identity;
            Vector3 ls = t.lossyScale;
            label.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
        }
    }

    private static GameObject FindIncludingInactive(string name)
    {
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name.Trim() == name) return t.gameObject;
        return null;
    }

    /// <summary>Depth of the counter's (tilted) top at a given height on screen,
    /// from where the old dishes sat on it.</summary>
    private static float CounterZ(float y) => HandCarry.CounterZ(y);

    /// <summary>Free spots on the counter top, back rows first: grid points
    /// where nothing interactable (a plate, the board, the sink...) is.</summary>
    private static Transform spotsRoot;

    private static List<Vector2> FreeSpots(int count, float dishWidth)
    {
        var spots = new List<Vector2>();
        float step = dishWidth + 0.12f;
        float half = dishWidth * 0.5f + 0.05f;
        for (float y = 2.15f; y >= -0.8f && spots.Count < count; y -= step)
        {
            for (float x = -3.9f; x <= 4.1f && spots.Count < count; x += step)
            {
                Vector3 c = new Vector3(x, y, CounterZ(y));
                bool blocked = false;
                foreach (Collider col in Physics.OverlapBox(c, new Vector3(half, half, 3f)))
                {
                    if (col.GetComponentInParent<Interactable>() != null) { blocked = true; break; }
                }
                if (!blocked)
                    foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    {
                        // Anything carryable, plus the counter props (the utensil jar...).
                        if (!r.enabled || r is ParticleSystemRenderer || r.GetComponent<TextMesh>() != null) continue;
                        if (r.transform.IsChildOf(spotsRoot)) continue;
                        bool prop = r.GetComponentInParent<Carryable>() != null;
                        for (Transform p = r.transform; p != null && !prop; p = p.parent)
                            if (p.name == "CookingUtensils" || p.name == "Tableware" || p.name == "Cooking_Ingredients") prop = true;
                        if (!prop) continue;
                        Bounds rb = r.bounds;
                        if (Mathf.Abs(rb.center.x - x) < rb.extents.x + half && Mathf.Abs(rb.center.y - y) < rb.extents.y + half)
                        { blocked = true; break; }
                    }
                if (!blocked) spots.Add(new Vector2(x, y));
            }
        }
        return spots;
    }
}
