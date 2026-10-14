using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Kitchen > Setup Exam Buttons: puts the 제출 and 빠른 모드 buttons in
/// the scene under the 정리 tab (same size as the tab), so they're there in
/// the editor too - not only made at play.
/// </summary>
public static class ExamButtonsSetup
{
    private const string MatFolder = "Assets/Generated/UI";

    [MenuItem("Tools/Kitchen/Setup Exam Buttons")]
    public static void Setup()
    {
        CleanupTabButton tab = Object.FindFirstObjectByType<CleanupTabButton>();
        if (tab == null) { Debug.LogError("[Setup] 정리 탭(CleanupTabButton)이 씬에 없어요"); return; }
        Renderer tr = tab.GetComponent<Renderer>();
        Bounds tb = tr != null ? tr.bounds : new Bounds(tab.transform.position, new Vector3(2f, 0.6f, 0.1f));

        Make<SubmitButton>(tab.transform.parent, "SubmitButton", "제출", Mat("ExamButton_Submit", new Color(0.32f, 0.36f, 0.4f)),
            new Vector3(tb.center.x, tb.min.y - tb.size.y * 0.75f, tb.min.z - 0.01f), tb.size);
        Make<SpeedModeButton>(tab.transform.parent, "SpeedModeButton", "빠른 모드 OFF", Mat("ExamButton_Speed", new Color(0.32f, 0.36f, 0.4f)),
            new Vector3(tb.center.x, tb.min.y - tb.size.y * 2.05f, tb.min.z - 0.01f), new Vector3(tb.size.x, tb.size.y * 0.8f, tb.size.z));
        EditorSceneManager.MarkSceneDirty(tab.gameObject.scene);
        Debug.Log("[Setup] 제출 · 빠른 모드 버튼을 씬에 만들었어요 (Ctrl+S로 저장)");
    }

    private static Material Mat(string name, Color c)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
        if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets/Generated", "UI");
        string path = $"{MatFolder}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Sprites/Default")) { color = c };
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }

    private static void Make<T>(Transform parent, string name, string text, Material mat, Vector3 pos, Vector3 size) where T : Component
    {
        T existing = Object.FindFirstObjectByType<T>();
        GameObject go = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.rotation = Quaternion.identity;
        go.transform.position = pos;
        go.transform.localScale = Vector3.one;
        Vector3 ls = go.transform.lossyScale;
        go.transform.localScale = new Vector3(size.x / ls.x, size.y / ls.y, 1f);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (go.GetComponent<MeshCollider>() is MeshCollider mc) Object.DestroyImmediate(mc);
        if (go.GetComponent<BoxCollider>() == null) go.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 0.2f);
        if (go.GetComponent<T>() == null) go.AddComponent<T>();

        Transform lt = go.transform.Find("Label");
        if (lt == null) { lt = new GameObject("Label").transform; lt.SetParent(go.transform, false); }
        lt.localPosition = new Vector3(0f, 0f, -0.05f);
        ls = go.transform.lossyScale;
        lt.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f);
        TextMesh tm = lt.GetComponent<TextMesh>();
        if (tm == null) tm = lt.gameObject.AddComponent<TextMesh>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.font = font;
        lt.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        tm.text = text;
        tm.fontSize = 60;
        tm.characterSize = size.y * 0.09f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;

        var so = new SerializedObject(go.GetComponent<T>());
        if (so.FindProperty("label") is SerializedProperty pl) pl.objectReferenceValue = tm;
        if (so.FindProperty("back") is SerializedProperty pb) pb.objectReferenceValue = go.GetComponent<Renderer>();
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
