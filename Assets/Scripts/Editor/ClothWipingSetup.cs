using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Kitchen > Setup Cloth Wiping - for 행주로 닦기 (ClothWiper):
/// - CleaningCloth's model is one mesh holding all three folded cloths; the
///   top one is split off into its own child "CleaningCloth_Top" (its own
///   mesh asset, a click box), the other two stay in CleaningCloth;
/// - ClothWiper on CleaningCloth, its targetCloth set to that child (from
///   then on it's whatever the Inspector says - not the order or colour);
/// - WipeableSurface on the counter tops (Counter_R) and the strips round the
///   sink (Counter_F/B/L) - clean at the start (testStains 0; stains come
///   while cooking, CookingMess).
/// Re-running doesn't split again. Undoable; save the scene after.
/// </summary>
public static class ClothWipingSetup
{
    private const string Folder = "Assets/Generated/CleaningCloth";

    [MenuItem("Tools/Kitchen/Setup Cloth Wiping")]
    private static void Setup()
    {
        GameObject cloth = GameObject.Find("CleaningCloth");
        if (cloth == null) { Debug.LogWarning("[행주] CleaningCloth not found"); return; }

        Transform top = cloth.transform.Find("CleaningCloth_Top");
        if (top == null) top = SplitTop(cloth);
        if (top == null) return;

        ClothWiper wiper = cloth.GetComponent<ClothWiper>();
        if (wiper == null) wiper = Undo.AddComponent<ClothWiper>(cloth);
        var so = new SerializedObject(wiper);
        so.FindProperty("targetCloth").objectReferenceValue = top;
        so.ApplyModifiedProperties();

        var stains = new Dictionary<string, int> { { "Counter_R", 0 }, { "Counter_F", 0 }, { "Counter_B", 0 }, { "Counter_L", 0 }, { "Sink_Bottom", 0 } };
        foreach (var kv in stains)
        {
            GameObject s = GameObject.Find(kv.Key);
            if (s == null) { Debug.LogWarning($"[행주] {kv.Key} not found"); continue; }
            WipeableSurface ws = s.GetComponent<WipeableSurface>();
            if (ws == null) ws = Undo.AddComponent<WipeableSurface>(s);
            var sso = new SerializedObject(ws);
            sso.FindProperty("testStains").intValue = kv.Value;
            sso.ApplyModifiedProperties();
        }
        EditorSceneManager.MarkSceneDirty(cloth.scene);
        Selection.activeObject = wiper;
        Debug.Log($"[행주] 설정 완료 - 대상 {top.name}, 닦는 면 {stains.Count}개. 씬을 저장하세요.");
    }

    /// <summary>The three cloths are separate pieces of one mesh: found as the
    /// connected parts; the one highest up the counter's slope is the top.</summary>
    private static Transform SplitTop(GameObject cloth)
    {
        MeshFilter mf = cloth.GetComponent<MeshFilter>();
        MeshRenderer mr = cloth.GetComponent<MeshRenderer>();
        Mesh src = mf != null ? mf.sharedMesh : null;
        if (src == null || mr == null) { Debug.LogWarning("[행주] CleaningCloth has no mesh"); return null; }

        Vector3[] v = src.vertices;
        int[] tris = src.triangles;
        int[] parent = new int[v.Length];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { parent[Find(a)] = Find(b); }
        var byPos = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < v.Length; i++)
        {
            var key = Vector3Int.RoundToInt(v[i] * 10000f);
            if (byPos.TryGetValue(key, out int o)) Union(i, o); else byPos[key] = i;
        }
        for (int t = 0; t < tris.Length; t += 3) { Union(tris[t], tris[t + 1]); Union(tris[t], tris[t + 2]); }

        var sum = new Dictionary<int, Vector3>();
        var count = new Dictionary<int, int>();
        for (int i = 0; i < v.Length; i++)
        {
            int c = Find(i);
            sum[c] = (sum.TryGetValue(c, out Vector3 s) ? s : Vector3.zero) + cloth.transform.TransformPoint(v[i]);
            count[c] = (count.TryGetValue(c, out int n) ? n : 0) + 1;
        }
        if (sum.Count < 2) { Debug.LogWarning($"[행주] only {sum.Count} piece(s) in the mesh - nothing split"); return null; }
        int topId = -1;
        float best = float.MinValue;
        foreach (var kv in sum)
        {
            float h = Vector3.Dot(kv.Value / count[kv.Key], Carryable.CounterUp);
            if (h > best) { best = h; topId = kv.Key; }
        }

        if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Generated", "CleaningCloth");
        Mesh topMesh = Subset(src, tris, i => Find(i) == topId, "CleaningCloth_Top");
        Mesh restMesh = Subset(src, tris, i => Find(i) != topId, "CleaningCloth_Rest");
        AssetDatabase.CreateAsset(topMesh, $"{Folder}/CleaningCloth_Top.asset");
        AssetDatabase.CreateAsset(restMesh, $"{Folder}/CleaningCloth_Rest.asset");
        AssetDatabase.SaveAssets();

        Undo.RecordObject(mf, "Split cleaning cloth");
        mf.sharedMesh = restMesh;
        var go = new GameObject("CleaningCloth_Top");
        Undo.RegisterCreatedObjectUndo(go, "Split cleaning cloth");
        go.transform.SetParent(cloth.transform, false);
        go.layer = cloth.layer;
        go.AddComponent<MeshFilter>().sharedMesh = topMesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = topMesh.bounds.center;
        box.size = topMesh.bounds.size;
        Debug.Log($"[행주] 맨 위 행주 분리: {count[topId]}개 정점 -> {go.name}");
        return go.transform;
    }

    private static Mesh Subset(Mesh src, int[] tris, System.Func<int, bool> keep, string name)
    {
        Vector3[] v = src.vertices;
        Vector3[] nrm = src.normals;
        Vector4[] tan = src.tangents;
        Vector2[] uv = src.uv;
        Color[] col = src.colors;
        var map = new Dictionary<int, int>();
        var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nt = new List<Vector4>(); var nu = new List<Vector2>(); var nc = new List<Color>();
        var nTris = new List<int>();
        int Map(int i)
        {
            if (map.TryGetValue(i, out int j)) return j;
            j = nv.Count;
            map[i] = j;
            nv.Add(v[i]);
            if (nrm.Length == v.Length) nn.Add(nrm[i]);
            if (tan.Length == v.Length) nt.Add(tan[i]);
            if (uv.Length == v.Length) nu.Add(uv[i]);
            if (col.Length == v.Length) nc.Add(col[i]);
            return j;
        }
        for (int t = 0; t < tris.Length; t += 3)
            if (keep(tris[t])) { nTris.Add(Map(tris[t])); nTris.Add(Map(tris[t + 1])); nTris.Add(Map(tris[t + 2])); }
        var m = new Mesh { name = name };
        m.SetVertices(nv);
        if (nn.Count == nv.Count) m.SetNormals(nn);
        if (nt.Count == nv.Count) m.SetTangents(nt);
        if (nu.Count == nv.Count) m.SetUVs(0, nu);
        if (nc.Count == nv.Count) m.SetColors(nc);
        m.SetTriangles(nTris, 0);
        m.RecalculateBounds();
        return m;
    }
}
