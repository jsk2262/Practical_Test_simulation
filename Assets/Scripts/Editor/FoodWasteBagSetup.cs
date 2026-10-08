using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Puts the 음식물쓰레기 봉투 (FoodWasteBag) in a corner of the sink: a stand-in
/// open bag under "Model" (swap that child for a real model later), a click box
/// over its open mouth, the "음식물 버리기" hint, all wired into the component.
/// Re-running rebuilds it in place.
/// </summary>
public static class FoodWasteBagSetup
{
    private const string MatDir = "Assets/Generated";

    [MenuItem("Tools/Kitchen/Setup Food Waste Bag")]
    private static void Setup()
    {
        WashBasin sink = Object.FindFirstObjectByType<WashBasin>();
        Collider sinkCol = sink != null ? sink.GetComponent<Collider>() : null;
        if (sinkCol == null) { Debug.LogError("[WasteBag] no sink (WashBasin) in the scene"); return; }

        GameObject old = GameObject.Find("FoodWasteBag");
        if (old != null) Undo.DestroyObjectImmediate(old);

        // Front-left corner of the sink's basin (its visible Sink_* walls - the
        // sink's own click box isn't where the basin shows), on the counter's tilt.
        Bounds sb = sinkCol.bounds;
        bool any = false;
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.name.StartsWith("Sink_Wall")) continue;
            if (!any) { sb = r.bounds; any = true; } else sb.Encapsulate(r.bounds);
        }
        // (the walls are tilted boxes - their bounds reach far below where the basin
        // shows, so measure down from its top edge)
        float y = sb.max.y - 2.25f;
        Vector3 pos = new Vector3(sb.min.x + 0.55f, y, HandCarry.CounterZ(y) - 0.3f);

        var root = new GameObject("FoodWasteBag");
        Undo.RegisterCreatedObjectUndo(root, "Food waste bag");
        if (sink.transform.parent != null) root.transform.SetParent(sink.transform.parent, true);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(-41f, 0f, 0f); // standing on the counter (its up = the counter's up)

        // Stand-in model: a dark bag, its open mouth rolled at the top, the inside dark.
        var model = new GameObject("Model").transform;
        model.SetParent(root.transform, false);
        Part(model, "Body", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0f), new Vector3(0.62f, 0.42f, 0.62f), Mat("WasteBag_Body", new Color(0.18f, 0.32f, 0.2f), 0.55f));
        GameObject rim = Part(model, "Rim", PrimitiveType.Cylinder, new Vector3(0f, 0.42f, 0f), new Vector3(0.7f, 0.04f, 0.7f), Mat("WasteBag_Rim", new Color(0.3f, 0.5f, 0.3f), 0.4f));
        Part(model, "Opening", PrimitiveType.Cylinder, new Vector3(0f, 0.47f, 0f), new Vector3(0.58f, 0.005f, 0.58f), Mat("WasteBag_Inside", new Color(0.05f, 0.06f, 0.05f), 0.1f));

        var mouth = new GameObject("Mouth").transform;
        mouth.SetParent(root.transform, false);
        mouth.localPosition = new Vector3(0f, 0.45f, 0f);

        // The drop zone: a box over the open mouth (and the top of the bag).
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.3f, 0f);
        box.size = new Vector3(0.8f, 0.4f, 0.8f);

        // "음식물 버리기", above the bag, facing the camera.
        var hintGo = new GameObject("Hint");
        hintGo.transform.SetParent(root.transform, false);
        hintGo.transform.position = pos + new Vector3(0f, 0.75f, -0.6f);
        hintGo.transform.rotation = Quaternion.identity;
        TextMesh hint = hintGo.AddComponent<TextMesh>();
        hint.text = "음식물 버리기";
        hint.fontSize = 48;
        hint.characterSize = 0.04f;
        hint.anchor = TextAnchor.MiddleCenter;
        hint.color = new Color(1f, 0.9f, 0.4f);
        hintGo.SetActive(false);

        FoodWasteBag bag = root.AddComponent<FoodWasteBag>();
        var so = new SerializedObject(bag);
        so.FindProperty("mouth").objectReferenceValue = mouth;
        so.FindProperty("hint").objectReferenceValue = hint;
        so.FindProperty("rimRenderer").objectReferenceValue = rim.GetComponent<Renderer>();
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"[WasteBag] FoodWasteBag at {pos:F2} (sink {sb.center:F2}) - save the scene.");
    }

    private static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 local, Vector3 scale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    private static Material Mat(string name, Color color, float smooth)
    {
        string path = $"{MatDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smooth);
        EditorUtility.SetDirty(m);
        return m;
    }
}
