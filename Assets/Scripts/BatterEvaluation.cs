using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grades each recipe's 튀김옷 at the final evaluation - two separate results,
/// never folded into one: 배합 (the mix: what was measured in, against the
/// mixes registered in the recipe) and 버무림 (how far it was mixed through).
///
/// 배합: the registered mixes are for the recipe's base meat; they're scaled to
/// the meat there actually is (목표량 = 기준량 × 고기량 ÷ 기준 고기량) and the
/// measured mix has to match one of them (to within half a mL - the spoons
/// only make whole 5 mL steps, so nothing is rounded to make it fit; a scaled
/// target the spoons can't make is reported to the developer). A match takes
/// that mix's grade - only if the mix is verified; an unverified mix, or a
/// mix that matches none, is 미검증 (no deduction). Base meat unset: 설정 누락,
/// no ratio grading at all. Points only from verified scoring rules (ExamScoring).
/// Called by DishEvaluation at the end - nothing is judged while cooking.
/// </summary>
public static class BatterEvaluation
{
    public const string SetupSection = "미설정 · 미검증 항목";

    public struct MixResult
    {
        public string status;          // 설정 누락 / 미실시 / 미검증 / 적정 / 사용가능 / 부적정
        public CookingRecipe.Grade grade;
        public string detail;
    }

    /// <summary>The 배합 result alone (also for testing).</summary>
    public static MixResult GradeMix(CookingRecipe recipe, BatterCoating coat)
    {
        var res = new MixResult { grade = CookingRecipe.Grade.미검증 };
        if (!recipe.BaseMeatSet)
        {
            res.status = "설정 누락";
            res.detail = "기준 고기량 미설정 - 비율 평가를 하지 않음";
            return res;
        }
        if (coat == null || !coat.HasAny)
        {
            res.status = "미실시";
            res.detail = "튀김옷 재료가 들어가지 않음";
            return res;
        }
        IngredientItem meat = coat.Meat;
        float basis = recipe.meatBasis == CookingRecipe.MeatBasis.지급량 ? meat.GivenGrams : meat.AmountGrams;
        string measured = $"앙금녹말 {coat.SettledStarchMl:0}mL, 흰자 {coat.EggWhiteMl:0}mL" + OffRecipeText(coat);
        if (basis <= 0f)
        {
            res.status = "설정 누락";
            res.detail = $"{measured} - 고기량 데이터 없음 ({meat.name}.amountGrams)";
            return res;
        }
        float scale = basis / recipe.baseMeatGrams;
        string basisText = $"고기 {basis:0.#}g ({recipe.meatBasis}) / 기준 {recipe.baseMeatGrams:0.#}g";

        if (coat.OffRecipeMl > 0.01f)
        {
            res.grade = recipe.offRecipeGrade;
            res.status = recipe.offRecipeGrade.ToString();
            res.detail = $"{measured} - 레시피 외 재료가 들어감 ({basisText})";
            return res;
        }

        BatterMix matched = null;
        foreach (BatterMix mix in recipe.batterMixes)
        {
            float ts = mix.settledStarchMl * scale, tw = mix.eggWhiteMl * scale;
            if (!Spoonable(ts) || !Spoonable(tw))
                Debug.LogWarning($"[레시피 확인 필요] {recipe.displayName} '{mix.label}': 고기 {basis:0.#}g 환산 목표 앙금 {ts:0.##}mL / 흰자 {tw:0.##}mL - 5mL 정량 스푼으로 만들 수 없는 값");
            if (Mathf.Abs(coat.SettledStarchMl - ts) < 0.5f && Mathf.Abs(coat.EggWhiteMl - tw) < 0.5f) { matched = mix; break; }
        }
        if (matched == null)
        {
            res.status = "미검증";
            res.detail = $"{measured} - 등록된 배합과 다름, 판단 기준 없음 ({basisText})";
        }
        else if (!matched.verified)
        {
            res.status = "미검증";
            res.detail = $"{measured} - 테스트 배합 '{matched.label}'과 같음 (검증 전이라 정답으로 쓰지 않음, {basisText})";
        }
        else
        {
            res.grade = matched.grade;
            res.status = matched.grade.ToString();
            res.detail = $"{measured} - 배합 '{matched.label}' ({basisText})";
        }
        return res;
    }

    private static bool Spoonable(float ml) => Mathf.Abs(ml / 5f - Mathf.Round(ml / 5f)) < 0.002f;

    private static string OffRecipeText(BatterCoating coat)
    {
        var extra = new List<string>();
        foreach (var kv in coat.Parts)
            if (kv.Key != BatterCoating.Settled && kv.Key != BatterCoating.White && kv.Key != BatterCoating.BeatenWhite && kv.Value > 0.01f)
                extra.Add($"{kv.Key} {kv.Value:0}mL");
        return extra.Count > 0 ? $" (그 외: {string.Join(", ", extra)})" : "";
    }

    /// <summary>The 튀김옷 items for one recipe (DishEvaluation calls this at the
    /// end): 배합, 코팅량, 균일도 (버무림), 튀긴 뒤 유지 - each judged on its own.</summary>
    public static void AddItems(CookingRecipe recipe)
    {
        IngredientItem meat = null;
        foreach (IngredientItem i in Object.FindObjectsByType<IngredientItem>(FindObjectsSortMode.None))
            if (i.IngredientName == recipe.meatIngredientName && (meat == null || BatterCoating.Of(i, false) != null)) meat = i;
        BatterCoating coat = BatterCoating.Of(meat, false);
        string dish = recipe.displayName, id = recipe.recipeId;

        // 배합 - the mix against the registered mixes (grade), apart from everything else
        MixResult mix = GradeMix(recipe, coat);
        var mixItem = new ExamEvaluation.Item { ruleId = $"{id}.batter.mix", dish = dish, title = "튀김옷 배합", observed = mix.detail };
        ScoringRule mixRule = ExamScoring.Current.Rule(mixItem.ruleId);
        switch (mix.status)
        {
            case "적정": case "사용가능":
                mixItem.status = ExamEvaluation.Status.적정;
                mixItem.pointsText = mix.status == "사용가능" ? "사용 가능 배합 (질감 차이 있음)" : "적정 배합";
                break;
            case "부적정":
                mixItem.status = ExamEvaluation.Status.감점;
                mixItem.official = mixRule != null && mixRule.official && mixRule.rangeVerified;
                mixItem.points = mixItem.official && mixRule.pointsVerified ? mixRule.points : 0f;
                mixItem.pointsText = mixItem.points > 0f ? $"-{mixItem.points:0.#}점 (공식)" : mixItem.official ? "공식 · 배점 미설정" : "연습 감점 · 공식 점수 아님";
                mixItem.cause = "등록된 부적정 배합과 같음";
                mixItem.tip = mixRule != null ? mixRule.tip : "";
                break;
            default:
                mixItem.status = ExamEvaluation.Status.평가보류;
                mixItem.pointsText = mix.status; // 미검증 / 설정 누락 / 미실시
                break;
        }
        ExamEvaluation.Add(mixItem);

        if (coat == null || !coat.HasAny)
        {
            ExamEvaluation.Add(ExamEvaluation.Judge($"{id}.batter.coat", dish, "튀김옷 코팅량과 균일도", null, "",
                "튀김옷을 입히지 않음", "튀김옷 재료(앙금녹말·흰자)를 고기에 넣은 기록이 없음"));
            return;
        }

        // 앙금: spooned before its water was poured off - a wetter 앙금 went in
        ExamEvaluation.Add(ExamEvaluation.Judge($"{id}.batter.drained", dish, "앙금 윗물 제거", coat.WetSettledMl,
            "윗물을 버린 앙금을 사용함",
            $"윗물을 버리지 않은 물기 많은 앙금이 들어감 ({coat.WetSettledMl:0}mL)",
            "기록: 앙금이 가라앉은 뒤 윗물을 따라 버리기 전에 떠서 사용",
            "앙금이 가라앉으면 윗물을 따라 버린 뒤 앙금만 떠서 쓰세요."));

        string load = coat.LoadPer100g >= 0f ? $"고기 100g당 약 {coat.LoadPer100g:0.#}mL" : "고기량 미상";
        string word = $"질감 {coat.TextureWord}";
        string amountCause = coat.TextureWord == "묽음" ? $"기록: 액체가 녹말 대비 많음 (액체/녹말 {coat.LiquidRatio:0.00})"
                           : coat.TextureWord == "뻑뻑함" ? $"기록: 녹말이 액체 대비 많음 (액체/녹말 {coat.LiquidRatio:0.00})"
                           : $"기록: 튀김옷 총량 {coat.BatterMl:0}mL ({load})";
        ExamEvaluation.Add(ExamEvaluation.Judge($"{id}.batter.coat", dish, "튀김옷 코팅량", coat.Thickness,
            $"코팅이 적당한 두께로 입혀짐 ({load}, {word})",
            coat.Thickness < 0.5f ? $"코팅이 얇음 ({load}, {word})" : $"코팅이 두꺼움 ({load}, {word})",
            amountCause, "앙금녹말과 흰자의 양을 고기 양에 맞춰 계량하고, 넣은 뒤 충분히 버무리세요."));

        ExamEvaluation.Add(ExamEvaluation.Judge($"{id}.batter.even", dish, "튀김옷 균일도 (버무림)", coat.MixProgress,
            $"고르게 버무려짐 ({coat.Strokes}회)",
            $"덜 버무려져 코팅이 고르지 않음 ({coat.MixProgress * 100f:0}%)",
            coat.NeedsRemix ? "기록: 버무린 뒤 재료를 더 넣고 다시 섞지 않음" : $"기록: 버무린 횟수 {coat.Strokes}회",
            "재료를 모두 넣은 뒤 고기 전체에 고루 묻을 때까지 버무리세요."));

        ExamEvaluation.Add(ExamEvaluation.Judge($"{id}.batter.retention", dish, "튀긴 뒤 튀김옷 유지",
            coat.Fried ? coat.Retention : (float?)null,
            $"튀긴 뒤 튀김옷이 잘 붙어 있음 (추정 {coat.Retention * 100f:0}%)",
            coat.Fried ? $"튀기는 중 튀김옷이 일부 벗겨짐 (추정 {coat.Retention * 100f:0}% 남음)" : "튀기지 않음 - 판단 불가",
            coat.Fried ? (coat.ColdOilSeconds > 1f ? $"기록: 기름 온도가 오르기 전 {coat.ColdOilSeconds:0}초 튀김" : $"기록: {word}, 버무림 {coat.MixProgress * 100f:0}%")
                       : "튀김 기록 없음",
            "기름 온도를 확인한 뒤 넣고, 튀김옷 농도를 맞추세요."));
    }

    /// <summary>What's still unset or unverified in the recipe - listed as such.</summary>
    public static void ReportUnsetItems(CookingRecipe recipe)
    {
        var missing = new List<string>();
        if (!recipe.BaseMeatSet) missing.Add("기준 고기량");
        if (recipe.hasBatter)
        {
            int verified = 0;
            foreach (BatterMix m in recipe.batterMixes) if (m.verified) verified++;
            if (verified == 0) missing.Add("검증된 튀김옷 배합 없음 (테스트 배합만)");
        }
        AddItems(missing, recipe.marinade, "밑간");
        AddItems(missing, recipe.sauce, "소스");
        AddItems(missing, recipe.otherItems, "");
        if (missing.Count > 0)
            ExamEvaluation.Report(SetupSection, $"{recipe.recipeId}.unset", $"{recipe.displayName}: {string.Join(", ", missing)} - 평가하지 않음");
    }

    private static void AddItems(List<string> into, List<RecipeItem> items, string group)
    {
        foreach (RecipeItem it in items)
        {
            string label = string.IsNullOrEmpty(group) ? it.name : $"{group} {it.name}";
            if (it.amount <= 0f) into.Add($"{label} 미설정");
            else if (!it.verified) into.Add($"{label} {it.asWritten} 미검증");
        }
    }
}
