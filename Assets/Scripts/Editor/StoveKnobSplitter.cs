using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Kitchen > Split Stove Knob Lever - the knob model (Assets/NEW/
/// StoveHandle.fbx) is one mesh, so turning it turned the whole knob. This
/// splits it into the fixed body and the lever on top: along the knob's shaft
/// (its own axis facing the camera most), everything above the top of the
/// round body is the lever. The body stays on StoveHandle; the lever becomes
/// a child "Lever" that StoveKnob turns about the shaft. Re-running redoes it.
/// </summary>
public static class StoveKnobSplitter
{
    private const string Fbx = "Assets/NEW/StoveHandle.fbx";
    private const string OutDir = "Assets/Models/Generated";

    [MenuItem("Tools/Kitchen/Split Stove Knob Lever")]
    private static void Split()
    {
        StoveKnob knob = Object.FindFirstObjectByType<StoveKnob>(FindObjectsInactive.Include);
        if (knob == null) { Debug.LogError("[Knob] no StoveKnob in the open scene"); return; }

        var imp = (ModelImporter)AssetImporter.GetAtPath(Fbx);
        if (imp != null && !imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
        Mesh src = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(Fbx)) if (o is Mesh m) { src = m; break; }
        if (src == null || !src.isReadable) { Debug.LogError("[Knob] knob mesh not readable"); return; }

        Transform t = knob.transform;
        Transform old = t.Find("Lever");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        Vector3[] v = src.vertices;
        Vector3 center = src.bounds.center;

        // Shaft: the model axis (and side) above whose round body a long thin bar
        // sticks out - judged by shape, not by how the knob happens to be posed.
        Vector3 shaft = Vector3.up;
        float bestScore = -1f;
        foreach (Vector3 a in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
        {
            float score = BarScore(v, center, a);
            if (score > bestScore) { bestScore = score; shaft = a; }
        }
        float hMin = float.MaxValue, hMax = float.MinValue, rMax = 0f;
        var h = new float[v.Length];
        var rad = new float[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 p = v[i] - center;
            h[i] = Vector3.Dot(p, shaft);
            rad[i] = (p - shaft * h[i]).magnitude;
            hMin = Mathf.Min(hMin, h[i]); hMax = Mathf.Max(hMax, h[i]); rMax = Mathf.Max(rMax, rad[i]);
        }

        // Top of the round body: the highest point out at the body's rim.
        float bodyTop = RimTop(h, rad, rMax);
        float cut = bodyTop + (hMax - hMin) * 0.02f;

        // The lever is a bar across the top: find its length direction (principal
        // axis of what's above the body, in the plane across the shaft) and its
        // width at the ends, so a domed top of the body around it stays body.
        Vector3 e1 = Vector3.Cross(shaft, Mathf.Abs(shaft.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
        Vector3 e2 = Vector3.Cross(shaft, e1);
        float sxx = 0f, syy = 0f, sxy = 0f;
        for (int i = 0; i < v.Length; i++)
        {
            if (h[i] <= cut) continue;
            Vector3 p = v[i] - center;
            float x = Vector3.Dot(p, e1), y = Vector3.Dot(p, e2);
            sxx += x * x; syy += y * y; sxy += x * y;
        }
        float theta = 0.5f * Mathf.Atan2(2f * sxy, sxx - syy);
        Vector3 along = e1 * Mathf.Cos(theta) + e2 * Mathf.Sin(theta);
        Vector3 across = Vector3.Cross(shaft, along);
        float halfWidth = 0f;
        for (int i = 0; i < v.Length; i++)
        {
            if (h[i] <= cut) continue;
            Vector3 p = v[i] - center;
            if (Mathf.Abs(Vector3.Dot(p, along)) > rMax * 0.5f) halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(p, across)));
        }
        halfWidth = halfWidth > 0f ? halfWidth * 1.1f : rMax * 0.3f;

        int[] tris = src.triangles;
        var baseTris = new List<int>();
        var leverTris = new List<int>();
        for (int i = 0; i < tris.Length; i += 3)
        {
            float c = (h[tris[i]] + h[tris[i + 1]] + h[tris[i + 2]]) / 3f;
            Vector3 cen = (v[tris[i]] + v[tris[i + 1]] + v[tris[i + 2]]) / 3f - center;
            bool inBar = Mathf.Abs(Vector3.Dot(cen, across)) <= halfWidth;
            var list = c > cut && inBar ? leverTris : baseTris;
            list.Add(tris[i]); list.Add(tris[i + 1]); list.Add(tris[i + 2]);
        }
        float share = leverTris.Count / (float)tris.Length;
        if (share < 0.01f || share > 0.7f)
        {
            Debug.LogError($"[Knob] couldn't find a lever on top (lever share {share:P0}) - nothing changed.");
            return;
        }

        Mesh baseMesh = Save(src, baseTris, "StoveKnob_Base");
        Mesh leverMesh = Save(src, leverTris, "StoveKnob_Lever");

        MeshFilter mf = t.GetComponent<MeshFilter>();
        Undo.RecordObject(mf, "Split Knob");
        mf.sharedMesh = baseMesh;

        var lever = new GameObject("Lever");
        Undo.RegisterCreatedObjectUndo(lever, "Split Knob");
        lever.transform.SetParent(t, false);
        lever.AddComponent<MeshFilter>().sharedMesh = leverMesh;
        lever.AddComponent<MeshRenderer>().sharedMaterials = t.GetComponent<MeshRenderer>().sharedMaterials;

        var so = new SerializedObject(knob);
        so.FindProperty("lever").objectReferenceValue = lever.transform;
        so.FindProperty("shaftLocal").vector3Value = shaft;
        so.FindProperty("pivotLocal").vector3Value = center;
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
        Debug.Log($"[Knob] split: lever {leverTris.Count / 3} tris ({share:P0}), shaft {shaft} - save the scene.");
    }

    /// <summary>How bar-like the part above the body's top is, looking along
    /// shaft: elongation of its footprint across the shaft (long/short), 0 when
    /// that part is implausibly small or large.</summary>
    private static float BarScore(Vector3[] v, Vector3 center, Vector3 shaft)
    {
        float hMin = float.MaxValue, hMax = float.MinValue, rMax = 0f;
        var h = new float[v.Length];
        var rad = new float[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 p = v[i] - center;
            h[i] = Vector3.Dot(p, shaft);
            rad[i] = (p - shaft * h[i]).magnitude;
            hMin = Mathf.Min(hMin, h[i]); hMax = Mathf.Max(hMax, h[i]); rMax = Mathf.Max(rMax, rad[i]);
        }
        float bodyTop = RimTop(h, rad, rMax);
        float cut = bodyTop + (hMax - hMin) * 0.02f;

        Vector3 e1 = Vector3.Cross(shaft, Mathf.Abs(shaft.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
        Vector3 e2 = Vector3.Cross(shaft, e1);
        float sxx = 0f, syy = 0f, sxy = 0f;
        int n = 0;
        for (int i = 0; i < v.Length; i++)
        {
            if (h[i] <= cut) continue;
            Vector3 p = v[i] - center;
            float x = Vector3.Dot(p, e1), y = Vector3.Dot(p, e2);
            sxx += x * x; syy += y * y; sxy += x * y;
            n++;
        }
        float share = n / (float)v.Length;
        if (share < 0.02f || share > 0.5f) return 0f;
        float tr = sxx + syy, det = sxx * syy - sxy * sxy;
        float disc = Mathf.Sqrt(Mathf.Max(tr * tr / 4f - det, 0f));
        float l1 = tr / 2f + disc, l2 = Mathf.Max(tr / 2f - disc, 1e-9f);
        return Mathf.Sqrt(l1 / l2);
    }

    /// <summary>Height of the round body's top: the rim's points (out at the
    /// body's radius), ignoring the highest 15% - a lever running right across
    /// the top reaches the rim too, and would otherwise count as the body.</summary>
    private static float RimTop(float[] h, float[] rad, float rMax)
    {
        var rim = new List<float>();
        for (int i = 0; i < h.Length; i++) if (rad[i] > rMax * 0.85f) rim.Add(h[i]);
        if (rim.Count == 0) return float.MinValue;
        rim.Sort();
        return rim[Mathf.Clamp(Mathf.FloorToInt(rim.Count * 0.85f), 0, rim.Count - 1)];
    }

    private static Mesh Save(Mesh src, List<int> tris, string name)
    {
        string path = $"{OutDir}/{name}.asset";
        Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (m == null) { m = new Mesh(); AssetDatabase.CreateAsset(m, path); }
        m.Clear();
        m.indexFormat = src.indexFormat;
        m.vertices = src.vertices;
        m.normals = src.normals;
        m.uv = src.uv;
        if (src.tangents.Length == src.vertexCount) m.tangents = src.tangents;
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        m.name = name;
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
        return m;
    }
}
