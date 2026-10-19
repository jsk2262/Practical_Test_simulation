using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전체 과정 영상의 요리 부분 (썰기부터 채점까지). 장면마다 할 일을 순서대로
/// 줄 세워(Work) 하나씩 진행하고, 손이 묶인 동작(썰기 · 붓기 · 젓기 …)이 끝나야
/// 다음으로 넘어간다. 자막은 "왜 이 과정이 들어가 있는지".
/// </summary>
public static partial class DemoFilm
{
    // ---- 할 일 줄 세우기 ----
    private class Work
    {
        private readonly List<Func<float, bool>> steps = new List<Func<float, bool>>();
        private int i;
        private float t0 = -1f;
        public bool Done => i >= steps.Count;

        /// <summary>한 번 하고, 손이 풀릴 때까지 (최소 gap초) 기다림.</summary>
        public Work Do(Action a, float gap = 0.25f)
        {
            bool did = false;
            steps.Add(t =>
            {
                if (!did)
                {
                    did = true;
                    try { a(); } catch (Exception e) { Debug.LogWarning("[DemoFilm] " + e.Message); }
                    return false;
                }
                return t >= gap / spd && !Busy;
            });
            return this;
        }

        public Work Wait(float seconds) { steps.Add(t => t >= seconds / spd); return this; }
        public Work Until(Func<bool> f, float max = 8f) { steps.Add(t => f() || t >= max); return this; }

        public void Tick()
        {
            if (Done) return;
            if (t0 < 0f) t0 = Time.unscaledTime;
            if (steps[i](Time.unscaledTime - t0)) { i++; t0 = -1f; }
        }
    }

    private static Work work;
    private static readonly System.Reflection.FieldInfo CuttingField =
        typeof(IngredientItem).GetField("isCutting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    /// <summary>손이 묶였거나 무언가 썰고 있는 중.</summary>
    private static bool Busy
    {
        get
        {
            if (Hand != null && Hand.IsLocked) return true;
            if (CuttingField != null)
                foreach (IngredientItem i in UnityEngine.Object.FindObjectsByType<IngredientItem>(FindObjectsSortMode.None))
                    if ((bool)CuttingField.GetValue(i)) return true;
            return false;
        }
    }

    // ---- 자주 쓰는 것들 ----
    private static MovableBowl TangPlate => Bowl("plateSize2");
    private static MovableBowl MapoPlate => Bowl("plateSize2_");
    private static MovableBowl Pan => Bowl("FryingPan");
    private static MovableBowl Wok => Bowl("Wok");
    private static MovableBowl PorkBowl => Bowl("bowlSize1.5");
    private static MovableBowl StarchBowl => Bowl("bowlSize1x2");
    private static MovableBowl EggBowl => Bowl("bowlSize1_");
    private static MovableBowl ChiliOilBowl => Bowl("bowlSize2x3");
    private static MovableBowl FriedPlate => Bowl("plateSize1.5");
    private static WashBasin Sink => UnityEngine.Object.FindFirstObjectByType<WashBasin>();
    private static ToolItem Knife => Tool(ToolKind.Knife);
    private static ToolItem WoodenSpoon => GameObject.Find("Spoon_wood")?.GetComponent<ToolItem>();
    private static ToolItem SteelSpoon => GameObject.Find("Spoon_steel")?.GetComponent<ToolItem>();
    private static Strainer BigStrainer => GameObject.Find("Strainer_Large")?.GetComponent<Strainer>();
    private static Cheesecloth Cloth => GameObject.Find("Cheesecloth_1")?.GetComponent<Cheesecloth>();
    private static ToolItem Chopsticks => UnityEngine.Object.FindFirstObjectByType<FryingChopsticks>();

    /// <summary>그 접시(그릇)에 담긴, 이 이름의 재료.</summary>
    private static IngredientItem IngIn(string key, MovableBowl where)
    {
        foreach (IngredientItem i in UnityEngine.Object.FindObjectsByType<IngredientItem>(FindObjectsSortMode.None))
            if (i.IngredientName == key && (where == null || (object)i.CurrentContainer == where)) return i;
        return null;
    }

    private static IngredientItem AnyIng(string key) => IngIn(key, null);

    private static void Click(Component c) { if (c is IClickable k) k.OnClicked(); }

    private static void OnBurner(MovableBowl vessel, StoveBurner.FlameLevel level)
    {
        if (vessel == null) return;
        Take(vessel);
        BurnerR?.ForceActivate();
        BurnerR?.SetLevel(level);
        Burner(true);
    }

    /// <summary>화구 위에 있는 것을 내려놓음 (화구는 하나만).</summary>
    private static void OffBurner(MovableBowl vessel, float x, float y)
    {
        if (vessel == null || vessel.CurrentBurner == null) return;
        Take(vessel);
        PutDown(x, y);
    }

    /// <summary>도마에 올려 칼로 썰고, 썬 것을 원래 접시로.</summary>
    private static void AddCut(Work w, Func<IngredientItem> ing, IngredientItem.CutMethod method, CutSpec? spec, bool trim, MovableBowl back)
    {
        IngredientItem target = null;
        w.Do(() =>
        {
            target = ing();
            if (target == null) return;
            if (Hand.Current != null) PutDown(-1.5f, -0.6f);
            Hand.PickUp(target);
            CuttingBoardController.Instance?.ForceActivate();
            Take(Knife);
        }, 0.1f);
        if (trim) w.Do(() => { if (target != null && target.CanTrim) target.PerformTrim(); }, 0.2f);
        w.Do(() =>
        {
            if (target == null) return;
            if (spec.HasValue) target.PerformCut(method, spec.Value);
            else target.PerformCut(method);
        }, 0.3f);
        w.Do(() =>
        {
            if (target == null || back == null) return;
            PutDown(-1.5f, -0.6f); // (칼 내려놓고)
            Take(target);
            back.ForceActivate();
        }, 0.1f);
    }

    /// <summary>이 팬의 소스를 매 틱 저음 (볶는 동안 계속 젓기 - 빨리 돌려도 젓기가 끊기지 않게).</summary>
    private static MovableBowl stirPan;

    /// <summary>웍에 든 고기 조각들을 들고 있는 체망에 하나씩 건짐.</summary>
    private static void ScoopWok()
    {
        Strainer s = BigStrainer;
        if (s == null || (object)Hand.Current != s) return;
        foreach (IngredientItem i in UnityEngine.Object.FindObjectsByType<IngredientItem>(FindObjectsSortMode.None))
            if ((object)i.CurrentContainer == Wok) s.TakeIn(i);
    }

    private static void Stirs(Work w, Func<MovableBowl> pan, int n, float gap = 0.45f)
    {
        w.Do(() => Take(WoodenSpoon), 0.1f);
        for (int k = 0; k < n; k++) w.Do(() => pan()?.ForceActivate(), gap);
    }

    private static void Season(Work w, MovableBowl pan, string name, float ml)
    {
        w.Do(() => pan?.AddSeasoning(name, ml), 0.15f);
    }


    /// <summary>요리 하나 끝: 팬을 개수대에서 씻고, 조리대에 내려 키친타올 한 장으로
    /// 물기를 닦은 뒤 쓴 타올은 일반쓰레기통에.</summary>
    private static void WashAndDryPan(Work w)
    {
        w.Do(() => { stirPan = null; Burner(false); Cam(-4.6f, 0.1f, 2.4f, 0.6f); Take(Pan); Click(Sink); }, 0.4f);
        w.Do(() => { PutDown(-3.4f, -0.5f); }, 0.1f);
        w.Do(() => Click(UnityEngine.Object.FindFirstObjectByType<KitchenTowelDispenser>()), 0.2f); // (한 장 뽑기)
        w.Do(() => Click(Pan), 0.4f);                                                              // (물기 닦기)
        w.Do(() => { if (Hand.Current != null) Click(UnityEngine.Object.FindFirstObjectByType<GeneralTrashCan>()); }, 0.2f);
        w.Do(() => { if (Hand.Current != null) PutDown(-3.5f, -2.2f); }, 0.1f);
    }

    private static readonly System.Reflection.FieldInfo MincePressGap =
        typeof(FoodPieceGroup).GetField("nextMincePress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    /// <summary>국자로 다진 고기를 꾹꾹 눌러 풀기: 가장 큰 덩이부터 몇 번 눌러 반씩 나누고,
    /// 팬 여기저기를 헤집어 조각을 펼친다 (손은 그 자리로 살짝 움직임).</summary>
    private static void PressMinceForFilm()
    {
        if (Pan == null) return;
        MovableBowl.Interior inside = Pan.GetInterior();
        foreach (FoodPieceGroup group in Pan.GetComponentsInChildren<FoodPieceGroup>())
        {
            IngredientItem ing = group.GetComponent<IngredientItem>();
            if (ing == null || !ing.IsMincedMeat) continue;
            Vector3 last = inside.floorCenter;
            for (int k = 0; k < 4; k++)
            {
                FoodPiece largest = null;
                foreach (FoodPiece p in group.Pieces)
                    if (p != null && (largest == null || p.Grams > largest.Grams)) largest = p;
                if (largest == null) break;
                last = largest.transform.position;
                MincePressGap?.SetValue(group, 0f); // (빨리 돌리는 영상이라 누르는 간격 제한은 건너뜀)
                group.PressMinceAt(last, inside.radius * 0.3f);
            }
            HandController hc = HandController.Instance;
            if (hc != null && Hand.Current is ToolItem tool)
                hc.SetGestureOffset(hc.GestureOffset + ToolItem.TowardSpoonHead(tool, last, 0.2f));
        }
        // 덩이가 한데 몰리지 않게 팬 여기저기를 헤집기
        Vector3 right = Vector3.ProjectOnPlane(Vector3.right, inside.up).normalized;
        float a = Time.unscaledTime * 3.1f;
        Pan.PokeAt(inside.floorCenter + Quaternion.AngleAxis(a * Mathf.Rad2Deg, inside.up) * right * inside.radius * 0.35f);
        PanSauce.Of(Pan, false)?.Stir();
    }

    private static Strainer SmallStrainer => GameObject.Find("Strainer_Small")?.GetComponent<Strainer>();
    private static MovableBowl Pot => Bowl("PotSmall");

    private static void BuildFullCooking(List<Scene> list)
    {
        void AddW(string title, string desc, float d, float fast, Func<Work> make, Action<float> extra = null)
        {
            list.Add(new Scene
            {
                title = title, desc = desc, d3 = d, d1 = d, teaserSpeed = fast,
                begin = () => { DemoCaption.Spotlight(null); work = make(); },
                tick = t =>
                {
                    work?.Tick();
                    if (stirPan != null) PanSauce.Of(stirPan, false)?.Stir();
                    extra?.Invoke(t);
                },
                until = () => work == null || work.Done,
            });
        }

        // ④ 오래 걸리는 것부터: 데칠 물 · 앙금 · 키친타올
        AddW("④ 오래 걸리는 것부터 — 데칠 물 · 앙금 · 키친타올", "왜? 물 끓이기와 앙금 가라앉히기는 시간이 걸려 먼저 시작해요. 감자전분 3T + 물 3T를 저어 두고, 고기는 키친타올에 감싸 핏물을 뺍니다", 9f, 5f, () =>
        {
            HandWashSequence seq = UnityEngine.Object.FindFirstObjectByType<HandWashSequence>();
            if (seq != null && seq.IsPlaying) seq.CancelWash();
            WaterTap tap = UnityEngine.Object.FindFirstObjectByType<WaterTap>();
            if (tap != null && tap.IsOn) tap.ForceActivate();
            HandHygiene.Washed();
            TS(8f);
            Cam(2.2f, 0.2f, 3.4f, 1.0f);
            var w = new Work();
            ToolItem spoon = ToolItem.MeasuringSpoon;
            // 데칠 물: 냄비에 물을 받아 아래 화구에 (끓는 데 시간이 걸림)
            w.Do(() => { OnBurner(Pot, StoveBurner.FlameLevel.Strong); Pot.SetFilledWithWater(true); }, 0.2f);
            // 앙금녹말: 전분 3T + 물 3T → 젓기 (가라앉히는 동안 다른 일)
            w.Do(() => { Take(StarchBowl); PutDown(0.4f, 0.9f); Take(spoon); }, 0.1f);
            for (int k = 0; k < 3; k++) w.Do(() => { spoon.LoadSeasoning("감자전분"); Click(StarchBowl); }, 0.15f);
            w.Do(() => { MovableBowl cup = Bowl("measuringCup"); cup?.SetWaterLevel(0.225f); Take(cup); Click(StarchBowl); }, 0.2f);
            w.Do(() => { PutDown(-0.6f, 0.9f); Take(spoon); Click(StarchBowl); }, 0.2f); // (젓기)
            // 고기 두 가지 · 두부: 키친타올로 핏물 · 물기 제거
            w.Do(() =>
            {
                PutDown(-0.9f, 0.9f);
                AnyIng("돼지등심")?.BlotWithTowel();
                AnyIng("돼지고기")?.BlotWithTowel();
                AnyIng("두부")?.BlotWithTowel();
            }, 0.2f);
            return w;
        });

        // ⑤ 채소 손질 (빨리)
        AddW("⑤ 채소 씻고 다듬어 요구사항대로 썰기", "탕수육 채소는 편으로(대파 심 제거 · 양파 · 오이 · 당근), 마파두부는 다지기(마늘 · 생강 · 대파 · 홍고추 씨 제거)", 7f, 6f, () =>
        {
            Cam(0.0f, -0.55f, 1.9f, 1.0f);
            TS(24f);
            var w = new Work();
            AddCut(w, () => IngIn("대파", TangPlate), IngredientItem.CutMethod.Slice, null, false, TangPlate);
            AddCut(w, () => IngIn("양파", TangPlate), IngredientItem.CutMethod.Slice, null, true, TangPlate);
            AddCut(w, () => IngIn("오이", TangPlate), IngredientItem.CutMethod.DiagonalCut, null, false, TangPlate);
            AddCut(w, () => IngIn("당근", TangPlate), IngredientItem.CutMethod.Slice, null, false, TangPlate);
            AddCut(w, () => IngIn("마늘", MapoPlate), IngredientItem.CutMethod.Dice, null, true, MapoPlate);
            AddCut(w, () => IngIn("생강", MapoPlate), IngredientItem.CutMethod.Dice, null, false, MapoPlate);
            AddCut(w, () => IngIn("대파", MapoPlate), IngredientItem.CutMethod.Dice, null, false, MapoPlate);
            AddCut(w, () => IngIn("고추", MapoPlate), IngredientItem.CutMethod.Dice, null, true, MapoPlate);
            return w;
        });

        // ⑥ 두부 썰기 · 데치기
        AddW("⑥ 두부 1.5cm 주사위 → 완두콩 · 두부 데치기", "요구사항 \"두부는 1.5cm 주사위, 으깨지지 않게\" — 끝을 정리하고 썬 뒤, 끓는 물에 완두콩(15~20초)과 두부를 데쳐 찬물에 헹궈요", 12f, 3f, () =>
        {
            TS(8f);
            Cam(0.0f, -0.55f, 1.7f, 1.0f);
            var w = new Work();
            w.Do(() => Say("tts7", 0f, teaser ? 2.5f : 6.5f), 0.05f); // (마파두부 요구사항)
            AddCut(w, () => IngIn("두부", MapoPlate), IngredientItem.CutMethod.Cube, IngredientItem.BuildSpec(IngredientItem.CutMethod.Cube, 1.5f), false, MapoPlate);
            w.Do(() => { Cam(5.45f, -0.1f, 1.7f, 0.8f); SetField(Pot, "boilProgress", 400f); TS(6f); }, 0.3f);
            // 완두콩: 체망에 담아 끓는 물에 (체망은 손에)
            w.Do(() => { Take(SmallStrainer); SmallStrainer?.TakeIn(IngIn("완두콩", TangPlate)); }, 0.2f);
            w.Do(() => Click(Pot), 0.3f);
            // 두부도 끓는 물에
            w.Do(() => { PutDown(2.2f, 0.9f); Take(IngIn("두부", MapoPlate)); Click(Pot); }, 0.3f);
            w.Wait(1.4f);
            // 건지기: 완두콩은 체망으로 → 찬물에 헹궈 접시로, 두부는 접시로
            w.Do(() =>
            {
                Strainer s = SmallStrainer;
                Take(s);
                IngredientItem peas = AnyIng("완두콩");
                if (s != null && peas != null && (object)peas.CurrentContainer == Pot) s.TakeIn(peas);
            }, 0.2f);
            w.Do(() => Click(Sink), 0.4f); // (찬물 헹굼)
            w.Do(() => Click(TangPlate), 0.3f);
            // (체망에서 쏟은 자리 그대로 남지 않게 접시 위로 옮겨 놓기)
            w.Do(() => { PutDown(-2.6f, 0.9f); Take(AnyIng("완두콩")); TangPlate?.ForceActivate(); }, 0.2f);
            w.Do(() =>
            {
                PutDown(-2.6f, 0.9f);
                IngredientItem tofu = AnyIng("두부");
                CookableItem ck = tofu != null ? tofu.GetComponent<CookableItem>() : null;
                if (ck != null) SetField(ck, "<CurrentStage>k__BackingField", CookableItem.Stage.Done); // (데쳐 익힘)
                Take(tofu); MapoPlate?.ForceActivate();
            }, 0.2f);
            w.Do(() => { Burner(false); OffBurner(Pot, 3.2f, -2.4f); }, 0.1f);
            return w;
        });

        // ⑦ 고추기름
        AddW("⑦ 고추기름 — 식용유 2T + 고춧가루 1T, 타지 않게 저어 면보에", "요구사항 \"고추기름을 만들어 사용\" — 불에 올린 순간부터 계속 저어 타지 않게 볶고, 면보를 받친 그릇에 걸러 기름만 씁니다", 10f, 3f, () =>
        {
            TS(8f);
            Cam(4.9f, -0.3f, 2.2f, 1.0f);
            var w = new Work();
            w.Do(() => { OnBurner(Pan, StoveBurner.FlameLevel.Medium); Pan.AddSeasoning("식용유", 30f); Pan.AddSeasoning("고춧가루", 15f); stirPan = Pan; }, 0.2f);
            w.Do(() => Take(WoodenSpoon), 0.1f);
            Stirs(w, () => Pan, 7, 0.6f);
            w.Do(() => stirPan = null, 0.05f);
            w.Do(() => { Take(ChiliOilBowl); PutDown(3.4f, -1.4f); }, 0.1f);
            w.Do(() => { Take(Cloth); Click(ChiliOilBowl); }, 0.2f);
            w.Do(() => { Burner(false); Take(Pan); Click(ChiliOilBowl); }, 0.3f);
            w.Until(() => Cloth == null || !Cloth.IsFiltering, 6f);
            WashAndDryPan(w);
            w.Do(() => { PutDown(2.4f, -2.6f); }, 0.1f);
            w.Do(() => { Click(Cloth); PutDown(-3.6f, -2.2f); }, 0.1f);
            return w;
        });

        // ⑧ 돼지고기 손질 - 핵심 (효과)
        AddW("⑧ 돼지고기 4 × 1 × 1cm — 지방 제거 · 끝 다듬기 → 포뜨기 → 1cm 썰기", "요구사항 \"길이 4cm, 두께 1cm\" — 위아래를 잘라 4cm로 맞추고, 칼을 눕혀 1cm 두께로 포를 뜬 뒤 1cm 너비로 썰어요 (실제 속도 약 41초)", 13f, 2f, () =>
        {
            TS(4f);
            Cam(0.0f, -0.45f, 1.25f, 1.0f); // (도마 가까이)
            DemoCaption.Spotlight("★ 핵심 장면", "① 끝 다듬기 → ② 포뜨기 1cm → ③ 1cm 썰기");
            var w = new Work();
            w.Do(() => Say("tts6", 0f, teaser ? 2.5f : 6.5f), 0.05f); // (탕수육 요구사항)
            AddCut(w, () => IngIn("돼지등심", TangPlate), IngredientItem.CutMethod.Julienne, new CutSpec(1f, 4f, 1f), false, null);
            w.Do(() =>
            {
                DemoCaption.Spotlight("★ 핵심 장면", "4 × 1 × 1cm 막대 완성");
                MovableBowl b = PorkBowl;
                PutDown(-1.5f, -0.6f);
                Take(b); PutDown(1.6f, 0.9f);
                Take(AnyIng("돼지등심"));
                b?.ForceActivate();
            }, 1.2f);
            w.Do(() => DemoCaption.Spotlight(null), 0.05f);
            return w;
        });

        // ⑨ 밑간 · 흰자 · 앙금 · 버무리기
        AddW("⑨ 밑간(간장 · 청주) → 흰자 1T + 앙금 2T → 버무리기", "요구사항 \"앙금녹말을 만들어 사용\" — 가라앉은 앙금의 윗물을 버리고, 밑간한 고기에 흰자와 앙금을 넣어 고루 버무려요", 12f, 4f, () =>
        {
            TS(8f);
            Cam(1.2f, 0.5f, 2.2f, 1.0f);
            var w = new Work();
            ToolItem spoon = ToolItem.MeasuringSpoon;
            // 앙금: 다 가라앉았으면 윗물 버리기
            w.Do(() =>
            {
                PortionedCondiment pc = StarchBowl != null ? StarchBowl.GetComponent<PortionedCondiment>() : null;
                if (pc != null && pc.CurrentStage == PortionedCondiment.Stage.Slurry)
                {
                    var f = typeof(PortionedCondiment).GetField("settleSeconds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    SetField(pc, "settleTimer", f != null ? (float)f.GetValue(pc) - 0.05f : 299.9f);
                }
            }, 0.3f);
            w.Do(() => { Take(StarchBowl); Click(Sink); }, 0.3f); // (윗물 버리기)
            w.Do(() => { PutDown(0.4f, 0.9f); }, 0.1f);
            // 밑간: 간장 1t · 청주 1t
            w.Do(() => { Take(spoon); if (!spoon.UsingTeaspoon) spoon.TryFlip(); }, 0.5f);
            w.Do(() => { spoon.LoadSeasoning("간장"); Click(PorkBowl); }, 0.2f);
            w.Do(() => { spoon.LoadSeasoning("청주"); Click(PorkBowl); }, 0.2f);
            // 흰자 1T
            w.Do(() => { PutDown(-0.6f, 0.9f); Take(EggBowl); PutDown(2.6f, 0.9f); }, 0.1f);
            w.Do(() =>
            {
                IngredientItem egg = AnyIng("달걀");
                Take(egg);
                egg?.GetComponent<EggCracker>()?.CrackIntoAction(EggBowl).OnClicked();
            }, 0.4f);
            w.Do(() => { Take(spoon); if (spoon.UsingTeaspoon) spoon.TryFlip(); }, 0.5f);
            w.Do(() =>
            {
                EggContents ec = EggContents.Of(EggBowl, false);
                if (ec == null) return;
                if (spoon.TryLoadUpTo("흰자", ec.AvailableMl(EggContents.Part.White))) ec.TakeMl(EggContents.Part.White, spoon.LoadedMl);
                Click(PorkBowl);
            }, 0.3f);
            // 앙금 2T
            for (int k = 0; k < 2; k++) w.Do(() => { Click(StarchBowl); Click(PorkBowl); }, 0.3f);
            // 버무리기
            w.Do(() => { PutDown(-0.6f, 0.9f); AnyIng("돼지등심")?.KneadMarinade(6); Take(WoodenSpoon); }, 0.1f);
            for (int k = 0; k < 6; k++) w.Do(() => Click(IngIn("돼지등심", PorkBowl)), 0.2f);
            return w;
        });

        // ⑩ 기름 온도 확인 → 두 번 튀기기
        AddW("⑩ 앙금 조각으로 기름 온도(170~180℃) 확인 → 한 조각씩 → 두 번 튀기기", "들어갔다 2~3초 안에 떠오르면 OK. 고기는 한 조각씩 넣고 튀김젓가락으로 붙지 않게 떼어 주며, 건져 잠깐 식혔다 한 번 더 튀겨요", 16f, 3f, () =>
        {
            TS(1f);
            Cam(5.45f, 0.05f, 1.5f, 1.0f);
            var w = new Work();
            w.Do(() => { PutDown(-0.6f, 0.9f); OnBurner(Wok, StoveBurner.FlameLevel.Medium); SetField(Wok, "oilHeat", 185f); PutDown(2.2f, 0.9f); }, 0.2f);
            w.Do(() => { BowlMixInteraction.For(PorkBowl).TryPickup(BowlPickupKind.BatterPiece); Click(Wok); }, 0.2f);
            w.Wait(3.2f);
            for (int k = 0; k < 14; k++)
                w.Do(() =>
                {
                    if (PorkBowl == null || !BowlMixInteraction.For(PorkBowl).TryPickup(BowlPickupKind.MeatPiece)) return;
                    Click(Wok);
                    // (넣자마자 붙지 않게 바로바로 떼어 놓기)
                    foreach (IngredientItem i in UnityEngine.Object.FindObjectsByType<IngredientItem>(FindObjectsSortMode.None))
                        if ((object)i.CurrentContainer == Wok)
                            foreach (FoodPiece p in i.GetComponentsInChildren<FoodPiece>()) Wok.PokeAt(p.transform.position);
                }, 0.15f);
            w.Do(() => { PutDown(2.2f, 0.9f); Take(Chopsticks); TS(3f); }, 0.1f);
            for (int k = 0; k < 4; k++)
                w.Do(() =>
                {
                    foreach (IngredientItem i in UnityEngine.Object.FindObjectsByType<IngredientItem>(FindObjectsSortMode.None))
                        if ((object)i.CurrentContainer == Wok)
                            foreach (FoodPiece p in i.GetComponentsInChildren<FoodPiece>()) Wok.PokeAt(p.transform.position);
                }, 0.5f);
            w.Wait(2.0f);
            // 2차 튀김: 건져 잠깐 식혔다 다시
            w.Do(() => { PutDown(2.2f, 0.9f); Take(BigStrainer); ScoopWok(); }, 0.2f);
            w.Wait(1.0f);
            w.Do(() => Click(Wok), 0.3f);
            w.Wait(2.0f);
            w.Do(() => ScoopWok(), 0.2f);
            w.Do(() => { Take(FriedPlate); PutDown(3.0f, 0.9f); Take(BigStrainer); Click(FriedPlate); }, 0.3f);
            w.Do(() => { Burner(false); OffBurner(Wok, 3.0f, -2.9f); }, 0.1f);
            return w;
        });

        // ⑪ 탕수육 소스 · 완성
        AddW("⑪ 탕수육 — 파 · 청주 · 채소 볶고 소스 → 전분물로 농도 → 버무려 완성", "팬에 기름, 중불: 파 + 청주 2t + 채소(오이 · 완두 제외) 1분 → 간장 2t · 식초 3t · 설탕 3t · 물 1C → 끓으면 전분물을 조금씩 → 튀김 · 오이 · 완두콩 버무림", 13f, 4f, () =>
        {
            TS(4f);
            Cam(5.45f, 0.05f, 1.6f, 1.0f);
            var w = new Work();
            w.Do(() => { OnBurner(Pan, StoveBurner.FlameLevel.Medium); Pan.AddSeasoning("식용유", 15f); stirPan = Pan; }, 0.2f);
            w.Do(() => { Take(IngIn("대파", TangPlate)); Click(Pan); Pan.AddSeasoning("청주", 10f); }, 0.2f);
            w.Do(() => { Take(IngIn("양파", TangPlate)); Click(Pan); }, 0.2f);
            w.Do(() => { Take(IngIn("당근", TangPlate)); Click(Pan); }, 0.2f);
            w.Do(() => { Take(IngIn("목이버섯", TangPlate)); Click(Pan); }, 0.2f);
            Stirs(w, () => Pan, 2);
            Season(w, Pan, "간장", 10f);
            Season(w, Pan, "식초", 15f);
            Season(w, Pan, "설탕", 15f);
            Season(w, Pan, "물", 200f);
            w.Do(() => { BurnerR?.SetLevel(StoveBurner.FlameLevel.Strong); TS(10f); }, 0.05f);
            w.Wait(2.0f); // (끓을 때까지)
            for (int k = 0; k < 2; k++)
            {
                w.Do(() => { Pan.AddSeasoning("전분(전분물)", 8f); Pan.AddSeasoning("물", 20f); }, 0.1f);
                Stirs(w, () => Pan, 1, 0.35f);
            }
            w.Wait(1.6f); // (저으며 끓여 농도 잡기)
            w.Do(() => { BurnerR?.SetLevel(StoveBurner.FlameLevel.Medium); TS(4f); }, 0.05f);
            w.Do(() =>
            {
                PutDown(2.2f, 0.9f);
                foreach (IngredientItem i in UnityEngine.Object.FindObjectsByType<IngredientItem>(FindObjectsSortMode.None))
                    if (i.IngredientName == "돼지등심" && (object)i.CurrentContainer == FriedPlate) { Hand.PickUp(i); Click(Pan); }
            }, 0.2f);
            w.Do(() => { Take(IngIn("오이", TangPlate)); Click(Pan); }, 0.2f);
            w.Do(() => { Take(IngIn("완두콩", TangPlate)); Click(Pan); }, 0.2f);
            Stirs(w, () => Pan, 3);
            w.Do(() =>
            {
                stirPan = null;
                Burner(false);
                Take(Pan);
                Pan.GetComponent<FryingPanPlating>()?.PourInto(GameObject.Find("PlatingPlate1")?.GetComponent<PlatingDestination>());
            }, 0.4f);
            return w;
        });

        // ⑫ 팬 씻기 → 마파두부
        AddW("⑫ 팬을 깨끗이 씻고 닦은 뒤 → 마파두부", "고추기름 1T, 센불에 파 · 마늘 · 생강 10초 → 다진 고기 30초, 국자로 눌러 풀어 줌(중요) → 약불 두반장 1T · 간장 2t · 설탕 1t · 후추 → 끓으면 전분물 → 두부 · 고추 → 참기름 1t", 17f, 4f, () =>
        {
            TS(3f);
            var w = new Work();
            WashAndDryPan(w);
            w.Do(() => Cam(5.45f, 0.05f, 1.6f, 0.8f), 0.1f);
            w.Do(() => { OnBurner(Pan, StoveBurner.FlameLevel.Medium); stirPan = Pan; }, 0.2f); // (중불 - 센 불이면 다진 고기가 탐)
            w.Do(() => { Take(ChiliOilBowl); Click(Pan); }, 0.3f);
            w.Do(() => { PutDown(3.4f, -1.4f); Take(IngIn("대파", MapoPlate)); Click(Pan); }, 0.2f);
            w.Do(() => { Take(IngIn("마늘", MapoPlate)); Click(Pan); }, 0.2f);
            w.Do(() => { Take(IngIn("생강", MapoPlate)); Click(Pan); }, 0.2f);
            Stirs(w, () => Pan, 2);
            w.Do(() => { Take(IngIn("돼지고기", MapoPlate)); Click(Pan); }, 0.2f);
            w.Do(() => Take(SteelSpoon), 0.1f);
            // 국자로 계속 눌러 잘게 풀기
            for (int n = 0; n < 12; n++) w.Do(PressMinceForFilm, 0.15f);
            Stirs(w, () => Pan, 3);
            w.Do(() => BurnerR?.SetLevel(StoveBurner.FlameLevel.Weak), 0.05f);
            Season(w, Pan, "두반장", 15f);
            Season(w, Pan, "간장", 10f);
            Season(w, Pan, "설탕", 5f);
            Season(w, Pan, "흑후추", 1f);
            Season(w, Pan, "물", 100f);
            w.Do(() => { BurnerR?.SetLevel(StoveBurner.FlameLevel.Strong); TS(10f); }, 0.05f);
            w.Wait(1.5f); // (끓으면)
            for (int k = 0; k < 2; k++)
            {
                w.Do(() => { BurnerR?.SetLevel(StoveBurner.FlameLevel.Weak); Pan.AddSeasoning("전분(전분물)", 6f); Pan.AddSeasoning("물", 20f); }, 0.1f);
                Stirs(w, () => Pan, 1);
            }
            w.Wait(1.4f); // (저으며 농도 잡기)
            w.Do(() => TS(4f), 0.05f);
            w.Do(() => { PutDown(2.2f, 0.9f); Take(IngIn("두부", MapoPlate)); Click(Pan); }, 0.2f);
            w.Do(() => { Take(IngIn("고추", MapoPlate)); Click(Pan); }, 0.2f);
            w.Do(() => Take(SteelSpoon), 0.1f);
            w.Wait(1.2f); // (두부는 세게 젓지 않고 소스만 살살)
            w.Do(() => { stirPan = null; Burner(false); Pan.AddSeasoning("참기름", 5f); }, 0.2f);
            w.Do(() =>
            {
                Take(Pan);
                Pan.GetComponent<FryingPanPlating>()?.PourInto(GameObject.Find("PlatingPlate2")?.GetComponent<PlatingDestination>());
            }, 0.4f);
            return w;
        });

        // ⑬ 제출 (빨리)
        AddW("⑬ 두 접시를 제출대에 → 제출", "제출대에 올린 접시만 제출로 인정 — 제출 뒤엔 요리를 고칠 수 없어요", 5f, 4f, () =>
        {
            TS(1f);
            Cam(7.0f, -0.2f, 3.0f, 0.8f);
            var w = new Work();
            w.Do(() => { PutDown(3.6f, -1.5f); Take(Bowl("PlatingPlate1")); SubmitShelf.Instance?.ForceActivate(); }, 0.6f);
            w.Do(() => { Take(Bowl("PlatingPlate2")); SubmitShelf.Instance?.ForceActivate(); }, 0.6f);
            w.Do(() => Click(UnityEngine.Object.FindFirstObjectByType<SubmitButton>()), 1.0f);
            return w;
        });

        // ⑭ 정리 (빨리)
        AddW("⑭ 마무리 정리 — 설거지 · 선반 진열 · 손 씻기", "정리 · 청소도 위생 점수예요", 6f, 5f, () =>
        {
            TS(4f);
            Cam(-4.4f, -1.9f, 3.0f, 0.8f);
            var w = new Work();
            w.Do(() => { Take(PorkBowl); Click(Sink); }, 0.3f);
            w.Do(() => GameObject.Find("PlateShelf1")?.GetComponent<DishShelfController>()?.ForceActivate(), 0.3f);
            w.Do(() => { Take(EggBowl); Click(Sink); }, 0.3f);
            w.Do(() => GameObject.Find("PlateShelf2")?.GetComponent<DishShelfController>()?.ForceActivate(), 0.3f);
            w.Do(() =>
            {
                if (Hand.Current != null) PutDown(-2f, -0.5f);
                WaterTap tap = UnityEngine.Object.FindFirstObjectByType<WaterTap>();
                if (tap != null && !tap.IsOn) tap.ForceActivate();
                Click(Sink);
            }, 0.3f);
            w.Wait(2.5f);
            w.Do(() =>
            {
                HandWashSequence seq = UnityEngine.Object.FindFirstObjectByType<HandWashSequence>();
                if (seq != null && seq.IsPlaying) seq.CancelWash();
                HandHygiene.Washed();
                WaterTap tap = UnityEngine.Object.FindFirstObjectByType<WaterTap>();
                if (tap != null && tap.IsOn) tap.ForceActivate();
            }, 0.1f);
            return w;
        });

        AddW("⑮ 정리 완료 → 채점 (합격 · 불합격 · 실격)", "문제지 배점(위생 5 · 조리기술 30 · 작품 15)으로 바로 채점하고 항목별 이유를 보여 줘요", 13f, 1.6f, () =>
        {
            TS(1f);
            Cam(0f, 0.6f, 6.8f, 0.6f);
            var w = new Work();
            w.Do(() => Click(UnityEngine.Object.FindFirstObjectByType<SubmitButton>()), 0.5f);
            return w;
        }, t =>
        {
            ExamResultView v = UnityEngine.Object.FindFirstObjectByType<ExamResultView>();
            if (v != null && t * spd > 4f) SetField(v, "scroll", new Vector2(0f, (t * spd - 4f) * 60f));
        });

        list.Add(new Scene
        {
            d3 = 4f, d1 = 4f, teaserSpeed = 1.4f,
            begin = () => { DemoCaption.Spotlight(null); DemoCaption.Card("기능사 실기 시뮬레이션", "실제 시험처럼 연습하고, 기준대로 피드백 받기"); },
        });
    }
}
