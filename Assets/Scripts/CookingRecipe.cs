using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One dish's recipe data (탕수육, 마파두부 ...): the meat portion it's written
/// for, the 튀김옷 (batter) mixes that are registered for it and how each is
/// graded, the texture/coating look settings and when 버무림 counts as done
/// (points and ranges: ExamScoring, per rule ID). Kept apart from the code so the numbers
/// can be set once they're known - nothing here is a made-up answer:
/// an unset amount stays 0 (미설정) and every mix starts 미검증 (unverified).
/// Loaded from Resources/Recipes (RecipeBook).
/// </summary>
[CreateAssetMenu(menuName = "Kitchen/Cooking Recipe", fileName = "Recipe")]
public class CookingRecipe : ScriptableObject
{
    public enum Grade { 미검증, 적정, 사용가능, 부적정 }
    public enum MeatBasis { 지급량, 손질후현재량 }

    [Header("레시피")]
    public string recipeId = "";
    public string displayName = "";
    [Tooltip("The ingredient this recipe's meat is (IngredientItem.IngredientName), e.g. 돼지등심.")]
    public string meatIngredientName = "";
    [Tooltip("The meat portion the amounts below are written for (g). 0 = 미설정: no ratio grading is run.")]
    public float baseMeatGrams;
    [Tooltip("Which meat amount the mixes are scaled by: the portion given (before trimming) or what's left after trimming.")]
    public MeatBasis meatBasis = MeatBasis.지급량;
    [TextArea] public string sourceNote = "";

    [Header("튀김옷 (batter)")]
    public bool hasBatter;
    [Tooltip("Mixes registered for baseMeatGrams - each with its own grade; several may be allowed at once (never averaged).")]
    public List<BatterMix> batterMixes = new List<BatterMix>();
    [Tooltip("How a mix with something outside the recipe in it (노른자, 전란, dry 감자전분 ...) is graded.")]
    public Grade offRecipeGrade = Grade.미검증;
    public BatterLook batterLook = new BatterLook();
    [Tooltip("Mixing strokes (one click each with a spatula/chopsticks/spoon) to go from 0 to fully mixed.")]
    [Min(1)] public int strokesToMix = 6;
    [Tooltip("Mixed this far (0..1) counts as 버무림 완료.")]
    [Range(0.5f, 1f)] public float mixDoneAt = 0.95f;

    [Header("밑간 / 소스 / 기타 항목 (data only - graded only once verified)")]
    public List<RecipeItem> marinade = new List<RecipeItem>();
    public List<RecipeItem> sauce = new List<RecipeItem>();
    public List<RecipeItem> otherItems = new List<RecipeItem>();

    // (감점 points and ranges: per rule ID in ExamScoring - e.g. "{recipeId}.batter.mix")

    public bool BaseMeatSet => baseMeatGrams > 0f;
}

/// <summary>One registered batter mix: amounts for the recipe's base meat.</summary>
[Serializable]
public class BatterMix
{
    public string label = "";
    [Tooltip("앙금녹말 (drained of its water), mL.")]
    public int settledStarchMl;
    [Tooltip("계란 흰자, mL.")]
    public int eggWhiteMl;
    public CookingRecipe.Grade grade = CookingRecipe.Grade.미검증;
    [Tooltip("Checked by someone (instructor / tested) - an unchecked mix is never used as an answer.")]
    public bool verified;
    [TextArea] public string source = "";
}

/// <summary>A recipe line that's data only for now (sauce, marinade, tofu ...).
/// amount 0 = 미설정.</summary>
[Serializable]
public class RecipeItem
{
    public string name = "";
    public float amount;
    public string unit = "mL";
    [Tooltip("As written (e.g. 4T) - kept so a spoon volume isn't confused with a weight.")]
    public string asWritten = "";
    public bool verified;
    [TextArea] public string note = "";
}

/// <summary>How the batter looks - tuning curves for the look only, not a
/// physical model of starch and egg white.</summary>
[Serializable]
public class BatterLook
{
    [Tooltip("x: liquid ÷ starch (mL ÷ mL: 흰자 etc. and the marinade's liquids over 앙금녹말) → y: stiffness 0 (runny) .. 1 (stiff).")]
    public AnimationCurve stiffnessByLiquidRatio = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(0.35f, 0.78f), new Keyframe(0.65f, 0.5f), new Keyframe(1f, 0.25f), new Keyframe(1.6f, 0.05f));
    [Tooltip("x: batter (mL) per 100 g of meat → y: coat thickness 0..1.")]
    public AnimationCurve thicknessByLoad = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(10f, 0.3f), new Keyframe(25f, 0.6f), new Keyframe(50f, 1f));
    [Tooltip("How much bigger a fully thick coat draws a piece (look only - the amounts don't change).")]
    public float maxInflate = 0.22f;
    public Color batterColor = new Color(0.96f, 0.94f, 0.86f);
    [Tooltip("Stiffness above this: lumpy, clumped coat.")]
    [Range(0f, 1f)] public float lumpyAbove = 0.68f;
    [Tooltip("Stiffness below this: thin coat, liquid left in the bowl.")]
    [Range(0f, 1f)] public float runnyBelow = 0.32f;
    [Tooltip("x: stiffness → y: how much of the coat stays on in the frying (estimate tuning): runny slides off, very stiff cracks a little.")]
    public AnimationCurve retentionByStiffness = new AnimationCurve(
        new Keyframe(0f, 0.3f), new Keyframe(0.3f, 0.7f), new Keyframe(0.55f, 1f), new Keyframe(0.85f, 0.9f), new Keyframe(1f, 0.75f));
    [Tooltip("1차 튀김: 하얗게 굳은 튀김옷 색.")]
    public Color firstFryColor = new Color(0.97f, 0.93f, 0.78f);
    [Tooltip("2차 튀김부터: 노릇한 색.")]
    public Color friedColor = new Color(0.86f, 0.62f, 0.28f);
    [Tooltip("튀김옷이 얇게 묻은 곳에 비치는 고기 색 (불투명 재질이라 투명도 대신 이 색과 섞는다).")]
    public Color meatShowColor = new Color(0.72f, 0.42f, 0.36f);
    [Tooltip("Seconds in hot oil to go fully golden (look).")]
    public float fryBrownSeconds = 40f;
}

/// <summary>The recipes in Resources/Recipes.</summary>
public static class RecipeBook
{
    private static CookingRecipe[] all;

    public static IReadOnlyList<CookingRecipe> All => all ?? (all = Resources.LoadAll<CookingRecipe>("Recipes"));

    /// <summary>The recipe whose batter this ingredient takes (null if none).</summary>
    public static CookingRecipe BatterFor(string ingredientName)
    {
        if (string.IsNullOrEmpty(ingredientName)) return null;
        foreach (CookingRecipe r in All)
            if (r != null && r.hasBatter && r.meatIngredientName == ingredientName) return r;
        return null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay() => all = null;
}
