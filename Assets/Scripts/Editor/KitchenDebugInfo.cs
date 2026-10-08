using UnityEditor;
using UnityEngine;

/// <summary>Tools > Kitchen > Test > Log View Info - camera and a named
/// object's renderer/collider state, for checking layout problems.</summary>
public static class KitchenDebugInfo
{
    /// <summary>PotSmall's water sat a little right of its body - nudged left (model space).</summary>
    /// <summary>The water now finds the pot's body itself - the old hand nudge
    /// only pushed it further off.</summary>
    [MenuItem("Tools/Kitchen/Reset Vessel Water Offsets")]
    private static void ResetWaterOffsets()
    {
        foreach (MovableBowl b in Object.FindObjectsByType<MovableBowl>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(b);
            SerializedProperty p = so.FindProperty("waterLocalOffset");
            if (p.vector3Value == Vector3.zero) continue;
            p.vector3Value = Vector3.zero;
            so.ApplyModifiedProperties();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(b.gameObject.scene);
            Debug.Log($"[ViewInfo] {b.name} water offset reset");
        }
    }

    [MenuItem("Tools/Kitchen/Nudge PotSmall Water Left")]
    private static void NudgePotWater()
    {
        GameObject pot = GameObject.Find("PotSmall") ?? GameObject.Find("FreePotSmall");
        MovableBowl b = pot != null ? pot.GetComponent<MovableBowl>() : null;
        if (b == null) return;
        var so = new SerializedObject(b);
        so.FindProperty("waterLocalOffset").vector3Value = new Vector3(-0.01f, 0f, 0f);
        so.ApplyModifiedProperties();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(pot.scene);
        Debug.Log("[ViewInfo] PotSmall water offset (-0.01, 0, 0)");
    }

    [MenuItem("Tools/Kitchen/Test/Log Vessels")]
    private static void LogVessels()
    {
        WaterTap tap = Object.FindFirstObjectByType<WaterTap>();
        var tso = tap != null ? new SerializedObject(tap) : null;
        GameObject stream = tso?.FindProperty("waterStream").objectReferenceValue as GameObject;
        Transform fill = tso?.FindProperty("fillPoint").objectReferenceValue as Transform;
        Vector3 fp = fill != null ? fill.position : stream != null ? stream.transform.position : Vector3.zero;
        WashBasin sink = Object.FindFirstObjectByType<WashBasin>();
        Debug.Log($"[ViewInfo] fill point={fp:F2} radius={tso?.FindProperty("fillRadius").floatValue} sink={sink?.transform.position:F2}");
        foreach (MovableBowl b in Object.FindObjectsByType<MovableBowl>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(b);
            Renderer r = b.GetComponent<Renderer>();
            Debug.Log($"[ViewInfo] {b.name} water={b.CanHoldWater} dish={b.IsDish} handle={b.HoldsByHandle} pivot={b.transform.position:F2} rot={b.transform.eulerAngles:F0} bounds={(r ? $"{r.bounds.center:F2}/{r.bounds.size:F2}" : "-")} local={(r ? $"{r.localBounds.center:F3}/{r.localBounds.size:F3}" : "-")} waterOffset={so.FindProperty("waterLocalOffset").vector3Value:F3}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Log Seasoning Objects")]
    private static void LogSeasoningObjects()
    {
        string[] keys = { "chili", "jongji", "bottle", "spoon", "plate_", "sauce", "pepper", "salt", "cup" };
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string ln = t.name.ToLower();
            if (System.Array.Find(keys, k => ln.Contains(k)) == null) continue;
            if (ln.StartsWith("label_")) continue;
            Renderer r = t.GetComponent<Renderer>();
            Collider c = t.GetComponent<Collider>();
            string comps = string.Join(",", System.Array.ConvertAll(t.GetComponents<MonoBehaviour>(), m => m ? m.GetType().Name : "null"));
            Debug.Log($"[ViewInfo] {t.name} active={t.gameObject.activeInHierarchy} parent={t.parent?.name} pos={t.position:F2} lossy={t.lossyScale:F2} rend={(r ? $"{r.bounds.center:F2}/{r.bounds.size:F2}" : "-")} col={(c ? $"{c.GetType().Name} {c.bounds.center:F2}/{c.bounds.size:F2} en={c.enabled}" : "-")} [{comps}] children={t.childCount}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Log Ingredient Models")]
    private static void LogIngredientModels()
    {
        foreach (IngredientItem i in Object.FindObjectsByType<IngredientItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            MeshFilter mf = i.GetComponent<MeshFilter>();
            Renderer r = i.GetComponent<Renderer>();
            string mats = r ? string.Join(",", System.Array.ConvertAll(r.sharedMaterials, m => m ? m.name : "null")) : "-";
            Debug.Log($"[ViewInfo] ING {i.name} active={i.gameObject.activeInHierarchy} parent={i.transform.parent?.name} mesh={(mf && mf.sharedMesh ? mf.sharedMesh.name + " @" + AssetDatabase.GetAssetPath(mf.sharedMesh) : "-")} mats={mats} rot={i.transform.eulerAngles:F0} lossy={i.transform.lossyScale:F2} bounds={(r ? r.bounds.size.ToString("F2") : "-")} pos={i.transform.position:F2}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Log Tableware")]
    private static void LogTableware()
    {
        GameObject tw = GameObject.Find("Tableware");
        if (tw == null) { Debug.Log("[ViewInfo] no Tableware"); return; }
        foreach (Transform t in tw.GetComponentsInChildren<Transform>(true))
        {
            if (t.parent != tw.transform && t.GetComponent<Carryable>() == null) continue;
            Renderer r = t.GetComponentInChildren<Renderer>(true);
            Carryable c = t.GetComponent<Carryable>();
            Debug.Log($"[ViewInfo] TW {t.name} active={t.gameObject.activeInHierarchy} self={t.gameObject.activeSelf} parent={t.parent?.name} pos={t.position:F2} scale={t.lossyScale:F2} rend={(r ? $"{r.enabled} {r.bounds.center:F2}/{r.bounds.size:F2}" : "-")} container={(c?.CurrentContainer as Object)?.name}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Log Condiment Dish Tree")]
    private static void LogDishTree()
    {
        foreach (string n in new[] { "Plate_RedChiliPowder ", "Plate_Doubanjiang ", "measuringSpoon", "Jongji_1" })
        {
            GameObject go = GameObject.Find(n);
            if (go == null) { Debug.Log($"[ViewInfo] {n}: none"); continue; }
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                Renderer r = t.GetComponent<Renderer>();
                MeshFilter mf = t.GetComponent<MeshFilter>();
                string mats = r ? string.Join(",", System.Array.ConvertAll(r.sharedMaterials, m => m ? m.name : "null")) : "-";
                Debug.Log($"[ViewInfo] {n} > {t.name} local={t.localPosition:F3} rot={t.localEulerAngles:F0} scl={t.localScale:F3} rend={(r ? $"{r.bounds.center:F2}/{r.bounds.size:F2}" : "-")} mesh={(mf && mf.sharedMesh ? mf.sharedMesh.name + " " + AssetDatabase.GetAssetPath(mf.sharedMesh) : "-")} mats={mats}");
            }
        }
        foreach (SeasoningBottle b in Object.FindObjectsByType<SeasoningBottle>(FindObjectsSortMode.None))
            Debug.Log($"[ViewInfo] bottle {b.name} = '{b.SeasoningName}'");
    }

    [MenuItem("Tools/Kitchen/Test/Log Pans")]
    private static void LogPans()
    {
        foreach (string n in new[] { "PotSmall", "FryingPan", "Wok" })
        {
            Carryable c = GameObject.Find(n)?.GetComponent<Carryable>();
            if (c == null) { Debug.Log($"[ViewInfo] {n}: none"); continue; }
            Transform t = c.transform;
            Renderer r = c.GetComponent<Renderer>();
            string body = c is MovableBowl b ? $"{b.BodyCenterWorld():F2}" : "-";
            Debug.Log($"[ViewInfo] {n} parent={t.parent?.name}({t.parent?.lossyScale:F2}) local={t.localScale:F2} lossy={t.lossyScale:F2} base={c.BaseLocalScale:F2} rot={t.eulerAngles:F0} baseRot={c.BaseRotation.eulerAngles:F0} spin={c.UserSpin.eulerAngles:F0} bounds={(r ? $"{r.bounds.center:F2}/{r.bounds.size:F2}" : "-")} body={body}");
        }
        foreach (string n in new[] { "KitchenShelf1", "KitchenShelf2", "KitchenShelf3" })
        {
            GameObject go = GameObject.Find(n);
            Collider col = go?.GetComponent<Collider>();
            if (go != null) Debug.Log($"[ViewInfo] {n} parent={go.transform.parent?.name}({go.transform.parent?.lossyScale:F2}, rot {go.transform.parent?.eulerAngles:F0}) rot={go.transform.eulerAngles:F0} bounds={(col ? $"{col.bounds.center:F2}/{col.bounds.size:F2}" : "-")}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Log Tabs")]
    private static void LogTabs()
    {
        foreach (string n in new[] { "CleanupTab", "RecipesSauce", "CutSpecification", "Hint", "Label_CleanupTab", "Label_SauceInfoButton", "Label_HintButton" })
        {
            GameObject go = GameObject.Find(n);
            if (go == null) { Debug.Log($"[ViewInfo] {n}: none"); continue; }
            var sb = new System.Text.StringBuilder($"[ViewInfo] {n} pos={go.transform.position:F2} ");
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material m = r.sharedMaterial;
                sb.Append($"| {r.name} {r.GetType().Name} mat={(m ? m.name : "null")} shader={(m ? m.shader.name : "-")} ");
                if (m != null && m.HasProperty("_BaseColor")) sb.Append($"base={m.GetColor("_BaseColor")} ");
                if (m != null && m.HasProperty("_Color")) sb.Append($"color={m.GetColor("_Color")} ");
                if (m != null && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap")) sb.Append($"tex={m.GetTexture("_BaseMap").name} ");
            }
            Debug.Log(sb.ToString());
        }
    }

    [MenuItem("Tools/Kitchen/Test/Log Burner Flames")]
    private static void LogFlames()
    {
        foreach (StoveBurner b in Object.FindObjectsByType<StoveBurner>(FindObjectsSortMode.None))
        {
            Collider bc = b.GetComponent<Collider>();
            Debug.Log($"[ViewInfo] {b.name} pos={b.transform.position:F2} collider={(bc ? bc.bounds.center.ToString("F2") : "-")}");
            var so = new SerializedObject(b);
            foreach (string f in new[] { "flameWeak", "flameMedium", "flameStrong" })
            {
                GameObject fx = so.FindProperty(f).objectReferenceValue as GameObject;
                if (fx == null) continue;
                var sb = new System.Text.StringBuilder($"[ViewInfo] {b.name}.{f} = {fx.name} active={fx.activeInHierarchy}: ");
                foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    sb.Append($"{ps.name}(size={main.startSizeMultiplier:F2} rate={ps.emission.rateOverTimeMultiplier:F0} col={main.startColor.color} lights={ps.lights.enabled}) ");
                }
                foreach (Light l in fx.GetComponentsInChildren<Light>(true)) sb.Append($"Light {l.name} int={l.intensity:F1} range={l.range:F1} ");
                foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(false))
                {
                    var pr = ps.GetComponent<ParticleSystemRenderer>();
                    Camera cam = Camera.main;
                    string scr = cam != null && pr != null ? cam.WorldToScreenPoint(pr.bounds.center).ToString("F0") : "-";
                    sb.Append($"| live {ps.name}: n={ps.particleCount} playing={ps.isPlaying} rend={(pr ? pr.enabled + " mat=" + (pr.sharedMaterial ? pr.sharedMaterial.name + "/" + pr.sharedMaterial.shader.name : "null") + $" bounds={pr.bounds.center:F2}/{pr.bounds.size:F2} screen={scr} layer={LayerMask.LayerToName(ps.gameObject.layer)}" : "-")} ");
                }
                Debug.Log(sb.ToString());
            }
        }
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.StartsWith("Flame_") && t.GetComponent<Renderer>())
                Debug.Log($"[ViewInfo] {t.name} own renderer enabled={t.GetComponent<Renderer>().enabled} mat={t.GetComponent<Renderer>().sharedMaterial?.name}");
    }

    [MenuItem("Tools/Kitchen/Test/Log Station Body")]
    private static void LogBody()
    {
        foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!r.name.StartsWith("Body") && !r.name.StartsWith("Sink_")) continue;
            Debug.Log($"[ViewInfo] {r.name} active={r.gameObject.activeInHierarchy} rend={r.enabled} parent={r.transform.parent?.name} localPos={r.transform.localPosition:F3} localScale={r.transform.localScale:F3} bounds={r.bounds.center:F2}/{r.bounds.size:F2}");
        }
    }

    /// <summary>A solid copy of the station's Body left outside the station
    /// model (directly under KitchenStation or a group) fills the sink recess
    /// back in - switched off (not deleted; undoable).</summary>
    [MenuItem("Tools/Kitchen/Disable Stray Station Body Copy")]
    private static void DisableStrayBody()
    {
        int n = 0;
        foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (r.name != "Body" || r.transform.parent == null || r.transform.parent.name == "CookingStation_Wide") continue;
            Undo.RecordObject(r.gameObject, "Disable Stray Body");
            r.gameObject.SetActive(false);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
            Debug.Log($"[ViewInfo] switched off stray Body under {r.transform.parent.name}");
            n++;
        }
        if (n == 0) Debug.Log("[ViewInfo] no stray Body found");
    }

    [MenuItem("Tools/Kitchen/Test/Log Burner Clicks")]
    private static void LogBurnerClicks()
    {
        Camera cam = Camera.main;
        Physics.SyncTransforms();
        int mask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
        foreach (StoveBurner b in Object.FindObjectsByType<StoveBurner>(FindObjectsSortMode.None))
        {
            foreach (Vector3 off in new[] { Vector3.zero, new Vector3(0.4f, 0f, 0f), new Vector3(-0.4f, 0f, 0f), new Vector3(0f, 0.3f, 0f), new Vector3(0f, -0.3f, 0f) })
            {
                Ray ray = cam.ScreenPointToRay(cam.WorldToScreenPoint(b.transform.position + off));
                RaycastHit[] hits = Physics.RaycastAll(ray, 100f, mask);
                System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
                var names = new System.Collections.Generic.List<string>();
                foreach (var h in hits) names.Add(h.collider.name + (h.collider.GetComponentInParent<Interactable>() ? "*" : ""));
                Debug.Log($"[ViewInfo] {b.name}{off:F1}: {string.Join(" > ", names)}");
            }
        }
    }

    [MenuItem("Tools/Kitchen/Test/Log Stove Knob")]
    private static void LogKnob()
    {
        StoveKnob k = Object.FindFirstObjectByType<StoveKnob>();
        if (k == null) { Debug.Log("[ViewInfo] no knob"); return; }
        Debug.Log($"[ViewInfo] knob {k.name} pos={k.transform.position:F2} rot={k.transform.eulerAngles:F0} scale={k.transform.lossyScale:F3} children={k.transform.childCount}");
        foreach (MeshFilter mf in k.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh m = mf.sharedMesh;
            Debug.Log($"[ViewInfo]   {mf.name} mesh={(m ? m.name : "null")} asset={(m ? AssetDatabase.GetAssetPath(m) : "-")} verts={(m ? m.vertexCount : 0)} sub={(m ? m.subMeshCount : 0)} readable={(m ? m.isReadable : false)} local={(m ? m.bounds.center.ToString("F4") + "/" + m.bounds.size.ToString("F4") : "-")}");
        }
        foreach (Collider c in k.GetComponentsInChildren<Collider>(true)) Debug.Log($"[ViewInfo]   collider {c.name} {c.GetType().Name} {c.bounds.center:F2}/{c.bounds.size:F2}");
        foreach (string f in new[] { "Flame_L", "Flame_R" })
        {
            GameObject g = GameObject.Find(f);
            if (g) foreach (Collider c in g.GetComponents<Collider>()) Debug.Log($"[ViewInfo] {f} collider {c.GetType().Name} en={c.enabled} {c.bounds.center:F2}/{c.bounds.size:F2}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Log ChineseCleaver FBX")]
    private static void LogCleaverFbx()
    {
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/NEW/ChineseCleaver.fbx");
        if (fbx == null) { Debug.Log("[ViewInfo] ChineseCleaver.fbx not found"); return; }
        Debug.Log($"[ViewInfo] fbx root rot={fbx.transform.localEulerAngles:F0} scale={fbx.transform.localScale:F3}");
        foreach (MeshFilter mf in fbx.GetComponentsInChildren<MeshFilter>(true))
        {
            Renderer r = mf.GetComponent<Renderer>();
            Mesh m = mf.sharedMesh;
            var mats = new System.Collections.Generic.List<string>();
            foreach (Material mat in r.sharedMaterials) mats.Add(mat ? mat.name : "null");
            Debug.Log($"[ViewInfo]   {mf.name} mesh={m.name} verts={m.vertexCount} sub={m.subMeshCount} bounds={m.bounds.size:F4} readable={m.isReadable} localRot={mf.transform.localEulerAngles:F0} localScale={mf.transform.localScale:F3} mats=[{string.Join(", ", mats)}]");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Log Knife")]
    private static void LogKnife()
    {
        bool any = false;
        foreach (ToolItem t in Object.FindObjectsByType<ToolItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.Kind != ToolKind.Knife) continue;
            any = true;
            string path = t.name;
            for (Transform p = t.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
            Renderer r = t.GetComponent<Renderer>();
            MeshFilter mf = t.GetComponent<MeshFilter>();
            Mesh m = mf != null ? mf.sharedMesh : null;
            Debug.Log($"[ViewInfo] knife mesh={(m != null ? $"{m.name} verts={m.vertexCount} asset={AssetDatabase.GetAssetPath(m)}" : "NONE")} mats={(r ? r.sharedMaterials.Length : 0)} collider={(t.GetComponent<Collider>() ? t.GetComponent<Collider>().GetType().Name : "-")}");
            foreach (Transform c in t.transform) Debug.Log($"[ViewInfo]   child {c.name} rend={(c.GetComponent<Renderer>() ? c.GetComponent<Renderer>().bounds.size.ToString("F2") : "-")}");
            Debug.Log($"[ViewInfo] knife {path} active={t.gameObject.activeInHierarchy} rend={(r ? r.enabled.ToString() : "-")} pos={t.transform.position:F2} lossy={t.transform.lossyScale:F3} rot={t.transform.eulerAngles:F0} bounds={(r ? $"{r.bounds.center:F2}/{r.bounds.size:F2}" : "-")}");
        }
        if (!any) Debug.Log("[ViewInfo] no knife (ToolItem Kind=Knife) in the open scene");
    }

    [MenuItem("Tools/Kitchen/Test/Log Racks And Utensils")]
    private static void LogRacks()
    {
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!t.name.Contains("HangingRack") && !t.name.Contains("CookingUtensils")) continue;
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.zero);
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            var comps = new System.Text.StringBuilder();
            foreach (Component c in t.GetComponents<Component>()) if (!(c is Transform)) comps.Append(c.GetType().Name).Append(',');
            Debug.Log($"[ViewInfo] {t.name} parent={(t.parent ? t.parent.name : "-")} pos={t.position:F2} rot={t.eulerAngles:F0} scale={t.lossyScale:F2} bounds={b.center:F2}/{b.size:F2} comps={comps} children={t.childCount}");
            foreach (Transform c in t)
            {
                Renderer cr = c.GetComponentInChildren<Renderer>(true);
                var cc = new System.Text.StringBuilder();
                foreach (Component x in c.GetComponents<Component>()) if (!(x is Transform)) cc.Append(x.GetType().Name).Append(',');
                Debug.Log($"[ViewInfo]    - {c.name} pos={c.position:F2} rot={c.eulerAngles:F0} scale={c.lossyScale:F2} {(cr ? $"bounds={cr.bounds.center:F2}/{cr.bounds.size:F2}" : "")} comps={cc}");
            }
        }
    }

    [MenuItem("Tools/Kitchen/Test/Log Hierarchy Groups")]
    private static void LogGroups()
    {
        GameObject station = GameObject.Find("KitchenStation");
        if (station == null) return;
        var sb = new System.Text.StringBuilder("[ViewInfo] groups:\n");
        foreach (Transform g in station.transform)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (Transform c in g) names.Add(c.name);
            sb.Append($"{g.name} ({g.childCount}): {string.Join(", ", names)}\n");
        }
        var roots = new System.Collections.Generic.List<string>();
        foreach (GameObject r in station.scene.GetRootGameObjects()) roots.Add(r.name);
        sb.Append("scene roots: " + string.Join(", ", roots));
        Debug.Log(sb.ToString());
    }

    [MenuItem("Tools/Kitchen/Test/Log Cutting Board Hits")]
    private static void BoardHits()
    {
        Camera cam = Camera.main;
        CuttingBoardController board = Object.FindFirstObjectByType<CuttingBoardController>();
        if (cam == null || board == null) return;
        Physics.SyncTransforms();
        Bounds b = board.GetComponent<Collider>().bounds;
        int mask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
        var sb = new System.Text.StringBuilder($"[ViewInfo] board {board.name} bounds={b.center:F2}/{b.size:F2} scale={board.transform.lossyScale:F2}\n");
        for (float fy = 0.8f; fy >= 0.19f; fy -= 0.3f)
        {
            for (float fx = 0.1f; fx <= 0.91f; fx += 0.2f)
            {
                Vector3 p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, fx), Mathf.Lerp(b.min.y, b.max.y, fy), b.center.z);
                Ray ray = cam.ScreenPointToRay(cam.WorldToScreenPoint(p));
                string hit = Physics.Raycast(ray, out RaycastHit h, 100f, mask) ? h.collider.name : "-";
                sb.Append($"{fx:0.0},{fy:0.0}:{hit} | ");
            }
            sb.Append('\n');
        }
        Debug.Log(sb.ToString());
    }

    [MenuItem("Tools/Kitchen/Test/Log Vessel Clicks")]
    private static void VesselClicks()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Physics.SyncTransforms();
        int mask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
        foreach (MovableBowl v in Object.FindObjectsByType<MovableBowl>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string n = v.name.ToLower();
            if (!(n.Contains("pot") || n.Contains("pan") || n.Contains("wok"))) continue;
            Renderer r = v.GetComponent<Renderer>();
            Collider[] cols = v.GetComponents<Collider>();
            string colInfo = "";
            foreach (Collider c in cols) colInfo += $"{c.GetType().Name}(en={c.enabled},{c.bounds.center:F2}/{c.bounds.size:F2}) ";
            Vector3 p = r != null ? r.bounds.center : v.transform.position;
            Ray ray = cam.ScreenPointToRay(cam.WorldToScreenPoint(p));
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f, mask);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            string order = "";
            for (int i = 0; i < Mathf.Min(hits.Length, 4); i++) order += hits[i].collider.name + " > ";
            Debug.Log($"[ViewInfo] {v.name} active={v.gameObject.activeInHierarchy} enabled={v.enabled} layer={LayerMask.LayerToName(v.gameObject.layer)} pos={v.transform.position:F2} cols: {colInfo}| click hits: {order}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Log Sauce Shelf")]
    private static void SauceShelf()
    {
        SauceShelfController shelf = Object.FindFirstObjectByType<SauceShelfController>(FindObjectsInactive.Include);
        if (shelf == null) { Debug.Log("[ViewInfo] no sauce shelf"); return; }
        Transform t = shelf.transform;
        Debug.Log($"[ViewInfo] shelf {t.name} parent={(t.parent ? t.parent.name : "-")} localRot={t.localEulerAngles:F1} worldRot={t.eulerAngles:F1} localScale={t.localScale:F2} lossy={t.lossyScale:F2}");
        Transform root = t.Find("ContentRoot");
        if (root != null) Debug.Log($"[ViewInfo] ContentRoot saved in scene: localScale={root.localScale:F3} rot={root.eulerAngles:F1} lossy={root.lossyScale:F3}");
        foreach (SeasoningBottle b in Object.FindObjectsByType<SeasoningBottle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Debug.Log($"[ViewInfo] bottle {b.name} parent={(b.transform.parent ? b.transform.parent.name : "-")} localScale={b.transform.localScale:F2} lossy={b.transform.lossyScale:F2} rot={b.transform.eulerAngles:F0}");
    }

    [MenuItem("Tools/Kitchen/Test/Log View Info")]
    private static void Log()
    {
        Camera cam = Camera.main;
        if (cam != null)
            Debug.Log($"[ViewInfo] camera pos={cam.transform.position:F2} rot={cam.transform.eulerAngles:F0} ortho={cam.orthographic} size={cam.orthographicSize:F2} fov={cam.fieldOfView:F0} aspect={cam.aspect:F2}");

        foreach (IngredientItem item in Object.FindObjectsByType<IngredientItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!item.name.Contains("완두콩")) continue;
            Renderer r = item.GetComponent<Renderer>();
            if (cam != null)
            {
                Physics.SyncTransforms();
                Ray ray = cam.ScreenPointToRay(cam.WorldToScreenPoint(r.bounds.center));
                bool hit = Physics.Raycast(ray, out RaycastHit h, 100f, ~(1 << LayerMask.NameToLayer("Ignore Raycast")));
                Debug.Log($"[ViewInfo] click on {item.name} hits: {(hit ? h.collider.name : "nothing")}");
            }
            foreach (Collider c in item.GetComponents<Collider>())
                Debug.Log($"[ViewInfo] {item.name} pos={item.transform.position:F2} scale={item.transform.lossyScale:F3} rend={r.bounds.size:F3} collider={c.GetType().Name} enabled={c.enabled} bounds={c.bounds.center:F2}/{c.bounds.size:F3}");
        }
    }
}
