using UnityEngine;

/// <summary>
/// 자투리 - what trimming/cutting leaves over: peel, stem, seeds, the garlic's
/// hard end, the uneven ends cut off to get to the spec. Food waste by type
/// (WasteRules.IsFood), not by name. Trimming and cutting drop these in the
/// cutting board's left corner (CuttingBoardController.AddScrap); they can be
/// picked up, gathered into a bowl, or - in clean-up (정리) - swept all at once
/// onto the knife's blade, then thrown into the FoodWasteBag.
/// </summary>
public class FoodScrap : Carryable
{
    /// <summary>Which ingredient it came off (IngredientItem.IngredientName).</summary>
    public string SourceName { get; private set; } = "";
    /// <summary>What kind of scrap: 껍질, 꼭지, 씨, 뿌리, 자투리...</summary>
    public string Kind { get; private set; } = "자투리";
    /// <summary>Its share of the ingredient's weight, when the ingredient has one (0 = unknown).</summary>
    public float Grams { get; set; }

    /// <summary>Knife in hand, clean-up on: a click on any scrap sweeps every scrap
    /// on the board onto the blade (칼등에 모아서), to carry to the waste bag.</summary>
    protected override void OnClickedWhileHandFull(Carryable held)
    {
        if (held is ToolItem knife && knife.Kind == ToolKind.Knife)
        {
            // (only in clean-up - otherwise the 정리 tab is pointed out)
            if (!CleanupMode.IsOn) { CleanupMode.NotifyBlocked(); return; }
            knife.SweepScraps(CurrentContainer as CuttingBoardController ?? CuttingBoardController.Instance, this);
        }
    }

    public override bool CanAcceptDrop(Carryable item) =>
        item is ToolItem knife && knife.Kind == ToolKind.Knife && CleanupMode.IsOn;

    /// <summary>A small scrap of the given ingredient, its color, roughly size (world units).</summary>
    public static FoodScrap Create(string sourceName, string kind, Color color, float size, Vector3 worldPos)
    {
        bool flat = kind == "껍질" || kind == "밑동" || kind == "지방" || kind == "단면";
        GameObject go = GameObject.CreatePrimitive(flat ? PrimitiveType.Cube : PrimitiveType.Sphere);
        go.name = $"Scrap_{sourceName}_{kind}";
        go.transform.position = worldPos;
        go.transform.rotation = Quaternion.AngleAxis(Random.Range(0f, 180f), Carryable.CounterUp) * Quaternion.Euler(-41f, 0f, 0f);
        go.transform.localScale = flat ? new Vector3(size * 1.3f, size * 0.15f, size * 0.8f)
                                       : new Vector3(size, size * 0.6f, size * 0.8f);
        Renderer r = go.GetComponent<Renderer>();
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit != null) r.material = new Material(lit);
        r.material.color = color;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // A bigger click box than the crumb itself, so it can still be picked up.
        Collider own = go.GetComponent<Collider>();
        if (own != null) Destroy(own);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = Vector3.one * 1.6f;
        FoodScrap scrap = go.AddComponent<FoodScrap>();
        scrap.SourceName = sourceName;
        scrap.Kind = kind;
        return scrap;
    }
}

/// <summary>Food or not (for the waste bag) - by component and ingredient data, not by name.</summary>
public static class WasteRules
{
    /// <summary>An ingredient (raw, cut or cooked) or a trimming scrap.</summary>
    public static bool IsFood(Carryable c) => c is IngredientItem || c is FoodScrap;

    /// <summary>Whether what's in hand has food to throw out: food itself, a
    /// bowl/pot/pan with food (or portioned starch) in it, the knife carrying
    /// pieces/scraps on its blade, the strainer with pieces in it. An empty
    /// container or a tool on its own has none.</summary>
    public static bool HasFoodToDump(Carryable held)
    {
        if (held == null) return false;
        if (GeneralTrashCan.IsEggShell(held)) return false; // (general waste, not food)
        if (IsFood(held)) return true;
        if (held is MovableBowl bowl)
        {
            if (bowl.HasPortion) return true;
            if (bowl.HasEgg) return true;
            if (PanSauce.Of(bowl, false) is PanSauce sauce && sauce.Amount > 0.01f) return true;
            foreach (Carryable c in bowl.Contents) if (IsFood(c)) return true;
            return false;
        }
        if (held is Strainer strainer) return strainer.HasContents;
        if (held is Cheesecloth cloth) return cloth.HasResidue; // the chilli left in it after straining
        if (held is ToolItem knife && knife.Kind == ToolKind.Knife) return knife.KnifeLoad != null || knife.HasScraps;
        return false;
    }
}

/// <summary>What went into the food waste bag - ingredient, kind and count only
/// (no made-up weights: the scene carries no amount data for scraps).</summary>
public static class WasteLog
{
    public struct Entry
    {
        public string ingredient, kind;
        public float time;
        /// <summary>Only when the ingredient's weight is known (0 = not recorded).</summary>
        public float grams;
    }

    public static readonly System.Collections.Generic.List<Entry> Entries = new System.Collections.Generic.List<Entry>();

    public static void Add(string ingredient, string kind, float grams = 0f)
    {
        Entries.Add(new Entry { ingredient = ingredient, kind = kind, time = Time.time, grams = grams });
        Debug.Log($"[음식물쓰레기] {ingredient} {kind}{(grams > 0f ? $" {grams:0.#}g" : "")} 버림 (누적 {Entries.Count}개)");
    }

    public static int CountOf(string ingredient)
    {
        int n = 0;
        foreach (Entry e in Entries) if (e.ingredient == ingredient) n++;
        return n;
    }
}
