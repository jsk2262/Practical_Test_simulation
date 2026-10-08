using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Kitchen > Setup Cleanup Tab - for the 정리 (clean-up) mode:
/// - a "정리" tab beside 힌트/규격/양념 (a copy of the rightmost tab and its
///   label, one tab-spacing further right) running CleanupTabButton;
/// - marks the dishes (MovableBowl.isDish: plates/saucers - not pots, pans, the
///   wok, the measuring cup or the water-holding bowls, which stay free to use
///   while cooking) so they only move in clean-up mode;
/// - makes sure PlateShelf1/2 are dish racks (DishShelfController).
/// Re-running replaces the tab. Undoable; save after.
/// </summary>
public static class CleanupSetup
{
    private const string TabName = "CleanupTab";
    private const string LabelName = "Label_CleanupTab";

    [MenuItem("Tools/Kitchen/Setup Cleanup Tab")]
    private static void Setup()
    {
        var scene = EditorSceneManager.GetActiveScene();
        int dishes = MarkDishes();
        EnsurePlateShelves();
        bool tab = MakeTab();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Cleanup] {dishes} dish(es) marked, 정리 tab {(tab ? "made" : "NOT made (no 힌트/규격/양념 tabs found)")} - save the scene.");
    }

    /// <summary>Only re-marks which MovableBowls are dishes (leaves the 정리 tab as it is).</summary>
    [MenuItem("Tools/Kitchen/Mark Dishes (clean-up only)")]
    private static void MarkDishesMenu()
    {
        int n = MarkDishes();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[Cleanup] {n} dish(es) marked (plates only - bowls, pots, pans stay free) - save the scene.");
    }

    /// <summary>A bowl whose own renderer is off and whose model is a child
    /// (Bowl_Prepped, Bowl_CutResult) kept its click box at the root - far from
    /// what shows, in front of everything (z -0.32), swallowing clicks meant
    /// for what's under it (the measuring cup). The box is refitted around the
    /// child model.</summary>
    /// <summary>A dish's whole ingredient set (탕수육: 8 items) has to fit on one
    /// plate/bowl when they're sorted per dish - the default 6 was too few.</summary>
    [MenuItem("Tools/Kitchen/Raise Plate Capacity (10)")]
    private static void RaisePlateCapacity()
    {
        int n = 0;
        foreach (MovableBowl b in Object.FindObjectsByType<MovableBowl>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!b.IsPlateOrBowl) continue;
            var so = new SerializedObject(b);
            SerializedProperty cap = so.FindProperty("capacity");
            if (cap.intValue >= 10) continue;
            cap.intValue = 10;
            so.ApplyModifiedProperties();
            n++;
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[Cleanup] {n} plates/bowls now hold up to 10");
    }

    [MenuItem("Tools/Kitchen/Fit Bowl Colliders To Models")]
    private static void FitBowlColliders()
    {
        foreach (MovableBowl b in Object.FindObjectsByType<MovableBowl>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Renderer own = b.GetComponent<Renderer>();
            if (own != null && own.enabled) continue;
            BoxCollider box = b.GetComponent<BoxCollider>();
            if (box == null) continue;

            Transform t = b.transform;
            bool any = false;
            Bounds local = new Bounds();
            foreach (Renderer r in b.GetComponentsInChildren<Renderer>(true))
            {
                if (r == own || !r.enabled || r.GetComponent<TextMesh>() != null) continue;
                // The bowl's own model only - not ingredients resting in it.
                Carryable owner = r.GetComponentInParent<Carryable>();
                if (owner != null && owner != b) continue;
                Bounds wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = t.InverseTransformPoint(c);
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                    else local.Encapsulate(p);
                }
            }
            if (!any) continue;
            Undo.RecordObject(box, "Fit bowl collider");
            Vector3 before = box.bounds.center;
            // Only a thin slab at the back of the model: the camera looks along
            // +z, so it still covers the whole plate on screen, but whatever
            // rests on the plate sits in front of it and gets the click.
            float depth = Mathf.Min(local.size.z, 0.05f);
            box.center = new Vector3(local.center.x, local.center.y, local.max.z - depth * 0.5f);
            box.size = new Vector3(local.size.x, local.size.y, depth);
            Debug.Log($"[Cleanup] {b.name}: click box moved {before:F2} -> {t.TransformPoint(local.center):F2}");
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    /// <summary>The prepped-ingredients plate is a dish too: it only moves in
    /// clean-up mode (its ingredients can still be taken off any time).</summary>
    [MenuItem("Tools/Kitchen/Mark Prepped Plate (clean-up only)")]
    private static void MarkPreppedPlate()
    {
        GameObject go = GameObject.Find("Plate_Model_Prepped");
        MovableBowl b = go != null ? go.GetComponentInParent<MovableBowl>() ?? go.GetComponentInChildren<MovableBowl>() : null;
        if (b == null)
        {
            string comps = go == null ? "not found" : string.Join(", ", System.Array.ConvertAll(go.GetComponents<Component>(), c => c.GetType().Name));
            Debug.LogWarning($"[Cleanup] Plate_Model_Prepped has no MovableBowl ({comps}), parent={go?.transform.parent?.name}");
            return;
        }
        var so = new SerializedObject(b);
        so.FindProperty("isDish").boolValue = true;
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(b.gameObject.scene);
        Debug.Log($"[Cleanup] {b.name} marked as dish (clean-up only) - save the scene.");
    }

    private static int MarkDishes()
    {
        int n = 0;
        foreach (MovableBowl b in Object.FindObjectsByType<MovableBowl>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string ln = (b.name + " " + (b.transform.parent ? b.transform.parent.name : "")).ToLower();
            // Bowls stay free too: they're needed to fetch water while cooking.
            bool vessel = b.GetComponent<WokDragHandler>() != null || b.HoldsByHandle || b.CanHoldWater
                          || ln.Contains("pot") || ln.Contains("pan") || ln.Contains("wok") || ln.Contains("measuring")
                          || (ln.Contains("bowl") && !ln.Contains("prepped"));
            var so = new SerializedObject(b);
            so.FindProperty("isDish").boolValue = !vessel;
            so.ApplyModifiedProperties();
            if (!vessel) n++;
        }
        return n;
    }

    private static void EnsurePlateShelves()
    {
        foreach (string shelf in new[] { "PlateShelf1", "PlateShelf2" })
        {
            GameObject go = GameObject.Find(shelf);
            if (go != null && go.GetComponent<DishShelfController>() == null)
            {
                EditorApplication.ExecuteMenuItem("Tools/Kitchen/Setup Plate Shelves");
                return;
            }
        }
    }

    private static bool MakeTab()
    {
        // The existing tabs: the clickable markers of 힌트 / 규격 / 양념.
        var tabs = new List<Transform>();
        foreach (InfoRevealButton b in Object.FindObjectsByType<InfoRevealButton>(FindObjectsInactive.Include, FindObjectsSortMode.None)) tabs.Add(b.transform);
        foreach (SequenceHintButton b in Object.FindObjectsByType<SequenceHintButton>(FindObjectsInactive.Include, FindObjectsSortMode.None)) tabs.Add(b.transform);
        if (tabs.Count == 0) return false;
        tabs.Sort((a, b) => a.position.x.CompareTo(b.position.x));
        Transform last = tabs[tabs.Count - 1];
        float step = tabs.Count > 1 ? (last.position.x - tabs[0].position.x) / (tabs.Count - 1) : 1.6f;

        // Its label: the TextMesh nearest to that tab.
        TextMesh label = null;
        float best = float.MaxValue;
        foreach (TextMesh tm in Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!tm.name.StartsWith("Label_")) continue;
            float d = Vector2.Distance(tm.transform.position, last.position);
            if (d < best) { best = d; label = tm; }
        }

        foreach (string old in new[] { TabName, LabelName })
        {
            GameObject o = GameObject.Find(old);
            if (o != null) Undo.DestroyObjectImmediate(o);
        }

        GameObject tab = Object.Instantiate(last.gameObject, last.parent);
        Undo.RegisterCreatedObjectUndo(tab, "Cleanup Tab");
        tab.name = TabName;
        tab.transform.position = last.position + Vector3.right * step;
        foreach (Transform child in tab.transform) Object.DestroyImmediate(child.gameObject); // no copied popups
        Object.DestroyImmediate(tab.GetComponent<InfoRevealButton>());
        Object.DestroyImmediate(tab.GetComponent<SequenceHintButton>());
        var btn = tab.AddComponent<CleanupTabButton>();
        var so = new SerializedObject(btn);
        so.FindProperty("tabRenderer").objectReferenceValue = tab.GetComponent<Renderer>();
        SequenceHintButton hint = Object.FindFirstObjectByType<SequenceHintButton>(FindObjectsInactive.Include);
        Renderer hintRend = hint != null ? hint.GetComponent<Renderer>() : null;
        so.FindProperty("onMaterial").objectReferenceValue = hintRend != null ? hintRend.sharedMaterial : null; // 힌트's yellow
        so.ApplyModifiedProperties();

        if (label != null)
        {
            GameObject lab = Object.Instantiate(label.gameObject, label.transform.parent);
            Undo.RegisterCreatedObjectUndo(lab, "Cleanup Tab");
            lab.name = LabelName;
            lab.transform.position = label.transform.position + Vector3.right * step;
            lab.GetComponent<TextMesh>().text = "정리";
        }
        return true;
    }
}
