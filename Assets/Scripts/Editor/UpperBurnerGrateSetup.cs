using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Kitchen > Setup Burner1 Grate Cleaning - for taking Burner1's grate
/// off in 정리 and cleaning the floor under it:
/// - BurnerGrate "Burner1_Grate" (under Stove) with its 5 parts set in the
///   Inspector (Burner1_Prong0-3, Burner1_Ring - looked up by name here, once),
///   the burner under it (the StoveBurner nearest it) and a storage spot
///   ("Burner1_GrateAside", the nearest open spot on the counter top);
/// - each part: a click box and BurnerGratePart (a click on any = the grate);
///   their parents aren't changed;
/// - "Burner1_FloorArea": the floor under the grate as a cleaning area of its
///   own (WipeableSurface + BurnerFloorDirt), just above the counter top there.
/// Re-running updates them (nothing made twice). Undoable; save the scene after.
/// </summary>
public static class UpperBurnerGrateSetup
{
    private static readonly string[] PartNames = { "Burner1_Prong0", "Burner1_Prong1", "Burner1_Prong2", "Burner1_Prong3", "Burner1_Ring" };

    [MenuItem("Tools/Kitchen/Setup Burner1 Grate Cleaning")]
    private static void Setup()
    {
        var parts = new List<Transform>();
        foreach (string n in PartNames)
        {
            GameObject p = GameObject.Find(n);
            if (p == null) { Debug.LogWarning($"[화구 받침] {n} not found"); return; }
            parts.Add(p.transform);
        }
        GameObject counter = GameObject.Find("Counter_R");
        GameObject stove = GameObject.Find("Stove");
        if (counter == null) { Debug.LogWarning("[화구 받침] Counter_R not found"); return; }

        // the grate's middle, and the burner under it
        Bounds gb = parts[4].GetComponent<Renderer>().bounds;
        StoveBurner burner = null;
        float best = float.MaxValue;
        foreach (StoveBurner b in Object.FindObjectsByType<StoveBurner>(FindObjectsSortMode.None))
        {
            float d = ((Vector2)(b.transform.position - gb.center)).sqrMagnitude;
            if (d < best) { best = d; burner = b; }
        }

        // controller
        GameObject ctrl = GameObject.Find("Burner1_Grate");
        if (ctrl == null)
        {
            ctrl = new GameObject("Burner1_Grate");
            Undo.RegisterCreatedObjectUndo(ctrl, "Setup Burner1 Grate");
            if (stove != null) ctrl.transform.SetParent(stove.transform, false);
            ctrl.transform.position = gb.center;
        }
        BurnerGrate grate = ctrl.GetComponent<BurnerGrate>();
        if (grate == null) grate = Undo.AddComponent<BurnerGrate>(ctrl);

        // parts: a click box each, and the link back to the grate
        foreach (Transform p in parts)
        {
            if (p.GetComponent<Collider>() == null) Undo.AddComponent<BoxCollider>(p.gameObject); // (sized to its mesh)
            BurnerGratePart part = p.GetComponent<BurnerGratePart>();
            if (part == null) part = Undo.AddComponent<BurnerGratePart>(p.gameObject);
            var pso = new SerializedObject(part);
            pso.FindProperty("grate").objectReferenceValue = grate;
            pso.ApplyModifiedProperties();
        }

        // the counter's top face (a scaled unit cube: local +y half)
        Transform ct = counter.transform;
        Vector3 up = ct.up;
        Plane top = new Plane(up, ct.TransformPoint(new Vector3(0f, 0.5f, 0f)));
        Vector3 floorCenter = top.ClosestPointOnPlane(gb.center);

        // storage spot: the nearest open part of the counter top (no collider over it)
        Camera cam = Camera.main;
        var ignore = new HashSet<Collider>();
        foreach (Transform p in parts) foreach (Collider c in p.GetComponentsInChildren<Collider>()) ignore.Add(c);
        if (burner != null) foreach (Collider c in burner.GetComponents<Collider>()) ignore.Add(c);
        // ...and clear of what shows of anything usable there (a click box can
        // be smaller than what shows: the prepped plate's is a point)
        var taken = new List<Rect>();
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || r.GetComponentInParent<IClickable>() == null) continue;
            if (r.GetComponentInParent<BurnerGratePart>() != null || r.GetComponentInParent<StoveBurner>() != null) continue;
            Bounds rb = r.bounds;
            if (rb.size.x > 6f) continue; // (whole-room things)
            taken.Add(new Rect(rb.min.x, rb.min.y, rb.size.x, rb.size.y));
        }
        Vector2 half = new Vector2(gb.extents.x + 0.05f, gb.extents.y + 0.05f);
        Vector3 aside = floorCenter;
        float bestAside = float.MaxValue;
        for (float x = -4f; x <= 6.6f; x += 0.25f)
            for (float y = -1.3f; y <= 2.0f; y += 0.25f)
            {
                Vector3 p = new Vector3(x, y, 0f);
                if (Mathf.Abs(p.x - gb.center.x) < gb.extents.x * 2f + 0.1f && Mathf.Abs(p.y - gb.center.y) < gb.extents.y * 2f + 0.1f) continue; // clear of the burner's floor
                var foot = new Rect(p.x - half.x, p.y - half.y, half.x * 2f, half.y * 2f);
                if (taken.Exists(t => t.Overlaps(foot))) continue;
                if (!Open(cam, top, ct, p, 0.8f, ignore)) continue;
                float d = ((Vector2)(p - gb.center)).sqrMagnitude;
                if (d < bestAside) { bestAside = d; aside = p; }
            }
        Transform asideT = ctrl.transform.Find("Burner1_GrateAside");
        if (asideT == null)
        {
            var a = new GameObject("Burner1_GrateAside");
            Undo.RegisterCreatedObjectUndo(a, "Setup Burner1 Grate");
            asideT = a.transform;
            asideT.SetParent(ctrl.transform, false);
        }
        Undo.RecordObject(asideT, "Setup Burner1 Grate");
        asideT.position = new Vector3(aside.x, aside.y, HandCarry.CounterZ(aside.y));

        var so = new SerializedObject(grate);
        SerializedProperty arr = so.FindProperty("parts");
        arr.arraySize = parts.Count;
        for (int i = 0; i < parts.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = parts[i];
        so.FindProperty("burner").objectReferenceValue = burner;
        so.FindProperty("asideSpot").objectReferenceValue = asideT;
        so.ApplyModifiedProperties();

        // the floor under the grate, as its own cleaning area
        GameObject floor = GameObject.Find("Burner1_FloorArea");
        if (floor == null)
        {
            floor = new GameObject("Burner1_FloorArea");
            Undo.RegisterCreatedObjectUndo(floor, "Setup Burner1 Grate");
            if (stove != null) floor.transform.SetParent(stove.transform, true);
            floor.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        }
        Undo.RecordObject(floor.transform, "Setup Burner1 Grate");
        floor.transform.SetPositionAndRotation(floorCenter + up * (0.004f - 0.01f), ct.rotation);
        Vector3 ps = floor.transform.parent != null ? floor.transform.parent.lossyScale : Vector3.one;
        floor.transform.localScale = new Vector3(1.85f / ps.x, 0.02f / ps.y, 1.85f / ps.z);
        WipeableSurface ws = floor.GetComponent<WipeableSurface>();
        if (ws == null) ws = Undo.AddComponent<WipeableSurface>(floor);
        BurnerFloorDirt dirt = floor.GetComponent<BurnerFloorDirt>();
        if (dirt == null) dirt = Undo.AddComponent<BurnerFloorDirt>(floor);
        var dso = new SerializedObject(dirt);
        dso.FindProperty("burner").objectReferenceValue = burner;
        dso.FindProperty("grate").objectReferenceValue = grate;
        dso.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(ctrl.scene);
        Selection.activeObject = grate;
        Debug.Log($"[화구 받침] 설정 완료 - 버너 {burner?.name}, 부품 {parts.Count}개, 보관 위치 {asideT.position:F2}, 바닥 청소 영역 {floor.name}. 씬을 저장하세요.");
    }

    /// <summary>The counter top shows at p (and around it, r) with nothing standing there.</summary>
    private static bool Open(Camera cam, Plane top, Transform counter, Vector3 p, float r, HashSet<Collider> ignore)
    {
        foreach (Vector3 o in new[] { Vector3.zero, new Vector3(r, 0f, 0f), new Vector3(-r, 0f, 0f), new Vector3(0f, r * 0.6f, 0f), new Vector3(0f, -r * 0.6f, 0f),
                                      new Vector3(r * 0.7f, r * 0.45f, 0f), new Vector3(-r * 0.7f, r * 0.45f, 0f), new Vector3(r * 0.7f, -r * 0.45f, 0f), new Vector3(-r * 0.7f, -r * 0.45f, 0f) })
        {
            Ray ray = cam.ScreenPointToRay(cam.WorldToScreenPoint(p + o));
            if (!top.Raycast(ray, out float d)) return false;
            Vector3 l = counter.InverseTransformPoint(ray.GetPoint(d));
            if (Mathf.Abs(l.x) > 0.47f || Mathf.Abs(l.z) > 0.47f) return false; // off the counter top
            foreach (RaycastHit h in Physics.RaycastAll(ray, d + 0.6f))
                if (!ignore.Contains(h.collider)) return false;
        }
        return true;
    }
}
