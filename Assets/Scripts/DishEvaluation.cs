using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The end-of-exam judging of the finished dishes and the clean-up, once, at
/// submission (ExamEvaluation.Evaluating). Only the final state is judged -
/// what's on the plate, what's left on the counter - from what was recorded
/// while cooking (amounts in, heat, stirring, clumping, water boiled off,
/// cleaning). Nothing is shown while cooking.
///
/// Rules (ranges, points, official or practice) come from ExamScoring by ID;
/// a rule without a verified range gives practice feedback only, a rule with
/// no range at all 평가 보류. A cause is named only when the records show it.
/// 실격: only by an active (enabled + verified + evidence) DisqualifyRule -
/// none by default, so 실격 is off.
/// </summary>
public static class DishEvaluation
{
    public const string Tangsuyuk = "탕수육", Mapo = "마파두부", Cleanup = "정리 · 위생";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        ExamEvaluation.Evaluating -= EvaluateAll;
        ExamEvaluation.Evaluating += EvaluateAll;
    }

    private static bool dishesDone;

    /// <summary>The dishes not handed in on a serving plate (미완성).</summary>
    public static readonly List<string> MissingDishes = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay()
    {
        dishesDone = false;
        MissingDishes.Clear();
    }

    /// <summary>At 제출 (ExamFlow): the dishes judged as they are now - they
    /// don't change after. Once only.</summary>
    public static void EvaluateDishes()
    {
        if (dishesDone) return;
        dishesDone = true;
        CheckSubmitted(Tangsuyuk, "tangsuyuk", "돼지등심");
        CheckSubmitted(Mapo, "mapo", "두부", "돼지고기");
        foreach (CookingRecipe recipe in RecipeBook.All)
            if (recipe != null && recipe.hasBatter) BatterEvaluation.AddItems(recipe);
        EvaluateSauceDish(Tangsuyuk, "tangsuyuk", "돼지등심");
        EvaluateMapo();
    }

    /// <summary>At 정리 완료 / time up: the clean-up, the records, 실격, then 합격/불합격.</summary>
    public static void EvaluateAll()
    {
        EvaluateDishes();
        EvaluateCleanup();
        AddRecords();
        CheckDisqualify();
        foreach (CookingRecipe recipe in RecipeBook.All)
            if (recipe != null) BatterEvaluation.ReportUnsetItems(recipe);
        PassFail.Decide();
    }

    /// <summary>Handed in on a serving plate (PlatingPlate), and how done it is.</summary>
    private static void CheckSubmitted(string dish, string id, params string[] mains)
    {
        IngredientItem onPlate = null, anywhere = null;
        foreach (string m in mains)
            foreach (IngredientItem i in Object.FindObjectsByType<IngredientItem>(FindObjectsSortMode.None))
            {
                if (i.IngredientName != m) continue;
                if (anywhere == null) anywhere = i;
                if (onPlate == null && i.Container != null && i.Container.GetComponent<PlatingDestination>() != null) onPlate = i;
            }
        if (onPlate == null)
        {
            MissingDishes.Add(dish);
            ExamEvaluation.Add(new ExamEvaluation.Item
            {
                ruleId = $"{id}.submitted", dish = dish, title = "완성품 제출", status = ExamEvaluation.Status.감점,
                observed = "완성 접시(PlatingPlate)에 담아 제출하지 않음", cause = anywhere != null && anywhere.Container != null ? $"기록: {anywhere.Container.name}에 남아 있음" : "",
                tip = "팬에서 완성 접시로 옮겨 담은 뒤 제출하세요.", pointsText = "미완성 - 불합격 사유",
            });
            return;
        }
        ExamEvaluation.Add(new ExamEvaluation.Item
        {
            ruleId = $"{id}.submitted", dish = dish, title = "완성품 제출", status = ExamEvaluation.Status.적정,
            observed = $"{onPlate.Container.name}에 담아 제출", pointsText = "제출됨",
        });
        // 익힘: the main ingredient's state as handed in
        CookableItem ck = onPlate.GetComponent<CookableItem>();
        float? done = null;
        string seen = "익힘 상태 기록 없음";
        if (ck != null)
        {
            done = ck.CurrentStage == CookableItem.Stage.Done ? 1f : ck.CurrentStage == CookableItem.Stage.InProgress ? 0.5f
                 : ck.CurrentStage == CookableItem.Stage.Raw ? 0f : 2f;
            seen = ck.CurrentStage == CookableItem.Stage.Raw ? "익히지 않은 채 제출" : ck.CurrentStage == CookableItem.Stage.InProgress ? "덜 익은 채 제출"
                 : ck.CurrentStage == CookableItem.Stage.Burnt ? "타서 눌어붙은 채 제출" : "지나치게 익힘";
        }
        ExamEvaluation.Add(ExamEvaluation.Judge($"{id}.doneness", dish, $"{onPlate.IngredientName} 익힘", done,
            "알맞게 익음", seen, ck != null ? $"기록: 익힘 진행 {ck.CookProgress:0}/{ck.TimeToCooked:0}" : "",
            "가운데까지 익을 때까지 가열하고, 바닥에 눌어붙지 않게 저어 주세요."));
    }

    // ---- 완성 그릇 ----

    private static IngredientItem Find(string ingredientName)
    {
        foreach (IngredientItem i in Object.FindObjectsByType<IngredientItem>(FindObjectsSortMode.None))
            if (i.IngredientName == ingredientName) return i;
        return null;
    }

    /// <summary>The sauce on the same plate/pan as the dish's main ingredient -
    /// nothing guessed from other containers.</summary>
    private static PanSauce SauceWith(IngredientItem main, out string where)
    {
        MovableBowl c = main != null ? main.Container : null;
        where = c != null ? c.name : "(그릇 밖)";
        PanSauce s = c != null ? PanSauce.Of(c, false) : null;
        return s != null && s.Amount > 0.01f ? s : null;
    }

    private static string Notes(PanSauce s)
    {
        var n = new List<string>(s.Notes);
        return n.Count > 0 ? "기록: " + string.Join(", ", n) : "";
    }

    private static float Darkness(Color c) => 1f - (0.299f * c.r + 0.587f * c.g + 0.114f * c.b);

    /// <summary>농도 · 뭉침 · 색 · 맛 균형 of a dish's finished sauce.</summary>
    private static void EvaluateSauceDish(string dish, string id, string mainIngredient)
    {
        IngredientItem main = Find(mainIngredient);
        PanSauce s = SauceWith(main, out string where);
        if (s == null)
        {
            ExamEvaluation.Add(ExamEvaluation.Judge($"{id}.sauce.thickness", dish, "소스 농도", null, "",
                $"완성 그릇({where})에 소스가 없음", "소스를 만든 그릇이 완성 그릇과 다르거나 소스를 붓지 않음 - 확인 불가"));
            return;
        }

        string starch = $"전분 {s.StarchSpoons * 15f:0}mL, 가열로 호화 {s.Gel * 100f:0}%"
                      + (s.EvaporatedMl > 0.5f ? $", 졸여서 줄어든 물 {s.EvaporatedMl:0}mL" : "");
        ExamEvaluation.Add(ExamEvaluation.Judge($"{id}.sauce.thickness", dish, "소스 농도", s.Thickness,
            "소스 농도가 적당함",
            s.Thickness > 0.6f ? "완성 소스가 지나치게 되직함" : "완성 소스가 묽음",
            $"기록: {starch} (원인 하나로 단정할 수 없음)",
            "전분 투입량과 투입 후 섞는 과정, 졸이는 시간을 확인하세요."));

        string lumpNotes = Notes(s);
        ExamEvaluation.Add(ExamEvaluation.Judge($"{id}.sauce.lumps", dish, "전분 뭉침", s.Lumps,
            "전분 뭉침 없음",
            "완성 소스에 전분 뭉침이 남음",
            lumpNotes.Length > 0 ? lumpNotes : "원인 기록 없음", // (the notes go with the sauce when it's moved)
            "전분은 물에 풀어 넣고, 넣는 즉시 저어 주세요."));

        Color col = s.LiquidColor;
        string colorCause = s.Burn >= 0.3f ? $"기록: 탄 정도 {s.Burn * 100f:0}% {Notes(s)}" : $"기록: 들어간 재료 {PartsText(s)}";
        ExamEvaluation.Add(ExamEvaluation.Judge($"{id}.sauce.color", dish, "소스 색", Mathf.Max(Darkness(col), s.Burn),
            "소스 색이 적당함",
            s.Burn >= 0.3f ? "소스가 탄 색을 띰" : "소스 색이 지나치게 어두움",
            colorCause,
            "간장·장류 양과 가열 시간을 확인하세요."));

        s.TasteEstimate(out float sweet, out float sour, out float salty);
        // (no verified reference yet: shown as an estimate, 평가 보류 - ExamScoring rule without a range)
        ExamEvaluation.Add(ExamEvaluation.Judge($"{id}.sauce.taste", dish, "맛 균형 (시뮬레이션 추정)", null, "",
            $"단맛 {sweet:0.00} · 신맛 {sour:0.00} · 짠맛 {salty:0.00} (액체 1스푼당 상대값 - 실제 맛 측정 아님)",
            "검증된 기준 배합이 없어 판단하지 않음"));
    }

    private static string PartsText(PanSauce s)
    {
        var list = new List<string>();
        foreach (var kv in s.Parts) if (kv.Value > 0.01f) list.Add($"{kv.Key} {kv.Value * 15f:0}mL");
        return string.Join(", ", list);
    }

    private static void EvaluateMapo()
    {
        IngredientItem tofu = Find("두부");
        PanSauce s = SauceWith(tofu, out string where);

        // 고추기름: what's in the finished dish only - oil burnt and thrown away
        // before isn't held against the dish
        bool discardedBurnt = false;
        foreach (WasteLog.Entry e in WasteLog.Entries) if (e.kind == "탄 것 폐기") discardedBurnt = true;
        bool used = s != null && s.InfusionOf("고춧가루") >= 0.3f && s.Parts.ContainsKey("식용유");
        string oilCause = s == null ? "완성 소스 없음" : used ? Notes(s) : "완성 소스에서 고추기름 성분이 확인되지 않음";
        if (discardedBurnt) oilCause = (oilCause.Length > 0 ? oilCause + " / " : "") + "탄 기름을 버리고 다시 만든 기록 있음 (버린 기름은 완성 음식 평가에 반영하지 않음)";
        ExamEvaluation.Add(ExamEvaluation.Judge("mapo.chilioil.burn", Mapo, "고추기름 (탄 정도 · 사용 여부)",
            used ? s.Burn : (float?)null,
            "고추기름이 쓰였고 타지 않음",
            used ? $"완성 소스의 고추기름이 탐 (탄 정도 {s.Burn * 100f:0}%)" : $"완성 그릇({where})에 고추기름이 쓰이지 않았거나 확인 불가",
            oilCause, "고춧가루는 약한 불에서 저어 가며 볶고, 탔다면 버리고 다시 만드세요."));

        EvaluateChiliOilMaking(used && s.Burn >= 0.3f);

        EvaluateSauceDish(Mapo, "mapo", "두부");

        // 두부 형태
        float dmg = 0f;
        int n = 0;
        if (tofu != null)
            foreach (FoodPiece p in tofu.GetComponentsInChildren<FoodPiece>()) { dmg += p.Damage; n++; }
        ExamEvaluation.Add(ExamEvaluation.Judge("mapo.tofu.shape", Mapo, "두부 형태 유지", n > 0 ? dmg / n : (float?)null,
            "두부가 형태를 유지함",
            n > 0 ? $"두부가 부서짐 (평균 손상 {dmg / n * 100f:0}%)" : "두부를 썰지 않았거나 찾을 수 없음 - 판단 불가",
            n > 0 ? "기록: 두부가 든 팬을 주걱으로 저은 만큼 손상 누적" : "",
            "두부를 넣은 뒤에는 주걱으로 세게 젓지 말고 팬을 흔들어 섞으세요."));
    }

    /// <summary>고추기름 만드는 과정 (ChiliOilLog): 주걱으로 저으며 볶았는지
    /// (조리과정), 타서 연기 날 만큼 과열됐는지 (안전), 면보에 걸렀는지
    /// (조리과정). Every batch counts - one burnt and thrown away too. If the
    /// burnt oil is also in the finished dish, that's the same problem as the
    /// dish's own 고추기름 item: shown, but not taken off twice.</summary>
    private static void EvaluateChiliOilMaking(bool burntInDish)
    {
        if (!ChiliOilLog.Made)
        {
            ExamEvaluation.Add(ExamEvaluation.Judge("mapo.chilioil.stir", Mapo, "고추기름 볶기 - 젓기 (조리과정)", null, "",
                "고추기름을 볶은 기록 없음 - 판단 불가", ""));
            return;
        }
        string batches = ChiliOilLog.Batches > 1 ? $" ({ChiliOilLog.Batches}번 만듦)" : "";
        ExamEvaluation.Add(ExamEvaluation.Judge("mapo.chilioil.stir", Mapo, "고추기름 볶기 - 젓기 (조리과정)", ChiliOilLog.StirredShare,
            $"주걱으로 저어 가며 볶음 (볶는 시간의 {ChiliOilLog.StirredShare * 100f:0}%){batches}",
            $"볶는 동안 젓지 않고 둔 시간이 김 (저은 시간 {ChiliOilLog.StirredShare * 100f:0}%){batches}",
            $"기록: 볶은 시간 {ChiliOilLog.HotSeconds:0}초 중 저은 시간 {ChiliOilLog.StirredSeconds:0}초, 가장 오래 젓지 않은 구간 {ChiliOilLog.LongestUnstirred:0}초",
            "기름과 고춧가루를 넣으면 불 옆을 떠나지 말고 주걱으로 계속 저으세요."));

        ExamEvaluation.Item burn = ExamEvaluation.Judge("mapo.chilioil.overheat", Mapo, "고추기름 과열 · 탐 (안전)", ChiliOilLog.MaxBurn,
            "볶는 동안 기름이 타지 않음",
            $"볶는 중 기름이 타 연기가 날 만큼 과열됨 (탄 정도 최대 {ChiliOilLog.MaxBurn * 100f:0}%, 타는 동안 {ChiliOilLog.BurningSeconds:0}초){batches}",
            $"기록: 가장 오래 젓지 않은 구간 {ChiliOilLog.LongestUnstirred:0}초 - 젓기와 불 조절을 함께 확인 (원인 하나로 단정하지 않음)",
            "약불에서 저어 가며 볶고, 고춧가루 색이 우러나면 바로 불을 끄고 거르세요.");
        if (burn.status == ExamEvaluation.Status.감점 && burntInDish)
        {
            // the same burnt oil is already marked on the dish (고추기름 탄 정도): not twice
            burn.points = 0f;
            burn.duplicate = true;
            burn.pointsText = "중복 감점 없음 (완성 음식 '고추기름' 항목에 반영)";
        }
        ExamEvaluation.Add(burn);

        ExamEvaluation.Add(ExamEvaluation.Judge("mapo.chilioil.strain", Mapo, "고추기름 면보 거르기 (조리과정)", ChiliOilLog.StrainedSpoons > 0.05f ? 1f : 0f,
            $"면보에 걸러 고추기름만 사용 ({ChiliOilLog.StrainedSpoons * 15f:0}mL)",
            "고춧가루를 걸러내지 않음",
            "기록: 면보로 거른 고추기름 없음",
            "볶은 기름은 면보를 깐 볼에 부어 고춧가루를 걸러내세요."));
    }

    // ---- 정리 · 위생 ----

    private static void EvaluateCleanup()
    {
        foreach (BurnerFloorDirt f in BurnerFloorDirt.All)
        {
            if (f == null) continue;
            if (!f.NeedsCleaning)
            {
                var none = new ExamEvaluation.Item { ruleId = "cleanup.burnerfloor." + f.name, dish = Cleanup, title = $"{f.Burner?.name} 받침 아래 청소",
                    status = ExamEvaluation.Status.적정, observed = "사용하지 않아 오염 없음", pointsText = "해당 없음" };
                ExamEvaluation.Add(none);
                continue;
            }
            ExamEvaluation.Item it = ExamEvaluation.Judge("cleanup.burnerfloor", Cleanup, $"{f.Burner?.name} 받침 아래 청소", f.DirtLeft,
                "받침 아래가 깨끗함" + (f.GrateInstalled ? "" : " (받침 미장착)"),
                "받침 아래 오염이 남은 채 제출" + (f.GrateInstalled ? "" : " (받침 미장착)"),
                f.SoiledAgainAfterCleaning ? "기록: 한 번 닦았지만 이후 다시 오염된 상태로 제출됨" : f.WasCleanedOnce ? "" : "기록: 받침을 빼고 닦은 기록 없음",
                "정리 때 받침을 빼고 아래를 닦은 뒤, 다시 조리하지 말고 제출하세요.");
            it.ruleId = "cleanup.burnerfloor." + f.name;
            ExamEvaluation.Add(it);
        }

        float left = 0f;
        int stains = 0;
        foreach (WipeableSurface w in WipeableSurface.All)
        {
            if (w == null || w.GetComponent<BurnerFloorDirt>() != null) continue;
            left += w.DirtLeft;
            stains += w.StainCount;
        }
        ExamEvaluation.Add(ExamEvaluation.Judge("cleanup.surfaces", Cleanup, "조리대 · 개수대 얼룩", left,
            "조리대가 깨끗함", $"얼룩이 남은 채 제출 (얼룩 {stains}곳)",
            stains > 0 ? "기록: 조리 중 생긴 얼룩(물·자투리·기름)을 다 닦지 않음" : "",
            "정리 때 행주로 조리대와 개수대 주변을 닦으세요."));
    }

    /// <summary>What was recorded while cooking (위생·조리과정): each once, as a practice result
    /// unless a verified rule with that ID says otherwise.</summary>
    private static void AddRecords()
    {
        var seen = new HashSet<string>();
        foreach (ExamDeductions.Entry e in ExamDeductions.Entries)
            AddRecord(seen, e.category, e.reason);
        if (HygieneLog.Instance != null)
            foreach (string v in HygieneLog.Instance.Violations) AddRecord(seen, ExamDeductions.Hygiene, v);
    }

    private static void AddRecord(HashSet<string> seen, string category, string reason)
    {
        string id = $"record.{category}.{reason}";
        if (!seen.Add(id)) return;
        // (a repeat of the same thing - "... (2회째)" - goes by the same rule)
        ScoringRule rule = ExamScoring.Current.Rule(id)
            ?? ExamScoring.Current.Rule(System.Text.RegularExpressions.Regex.Replace(id, @" \(\d+회째\)$", ""));
        var it = new ExamEvaluation.Item
        {
            ruleId = id, dish = Cleanup, title = rule != null && !string.IsNullOrEmpty(rule.title) ? rule.title : $"{category} 기록",
            status = ExamEvaluation.Status.감점, observed = reason, cause = "조리 중 기록", tip = rule != null ? rule.tip : "",
        };
        it.official = rule != null && rule.official && rule.rangeVerified;
        it.points = it.official && rule.pointsVerified ? rule.points : 0f;
        // (a practice rule's weight is shown - how serious it is - but not taken off the score)
        it.pointsText = it.points > 0f ? $"-{it.points:0.#}점 (공식)" : it.official ? "공식 · 배점 미설정"
            : rule != null && rule.points > 0f ? $"연습 감점 -{rule.points:0.#}점 (중대) · 공식 점수 아님" : "연습 감점 · 공식 점수 아님";
        ExamEvaluation.Add(it);
    }

    // ---- 실격 (active rules only) ----

    private static void CheckDisqualify()
    {
        int active = 0;
        foreach (DisqualifyRule r in ExamScoring.Current.disqualifyRules)
        {
            if (r == null || !r.Active) continue;
            active++;
            if (!Met(r, out string seen)) continue;
            ExamEvaluation.Add(new ExamEvaluation.Item
            {
                ruleId = "dq." + r.id, dish = r.dish, title = r.title, status = ExamEvaluation.Status.실격,
                official = true, pointsText = "공식 실격", observed = seen, cause = "근거: " + r.evidence,
            });
        }
        if (active == 0)
            ExamEvaluation.Report(BatterEvaluation.SetupSection, "dq.off", "실격 판정: 비활성 (검증된 실격 규칙 없음)");
    }

    private static bool Met(DisqualifyRule r, out string seen)
    {
        seen = "";
        string main = r.dish == Mapo ? "두부" : "돼지등심";
        PanSauce s = SauceWith(Find(main), out string where);
        switch (r.conditionKey)
        {
            case "dish_missing":
                seen = $"완성 그릇({where})에 완성 음식이 없음";
                return Find(main) == null || s == null;
            case "upper_burner_lit":
                seen = StoveBurner.RecordedLit.Count > 0 ? "위쪽(1번) 화구에 불을 켬" : "";
                return StoveBurner.RecordedLit.Count > 0;
            case "sauce_burnt":
                seen = s != null ? $"완성 소스 탄 정도 {s.Burn * 100f:0}%" : "";
                return s != null && s.Burn >= r.threshold;
        }
        return false;
    }
}
