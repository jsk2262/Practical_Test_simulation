using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Kitchen > Setup Cheesecloth - puts the folded 면보 (Cheesecloth, for
/// straining - not for wiping) on the counter top (Counter_R), stacked one on
/// another like the 행주 (CleaningCloth), at the first open spot of a few near
/// the sink end: the top one is taken first, then the next. Each is a cloth
/// of its own. Re-running restacks them there (and adds any missing).
/// Undoable; save the scene after.
/// </summary>
public static class CheeseclothSetup
{
    private const int Count = 2;
    private const float Layer = 0.052f; // one folded cloth's thickness (+ a hair)

    [MenuItem("Tools/Kitchen/Setup Cheesecloth")]
    private static void Setup()
    {
        GameObject counter = GameObject.Find("Counter_R");
        if (counter == null) { Debug.LogWarning("[면보] Counter_R not found"); return; }
        Camera cam = Camera.main;

        // (the single one made before becomes the first of the stack)
        GameObject old = GameObject.Find("Cheesecloth");
        if (old != null) { Undo.RecordObject(old, "Setup Cheesecloth"); old.name = "Cheesecloth_1"; }

        var cloths = new GameObject[Count];
        for (int i = 0; i < Count; i++)
        {
            string n = $"Cheesecloth_{i + 1}";
            GameObject go = GameObject.Find(n);
            if (go == null)
            {
                go = new GameObject(n);
                Undo.RegisterCreatedObjectUndo(go, "Setup Cheesecloth");
                BoxCollider box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(0.42f, 0.06f, 0.34f);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                go.AddComponent<Cheesecloth>();
            }
            Undo.RecordObject(go.transform, "Setup Cheesecloth");
            cloths[i] = go;
        }

        // the counter's top face (a scaled unit cube: local +y half)
        Transform t = counter.transform;
        Vector3 up = t.up;
        Plane top = new Plane(up, t.TransformPoint(new Vector3(0f, 0.5f, 0f)));
        Vector3 spot = Vector3.zero;
        bool found = false;
        foreach (Vector2 xy in new[] { new Vector2(-3.7f, -0.9f), new Vector2(-3.7f, -0.4f), new Vector2(-3.1f, -1.0f), new Vector2(-2.6f, -1.0f), new Vector2(-3.7f, 1.4f), new Vector2(3.2f, -1.0f) })
        {
            Ray ray = cam.ScreenPointToRay(cam.WorldToScreenPoint(new Vector3(xy.x, xy.y, 0f)));
            if (!top.Raycast(ray, out float d)) continue;
            Vector3 p = ray.GetPoint(d);
            if (Physics.Raycast(ray, out RaycastHit hit, d + 0.6f) && System.Array.IndexOf(cloths, hit.collider.gameObject) < 0) continue; // something stands there
            spot = p;
            found = true;
            break;
        }
        if (!found) Debug.LogWarning("[면보] no open spot found on the counter - placed at the first one anyway");

        Vector3 right = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
        Vector3 back = Vector3.Cross(right, up).normalized;
        for (int i = 0; i < Count; i++)
        {
            // stacked up the counter's up, each a little off the one under it (as folded cloths lie)
            Vector3 at = spot + up * (0.03f + Layer * i) + right * (0.015f * i) + back * (-0.01f * i);
            Quaternion turn = Quaternion.AngleAxis(i % 2 == 0 ? 0f : 4f, up);
            cloths[i].transform.SetPositionAndRotation(at, turn * Quaternion.LookRotation(back, up));
            cloths[i].transform.localScale = Vector3.one;
        }
        EditorSceneManager.MarkSceneDirty(cloths[0].scene);
        Selection.activeObject = cloths[Count - 1];
        Debug.Log($"[면보] {Count}장 겹쳐 놓음: {spot:F2} - 씬을 저장하세요.");
    }
}
