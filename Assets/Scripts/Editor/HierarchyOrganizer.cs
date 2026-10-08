using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Kitchen > Organize Hierarchy - files the open scene's top-level
/// objects (scene roots and KitchenStation's direct children) into category
/// groups under KitchenStation: 가구류, 화구, 개수대, 도마, 칼, 냄비, 냄비뚜껑,
/// 후라이팬, 웍, 접시류, 요리도구류, 위생도구, 소스류, 요리재료, 손, 이외.
///
/// Safe for gameplay: groups are empty objects at the origin (no rotation,
/// scale 1), objects keep their world position, and only top-level objects
/// move - children that belong to something (ingredients inside a bowl, a
/// bowl's anchors/ContentRoot, a flame's VFX, the knife resting on the board)
/// stay with their owner. Camera, light and volume stay at the scene root.
/// Anything it can't classify is left where it is and logged. Undoable.
/// </summary>
public static class HierarchyOrganizer
{
    private const string RootName = "KitchenStation";

    private static readonly string[] GroupOrder =
    {
        "가구류", "화구", "개수대", "도마", "칼", "냄비", "냄비뚜껑", "후라이팬", "웍",
        "접시류", "요리도구류", "위생도구", "소스류", "요리재료", "손", "이외",
    };

    private static readonly HashSet<string> StayAtRoot = new HashSet<string>
    {
        "Main Camera", "Directional Light", "Global Volume", "EventSystem", RootName,
    };

    [MenuItem("Tools/Kitchen/Organize Hierarchy")]
    private static void Organize()
    {
        var scene = EditorSceneManager.GetActiveScene();
        GameObject stationGo = null;
        foreach (GameObject r in scene.GetRootGameObjects()) if (r.name == RootName) stationGo = r;
        if (stationGo == null) { Debug.LogError($"[Organize] No '{RootName}' at the scene root."); return; }
        Transform station = stationGo.transform;

        // Candidates: scene roots + KitchenStation's direct children (not the groups themselves).
        var candidates = new List<Transform>();
        foreach (GameObject r in scene.GetRootGameObjects())
            if (!StayAtRoot.Contains(r.name)) candidates.Add(r.transform);
        foreach (Transform c in station)
            if (System.Array.IndexOf(GroupOrder, c.name) < 0) candidates.Add(c);

        var groups = new Dictionary<string, Transform>();
        int moved = 0;
        var skipped = new List<string>();
        foreach (Transform t in candidates)
        {
            string group = Classify(t);
            if (group == null) { skipped.Add(t.name); continue; }

            if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject) && !PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject))
            {
                skipped.Add(t.name + " (inside a prefab)");
                continue;
            }

            Undo.SetTransformParent(t, Group(station, group, groups), "Organize Hierarchy");
            moved++;
        }

        // Groups in the listed order, at the top of KitchenStation.
        int index = 0;
        foreach (string g in GroupOrder)
            if (groups.TryGetValue(g, out Transform gt)) gt.SetSiblingIndex(index++);

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Organize] {moved} object(s) filed into {groups.Count} group(s) under {RootName}."
                  + (skipped.Count > 0 ? $" Left as they were: {string.Join(", ", skipped)}" : "") + " - save the scene.");
    }

    private static Transform Group(Transform station, string name, Dictionary<string, Transform> cache)
    {
        if (cache.TryGetValue(name, out Transform g)) return g;
        g = station.Find(name);
        if (g == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Organize Hierarchy");
            g = go.transform;
            Undo.SetTransformParent(g, station, "Organize Hierarchy");
            g.localPosition = Vector3.zero;
            g.localRotation = Quaternion.identity;
            g.localScale = Vector3.one;
        }
        cache[name] = g;
        return g;
    }

    /// <summary>Which group an object goes in - by what it does (its
    /// components, anywhere on it or its children) first, then by name.</summary>
    private static string Classify(Transform t)
    {
        string n = t.name;
        string ln = n.ToLower();
        GameObject go = t.gameObject;

        bool Has<T>() where T : Component => go.GetComponentInChildren<T>(true) != null;
        bool NameHas(params string[] keys)
        {
            foreach (string k in keys) if (ln.Contains(k.ToLower())) return true;
            return false;
        }

        if (go.GetComponentInChildren<HandController>(true) != null || go.GetComponentInChildren<SupportHandController>(true) != null
            || go.GetComponentInChildren<HandWashSequence>(true) != null) return "손";

        if (go.GetComponent<StoveBurner>() || go.GetComponent<StoveKnob>() || NameHas("Flame_", "StoveBody", "BurnerR_Model", "Burner")) return "화구";
        if (go.GetComponent<WashBasin>() || go.GetComponent<WaterTap>() || NameHas("WaterStream", "SinkBasin", "Faucet", "Sink")) return "개수대";
        if (go.GetComponent<CuttingBoardController>() || go.GetComponent<CuttingBoardFlipZone>() || go.GetComponent<CuttingBoardWashZone>()) return "도마";

        ToolItem tool = go.GetComponentInChildren<ToolItem>(true);
        if (go.GetComponent<KitchenTowelDispenser>() || (tool != null && tool.IsTowelKind) || NameHas("Towel", "Tissue", "행주")) return "위생도구";
        if (tool != null && tool.Kind == ToolKind.Knife && go.GetComponent<ToolItem>() != null) return "칼";

        if (go.GetComponent<PotLid>()) return "냄비뚜껑";
        MovableBowl vessel = go.GetComponent<MovableBowl>();
        if (vessel != null)
        {
            if (go.GetComponent<WokDragHandler>() || NameHas("wok")) return "웍";
            if (NameHas("pan")) return "후라이팬";
            if (NameHas("pot")) return "냄비";
            if (NameHas("doubanjiang", "chilipowder", "sauce")) return "소스류";
        }

        if (go.GetComponentInChildren<SeasoningBottle>(true) != null || go.GetComponent<SauceShelfController>()) return "소스류";

        if (vessel != null || go.GetComponent<DishShelfController>() || NameHas("plate", "bowl", "jongji", "dish")) return "접시류";

        if (tool != null || go.GetComponentInChildren<Strainer>(true) != null || go.GetComponentInChildren<MovableBowl>(true) != null
            || NameHas("utensil", "spoon", "measuring", "scoop", "whisk", "flipper")) return "요리도구류";

        if (go.GetComponent<IngredientItem>() || NameHas("vegetable", "ingredient")) return "요리재료";

        if (go.GetComponent<KitchenShelfController>() || NameHas("Station3D", "Background", "Floor", "CounterBody", "Shelf", "ZoneBG", "Wall")) return "가구류";

        if (go.GetComponent<CookingPlanManager>() || go.GetComponent<ExamPaperFlip>() || go.GetComponent<InfoRevealButton>()
            || go.GetComponent<SequenceHintButton>() || go.GetComponent<CountdownTimer>() || go.GetComponent<TextMesh>()
            || NameHas("Label_", "ExamPage", "Recipe", "Hint", "CutSpecification", "Timer")) return "이외";

        return null;
    }
}
