using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class IngredientItem : Carryable
{
    public enum State { Raw, Washed, Cut }
    public enum SoakState { NotNeeded, Dry, Soaking, Soaked }
    /// <summary>Dice = 다지기 (fine mince), Cube = 사각썰기 (even cubes) - Cube is
    /// appended last so saved values keep their meaning.</summary>
    public enum CutMethod { None, Julienne, Slice, Dice, StairCut, FlowerCut, DiagonalCut, Tear, Other, Cube }

    /// <summary>Korean name shown in the cutting UI.</summary>
    public static string CutMethodName(CutMethod method)
    {
        switch (method)
        {
            case CutMethod.Slice: return "편썰기";
            case CutMethod.Julienne: return "채썰기";
            case CutMethod.Cube: return "사각썰기";
            case CutMethod.Dice: return "다지기";
            case CutMethod.StairCut: return "계단썰기";
            case CutMethod.FlowerCut: return "꽃모양썰기";
            case CutMethod.DiagonalCut: return "어슷썰기";
            case CutMethod.Tear: return "찢기";
            default: return "이외";
        }
    }

    [SerializeField] private string ingredientName = "재료";
    [SerializeField] private IngredientCategory category = IngredientCategory.None;
    [SerializeField] private bool isSoakable = false;
    [SerializeField] private float soakDuration = 5f;
    [Tooltip("다 불었을 때 크기 배수 (마른 목이버섯이 물을 먹어 부푸는 정도).")]
    [SerializeField] private float soakSwell = 1.4f;
    private float soakShownSwell = 1f; // 지금까지 부풀린 배수

    /// <summary>Meat needs to be blotted with a paper towel after cutting to draw
    /// off excess blood, like in the real exam - purely visual/practice, not
    /// hygiene-scored (HygieneLog is about cross-contamination, not this).</summary>
    [SerializeField] private bool needsBloodDrain = false;

    public enum DrainNeed { None, Blood, Moisture }

    /// <summary>Has to sit on a paper towel spread on a board/plate (ITowelLined)
    /// for drainSeconds before it's cooked - meat's blood (고기·다진고기 핏물),
    /// tofu's water (두부 물기). Cooking it without that is recorded in
    /// ExamDeductions (see CookableItem).</summary>
    [SerializeField] private DrainNeed drainOnTowel = DrainNeed.None;
    [SerializeField] private float drainSeconds = 3f;
    private float drainTimer;

    public DrainNeed DrainOnTowel => drainOnTowel;
    public bool IsTowelDrained { get; private set; }
    public bool NeedsTowelDrain => drainOnTowel != DrainNeed.None && !IsTowelDrained;
    private bool OnTowel => CurrentContainer is ITowelLined lined && lined.HasTowelLiner;

    /// <summary>Optional pre-modeled cut result (e.g. the onion's real half-mesh
    /// with an actual cross-section texture) - when set, cutting swaps this
    /// ingredient's own mesh/material to these instead of spawning the generic
    /// scaled-quad "Piece_i" placeholders every other ingredient still uses.
    /// Left null, behavior is unchanged.</summary>
    [SerializeField] private Mesh cutResultMesh;
    [SerializeField] private Material cutResultMaterial;

    /// <summary>A square block (두부): 사각썰기 goes the way it's done by hand -
    /// a thin slice off each side (단면), slabs across, the slabs laid down cut
    /// face down, cut into sticks, turned 90 degrees and cut again into cubes
    /// (see BlockDiceRoutine). Off: the generic slice-and-spread pieces.</summary>
    [SerializeField] private bool blockDice;

    /// <summary>A half onion, cut face down: sliced across it's half-moon slices
    /// (반달 모양) - each its layered rings - that topple over into a fan beside
    /// it (see CrescentRoutine), instead of swapping in one cut-result model.</summary>
    [SerializeField] private bool crescentSlices;

    /// <summary>How 편썰기/어슷썰기 goes for this one, prepared the way it's done
    /// by hand first (see StyledSliceRoutine): 오이 - the outer part cut off
    /// lengthwise, laid flat, sliced on the diagonal into skin-edged half ovals;
    /// 당근 - the rounded outsides trimmed off, laid down, sliced into flat
    /// rectangles; 대파 - slit lengthwise, the core taken out, pressed flat, cut
    /// into flat pieces.</summary>
    public enum SliceStyle { None, CucumberDiagonal, CarrotPlank, LeekFlat, MeatStrips }

    /// <summary>Comes already minced (다진 돼지고기): 다지기 only goes over it again.</summary>
    [SerializeField] private bool alreadyMinced;
    public bool IsMincedMeat => Category == IngredientCategory.Meat && alreadyMinced;

    /// <summary>고추 다듬기 the way it's done: the green cap cut off, stood up
    /// lengthwise and cut in half, the seeds scraped out with the measuring
    /// spoon (see ChiliTrimRoutine).</summary>
    [SerializeField] private bool chiliTrim;
    [SerializeField] private SliceStyle sliceStyle;
    [Tooltip("고기 채썰기: the strips spread out flat in rows after cutting (off: left standing where they were cut).")]
    [SerializeField] private bool spreadMeatStrips = false;

    /// <summary>The portion's weight in grams (0 = not known) - split over the
    /// pieces and scraps it's cut into (두부 150 g).</summary>
    [SerializeField] private float amountGrams;
    public float AmountGrams => amountGrams;

    /// <summary>Was washed at some point (kept once it's cut, for its pieces).</summary>
    public bool WasWashed { get; private set; }

    public IngredientCategory Category => category;
    public string IngredientName => ingredientName;

    private Renderer rend;
    private Mesh sharedMesh;
    private float soakTimer;

    public State CurrentState { get; private set; } = State.Raw;
    public CutMethod AppliedCut { get; private set; } = CutMethod.None;
    public CutSpec AppliedSpec { get; private set; }
    public SoakState CurrentSoakState { get; private set; } = SoakState.NotNeeded;
    public bool BloodDrained { get; private set; }

    /// <summary>True only once this piece actually needs (and hasn't yet had) a
    /// paper towel blot - before cutting there's nothing to blot yet.</summary>
    private bool NeedsBloodDrain => needsBloodDrain && CurrentState == State.Cut && !BloodDrained;

    private readonly Dictionary<string, float> appliedSeasonings = new Dictionary<string, float>(); // (mL)
    public IReadOnlyDictionary<string, float> AppliedSeasonings => appliedSeasonings;

    /// <summary>The portion as handed out (g), before any trimming - what a
    /// recipe's base meat amount is compared with (CookingRecipe.MeatBasis).</summary>
    public float GivenGrams { get; private set; }

    /// <summary>Where it is: the bowl/pan it sits in (null if loose or in hand).</summary>
    public MovableBowl Container => InBowl;

    /// <summary>A measuring spoonful tipped onto it (or into the bowl it's in):
    /// a 튀김옷 ingredient (앙금녹말, 흰자 ...) goes into its BatterCoating,
    /// anything else is its marinade (밑간).</summary>
    public void AddFromSpoon(string what, float ml)
    {
        if (!(ml > 0f)) return;
        if (BatterCoating.IsBatterIngredient(what) && RecipeBook.BatterFor(ingredientName) != null)
            BatterCoating.Of(this, true).Add(what, ml);
        else AddSeasoning(what, ml);
    }

    // ---- 한 점 (one piece taken on its own) ----

    /// <summary>One cut piece taken off on its own - a new ingredient of just
    /// that piece (to drop into the oil one at a time, say): its weight, its
    /// cooking and browning, and its share of the marinade, the batter and the
    /// sauce on the pieces go with it - moved, not copied (the totals stay the
    /// same). Null if it's the last piece (then the whole is taken as it is).</summary>
    public IngredientItem SplitOffPiece(FoodPiece piece)
    {
        FoodPieceGroup group = GetComponent<FoodPieceGroup>();
        if (group == null || piece == null || !group.Pieces.Contains(piece) || group.Count < 2) return null;
        Transform holder = piece.transform.parent;
        int index = piece.transform.GetSiblingIndex();
        float share = amountGrams > 0f ? Mathf.Clamp01(piece.Grams / amountGrams) : 1f / group.Count;

        GameObject go = Instantiate(gameObject, transform.position, transform.rotation, transform.parent);
        go.name = name.TrimEnd() + " (한 점)";
        IngredientItem clone = go.GetComponent<IngredientItem>();
        // only that piece in the copy
        Transform cHolder = holder == transform ? go.transform : go.transform.Find(holder.name);
        FoodPiece keep = cHolder != null && index < cHolder.childCount ? cHolder.GetChild(index).GetComponent<FoodPiece>() : null;
        foreach (FoodPiece fp in go.GetComponentsInChildren<FoodPiece>(true))
            if (fp != keep) DestroyImmediate(fp.gameObject);
        if (keep == null) { Destroy(go); return null; }
        foreach (Transform t in new List<Transform>(keep.GetComponentsInChildren<Transform>(true)))
            if (t != keep.transform && t.name.StartsWith("Batter")) DestroyImmediate(t.gameObject); // (its coat is drawn again)
        keep.CopyStateFrom(piece);
        // held by it: the piece at the middle of what's held, and only it clicked on
        keep.transform.position = go.transform.position;
        if (go.GetComponent<BoxCollider>() is BoxCollider box && keep.GetComponent<Renderer>() is Renderer kr)
        {
            Bounds b = kr.bounds;
            Vector3 s = go.transform.InverseTransformVector(b.size);
            box.center = go.transform.InverseTransformPoint(b.center);
            box.size = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        }
        FoodPieceGroup cGroup = go.GetComponent<FoodPieceGroup>();
        cGroup.Add(keep);

        // the ingredient's own state
        clone.CurrentState = CurrentState; clone.AppliedCut = AppliedCut; clone.AppliedSpec = AppliedSpec;
        clone.CurrentSoakState = CurrentSoakState; clone.BloodDrained = BloodDrained; clone.IsTowelDrained = IsTowelDrained;
        clone.WasWashed = WasWashed; clone.IsTrimmed = IsTrimmed;
        clone.MarinadeMix = MarinadeMix;
        clone.amountGrams = piece.Grams;
        amountGrams = Mathf.Max(0f, amountGrams - piece.Grams);
        clone.GivenGrams = GivenGrams * share;            // (the portion's share - all of them still add up to what was given)
        GivenGrams -= clone.GivenGrams;
        foreach (string k in new List<string>(appliedSeasonings.Keys))
        {
            float take = appliedSeasonings[k] * share;
            appliedSeasonings[k] -= take;
            clone.appliedSeasonings[k] = take;
        }
        if (GetComponent<CookableItem>() is CookableItem ck && go.GetComponent<CookableItem>() is CookableItem cck) cck.CopyFrom(ck);
        group.GiveCoatShareTo(cGroup, share);
        if (go.GetComponent<BatterCoating>() is BatterCoating copied) DestroyImmediate(copied); // (its data isn't copied - its share is moved below)
        if (BatterCoating.Of(this, false) is BatterCoating batter) batter.GiveShareTo(BatterCoating.Of(clone, true), share);

        group.Pieces.Remove(piece);
        Destroy(piece.gameObject);
        Debug.Log($"[{ingredientName}] 한 점 떼어냄 ({clone.amountGrams:0.#}g) - 남은 {group.Count}조각 {amountGrams:0.#}g");
        return clone;
    }

    // ---- 썬 채소를 조각 단위로 (팬에서 익고·타고·저으면 움직이게) ----

    /// <summary>썰려서 겉모양 조각(Piece_*)만 있고 조각 시뮬레이션(FoodPieceGroup)이 없는
    /// 재료(대파·오이·당근 등의 편썰기)를 조리 그릇에 넣을 때: 조각마다 FoodPiece를 붙여
    /// 고기 조각처럼 각자 익고, 노릇해지고, 타고, 저으면 움직이게 한다. 익는 설정
    /// (CookableItem)이 없던 재료면 채소용으로 붙인다. 이미 있으면 아무것도 안 함.</summary>
    public void EnsurePieceGroup()
    {
        if (GetComponent<FoodPieceGroup>() != null) return;
        // 썰지 않고 통째로 넣는 재료(완두콩 · 안 찢은 목이버섯)는 통째로 한 조각
        if (CurrentState != State.Cut && !MakeWholePiece()) return;
        var pieces = new List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            if (r.name.StartsWith("Piece_") && r.GetComponent<MeshFilter>() != null && r.GetComponent<FoodPiece>() == null) pieces.Add(r);
        if (pieces.Count == 0) return;

        if (GetComponent<CookableItem>() == null)
        {
            CookableItem ck = gameObject.AddComponent<CookableItem>();
            Color raw = rend != null && rend.sharedMaterial != null ? rend.sharedMaterial.color : Color.white;
            ck.SetupAsVegetable(raw);
        }
        FoodPieceGroup group = gameObject.AddComponent<FoodPieceGroup>();
        float grams = amountGrams > 0f ? amountGrams / pieces.Count : 0f;
        foreach (Renderer r in pieces)
        {
            if (r.GetComponent<Collider>() == null)
            {
                BoxCollider box = r.gameObject.AddComponent<BoxCollider>();
                box.center = r.GetComponent<MeshFilter>().sharedMesh.bounds.center;
                box.size = r.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            }
            FoodPiece fp = r.gameObject.AddComponent<FoodPiece>();
            // 조각 제 축에서의 크기. r.bounds 는 월드 축 기준 상자라, 비스듬히 놓인
            // 조각은 어느 축이 얇은지가 뒤바뀐다. FoodPiece.HalfSize 는 조각 제 축
            // (simRot * right/up/forward) 기준으로 쓰이므로, 어슷썰기 조각(두께는
            // 로컬 x, 접시 위에서는 65도 기울어 놓임)이 팬에서 모로 서 있었다.
            // 양파만 예외로 쓰던 식이 실은 모든 조각에 맞는 식이다.
            Vector3 scale = r.transform.lossyScale;                                        // 부모까지 더한 실제 배율
            Vector3 pieceSize = Vector3.Scale(r.GetComponent<MeshFilter>().sharedMesh.bounds.size, // 메시 제 축 크기
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));  // 음수 배율도 크기로
            fp.Init(this, grams, 0f, pieceSize);
            fp.UseOwnColors = true; // (조각마다 제 색 - 대파 흰·초록 등)
            group.Add(fp);
        }
        Debug.Log($"[{ingredientName}] 조각 {pieces.Count}개를 따로 익고 움직이게 함 (팬·냄비에 들어감)");
    }

    /// <summary>통째로 쓰는 재료(완두콩 · 목이버섯)를 팬에 넣을 때: 겉모양을 조각(Piece_0)
    /// 하나로 옮겨, 썬 채소처럼 저으면 움직이고 소스 · 열을 받게 한다.</summary>
    private bool MakeWholePiece()
    {
        if (ingredientName != "완두콩" && ingredientName != "목이버섯") return false;
        if (rend == null) rend = GetComponent<Renderer>();
        MeshFilter mf = GetComponent<MeshFilter>();
        if (rend == null || mf == null || mf.sharedMesh == null || !rend.enabled) return false;
        foreach (Transform t in transform) if (t.name.StartsWith("Piece_")) return false;
        GameObject piece = new GameObject("Piece_0");
        piece.transform.SetParent(transform, false);
        piece.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        piece.AddComponent<MeshRenderer>().sharedMaterials = rend.sharedMaterials;
        rend.enabled = false;
        return true;
    }

    // ---- 밑간 주무르기 ----

    /// <summary>How evenly the marinade is worked into it (0..1) - new seasoning
    /// put on isn't, until it's kneaded in (MeatKneader).</summary>
    public float MarinadeMix { get; private set; } = 1f;
    public bool HasMarinade => appliedSeasonings.Count > 0;

    /// <summary>One squeeze of the kneading with the marinade on it: worked in a
    /// little more, and the pieces a little glossy with it (only a light coat -
    /// not cooked-looking).</summary>
    public void KneadMarinade(int strokesToMix = 6)
    {
        if (!HasMarinade) return;
        MarinadeMix = Mathf.Min(1f, MarinadeMix + 1f / Mathf.Max(1, strokesToMix));
        Color c = Color.black;
        float w = 0f;
        foreach (var kv in appliedSeasonings)
        {
            Condiments.Info info = Condiments.Get(kv.Key);
            if (info.form != CondimentForm.Liquid) continue;
            c += info.color * kv.Value;
            w += kv.Value;
        }
        if (w <= 0f || !(GetComponent<FoodPieceGroup>() is FoodPieceGroup g)) return;
        Color tint = c / w;
        tint.a = 1f;
        foreach (FoodPiece p in g.Pieces)
            if (p != null && p.Coat < 0.25f) p.AddCoat(0.05f, tint, 0.25f);
    }

    protected override void Awake()
    {
        base.Awake();
        GivenGrams = amountGrams;
        rend = GetComponent<Renderer>();
        rend.material = new Material(rend.sharedMaterial);
        sharedMesh = GetComponent<MeshFilter>().sharedMesh;

        if (isSoakable) CurrentSoakState = SoakState.Dry;
    }

    void Update()
    {
        UpdateTowelDrain();
        // 물 담긴 볼 · 작은 접시에 담가 두면 불리기 시작 (수돗물 아래에서 시작하는 것과 같음)
        if (CurrentSoakState == SoakState.Dry && InBowl is MovableBowl soakIn && soakIn.WaterAmount > 0f) StartSoaking();
        if (CurrentSoakState != SoakState.Soaking) return;

        soakTimer += Time.deltaTime;
        rend.material.color = Color.Lerp(rend.material.color, new Color(0.55f, 0.4f, 0.35f), Time.deltaTime * 0.3f);
        float swell = Mathf.Lerp(1f, soakSwell, Mathf.Clamp01(soakTimer / soakDuration)); // 지금 부푼 배수
        transform.localScale *= swell / soakShownSwell;                                    // 물을 먹으며 점점 부풂 (그릇을 옮겨 크기 기준이 바뀌어도 늘어난 만큼만)
        soakShownSwell = swell;

        if (soakTimer >= soakDuration)
        {
            CurrentSoakState = SoakState.Soaked;
        }
    }

    /// <summary>Counts time resting on a spread paper towel (resets if it's
    /// taken off early); done, the towel has drawn the blood/water off.</summary>
    private void UpdateTowelDrain()
    {
        if (!NeedsTowelDrain) return;
        if (IsHeld || !OnTowel) { drainTimer = 0f; return; }

        drainTimer += Time.deltaTime;
        if (drainTimer < drainSeconds) return;

        MarkTowelDrained();
        Debug.Log($"{ingredientName}: 키친타올로 {(drainOnTowel == DrainNeed.Blood ? "핏물" : "물기")} 제거 완료");
    }

    /// <summary>Wrapped in the towel and pressed (TowelSpread folded over it):
    /// the blood/water is drawn off right away.</summary>
    public void BlotWithTowel()
    {
        if (drainOnTowel == DrainNeed.None && !needsBloodDrain) return;
        MarkTowelDrained();
        Debug.Log($"{ingredientName}: 키친타올로 감싸 {(drainOnTowel == DrainNeed.Blood || needsBloodDrain ? "핏물" : "물기")} 제거 완료");
    }

    /// <summary>Left to drain in the strainer resting across a bowl: its water's off.</summary>
    public void DrainInStrainer()
    {
        if (drainOnTowel == DrainNeed.None && !needsBloodDrain) return;
        MarkTowelDrained();
        Debug.Log($"{ingredientName}: 체에 받쳐 {(drainOnTowel == DrainNeed.Blood || needsBloodDrain ? "핏물" : "물기")} 제거 완료");
    }

    private void MarkTowelDrained()
    {
        if (IsTowelDrained) return;
        IsTowelDrained = true;
        BloodDrained = true;

        // Drawn off: meat goes a little paler, tofu just settles.
        if (drainOnTowel == DrainNeed.Blood)
        {
            rend.material.color = Color.Lerp(rend.material.color, Color.white, 0.2f);
            foreach (Transform child in GetComponentsInChildren<Transform>()) // (pieces may sit under a "Pieces" holder)
                if (child.name.StartsWith("Piece_") && child.TryGetComponent(out Renderer piece)) piece.material.color = Color.Lerp(piece.material.color, Color.white, 0.2f);
        }
        StopCoroutine(nameof(ChopPulse));
        StartCoroutine(nameof(ChopPulse));
    }

    public bool NeedsSoaking => isSoakable && CurrentSoakState != SoakState.Soaked;

    public void StartSoaking()
    {
        if (!isSoakable || CurrentSoakState == SoakState.Soaking || CurrentSoakState == SoakState.Soaked) return;
        CurrentSoakState = SoakState.Soaking;
        soakTimer = 0f;
        soakShownSwell = 1f; // 아직 안 부풂
    }

    public void Wash()
    {
        if (CurrentState != State.Raw) return;

        CurrentState = State.Washed;
        WasWashed = true;
        StopCoroutine(nameof(WashMotion));
        StartCoroutine(nameof(WashMotion));
    }

    private IEnumerator WashMotion()
    {
        Vector3 baseS = BaseLocalScale;
        float duration = 0.35f;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float decay = 1f - t / duration;
            float wobble = Mathf.Sin(t * Mathf.PI * 8f) * 0.1f * decay;
            transform.localScale = baseS * (1f + wobble);
            yield return null;
        }

        transform.localScale = baseS;
    }

    protected override bool OnClickedWhileHandEmpty()
    {
        // 완성 접시에 담긴 요리를 누르면: 음식만 집지 않고 접시째 든다 (제출대로)
        if (InBowl is MovableBowl served && served.CarriesAsServed)
        {
            HandCarry.Instance?.PickUp(served);
            return true;
        }

        // 팬 · 웍 · 냄비에 들어간 재료는 맨손으로 집지 않는다 - 튀김젓가락 · 국자 ·
        // 건지개로 꺼낸다. (손에 도구를 들고 누르면 OnClickedWhileHandFull 로 가므로
        // 여기서 막히는 것은 맨손뿐이다)
        if (InBowl is MovableBowl cooking && cooking.IsCookingVessel)
        {
            Notice("팬 안의 재료는 손으로 집을 수 없어요 (튀김젓가락 · 국자로 꺼내세요)"); // 왜 안 집히는지 알려 준다
            return true;                                                                   // 기본 집기 동작을 막는다
        }

        // 버무리기 by hand: the meat with its 튀김옷 (or a 밑간 not worked in yet) in
        // a bowl, not on the heat - both hands into the bowl (MeatKneader; held
        // down, they knead). One piece is taken by pressing and holding on it.
        if (InBowl is MovableBowl mixBowl && !mixBowl.IsCookingVessel && Category == IngredientCategory.Meat
            && MeatKneader.HasMeatToKnead(mixBowl))
        {
            MeatKneader.Begin(mixBowl);
            return true;
        }
        // 다 버무린 그릇의 고기: 더 버무리지 않고 [반죽 조각 / 고기 한 점 / 닫기] 선택창
        if (InBowl is MovableBowl doneBowl && !doneBowl.IsCookingVessel && Category == IngredientCategory.Meat
            && BowlMixInteraction.For(doneBowl).ShowsChooser())
        {
            BowlMixInteraction.For(doneBowl).OpenChooser();
            return true;
        }

        // (불린 목이버섯처럼 찢어 쓰는 재료: 길게 누르면 손으로 3등분 - IngredientTear.
        // 짧게 누르면 다른 재료처럼 그냥 집는다)

        return false;
    }

    /// <summary>True only while this ingredient is actually sitting on the cutting
    /// board - cutting (like in the real exam) has to happen there, not wherever the
    /// knife happens to touch it (a plate, the raw-ingredient strip, etc).</summary>
    private bool IsOnCuttingBoard => CurrentContainer != null && (object)CurrentContainer == CuttingBoardController.Instance;

    /// <summary>The bowl/pot/pan/plate it's sitting in, if any.</summary>
    private MovableBowl InBowl => !IsHeld && transform.parent != null ? transform.parent.GetComponentInParent<MovableBowl>() : null;

    public override bool CanAcceptDrop(Carryable item)
    {
        if (item is ToolItem spatula && spatula.Kind == ToolKind.Spatula) return InBowl != null;
        if (item is FryingChopsticks chop && chop.Pinched == null) return !IsHeld; // to pinch it up
        if (item is Strainer) return !IsHeld; // scooped into the basket
        if (item is SeasoningBottle) return true;
        if (item is ToolItem spoon && spoon.Kind == ToolKind.MeasuringSpoon && spoon.IsSpoonLoaded) return true;
        if (item is ToolItem knife && knife.Kind == ToolKind.Knife && CurrentState != State.Cut && !NeedsSoaking && IsOnCuttingBoard) return true;
        if (item is ToolItem scraper && scraper.Kind == ToolKind.Knife && scraper.KnifeLoad == null && CurrentState == State.Cut && IsOnCuttingBoard) return true;
        if (item is ToolItem towel && towel.Kind == ToolKind.PaperTowel && NeedsBloodDrain) return true;
        return false;
    }

    private void AddSeasoning(string seasoningName, float ml)
    {
        appliedSeasonings.TryGetValue(seasoningName, out float had);
        appliedSeasonings[seasoningName] = had + ml;
        MarinadeMix = 0f; // (new seasoning on it - not worked in until it's kneaded)
        Debug.Log($"[{ingredientName}] {seasoningName} {ml:0.#}mL (누적 {had + ml:0.#}mL)");
        StopCoroutine(nameof(ChopPulse));
        StartCoroutine(nameof(ChopPulse));
    }

    /// <summary>Blots the cut pieces with a paper towel to draw off blood - the
    /// original ingredient's own renderer is already off post-cut, so this tints
    /// each spawned Piece_i instead (each gets its own material instance the
    /// first time .material is touched, so this doesn't bleed into siblings
    /// still sharing rend.sharedMaterial).</summary>
    private void DrainBlood()
    {
        BloodDrained = true;
        if (drainOnTowel == DrainNeed.Blood) IsTowelDrained = true; // blotted - counts too

        foreach (Transform child in GetComponentsInChildren<Transform>())
        {
            Renderer pieceRend = child.GetComponent<Renderer>();
            if (pieceRend == null || !child.name.StartsWith("Piece_")) continue;
            pieceRend.material.color = Color.Lerp(pieceRend.material.color, Color.white, 0.3f);
        }

        StopCoroutine(nameof(ChopPulse));
        StartCoroutine(nameof(ChopPulse));
    }

    protected override void OnClickedWhileHandFull(Carryable held)
    {
        // The strainer in hand scoops this one piece up (the peas off their plate).
        if (held is Strainer strainer && !IsHeld)
        {
            if (CurrentContainer is MovableBowl vessel && !strainer.HasContents)
            {
                strainer.ScoopFrom(vessel);
                return;
            }
            if (strainer.TakeIn(this)) return;
        }

        // Frying chopsticks pinch this piece up (e.g. out of the hot oil).
        if (held is FryingChopsticks chop)
        {
            chop.TryPinch(this);
            return;
        }

        // Using a bottle here measures one spoonful into the measuring spoon
        // first (bottle goes back to the shelf, spoon ends up in hand)...
        if (held is SeasoningBottle bottle)
        {
            bottle.TryScoopWithSpoon();
            return;
        }

        // The spatula on a piece in the pan stirs the pan.
        // 버무리기: the meat coated in its bowl (not on the heat), one stroke a click.
        if (held is ToolItem mixer && BatterCoating.IsMixingTool(mixer) && InBowl is MovableBowl mixBowl && !mixBowl.IsCookingVessel
            && BatterCoating.Of(this, false) is BatterCoating batter && batter.HasAny)
        {
            batter.MixStroke(mixer, mixBowl);
            return;
        }

        if (held is ToolItem spatula && spatula.Kind == ToolKind.Spatula && InBowl is MovableBowl stirred)
        {
            stirred.StirWithSpatula();
            return;
        }

        // ...and the loaded spoon is what actually adds the dose - in the frying
        // pan it goes into the pan's sauce, not onto this one piece.
        if (held is ToolItem panSpoon && panSpoon.Kind == ToolKind.MeasuringSpoon && panSpoon.IsSpoonLoaded
            && InBowl is MovableBowl pan && pan.GetComponent<FryingPanPlating>() != null)
        {
            string sauceName = panSpoon.LoadedSeasoning;
            SaucePortion portion = panSpoon.LoadedSauce;
            float ml = panSpoon.ConsumeSeasoning();
            if (portion != null) pan.AddSaucePortion(portion);
            else pan.AddSeasoning(sauceName, ml);
            return;
        }
        if (held is ToolItem spoon && spoon.Kind == ToolKind.MeasuringSpoon && spoon.IsSpoonLoaded)
        {
            string seasoning = spoon.LoadedSeasoning;
            AddFromSpoon(seasoning, spoon.ConsumeSeasoning()); // (all of what's on it, in mL)
            return;
        }

        if (held is ToolItem towel && towel.Kind == ToolKind.PaperTowel && NeedsBloodDrain)
        {
            DrainBlood();
            return;
        }

        // Several on the board: the knife's first click on one that isn't the
        // work target only makes it the target (outlined) - not also a cut, a
        // scoop or the knife set down; the next click works on it.
        if (held is ToolItem picker && picker.Kind == ToolKind.Knife && IsOnCuttingBoard
            && CuttingBoardController.Instance is CuttingBoardController workBoard
            && (object)workBoard.Current != this && workBoard.Items.Count >= 2)
        {
            workBoard.Select(this);
            return;
        }

        // Already cut, on the board: the knife scoops the pieces up onto its
        // blade to carry them off (to a plate, the wok...).
        if (held is ToolItem scraper && scraper.Kind == ToolKind.Knife && CurrentState == State.Cut && IsOnCuttingBoard)
        {
            scraper.TryLoadOnKnife(this);
            return;
        }

        if (held is ToolItem tool && tool.Kind == ToolKind.Knife && CurrentState != State.Cut && !NeedsSoaking && !isCutting)
        {
            if (!IsOnCuttingBoard)
            {
                // Not a hygiene violation (doesn't touch HygieneLog's score) - just a
                // reminder that cutting has to happen on the board, like in the real exam.
                Debug.Log($"{ingredientName}: 도마 위에서만 손질할 수 있어요");
                return;
            }

            HygieneTracker knifeHygiene = tool.GetComponent<HygieneTracker>();
            HygieneTracker boardHygiene = CuttingBoardController.Instance?.Hygiene;

            bool knifeDirty = knifeHygiene != null && knifeHygiene.RegisterUse(category);
            bool boardDirty = boardHygiene != null && boardHygiene.RegisterUse(category);

            if (knifeDirty || boardDirty)
            {
                string what = knifeDirty && boardDirty ? "칼·도마" : knifeDirty ? "칼" : "도마";
                HygieneLog.Instance?.ReportViolation($"{ingredientName} 손질: {what} 세척 없이 교차 사용 (교차오염)");
            }
            if (knifeHygiene != null && knifeHygiene.NeedsWash)
                HygieneLog.Instance?.ReportViolation($"{ingredientName} 손질: 음식물쓰레기를 버린 칼을 씻지 않고 사용");

            ShowCutChooser(tool); // 칼을 든 자리에 썰기 메뉴를 띄운다
        }
    }

    /// <summary>The ring menu goes all round the knife in hand, in front of everything.
    /// 재료를 클릭했을 때와 다듬기가 끝난 뒤 모두 이 메뉴가 같은 자리에 나온다.</summary>
    private void ShowCutChooser(ToolItem knife)
    {
        Renderer knifeRend = knife.GetComponentInChildren<Renderer>(); // 칼 모델의 위치를 메뉴 중심으로
        Vector3 center = knifeRend != null ? knifeRend.bounds.center : transform.position; // 칼이 안 보이면 재료 자리
        Camera cam = Camera.main; // 현재 카메라 방향으로 앞에 배치한다.
        if (cam != null) center -= cam.transform.forward * 0.6f; // 회전한 씬에서 세계 Z로 밀지 않는다.
        CutMethodChooser.Instance.Show(center, PerformCut, CanTrim ? PerformTrim : (System.Action)null,
            category == IngredientCategory.Vegetable); // 야채면 편·어슷의 두께 칸을 뺀다
    }

    private bool isCutting;

    /// <summary>야채 편·어슷썰기의 두께(cm). 규격으로 묻지 않고 이 값으로 고정한다 - 종이처럼
    /// 얇지도, 고기·두부처럼 두툼하지도 않은 편 두께. 두께를 따지는 건 양을 많이 주는
    /// 메인재료(고기 채썰기, 두부 사각썰기)뿐이다.</summary>
    public const float VeggieSliceThicknessCm = 0.3f;

    /// <summary>Reference (standard-spec) thickness per method, in cm - the size our
    /// baseline piece visuals were tuned for. Chosen thickness scales visuals from here.</summary>
    public static float ReferenceThicknessCm(CutMethod method)
    {
        switch (method)
        {
            case CutMethod.Julienne: return 0.3f;
            case CutMethod.Dice: return 0.2f;
            case CutMethod.Cube: return 1.0f;
            case CutMethod.Other: return 1.0f;
            case CutMethod.StairCut: return 1.0f;
            case CutMethod.FlowerCut: return 1.0f;
            case CutMethod.DiagonalCut: return 1.5f;
            case CutMethod.Tear: return 1.0f;
            default: return 0.4f; // Slice
        }
    }

    /// <summary>Builds a full width/length/thickness spec for a method given just the
    /// thickness the player chose - width and length follow each method's usual shape.</summary>
    public static CutSpec BuildSpec(CutMethod method, float thicknessCm)
    {
        switch (method)
        {
            case CutMethod.Julienne: return new CutSpec(thicknessCm, 6f, thicknessCm);
            case CutMethod.Slice:
            case CutMethod.DiagonalCut: return new CutSpec(4f, 1f, VeggieSliceThicknessCm); // 편·어슷은 4 x 1 cm 편, 두께는 고정
            case CutMethod.Dice:
            case CutMethod.Cube:
            case CutMethod.Other: return new CutSpec(thicknessCm, thicknessCm, thicknessCm);
            default: return new CutSpec(thicknessCm * 4f, thicknessCm * 3f, thicknessCm);
        }
    }

    // ---- 다듬기 (trimming) ----

    /// <summary>What trimming takes off each ingredient (by its IngredientName):
    /// peel, stem end, seeds, the garlic's hard end... - each one a FoodScrap
    /// set aside in the board's left corner. Not listed: nothing to trim.</summary>
    private static readonly Dictionary<string, (string kind, Color color)[]> TrimScraps = new Dictionary<string, (string, Color)[]>
    {
        { "양파", new[] { ("껍질", new Color(0.75f, 0.5f, 0.22f)), ("꼭지", new Color(0.8f, 0.65f, 0.4f)) } },
        { "당근", new[] { ("껍질", new Color(0.85f, 0.4f, 0.1f)), ("꼭지", new Color(0.7f, 0.35f, 0.1f)) } },
        { "오이", new[] { ("꼭지", new Color(0.15f, 0.35f, 0.12f)), ("씨", new Color(0.85f, 0.9f, 0.7f)) } },
        { "고추", new[] { ("꼭지", new Color(0.3f, 0.45f, 0.15f)), ("씨", new Color(0.97f, 0.92f, 0.7f)) } },
        { "마늘", new[] { ("꼭지", new Color(0.8f, 0.72f, 0.55f)) } },
        { "생강", new[] { ("껍질", new Color(0.7f, 0.55f, 0.35f)) } },
        { "대파", new[] { ("겉잎", new Color(0.85f, 0.9f, 0.75f)) } },
        { "돼지등심", new[] { ("지방", new Color(0.97f, 0.94f, 0.88f)) } },
        { "목이버섯", new[] { ("밑동", new Color(0.3f, 0.2f, 0.15f)) } },
    };

    public bool IsTrimmed { get; private set; }
    public bool CanTrim => !IsTrimmed && CurrentState != State.Cut && TrimScraps.ContainsKey(ingredientName);

    /// <summary>반쪽 양파를 뒤집어 다듬고 폭 간격으로 썰어 가는 길(CrescentRoutine).
    /// 씬의 crescentSlices 체크 외에 양파는 이름으로도 이 길을 탄다 - 씬 값이 꺼져 있어도
    /// 양파는 늘 양파 순서대로 썰리게.</summary>
    private bool UsesCrescent => crescentSlices || ingredientName == "양파";

    /// <summary>이 썰기가 양파 길(CrescentRoutine)로 가는지 - 편썰기·채썰기·기타썰기만.</summary>
    private bool UsesCrescentCut(CutMethod method) => UsesCrescent
        && (method == CutMethod.Slice || method == CutMethod.Julienne || method == CutMethod.Other);

    /// <summary>다듬기: a few knife strokes take off the peel/stem/seeds (FoodScraps
    /// in the board's left corner); once per ingredient, before cutting.</summary>
    public void PerformTrim()
    {
        if (!CanTrim || isCutting || !IsOnCuttingBoard) return;
        // (다듬기는 재료 제자리에서만 칼이 오가고 떼어낸 건 도마 구석으로 가므로, 칼질과 달리 옆 공간을 따지지 않는다)
        CuttingBoardController.Instance.Select(this);
        IEnumerator trim;
        // 고기 (채썰기용): 다듬기 is cutting the ends off to the strips' length (4 cm).
        if (sliceStyle == SliceStyle.MeatStrips) trim = MeatTrimRoutine();
        // 오이·당근·대파: 썰기의 앞단계(끝 자르기 · 네모 다듬기 · 씨/심 제거 · 펼치기)만 재생한다.
        else if (sliceStyle == SliceStyle.CucumberDiagonal || sliceStyle == SliceStyle.CarrotPlank || sliceStyle == SliceStyle.LeekFlat)
            trim = VeggieTrimRoutine();
        // 양파: 위아래를 잘라 4cm로 맞추고 가운데 심을 빼낸다.
        else if (UsesCrescent) trim = OnionTrimRoutine();
        else trim = ingredientName == "생강" ? GingerTrimRoutine() : chiliTrim ? ChiliTrimRoutine() : TrimRoutine(); // 생강은 겉면을 깎아 노란 속살을 드러낸다.
        StartCoroutine(TrimThenChoose(WhileLocked(trim))); // 다듬고 나면 바로 썰기 메뉴로 넘어간다
    }

    /// <summary>다듬기가 끝나면 썰기 메뉴를 다시 띄운다 - 다듬기 다음은 칼질이니
    /// 칼을 든 채로 재료를 또 클릭하게 하지 않는다.</summary>
    private IEnumerator TrimThenChoose(IEnumerator trim)
    {
        yield return trim;                                              // 다듬기 동작과 손 잠금이 끝날 때까지
        if (!IsOnCuttingBoard || isCutting || CurrentState == State.Cut) yield break; // 도중에 들려 나갔거나 이미 썰렸으면 그만
        if (HandCarry.Instance?.Current is ToolItem knife && knife.Kind == ToolKind.Knife)
            ShowCutChooser(knife);                                      // 아직 칼을 들고 있으면 썰기 규격을 바로 고르게
    }

    /// <summary>다듬기 버튼(오이·당근·대파): 썰기의 앞단계만 재생하고 칼질은 하지 않는다.
    /// 썰 때와 같은 코드를 trimOnly로 돌리므로, 다듬기를 따로 하든 바로 썰든 보이는
    /// 동작이 같다 - 바로 썰면 그 앞단계가 칼질에 이어 붙어 한 번에 진행된다.</summary>
    private IEnumerator VeggieTrimRoutine()
    {
        isCutting = true;
        SupportHandController.Instance?.ShowNear(transform.position); // 재료를 받치는 손
        yield return StyledSliceRoutine(CutMethod.Slice, BuildSpec(CutMethod.Slice, VeggieSliceThicknessCm), true); // 앞단계만
        IsTrimmed = true;                                             // 중복 다듬기를 막고 썰기에서 건너뛰게 한다
        isCutting = false;
        SupportHandController.Instance?.Hide();
        Debug.Log($"{ingredientName}: 다듬기 완료 (썰기 앞단계)");
    }

    /// <summary>다듬기 버튼(양파): 위아래를 잘라 규격 길이로 맞추고 가운데 심을 빼낸다.</summary>
    private IEnumerator OnionTrimRoutine()
    {
        isCutting = true;
        HandController handCtl = HandController.Instance;
        ToolItem knife = HandCarry.Instance?.Current as ToolItem;
        Vector3 handBase = handCtl != null ? handCtl.RestPosition : Vector3.zero;
        SupportHandController.Instance?.ShowNear(transform.position);

        Bounds shown = VisibleBounds;                                  // 도마에 놓인 반쪽 양파
        float R = Mathf.Max(shown.extents.x, shown.extents.y);
        Vector3 up = Carryable.CounterUp, back = Vector3.Cross(Vector3.right, up).normalized;
        var rig = new GameObject("TrimRig").transform;                 // 도마 기준 좌표계 (X 가로 · Y 위 · Z 뒤)
        rig.SetParent(PieceHolder.For(transform), false);
        rig.SetPositionAndRotation(shown.center - up * (R * 0.5f), Quaternion.LookRotation(back, up));

        float knifeHoldBefore = knife != null ? knife.ChopHoldSeconds : 0f;
        if (knife != null) knife.ChopHoldSeconds = 4f * diceSlowness;  // 칼을 든 채로 기다리게
        if (knife != null && handCtl != null)
        {
            knife.PlayChopMotion();
            yield return new WaitForSeconds(0.08f);
            if (BladeXAt(knife, rig.position.y, out float bladeX)) handBase.x += rig.position.x - bladeX; // 칼날을 칼금에 맞춘다
        }

        yield return OnionTrim(rig, handBase, handCtl, knife, R, 4f * worldPerCm); // 규격 길이 4cm로

        if (knife != null) knife.ChopHoldSeconds = knifeHoldBefore;
        foreach (Transform t in rig.GetComponentsInChildren<Transform>())
            if (t.name.StartsWith("Piece_Trimmed")) t.SetParent(PieceHolder.For(transform), true); // 리그가 사라져도 다듬은 몸통은 남게
        Destroy(rig.gameObject);                                       // 임시 좌표계 정리
        FitColliderToVisible();                                        // 다듬은 몸통을 집을 수 있게
        IsTrimmed = true;
        isCutting = false;
        SupportHandController.Instance?.Hide();
        Debug.Log($"{ingredientName}: 다듬기 완료 (위아래 잘라 4cm · 가운데 심 제거)");
    }

    /// <summary>다듬은 양파 몸통의 단면이 도는 각 - 지급분이 1/4조각이라 반달(180°)의 절반.</summary>
    private const float QuarterSweep = Mathf.PI * 0.5f;

    /// <summary>양파 다듬기 동작: 1/4쪽 양파를 뒤집어 둥근 쪽이 위로 오게 놓고, 위아래(꼭지·뿌리)를
    /// 좌우에 둔 채 잘라 규격 길이(4cm)로 맞추고, 칼을 옆에 둔 채 가운데 뿌리심을 뒤로 뽑아낸 뒤,
    /// 길이 방향이 앞뒤로 가게 90도 돌린다 - 이제 칼이 폭을 가로질러 썰 수 있다.
    /// 다듬기 버튼과 반달썰기 앞단계가 이 하나를 같이 쓴다.</summary>
    private IEnumerator OnionTrim(Transform rig, Vector3 handBase, HandController handCtl, ToolItem knife, float R, float bodyLen)
    {
        Material coreM = SolidMaterial(new Color(0.96f, 0.94f, 0.82f), 0.5f); // 뿌리심 색
        float half = Mathf.Min(bodyLen, R * 1.8f) * 0.5f;                     // 규격 길이의 반 - 여기서 양 끝을 끊는다
        float raw = half * 2.8f;                                              // 다듬기 전 길이 - 양 끝에 떼어낼 만큼을 남긴다

        // 다듬을 몸통: 반원 단면(결이 보이는)을 길이 방향으로 민 반쪽 양파
        var body = new GameObject("Piece_Trimmed");                           // 다듬기만 하면 이 몸통이 도마에 남는다
        body.transform.SetParent(rig, false);
        body.AddComponent<MeshFilter>().sharedMesh = HalfMoonMesh(R, raw, 4, 18, false, QuarterSweep); // 평평한 자른 면 두 개 + 둥근 겉면 (지급분이 1/4쪽)
        body.AddComponent<MeshRenderer>().sharedMaterial = new Material(OnionSliceMaterial());
        rend.enabled = false;                                                 // 통째 모델은 숨기고 다듬을 몸통만 보인다

        // 둥근 쪽이 아래로 엎어져 있던 걸 뒤집어 위로 오게 한다.
        SupportHandController.Instance?.ShowNear(rig.TransformPoint(new Vector3(0f, R, R * 0.6f))); // 뒤집어 주는 손
        float flip = 0.5f * diceSlowness;
        for (float e = 0f; e < flip; e += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, e / flip);                     // 0(엎어진 자세) → 1(둥근 쪽 위)
            body.transform.localRotation = Quaternion.AngleAxis(180f * (1f - k), Vector3.right); // 앞뒤 축으로 넘긴다
            body.transform.localPosition = new Vector3(0f, R * (1f - k) + R * 0.3f * Mathf.Sin(k * Mathf.PI), 0f); // 들렸다 내려앉게
            yield return null;
        }
        body.transform.localRotation = Quaternion.identity;                   // 자른 면이 도마에, 둥근 쪽이 위로
        body.transform.localPosition = Vector3.zero;

        // 위아래(꼭지·뿌리)를 좌우에 두고 잘라 규격 길이로 맞춘다.
        yield return DiceChop(rig, handBase, -half, handCtl, knife);          // 꼭지 쪽 끝
        body.transform.localScale = new Vector3((raw * 0.5f + half) / raw, 1f, 1f); // 잘린 만큼 짧아지고
        body.transform.localPosition = new Vector3((raw * 0.5f - half) * 0.5f, 0f, 0f); // 남은 쪽으로 중심이 옮겨진다
        DropScrap("꼭지", OwnColor);
        yield return DiceChop(rig, handBase, half, handCtl, knife);           // 뿌리 쪽 끝
        body.transform.localScale = new Vector3(2f * half / raw, 1f, 1f);     // 규격 길이만 남는다
        body.transform.localPosition = Vector3.zero;
        DropScrap("뿌리", OwnColor);

        // 가운데 뿌리심: 칼을 옆에 두고 다른 손으로 뒤쪽으로 뽑아낸다.
        Transform core = Block(rig, "OnionCore", new Vector3(0f, R * 0.12f, 0f),
            new Vector3(half * 0.5f, R * 0.22f, R * 0.3f), coreM).transform;
        Vector3 aside = handBase + rig.right * (R + 0.5f);
        handCtl?.SlideTo(new Vector3(aside.x, aside.y, handBase.z), 0.3f * diceSlowness);
        yield return new WaitForSeconds(0.3f * diceSlowness);
        SupportHandController.Instance?.ShowNear(rig.TransformPoint(new Vector3(0f, R * 0.3f, R * 0.6f)));
        yield return SlideAway(core, new Vector3(0f, R * 0.2f, R * 1.4f), 0.6f * diceSlowness); // 뽑혀 나간다
        DropScrap("심", coreM.color);
        body.GetComponent<MeshFilter>().sharedMesh = HalfMoonMesh(R, raw, 4, 18, true, QuarterSweep); // 뽑아낸 자리가 비게 - 가장 안쪽 원(심)이 사라진다
        handCtl?.SlideTo(handBase, 0.25f * diceSlowness);                     // 손을 제자리로

        // 길이 방향을 앞뒤로 돌려 놓는다 - 칼은 이제 폭을 가로질러 들어간다.
        yield return Turn1(body.transform, Quaternion.identity, Quaternion.AngleAxis(90f, Vector3.up), 0.45f * diceSlowness);
    }

    private IEnumerator TrimRoutine()
    {
        isCutting = true;
        SupportHandController.Instance?.ShowNear(transform.position);
        foreach (var (kind, color) in TrimScraps[ingredientName])
        {
            HandController.Instance?.PlayChopGesture();
            (HandCarry.Instance?.Current as ToolItem)?.PlayChopMotion();
            yield return StartCoroutine(ChopPulse());
            DropScrap(kind, color);
            yield return new WaitForSeconds(0.2f);
        }
        IsTrimmed = true;
        isCutting = false;
        SupportHandController.Instance?.Hide();
        Debug.Log($"{ingredientName}: 다듬기 완료");
    }

    /// <summary>One scrap of this ingredient, set aside in the cutting board's left corner.</summary>
    private void DropScrap(string kind, Color color, float grams = 0f, float sizeOverride = 0f)
    {
        CuttingBoardController board = CuttingBoardController.Instance;
        if (board == null) return;
        Renderer r = GetComponent<Renderer>();
        float size = sizeOverride > 0f ? sizeOverride
                   : r != null ? Mathf.Clamp(Mathf.Max(r.bounds.size.x, r.bounds.size.y) * 0.18f, 0.06f, 0.14f) : 0.1f;
        FoodScrap scrap = FoodScrap.Create(ingredientName, kind, color, size, transform.position);
        scrap.Grams = grams;
        board.AddScrap(scrap);
    }

    /// <summary>Its own color (first material) - for the scraps cut off the ends.</summary>
    private Color OwnColor => rend != null && rend.sharedMaterial != null ? rend.sharedMaterial.color : new Color(0.8f, 0.8f, 0.7f);

    public void PerformCut(CutMethod method)
    {
        PerformCut(method, BuildSpec(method, ReferenceThicknessCm(method)));
    }

    public void PerformCut(CutMethod method, CutSpec spec)
    {
        if (CurrentState == State.Cut || isCutting) return;
        bool fans = (UsesStyledSlice(method) && sliceStyle != SliceStyle.MeatStrips)
                    || UsesCrescentCut(method);
        bool spreads = !fans && !UsesStyledSlice(method) && !(blockDice && method == CutMethod.Cube) && !alreadyMinced;
        if (!HasRoomToCut(false, fans ? 0.5f : 0f, spreads ? 0.2f : 0f)) return;
        if (IsOnCuttingBoard) CuttingBoardController.Instance.Select(this);
        StartCoroutine(WhileLocked(CutRoutine(method, spec)));
    }

    /// <summary>Whether this cut goes the hand-done way of its slice style:
    /// 오이 편썰기/어슷썰기, 당근·대파 편썰기, 고기 채썰기.</summary>
    private bool UsesStyledSlice(CutMethod method)
    {
        switch (sliceStyle)
        {
            case SliceStyle.CucumberDiagonal:
            case SliceStyle.CarrotPlank: return method == CutMethod.Slice || method == CutMethod.DiagonalCut; // 당근도 어슷썰기(반원)를 지원한다
            case SliceStyle.LeekFlat: return method == CutMethod.Slice;
            case SliceStyle.MeatStrips: return method == CutMethod.Julienne;
            default: return false;
        }
    }

    /// <summary>Clicks are held off while the knife works: a click on the board
    /// mid-cut used to set the knife down there (stuck to the board) while the
    /// cut carried on with an empty hand.</summary>
    private IEnumerator WhileLocked(IEnumerator routine)
    {
        HandCarry hand = HandCarry.Instance;
        if (hand != null) hand.IsLocked = true;
        SpeedMode.BeginAnimation(); // (빠른 모드: the cutting plays fast - even if something else unlocks the hand)
        try { yield return StartCoroutine(routine); }
        finally { SpeedMode.EndAnimation(); }
        if (IsOnCuttingBoard) CuttingBoardController.Instance.EnsureIngredientsAboveBoard();
        if (hand != null) hand.IsLocked = false;
    }

    private IEnumerator CutRoutine(CutMethod method, CutSpec spec)
    {
        isCutting = true;
        AppliedCut = method;
        AppliedSpec = spec;
        ClearTrimVisuals();

        // The off-hand holds the piece steady near where it actually sits (not
        // just the ingredient's own root) for the whole cut, then lets go.
        SupportHandController.Instance?.ShowNear(transform.position);

        Debug.Log($"[썰기] {ingredientName} {CutMethodName(method)} - " +
                  (alreadyMinced && method == CutMethod.Dice ? "다시 다지기"
                   : UsesStyledSlice(method) ? $"손질 순서대로 ({sliceStyle})"
                   : UsesCrescentCut(method) ? "반달썰기"
                   : blockDice && method == CutMethod.Cube ? "27조각 사각썰기"
                   : "기본 썰기"));

        // Already minced (다진 고기): 다지기 is just going over it once more with
        // the knife - a few chops, nothing about it changes.
        if (alreadyMinced && method == CutMethod.Dice)
        {
            for (int i = 0; i < 8; i++)
            {
                HandController.Instance?.PlayChopGesture();
                (HandCarry.Instance?.Current as ToolItem)?.PlayChopMotion();
                yield return StartCoroutine(ChopPulse());
                yield return new WaitForSeconds(0.12f * diceSlowness);
            }
            CurrentState = State.Cut;
            isCutting = false;
            SupportHandController.Instance?.Hide();
            Debug.Log($"{ingredientName}: 한 번 더 다지기 완료");
            yield break;
        }

        if (UsesStyledSlice(method))
        {
            cutAborted = false;
            yield return StartCoroutine(StyledSliceRoutine(method, spec));
            if (cutAborted)
            {
                // (couldn't be done - too short for the spec: left as it was)
                cutAborted = false;
                isCutting = false;
                SupportHandController.Instance?.Hide();
                yield break;
            }
            CurrentState = State.Cut;
            rend.enabled = false;
            isCutting = false;
            SupportHandController.Instance?.Hide();
            yield break;
        }

        // 네모 편으로 써는 재료: 통째 모양 → 네모 조각들 (양파는 아래 반달썰기로 간다 -
        // 뒤집어 다듬고 폭 간격으로 썰어 가는 손동작이 있으므로 이 간단한 길을 쓰지 않는다)
        if (cutsToFlatSquares && method != CutMethod.Dice && !UsesCrescentCut(method))
        {
            yield return StartCoroutine(FlatSquaresRoutine(spec, method)); // 네모 편 만들기
            CurrentState = State.Cut;            // 썬 상태로
            rend.enabled = false;                // 통째 모양은 숨김
            isCutting = false;                   // 썰기 끝
            DropScrap("자투리", OwnColor);        // 끝부분 자투리
            SupportHandController.Instance?.Hide(); // 받치던 손 뗌
            ExpandColliderToCoverPieces();       // 조각들을 집을 수 있게
            yield break;
        }

        if (UsesCrescentCut(method))
        {
            yield return StartCoroutine(CrescentRoutine(spec));
            CurrentState = State.Cut;
            rend.enabled = false;
            isCutting = false;
            SupportHandController.Instance?.Hide();
            yield break;
        }

        if (blockDice && method == CutMethod.Cube)
        {
            yield return StartCoroutine(BlockDiceRoutine());
            CurrentState = State.Cut;
            rend.enabled = false;
            isCutting = false;
            SupportHandController.Instance?.Hide();
            yield break;
        }

        // 다지기: 작은 정육면체 조각들로 (단면이 정사각형)
        if (method == CutMethod.Dice && MincesToCubes)
        {
            yield return StartCoroutine(MinceCubesRoutine(spec));
            CurrentState = State.Cut;
            rend.enabled = false;
            isCutting = false;
            DropScrap("자투리", OwnColor);
            SupportHandController.Instance?.Hide();
            ExpandColliderToCoverPieces();
            yield break;
        }

        int pieceCount;
        float spreadRange;
        float sx, sy;

        switch (method)
        {
            case CutMethod.Julienne:
                pieceCount = 9; spreadRange = 0.3f; sx = 0.10f; sy = 0.85f;
                break;
            case CutMethod.Dice:
                pieceCount = 12; spreadRange = 0.35f; sx = 0.14f; sy = 0.5f;
                break;
            case CutMethod.Cube:
                pieceCount = 9; spreadRange = 0.32f; sx = 0.25f; sy = 0.25f;
                break;
            case CutMethod.StairCut:
                pieceCount = 5; spreadRange = 0.28f; sx = 0.30f; sy = 0.30f;
                break;
            case CutMethod.FlowerCut:
                pieceCount = 6; spreadRange = 0.25f; sx = 0.22f; sy = 0.22f;
                break;
            case CutMethod.DiagonalCut:
                pieceCount = 4; spreadRange = 0.3f; sx = 0.40f; sy = 0.55f;
                break;
            case CutMethod.Tear:
                pieceCount = 5; spreadRange = 0.3f; sx = 0.28f; sy = 0.22f;
                break;
            default: // Slice
                pieceCount = 4; spreadRange = 0.3f; sx = 0.35f; sy = 0.85f;
                break;
        }

        // scale the piece visual by how far the chosen thickness is from the reference
        float thicknessRatio = Mathf.Clamp(spec.thicknessCm / ReferenceThicknessCm(method), 0.4f, 2.5f);
        sx *= thicknessRatio;

        // The model's own axes: its long side (cut across), its up (toward the
        // counter's up - kept full height) and the remaining across axis.
        Bounds meshBounds = sharedMesh != null ? sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
        Vector3 ms = meshBounds.size;
        int longAxis = ms.x >= ms.y && ms.x >= ms.z ? 0 : ms.y >= ms.z ? 1 : 2;
        int axA = (longAxis + 1) % 3, axB = (longAxis + 2) % 3;
        Vector3 dirA = Vector3.zero; dirA[axA] = 1f;
        Vector3 dirB = Vector3.zero; dirB[axB] = 1f;
        bool aIsUp = Mathf.Abs(Vector3.Dot(transform.TransformDirection(dirA).normalized, Carryable.CounterUp))
                   >= Mathf.Abs(Vector3.Dot(transform.TransformDirection(dirB).normalized, Carryable.CounterUp));
        int upAxis = aIsUp ? axA : axB, acrossAxis = aIsUp ? axB : axA;

        float across = method == CutMethod.Julienne ? 0.3f : method == CutMethod.Dice ? 0.25f
                     : method == CutMethod.Cube ? 0.5f : method == CutMethod.Tear ? 0.6f
                     : method == CutMethod.Other ? Mathf.Clamp01(sy) : 1f;
        Vector3 pieceScale = Vector3.one;
        pieceScale[longAxis] = Mathf.Clamp(0.85f / pieceCount * thicknessRatio, 0.04f, 1f);
        pieceScale[acrossAxis] = across;
        pieceScale[upAxis] = method == CutMethod.Dice ? 0.5f : 1f;

        if (cutResultMesh != null && cutResultMesh != sharedMesh) // (the same mesh again: swapping it only turned it round, looking bigger)
        {
            // Same chop timing/feedback as the generic path, just without
            // spawning placeholder pieces - the swap below is the "piece".
            for (int i = 0; i < pieceCount; i++)
            {
                HandController.Instance?.PlayChopGesture();
                (HandCarry.Instance?.Current as ToolItem)?.PlayChopMotion();
                yield return StartCoroutine(ChopPulse());
                yield return new WaitForSeconds(0.16f);
            }

            GetComponent<MeshFilter>().sharedMesh = cutResultMesh;
            if (cutResultMaterial != null) rend.material = cutResultMaterial;

            // The cut-result mesh (e.g. the halved onion) can come out standing
            // tall even when the whole one was round - lay it lengthwise too,
            // like everything else on the board.
            // (As its own spin, so it stays lying that way once picked up.)
            if (rend.bounds.size.y > rend.bounds.size.x * 1.1f)
            {
                Vector3 center = rend.bounds.center;
                Spin(90f);
                transform.rotation = RestingRotation;
                transform.position += center - rend.bounds.center;
            }

            CurrentState = State.Cut;
            isCutting = false;
            DropScrap("자투리", OwnColor); // the ends cut off to get to the spec
            // Let go here too - this branch used to return before the Hide()
            // at the end, leaving the off-hand stuck on the board.
            SupportHandController.Instance?.Hide();
            yield break;
        }

        for (int i = 0; i < pieceCount; i++)
        {
            // brief "chop impact" pulse, then one slice pops off - parented to this
            // ingredient (not the world) so it keeps traveling with it if carried later.
            HandController.Instance?.PlayChopGesture();
            (HandCarry.Instance?.Current as ToolItem)?.PlayChopMotion();
            yield return StartCoroutine(ChopPulse());

            GameObject piece = new GameObject($"Piece_{i}");
            piece.transform.SetParent(transform, false);
            piece.AddComponent<MeshFilter>().sharedMesh = sharedMesh;
            // Every material, not just the first: the portion models are skin + cut
            // faces, and with the skin alone the pieces showed nothing toward the
            // camera - the cut cucumber looked gone, though it still held the board.
            piece.AddComponent<MeshRenderer>().sharedMaterials = rend.sharedMaterials;
            piece.AddComponent<CutResultPiece>().Init(ingredientName, method, spec);

            // Laid out where the whole one lay: cut across its long side, the
            // pieces side by side along it (a little apart), each thin along that
            // side - slices keep the full cross-section, strips/dice narrower.
            float t = pieceCount <= 1 ? 0.5f : (float)i / (pieceCount - 1);
            Vector3 pos = meshBounds.center;
            pos[longAxis] += Mathf.Lerp(-0.5f, 0.5f, t) * meshBounds.size[longAxis] * 1.1f;
            pos[acrossAxis] += Random.Range(-0.06f, 0.06f) * meshBounds.size[acrossAxis];
            // (the scaled piece's own middle sits at Scale(center, pieceScale) from its pivot)
            piece.transform.localPosition = pos - Vector3.Scale(meshBounds.center, pieceScale);
            piece.transform.localScale = pieceScale;

            yield return new WaitForSeconds(0.16f);
        }

        CurrentState = State.Cut;
        rend.enabled = false;
        isCutting = false;
        DropScrap("자투리", OwnColor); // the ends cut off to get to the spec

        SupportHandController.Instance?.Hide();

        ExpandColliderToCoverPieces();
    }

    // ---- 두부 사각썰기 (blockDice): 27 independent pieces ----

    /// <summary>Real size of the model: world units per cm (두부: its 0.44 tall
    /// model = 4.5 cm). The cut spacing is the chosen spec (cm) times this - the
    /// model is never stretched to make the pieces come out even.</summary>
    [SerializeField] private float worldPerCm = 0.0978f;
    /// <summary>Thinnest end slice (단면) that's still taken off, cm.</summary>
    [SerializeField] private float minTrimCm = 0.2f;
    /// <summary>How slowly the dicing goes - 1 = a brisk pace, 2 (default) = the
    /// careful pace of the exam; every cut, turn and pause scales with it.</summary>
    [SerializeField, Min(0.2f)] private float diceSlowness = 2f;

    /// <summary>One piece while it's being cut: its size along its own axes (world).</summary>
    private sealed class CutPiece
    {
        public FoodPiece piece;
        public Vector3 size;
        public Transform T => piece.transform;
    }

    private TextMesh diceLabel;
    private readonly List<GameObject> diceGuides = new List<GameObject>();

    /// <summary>사각썰기 of a block, the way it's done by hand, every piece a real
    /// object from the cut that makes it (1 → 3 → 9 → 27): a thin slice off the
    /// left and right ends (scraps), two cuts across the width (3 slabs), the
    /// block turned 90° about its upright axis so its depth faces the knife, two
    /// cuts (9 sticks), tipped 90° onto its side so its height faces the knife,
    /// two cuts (27 cubes). The pieces stay together where they were cut and turn
    /// together; a label and guide lines show each step's cutting direction.</summary>
    private IEnumerator BlockDiceRoutine()
    {
        HandController handCtl = HandController.Instance;
        ToolItem knife = HandCarry.Instance?.Current as ToolItem;
        Vector3 handBase = handCtl != null ? handCtl.RestPosition : Vector3.zero;

        // Drawn in from the board's edge first, so it's all on the board.
        if (CuttingBoardController.Instance is CuttingBoardController board0 && IsOnCuttingBoard)
        {
            Bounds vb = VisibleBounds;
            Vector3 pull = RoomShift(board0.VisibleTopRect(), new Bounds(vb.center, vb.size + new Vector3(0.3f, 0.4f, 0f)));
            if (pull != Vector3.zero)
            {
                yield return SlideBy(pull, 0.2f);
                handBase += new Vector3(pull.x, pull.y, 0f);
                handCtl?.SlideTo(handBase, 0.15f);
                SupportHandController.Instance?.ShowNear(transform.position);
            }
        }

        // The rig: x across the board (the knife comes down along it), y the
        // counter's up, z back along the counter.
        Vector3 up = Carryable.CounterUp;
        Vector3 right = Vector3.ProjectOnPlane(transform.right, up);
        if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
        right.Normalize();
        if (Vector3.Dot(right, Vector3.right) < 0f) right = -right;
        Vector3 back = Vector3.Cross(right, up).normalized;

        Bounds mb = sharedMesh != null ? sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
        Vector3 ms = mb.size;
        float side = Mathf.Max(ms.x, ms.y, ms.z) * Mathf.Abs(transform.lossyScale.x);
        float Extent(Vector3 dir)
        {
            float e = 0f;
            for (int a = 0; a < 3; a++)
            {
                Vector3 axis = Vector3.zero; axis[a] = ms[a];
                e += Mathf.Abs(Vector3.Dot(transform.TransformVector(axis), dir));
            }
            return Mathf.Min(e, side);
        }
        float W = Extent(right), H = Extent(up), D = Extent(back);
        float volume = W * H * D;

        var rig = new GameObject("DiceRig").transform;
        rig.SetParent(transform, false);
        rig.position = transform.TransformPoint(mb.center);
        rig.rotation = Quaternion.LookRotation(back, up);
        rig.localScale = Vector3.one / Mathf.Max(Mathf.Abs(transform.lossyScale.x), 1e-4f);

        var pieces = new List<CutPiece> { MakeCutPiece(rig, Vector3.zero, Quaternion.identity, new Vector3(W, H, D), mb) };
        rend.enabled = false;
        Collider own = GetComponent<Collider>();
        if (own != null) own.enabled = false;
        Debug.Log($"[두부] 사각썰기 시작: {W / worldPerCm:0.0}×{H / worldPerCm:0.0}×{D / worldPerCm:0.0} cm, 간격 {SpecCm():0.0} cm");

        // Knife up in the chop stance, to line its blade up with each cut.
        // (and kept up between the slow cuts and turns, not laid down each time)
        float knifeHoldBefore = knife != null ? knife.ChopHoldSeconds : 0f;
        if (knife != null) knife.ChopHoldSeconds = 4f * diceSlowness;
        if (knife != null && handCtl != null)
        {
            knife.PlayChopMotion();
            yield return new WaitForSeconds(0.08f);
            if (BladeXAt(knife, rig.position.y, out float bladeX))
                handBase.x += rig.position.x - bladeX;
        }

        float spacing = SpecCm() * worldPerCm;

        // 1) 끝면: a thin slice off each end of the width.
        float trimCm = Mathf.Max((W / worldPerCm - 3f * SpecCm()) * 0.5f, minTrimCm);
        float trim = Mathf.Min(trimCm * worldPerCm, W * 0.2f);
        DiceStep(rig, "1/6  끝면 정리 · 자르는 방향: 폭(좌우) 양끝", new[] { -W * 0.5f + trim, W * 0.5f - trim }, pieces);
        foreach (float sign in new[] { -1f, 1f })
        {
            float line = sign * (W * 0.5f - trim);
            yield return DiceChop(rig, handBase, line, handCtl, knife);
            SplitAt(pieces, line, rig, mb);
            RemoveGuide(0);
            CutPiece end = pieces.Find(p => sign * RigPos(rig, p).x > W * 0.5f - trim);
            if (end != null)
            {
                pieces.Remove(end);
                float grams = amountGrams * end.size.x * end.size.y * end.size.z / volume;
                yield return SlideOff(end, rig, sign, 0.55f * diceSlowness);
                DropScrap("단면", OwnColor, grams, 0.18f); // a proper slice off the end, bigger than a peel
            }
        }

        // 2) Two cuts across the width: 3 slabs.
        yield return CutStep(rig, handBase, handCtl, knife, pieces, mb, spacing,
            "2/6  3등분 · 자르는 방향: 폭(좌우)", 3);

        // 3) Turned 90° about the upright axis: the depth now faces the knife.
        Label(rig, "3/6  90° 돌리기 · 세로축 기준 → 앞뒤(깊이)가 칼 방향");
        yield return new WaitForSeconds(0.4f * diceSlowness);
        yield return Turn(pieces, rig, Vector3.up, 1.1f * diceSlowness, false);
        yield return new WaitForSeconds(0.3f * diceSlowness);
        Debug.Log("[두부] 90° 회전: 세로(위)축 기준 - 이제 깊이 방향 절단");

        // 4) Two cuts: 9 sticks.
        yield return CutStep(rig, handBase, handCtl, knife, pieces, mb, spacing,
            "4/6  2번 절단 → 9조각 · 자르는 방향: 앞뒤(깊이)", 9);

        // 5) Tipped 90° onto its side: the height now faces the knife.
        Label(rig, "5/6  90° 눕히기 · 앞뒤축 기준 → 높이(위아래)가 칼 방향");
        yield return new WaitForSeconds(0.4f * diceSlowness);
        yield return Turn(pieces, rig, Vector3.forward, 1.1f * diceSlowness, true);
        yield return new WaitForSeconds(0.3f * diceSlowness);
        Debug.Log("[두부] 90° 회전: 앞뒤축 기준(눕힘) - 이제 높이 방향 절단");

        // 6) Two cuts: 27 cubes.
        yield return CutStep(rig, handBase, handCtl, knife, pieces, mb, spacing,
            "6/6  2번 절단 → 27조각 · 자르는 방향: 높이(위아래)", 27);

        handCtl?.SlideTo(handBase, 0.15f);
        ClearDiceMarks();
        if (knife != null) knife.ChopHoldSeconds = knifeHoldBefore;

        // They're this ingredient's pieces now - each its own object, moved
        // together by the group until they go into a pot, pan or plate.
        FoodPieceGroup group = GetComponent<FoodPieceGroup>();
        if (group == null) group = gameObject.AddComponent<FoodPieceGroup>();
        const float cooked = 0f; // cut raw
        int n = 0;
        foreach (CutPiece p in pieces)
        {
            p.T.SetParent(transform, true);
            p.T.name = $"Piece_{n++}";
            p.piece.Init(this, amountGrams * p.size.x * p.size.y * p.size.z / volume, cooked, p.size);
            p.T.gameObject.AddComponent<CutResultPiece>().Init(ingredientName, CutMethod.Cube, AppliedSpec);
            group.Add(p.piece);
        }
        Destroy(rig.gameObject);
        if (own != null) Destroy(own); // each piece has its own collider now

        // Slid back onto the board if they hang over.
        if (CuttingBoardController.Instance is CuttingBoardController board && IsOnCuttingBoard)
        {
            Vector3 shift = RoomShift(board.VisibleTopRect(), VisibleBounds);
            if (shift != Vector3.zero) yield return SlideBy(shift, 0.25f);
        }
        Debug.Log($"{ingredientName}: 사각썰기 완료 ({n}조각)");
    }

    // ---- 오이 어슷썰기 · 당근 편썰기 · 대파 편썰기 (sliceStyle) ----

    /// <summary>What the prep step leaves for slicing: the pieces standing side
    /// by side (left to right), where the knife comes down between them, and
    /// how each cut piece lies once it's fallen over onto the others.</summary>
    private sealed class SliceJob
    {
        public Transform rig;
        public float L, d, t;
        public float pieceL, pieceW; // 조각 한 개의 규격 길이 · 폭 (두께는 t)
        public bool trimOnly;        // 다듬기 버튼: 앞단계만 하고 칼질은 하지 않는다
        public readonly List<Transform> stack = new List<Transform>();
        public float[] cutX;          // cutX[i]: between piece i-1 and piece i
        public float rightEdge;       // where the stack ends on the right
        public float fanStep;         // how far apart the fallen pieces lie
        public float lean;            // degrees about the rig's back axis (negative: to the right)
        public System.Func<Transform, float> restY; // its height off the board, lying that way
        public bool turnLying;        // a little turn about the up axis as it lands (the leek's pieces)
    }

    /// <summary>편썰기/어슷썰기 the way it's done by hand: prepared first (오이: the
    /// outer part cut off lengthwise and laid flat, turned for the diagonal;
    /// 당근: the rounded outsides trimmed off, laid down; 대파: slit lengthwise,
    /// the core out, pressed flat), then sliced from the right end in, each
    /// piece tipping over onto the ones before it - a fan of slices, as on the
    /// board. Thickness is the chosen spec, at most 12 cuts.</summary>
    private IEnumerator StyledSliceRoutine(CutMethod method, CutSpec spec, bool trimOnly = false)
    {
        HandController handCtl = HandController.Instance;
        ToolItem knife = HandCarry.Instance?.Current as ToolItem;
        Vector3 handBase = handCtl != null ? handCtl.RestPosition : Vector3.zero;

        // Its size as modelled (world): the length and across.
        Vector3 ms = sharedMesh != null ? sharedMesh.bounds.size : Vector3.one;
        Vector3 ls = transform.lossyScale;
        float[] dims = { Mathf.Abs(ms.x * ls.x), Mathf.Abs(ms.y * ls.y), Mathf.Abs(ms.z * ls.z) };
        System.Array.Sort(dims);
        var job = new SliceJob { L = dims[2], d = dims[1], trimOnly = trimOnly }; // 다듬기만이면 앞단계에서 멈춘다
        // 조각 한 개의 규격: 가장 긴 값이 길이, 가장 짧은 값이 두께 - 4 · 1 · 1 을 어느
        // 칸에 적든 같은 조각이 나온다 (고기 채썰기가 규격을 읽는 방식과 같다). 재료
        // 자체의 굵기가 아니라 규격이 조각 크기를 정한다 (양파 4 x 1 편과 같은 방식).
        float lenCm = Mathf.Max(spec.widthCm, Mathf.Max(spec.lengthCm, spec.thicknessCm));  // 가장 긴 값 = 길이
        float thinCm = Mathf.Min(spec.widthCm, Mathf.Min(spec.lengthCm, spec.thicknessCm)); // 가장 짧은 값 = 두께
        float midCm = spec.widthCm + spec.lengthCm + spec.thicknessCm - lenCm - thinCm;     // 남은 값 = 폭
        if (lenCm <= 0f) { lenCm = 4f; midCm = 1f; thinCm = ReferenceThicknessCm(method); } // 규격을 안 적었을 때
        job.pieceL = lenCm * worldPerCm;                                                    // 조각 길이
        job.pieceW = Mathf.Max(midCm, 0.1f) * worldPerCm;                                   // 조각 폭
        job.t = Mathf.Max(Mathf.Max(thinCm, 0.1f) * worldPerCm, 0.008f);                    // 조각 두께 = 칼이 들어가는 간격

        // 고기는 재료 선반 자세(41도 기울어짐)로 들려 오므로 먼저 도마에 바로 눕힌다 -
        // 기울어진 채 재면 높이가 실제 두께보다 커 보여 포를 필요 이상으로 여러 번 뜬다.
        if (sliceStyle == SliceStyle.MeatStrips) yield return LayFlatOnBoard(0.35f * diceSlowness);

        // Room on the board for it and its fan of slices to the right.
        Vector3 center = VisibleBounds.center;
        // (the meat: cut where it lies, its sheets in front of and behind it)
        bool meatRow = sliceStyle == SliceStyle.MeatStrips;
        float fanRoom = meatRow ? 0.2f : 0.45f;
        if (CuttingBoardController.Instance is CuttingBoardController board0 && IsOnCuttingBoard)
        {
            Vector3 pull = RoomShift(board0.VisibleTopRect(), new Bounds(center + new Vector3(meatRow ? 0f : fanRoom * 0.5f, 0.05f, 0f),
                new Vector3(job.L + fanRoom + 0.3f, job.d * 2f + 0.3f, 0f)));
            if (pull != Vector3.zero)
            {
                yield return SlideBy(pull, 0.2f);
                center += pull;
                handBase += new Vector3(pull.x, pull.y, 0f);
                handCtl?.SlideTo(handBase, 0.15f);
                SupportHandController.Instance?.ShowNear(center);
            }
        }

        // The rig: x along the board, y the counter's up, z back; under the
        // "Pieces" holder, square and at world scale 1 whatever its own scale.
        Transform holder = PieceHolder.For(transform);
        Vector3 up = Carryable.CounterUp;
        Vector3 back = Vector3.Cross(Vector3.right, up).normalized;
        job.rig = new GameObject("SliceRig").transform;
        job.rig.SetParent(holder, false);
        job.rig.SetPositionAndRotation(center - up * (job.d * 0.5f), Quaternion.LookRotation(back, up));

        // Knife up, lined up with the cut lines, and kept up between the cuts.
        float knifeHoldBefore = knife != null ? knife.ChopHoldSeconds : 0f;
        if (knife != null) knife.ChopHoldSeconds = 4f * diceSlowness;
        if (knife != null && handCtl != null)
        {
            knife.PlayChopMotion();
            yield return new WaitForSeconds(0.08f);
            if (BladeXAt(knife, job.rig.position.y, out float bladeX)) handBase.x += job.rig.position.x - bladeX;
        }

        switch (sliceStyle)
        {
            case SliceStyle.CucumberDiagonal:
            case SliceStyle.CarrotPlank:
                // 오이·당근 공통. 어슷썰기는 원기둥을 세로로 반 갈라 반원 조각으로, 편썰기는
                // 옆면을 저며 내 네모난 4 x 1 편으로 (탕수육 야채는 모두 네모로 맞춘다).
                if (method == CutMethod.DiagonalCut)
                    yield return HalfRoundPrep(job, 20f, handBase, handCtl, knife); // 살짝 어슷 (45도는 너무 비스듬했다)
                else
                    yield return SquarePlankPrep(job, handBase, handCtl, knife);
                break;
            case SliceStyle.LeekFlat:
                yield return LeekPrep(job, handBase, handCtl, knife);
                break;
            case SliceStyle.MeatStrips:
                // (finishes its own pieces - their own meshes, colliders and
                // amounts; none of the slice-stack handling below)
                yield return MeatStripsPrep(job, spec, handBase, handCtl, knife);
                handCtl?.SlideTo(handBase, 0.15f);
                if (knife != null) knife.ChopHoldSeconds = knifeHoldBefore;
                Destroy(job.rig.gameObject);
                yield break;
        }

        if (trimOnly)
        {
            // 다듬기 버튼: 다듬은 모습(Piece_Trimmed)만 도마에 남기고 칼질은 하지 않는다.
            handCtl?.SlideTo(handBase, 0.15f);                     // 손을 제자리로
            if (knife != null) knife.ChopHoldSeconds = knifeHoldBefore; // 칼 들고 있는 시간 원복
            foreach (Transform t in job.rig.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("Piece_Trimmed")) t.SetParent(holder, true); // 리그가 사라져도 남게
            Destroy(job.rig.gameObject);                           // 임시 좌표계 정리
            FitColliderToVisible();                                // 다듬은 모습을 집을 수 있게
            yield break;
        }

        if (job.cutX != null) yield return SliceStackFromRight(job, handBase, handCtl, knife);

        handCtl?.SlideTo(handBase, 0.15f);
        if (knife != null) knife.ChopHoldSeconds = knifeHoldBefore;
        foreach (Transform s in job.stack)
        {
            s.SetParent(holder, true);
            s.gameObject.AddComponent<CutResultPiece>().Init(ingredientName, AppliedCut, spec);
        }
        Destroy(job.rig.gameObject);
        // (조각을 규격대로 만들므로 더 줄이지 않는다 - 줄이면 4 x 1 x 1 이 어긋난다)
        FitColliderToVisible();
        Debug.Log($"{ingredientName}: {CutMethodName(method)} 완료 ({job.stack.Count}조각, {job.pieceL / worldPerCm:0.0} x {job.pieceW / worldPerCm:0.0} x {job.t / worldPerCm:0.0} cm)");
    }

    /// <summary>어슷썰기(오이·당근): 양 끝을 떼고 원기둥을 세로로 반 갈라(반원 단면) 눕힌 뒤
    /// 비스듬히 돌려 둔다 - 그 다음 썰릴 반원 조각들이 나란히 선 모습이 된다. 규격은
    /// 반원의 지름이 가로(4cm), 눌린 높이가 세로(1cm)다.</summary>
    private IEnumerator HalfRoundPrep(SliceJob job, float angle, Vector3 handBase, HandController handCtl, ToolItem knife)
    {
        float r = job.d * 0.5f;
        bool carrot = sliceStyle == SliceStyle.CarrotPlank; // 당근이면 주황색으로 (오이와 같은 동작)
        if (!IsTrimmed) // 다듬기를 먼저 했으면 양 끝 떼는 칼질은 건너뛴다
        {
            yield return DiceChop(job.rig, handBase, -job.L * 0.25f, handCtl, knife);
            yield return DiceChop(job.rig, handBase, job.L * 0.25f, handCtl, knife);
        }
        rend.enabled = false;

        Material skin = SolidMaterial(carrot ? new Color(0.84f, 0.3f, 0.05f) : new Color(0.22f, 0.45f, 0.18f), 0.6f);  // 껍질
        Material flesh = SolidMaterial(carrot ? new Color(0.9f, 0.38f, 0.07f) : new Color(0.8f, 0.88f, 0.64f), 0.45f); // 속살
        Material core = SolidMaterial(carrot ? new Color(0.95f, 0.55f, 0.2f) : new Color(0.9f, 0.94f, 0.78f), 0.55f);  // 가운데 심
        var body = new GameObject("CucumberOuter").transform;
        body.SetParent(job.rig, false);
        // (a half disc extruded along local x: a half cylinder, flat side down)
        body.gameObject.AddComponent<MeshFilter>().sharedMesh = DiscSliceMesh(r, job.L, 0.12f, 0.45f, 16);
        body.gameObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { flesh, skin, core };
        DropScrap("자투리", flesh.color);

        // stood on its side, laid down flat (skin up)...
        yield return Turn1(body, Quaternion.AngleAxis(-90f, Vector3.right), Quaternion.identity, 0.5f * diceSlowness);
        // ...and turned for the diagonal
        if (angle != 0f) yield return Turn1(body, Quaternion.identity, Quaternion.AngleAxis(angle, Vector3.up), 0.5f * diceSlowness);
        Destroy(body.gameObject);

        // The slices it's about to become, side by side across the turned
        // cucumber - 규격대로 반원 단면이 길이(Z) x 폭(Y), 두께는 X.
        float rad = angle * Mathf.Deg2Rad;
        float pr = job.pieceL * 0.5f;                   // 반원의 반지름 = 조각 길이의 반
        float rise = job.pieceW / Mathf.Max(pr, 1e-4f); // 반원 높이(=반지름)를 조각 폭에 맞춰 누른다
        int n = Mathf.Clamp(Mathf.RoundToInt(job.L * Mathf.Cos(rad) / job.t), 2, 6); // 규격 두께로 몇 조각인지 (접시에 너무 많아 상한을 절반으로)
        float step = job.t * 1.04f;                     // 칼금이 보이게 조각 사이를 머리카락만큼 띄운다
        float span = n * step;                          // 조각들이 늘어서는 전체 길이
        job.cutX = new float[n];
        for (int i = 0; i < n; i++)
        {
            float x = -span * 0.5f + step * (i + 0.5f);
            job.cutX[i] = -span * 0.5f + step * i;
            var s = new GameObject($"Piece_{i}").transform;
            s.SetParent(job.rig, false);
            s.localPosition = new Vector3(x, 0f, -x * Mathf.Tan(rad));
            s.localScale = new Vector3(1f, rise, 1f);
            s.gameObject.AddComponent<MeshFilter>().sharedMesh = DiscSliceMesh(pr, job.t, 0.12f, 0.45f, 16);
            s.gameObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { new Material(flesh), new Material(skin), new Material(core) };
            job.stack.Add(s);
        }
        job.rightEdge = span * 0.5f + pr;
        job.fanStep = Mathf.Max(job.t * 0.9f, 0.018f); // (overlapping, gathered like slices set out on a plate)
        job.lean = -65f;
        job.restY = s => 0f;
    }

    /// <summary>편썰기(오이·당근): 양 끝을 떼고 둥근 옆면을 두 번 저며 내 네모난 몸통으로
    /// 만든다 (오이는 한 번 더 - 가운데 씨 든 속을 걷어내 세로를 규격 1cm에 맞춘다).
    /// 그 몸통을 눕혀 규격대로 네모난 4 x 1 편을 썬다. 앞의 네모 만들기가 곧 다듬기라,
    /// 다듬기를 먼저 했으면(IsTrimmed) 그 부분을 건너뛰고 바로 썬다.</summary>
    private IEnumerator SquarePlankPrep(SliceJob job, Vector3 handBase, HandController handCtl, ToolItem knife)
    {
        bool cucumber = sliceStyle == SliceStyle.CucumberDiagonal; // 오이면 씨까지 걷어낸다
        Material flesh = SolidMaterial(cucumber ? new Color(0.8f, 0.88f, 0.64f) : new Color(0.9f, 0.38f, 0.07f), 0.45f); // 속살
        Material skin = SolidMaterial(cucumber ? new Color(0.22f, 0.45f, 0.18f) : new Color(0.84f, 0.3f, 0.05f), 0.6f);  // 껍질 띠
        float w = job.d, h = job.d, trim = job.d * 0.18f;
        float bodyH = cucumber ? Mathf.Min(job.pieceW, h * 0.8f) : h; // 오이는 씨를 걷어낸 뒤 세로가 규격만큼 남는다
        GameObject body;

        if (IsTrimmed)
        {
            // 이미 다듬어 둔 상태: 네모난 몸통만 다시 세우고 칼질로 바로 넘어간다.
            rend.enabled = false;
            body = Block(job.rig, "Piece_Trimmed", new Vector3(0f, bodyH * 0.5f, 0f), new Vector3(job.L, bodyH, w - 2f * trim), flesh);
        }
        else
        {
            yield return DiceChop(job.rig, handBase, -job.L * 0.25f, handCtl, knife); // 한쪽 끝 자르기
            rend.enabled = false;
            body = Block(job.rig, "Piece_Trimmed", new Vector3(0f, h * 0.5f, 0f), new Vector3(job.L, h, w), flesh);
            GameObject sideA = Block(job.rig, "TrimA", new Vector3(0f, h * 0.5f, -(w - trim) * 0.5f), new Vector3(job.L, h * 0.9f, trim), flesh);
            body.transform.localScale = new Vector3(job.L, h, w - trim);          // 저며 낸 만큼 몸통이 얇아진다
            body.transform.localPosition = new Vector3(0f, h * 0.5f, trim * 0.5f);
            yield return SlideAway(sideA.transform, new Vector3(0f, 0f, -0.15f), 0.4f * diceSlowness);
            DropScrap("자투리", flesh.color);                                      // 저민 옆면은 자투리로

            yield return DiceChop(job.rig, handBase, job.L * 0.25f, handCtl, knife); // 반대쪽 끝·옆면
            GameObject sideB = Block(job.rig, "TrimB", new Vector3(0f, h * 0.5f, (w - trim) * 0.5f), new Vector3(job.L, h * 0.9f, trim), flesh);
            body.transform.localScale = new Vector3(job.L, h, w - 2f * trim);
            body.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            yield return SlideAway(sideB.transform, new Vector3(0f, 0f, 0.15f), 0.4f * diceSlowness);
            DropScrap("자투리", flesh.color);

            if (cucumber) yield return CucumberSeeds(job, body.transform, bodyH, handBase, handCtl, knife); // 오이: 가운데 씨 걷어내기
        }

        if (job.trimOnly) yield break; // 다듬기 버튼: 네모로 다듬은 몸통만 도마에 남긴다

        // laid down on a cut side
        yield return Turn1(body.transform, Quaternion.identity, Quaternion.AngleAxis(90f, Vector3.right), 0.5f * diceSlowness);
        Destroy(body);

        // 조각은 어슷썰기(반원)와 똑같은 자세로 둔다: 두께(X) x 세로(Y) x 가로(Z, 도마 앞뒤).
        // 전에는 가로 4cm를 위로 세워 둬서 조각들이 제 길이 방향으로 포개졌고, 그래서
        // 4 x 1 편 여러 장이 아니라 길쭉한 막대 하나처럼 보였다. 단면만 네모일 뿐
        // 놓이는 모습은 반원과 같아야 한다.
        int n = Mathf.Clamp(Mathf.RoundToInt(job.L / job.t), 2, 6); // 규격 두께로 몇 조각인지 (접시에 너무 많아 상한을 절반으로)
        float step = job.t * 1.04f;                                 // 칼금이 보이게 조각 사이를 머리카락만큼 띄운다
        float span = n * step;                                      // 조각들이 늘어서는 전체 길이
        job.cutX = new float[n];
        for (int i = 0; i < n; i++)
        {
            float x = -span * 0.5f + step * (i + 0.5f);
            job.cutX[i] = -span * 0.5f + step * i;
            GameObject s = Block(job.rig, $"Piece_{i}", new Vector3(x, job.pieceW * 0.5f, 0f), new Vector3(job.t, job.pieceW, job.pieceL), new Material(flesh));
            if (cucumber) Block(s.transform, "Skin", new Vector3(0f, 0.44f, 0f), new Vector3(1.02f, 0.13f, 1.02f), new Material(skin)); // 오이는 긴 면 한쪽에 껍질 띠가 남는다
            job.stack.Add(s.transform);
        }
        job.rightEdge = span * 0.5f + job.pieceL * 0.5f; // 반원 조각과 같은 자리에서 부채꼴이 시작되게
        job.fanStep = Mathf.Max(job.t * 0.9f, 0.018f); // (overlapping, gathered like slices set out on a plate)
        job.lean = -65f;                               // 반원 조각과 같은 기울기
        float a = 65f * Mathf.Deg2Rad;
        job.restY = s => Mathf.Sin(a) * s.localScale.x * 0.5f + Mathf.Cos(a) * s.localScale.y * 0.5f;
    }

    /// <summary>오이 씨 빼기: 네모로 다듬은 몸통 위를 칼로 한 번 길게 훑어 씨가 든 가운데
    /// 속을 걷어내고, 남은 살의 세로를 규격(1cm)에 맞춘다.</summary>
    private IEnumerator CucumberSeeds(SliceJob job, Transform body, float keepH, Vector3 handBase, HandController handCtl, ToolItem knife)
    {
        Material seedM = SolidMaterial(new Color(0.93f, 0.95f, 0.78f), 0.5f);   // 씨가 든 속 색
        Vector3 size = body.localScale;                                        // 지금 몸통 크기
        float cut = Mathf.Max(size.y - keepH, size.y * 0.1f);                  // 걷어낼 두께
        Transform seeds = Block(job.rig, "Seeds", new Vector3(0f, size.y - cut * 0.5f, 0f),
            new Vector3(size.x * 0.99f, cut, size.z * 0.92f), seedM).transform; // 몸통 윗부분이 씨가 든 속
        body.localScale = new Vector3(size.x, size.y - cut, size.z);           // 살은 규격 세로만 남는다
        body.localPosition = new Vector3(0f, (size.y - cut) * 0.5f, 0f);

        // 칼을 몸통 위로 길게 한 번 훑는다 (칼금을 내는 게 아니라 저며 걷어내는 동작).
        Vector3 from = handBase + job.rig.right * (job.L * 0.45f);             // 오른쪽 끝에서 시작
        Vector3 to = handBase - job.rig.right * (job.L * 0.45f);               // 왼쪽 끝까지
        handCtl?.SlideTo(new Vector3(from.x, from.y, handBase.z), 0.25f * diceSlowness);
        yield return new WaitForSeconds(0.3f * diceSlowness);
        handCtl?.SlideTo(new Vector3(to.x, to.y, handBase.z), 0.5f * diceSlowness);
        yield return new WaitForSeconds(0.55f * diceSlowness);
        handCtl?.SlideTo(handBase, 0.2f * diceSlowness);                       // 손을 제자리로
        yield return SlideAway(seeds, new Vector3(0f, 0.03f, -0.18f), 0.45f * diceSlowness); // 걷어낸 속을 밀어낸다
        DropScrap("씨", seedM.color);                                           // 씨는 음식물쓰레기로
    }

    /// <summary>대파 편썰기, as it's done (영상 6-8번): stood lengthwise (pointing
    /// back up the board), one cut down the middle along its length, the core
    /// - the inner tube - slid out (a scrap), the opened layers pressed flat,
    /// then cut lengthwise into 4 strips, turned, and cut once across: flat
    /// rectangles, white and green.</summary>
    private IEnumerator LeekPrep(SliceJob job, Vector3 handBase, HandController handCtl, ToolItem knife)
    {
        if (!IsTrimmed) yield return LeekTrim(job, handBase, handCtl, knife); // 안 다듬었으면 다듬기 동작부터 이어서
        else rend.enabled = false;                                            // 다듬어 둔 상태면 통째 모델만 숨긴다
        if (job.trimOnly) yield break;                                        // 다듬기 버튼: 펼친 대파를 도마에 남긴다
        yield return LeekSlice(job, handBase, handCtl, knife);                // 펼친 대파를 줄로 자르고 가로질러 썬다
    }

    /// <summary>대파 다듬기: 양 끝을 떼고 겉잎을 벗기고, 길이로 칼집을 한 번 넣어 가운데
    /// 심을 뽑아낸 뒤 벌어진 켜를 눌러 한 장으로 펼친다. 펼친 대파(Piece_Trimmed)가
    /// 남아 바로 썰 수 있다 - 썰기의 앞단계가 그대로 다듬기다.</summary>
    private IEnumerator LeekTrim(SliceJob job, Vector3 handBase, HandController handCtl, ToolItem knife)
    {
        float d = job.d, r = d * 0.5f, L = job.L;
        Material outer = SolidMaterial(new Color(0.86f, 0.93f, 0.78f), 0.5f);
        Material inner = SolidMaterial(new Color(0.85f, 0.9f, 0.45f), 0.5f);
        Material green = SolidMaterial(new Color(0.3f, 0.58f, 0.18f), 0.5f);
        Material white = SolidMaterial(new Color(0.94f, 0.96f, 0.88f), 0.5f);

        // 위아래(양 끝)를 떼고 겉잎을 벗긴다 - 야채는 모두 끝을 잘라내고 시작한다.
        yield return DiceChop(job.rig, handBase, -L * 0.42f, handCtl, knife);
        DropScrap("자투리", green.color);
        yield return DiceChop(job.rig, handBase, L * 0.42f, handCtl, knife);
        DropScrap("겉잎", outer.color);
        rend.enabled = false;

        // A tube along x (as it lay), with its core inside...
        Transform tube = Cylinder(job.rig, "LeekTube", new Vector3(0f, r, 0f), new Vector3(d, L * 0.5f, d), outer);
        Transform coreT = Cylinder(job.rig, "LeekCore", new Vector3(0f, r, 0f), new Vector3(d * 0.45f, L * 0.5f * 1.02f, d * 0.45f), inner);
        // ...stood lengthwise: turned to point back up the board (the knife's line).
        Quaternion along = Quaternion.AngleAxis(90f, Vector3.forward), back = Quaternion.AngleAxis(90f, Vector3.right);
        float dur = 0.5f * diceSlowness;
        for (float e = 0f; e < dur; e += Time.deltaTime)
        {
            Quaternion q = Quaternion.Slerp(along, back, Mathf.SmoothStep(0f, 1f, e / dur));
            tube.localRotation = q;
            coreT.localRotation = q;
            yield return null;
        }
        tube.localRotation = back;
        coreT.localRotation = back;

        // One cut down the middle, along its length.
        yield return DiceChop(job.rig, handBase, 0f, handCtl, knife);
        GameObject slit = Block(job.rig, "Slit", new Vector3(0f, d + 0.002f, 0f), new Vector3(0.006f, 0.004f, L * 0.95f), green);

        // The knife set aside to the right, out of the way...
        Vector3 aside = handBase + job.rig.right * (d + 0.55f);
        handCtl?.SlideTo(new Vector3(aside.x, aside.y, handBase.z), 0.35f * diceSlowness);
        yield return new WaitForSeconds(0.4f * diceSlowness);

        // ...the slit pulled open with the fingers: the leek spreads, the gap
        // widens and the core shows in it.
        Transform slitT = slit.transform;
        Vector3 tube0 = tube.localScale, core0 = coreT.localPosition;
        dur = 0.6f * diceSlowness;
        for (float e = 0f; e < dur; e += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, e / dur);
            tube.localScale = new Vector3(Mathf.Lerp(d, d * 1.5f, k), tube0.y, Mathf.Lerp(d, d * 0.75f, k));
            tube.localPosition = new Vector3(0f, Mathf.Lerp(r, r * 0.75f, k), 0f);
            slitT.localScale = new Vector3(Mathf.Lerp(0.006f, d * 0.55f, k), 0.004f, L * 0.95f);
            slitT.localPosition = new Vector3(0f, Mathf.Lerp(d, d * 0.76f, k) + 0.002f, 0f);
            coreT.localPosition = core0 + Vector3.up * (d * 0.3f * k);
            SupportHandController.Instance?.ShowNear(job.rig.TransformPoint(new Vector3(-d * 0.6f * k, d, 0f)));
            yield return null;
        }

        // The core pulled out of it, up the board - the other hand drawing it out.
        Vector3 coreFrom = coreT.localPosition, coreTo = coreFrom + new Vector3(0f, d * 0.2f, L * 1.1f);
        dur = 0.8f * diceSlowness;
        for (float e = 0f; e < dur; e += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, e / dur);
            coreT.localPosition = Vector3.Lerp(coreFrom, coreTo, k);
            SupportHandController.Instance?.ShowNear(job.rig.TransformPoint(coreT.localPosition + new Vector3(0f, d * 0.3f, L * 0.45f)));
            yield return null;
        }
        Destroy(coreT.gameObject);
        DropScrap("심", inner.color);

        // Opened out and pressed flat: the layers as one flat sheet.
        float W = Mathf.PI * r, sheet = 0.014f;
        Destroy(slit);
        var flat = Block(job.rig, "LeekSheet", new Vector3(0f, r, 0f), new Vector3(d, d, L), white).transform;
        Destroy(tube.gameObject);
        dur = 0.6f * diceSlowness;
        for (float e = 0f; e < dur; e += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, e / dur);
            flat.localScale = new Vector3(Mathf.Lerp(d, W, k), Mathf.Lerp(d, sheet, k), L);
            flat.localPosition = new Vector3(0f, Mathf.Lerp(r, sheet * 0.5f, k), 0f);
            yield return null;
        }
        flat.name = "Piece_Trimmed"; // 다듬어 펼친 대파 (다듬기만 할 때 도마에 남는다)
    }

    /// <summary>대파 썰기: 펼쳐 둔 대파를 길이로 4줄 자르고, 네 줄을 한 번에 돌려
    /// 가로질러 한 번 썰어 규격대로 네모난 편을 만든다.</summary>
    private IEnumerator LeekSlice(SliceJob job, Vector3 handBase, HandController handCtl, ToolItem knife)
    {
        float r = job.d * 0.5f, L = job.L;
        float W = Mathf.PI * r, sheet = 0.014f;                               // 펼쳤을 때의 폭 · 두께
        Material green = SolidMaterial(new Color(0.3f, 0.58f, 0.18f), 0.5f);  // 푸른 잎 쪽
        Material white = SolidMaterial(new Color(0.94f, 0.96f, 0.88f), 0.5f); // 흰 대 쪽
        Transform trimmed = job.rig.Find("Piece_Trimmed");
        if (trimmed != null) Destroy(trimmed.gameObject);                     // 펼친 대파를 줄 조각들로 바꾼다

        // Strips side by side (white toward the root, green toward the leaves -
        // each strip its own shade), then cut: 4 lengthwise...
        const int strips = 2; // 세로로 가르는 줄 수 (줄 x 2조각 - 접시에 너무 많아 절반으로)
        float sw = W / strips;
        var boxes = new List<Transform>();
        for (int i = 0; i < strips; i++)
        {
            Material m = new Material(white);
            m.color = Color.Lerp(white.color, green.color, (float)i / (strips - 1) * 0.85f);
            boxes.Add(Block(job.rig, "Strip", new Vector3(-W * 0.5f + sw * (i + 0.5f), sheet * 0.5f, 0f), new Vector3(sw * 0.98f, sheet, L), m).transform);
            boxes[i].gameObject.SetActive(false);
        }
        GameObject whole = Block(job.rig, "LeekSheet", new Vector3(0f, sheet * 0.5f, 0f), new Vector3(W, sheet, L), white);
        for (int i = 1; i < strips; i++)
        {
            yield return DiceChop(job.rig, handBase, -W * 0.5f + sw * i, handCtl, knife);
            if (i == 1) { Destroy(whole); foreach (Transform b in boxes) b.gameObject.SetActive(true); }
            foreach (Transform b in boxes)
                b.localPosition += Vector3.right * (b.localPosition.x > -W * 0.5f + sw * i ? 0.003f : -0.003f);
        }

        // ...turned a quarter round, and once across.
        var pivot = new GameObject("Turn").transform;
        pivot.SetParent(job.rig, false);
        foreach (Transform b in boxes) b.SetParent(pivot, true);
        float dur = 0.6f * diceSlowness; // (다듬기와 썰기를 나눠서 여기서 선언한다)
        for (float e = 0f; e < dur; e += Time.deltaTime)
        {
            pivot.localRotation = Quaternion.AngleAxis(90f * Mathf.SmoothStep(0f, 1f, e / dur), Vector3.up);
            yield return null;
        }
        pivot.localRotation = Quaternion.AngleAxis(90f, Vector3.up);
        foreach (Transform b in boxes)
        {
            b.SetParent(job.rig, true);
            Vector3 s = b.localScale;
            b.localRotation = Quaternion.identity;
            b.localScale = new Vector3(s.z, s.y, s.x);
        }
        Destroy(pivot.gameObject);

        yield return DiceChop(job.rig, handBase, 0f, handCtl, knife);
        float spread = job.pieceW * 1.08f / Mathf.Max(sw, 1e-4f); // 조각이 굵어진 만큼 줄 간격도 벌린다 (8%는 조각 사이 칼금)
        int n = 0;
        foreach (Transform b in boxes)
        {
            Vector3 p = b.localPosition;
            Material m = b.GetComponent<Renderer>().sharedMaterial;
            foreach (float side in new[] { -1f, 1f })
            {
                // 조각은 규격대로: 길이(X) x 두께(Y) x 폭(Z)
                GameObject piece = Block(job.rig, $"Piece_{n++}", new Vector3(p.x + side * (job.pieceL * 0.5f + 0.004f), job.t * 0.5f, p.z * spread),
                    new Vector3(job.pieceL, job.t, job.pieceW), m);
                piece.transform.localRotation = Quaternion.AngleAxis(Random.Range(-4f, 4f), Vector3.up);
                job.stack.Add(piece.transform);
            }
            Destroy(b.gameObject);
        }
        job.cutX = null; // cut where it lies - no fan
    }


    /// <summary>A box of meat while it's being cut: its size along the rig's axes.</summary>
    private sealed class MeatBox
    {
        public Transform t;
        public Vector3 size;
    }

    // ---- 고기 채썰기 (4×1×1 cm): real cuts through the model ----
    //
    // Board frame (the cutting rig): X across the board = the strips' width,
    // Y up off the board = their thickness, Z front-back = their length.
    // The model itself is cut (MeshSlicer): every part keeps the meat's own
    // surfaces and texture, and the cuts' faces get the cut-face material.

    [Header("고기 채썰기")]
    [Tooltip("다듬기 for meat: the ends cut off so it's this long (Z, cm) - the strips' length.")]
    [SerializeField] private float meatTrimLengthCm = 4f;
    /// <summary>The ends already cut to length (by 다듬기) - the cut doesn't do it again.</summary>
    private bool meatEndsTrimmed;
    private bool cutAborted;
    private const float SizeTolerance = 0.15f; // (of a spec size: what still counts as that size)

    private sealed class MeatChunk
    {
        public SliceMesh mesh;
        public GameObject go;
    }

    private sealed class MeatHolder { public MeatChunk block; }

    /// <summary>고기 채썰기, as it's done (first figure): ① the front and back ends cut
    /// off so it's the spec's length along Z (4 cm; skipped if 다듬기 did it);
    /// ② 포 뜨기 - the left hand pressing it down, the knife laid flat (parallel
    /// to the board) drawn in from the right to the left at 1 cm and 2 cm off
    /// the board: 3 sheets of 1 cm, left lying on one another; ③ the knife up
    /// again, down through all three at every 1 cm across X: strips 4 × 1 × 1 cm
    /// (5 cm wide, 3 thick → 15). Sizes off the spec by more than 15 % aren't
    /// forced: a thicker top / wider edge is cut off and kept apart (a scrap /
    /// a leftover strip). Each strip its own object: its mesh, a collider, its
    /// share of the amount (FoodPiece), moving on its own in a pan.</summary>
    private IEnumerator MeatStripsPrep(SliceJob job, CutSpec spec, Vector3 handBase, HandController handCtl, ToolItem knife)
    {
        float slowBefore = diceSlowness;
        diceSlowness *= 1.3f; // (every chop and pass below)
        Transform rig = job.rig;
        var chunks = new List<MeatChunk>();
        try
        {
            float wpc = worldPerCm;
            // A strip is long one way and thin the other two: the longest size
            // given is its length, whichever box it was written in (가로 4 ·
            // 세로 1 · 두께 1 is the same 4 cm strip - read as a 4 cm width it
            // cut the meat into a few thick slabs, still in one lump).
            if (spec.widthCm > spec.lengthCm) spec = new CutSpec(spec.lengthCm, spec.widthCm, spec.thicknessCm);
            if (spec.thicknessCm > spec.lengthCm) spec = new CutSpec(spec.widthCm, spec.thicknessCm, spec.lengthCm);
            float lengthW = (spec.lengthCm > 0f ? spec.lengthCm : meatTrimLengthCm) * wpc;  // Z
            float wideW = (spec.widthCm > 0f ? spec.widthCm : 1f) * wpc;                     // X
            float thickW = (spec.thicknessCm > 0f ? spec.thicknessCm : 1f) * wpc;            // Y
            float slow = diceSlowness;
            Renderer knifeR = knife != null ? knife.GetComponentInChildren<Renderer>() : null;
            Material[] mats = MeatMaterials();

            var start = new MeatChunk { mesh = SliceMesh.From(GetComponent<MeshFilter>(), rig) };
            float gramsPerVolume = amountGrams / Mathf.Max(start.mesh.Volume, 1e-6f);
            ShowChunk(start, rig, mats);
            rend.enabled = false;
            chunks.Add(start);

            // ① the ends, to length
            if (!meatEndsTrimmed)
            {
                var held = new MeatHolder { block = start };
                yield return MeatEnds(rig, held, lengthW, gramsPerVolume, mats, handBase, handCtl, knife, knifeR, slow);
                if (held.block == null)
                {
                    // too short: told so, nothing cut
                    foreach (MeatChunk c in chunks) if (c.go != null) Destroy(c.go);
                    rend.enabled = true;
                    cutAborted = true;
                    yield break;
                }
                chunks.Clear();
                chunks.Add(held.block);
            }

            // ② 포 뜨기: knife flat, right to left, at each sheet's top; the left hand on the meat
            Bounds b = chunks[0].mesh.Bounds;
            float H = b.size.y;
            int sheets = Mathf.Max(1, Mathf.FloorToInt((H + SizeTolerance * thickW) / thickW));
            var cuts = new List<float>();
            if (H - sheets * thickW > SizeTolerance * thickW) cuts.Add(b.min.y + sheets * thickW); // a thicker top: off first, kept apart
            for (int k = sheets - 1; k >= 1; k--) cuts.Add(b.min.y + k * thickW);
            // (the size it should be by now: 4 × 4 × 3 cm for 1 × 1 × 4 strips -
            // not stretched to fit: a model off that is said, it's a setting to fix)
            CheckMeatSize(b, wideW, thickW, lengthW);

            // [준비] the knife out to the right of the meat, turned flat (blade
            // left, toward the meat's right side); the left hand flat on top.
            float bladeRefX = rig.position.x;
            if (cuts.Count > 0)
            {
                yield return StepGate("② 칼을 고기 오른쪽으로 빼기");
                handCtl?.SlideTo(handBase + Vector3.right * (ScreenRightX * (b.extents.x + 0.45f)), 0.3f * slow); // 화면 오른쪽 바깥으로
                yield return new WaitForSeconds(0.3f * slow);
                yield return StepGate("② 칼을 눕히기 · 왼손 바닥으로 누르기");
                if (knife != null) yield return knife.TurnFlatAlongDepth(0.35f * slow);
                PalmOnTop(PalmPoint(rig, b));
            }
            float topLeftover = cuts.Count > 0 && H - sheets * thickW > SizeTolerance * thickW ? cuts[0] : float.NaN;
            int pass = 0;
            foreach (float y in cuts)
            {
                // lined up at the cut's height outside the meat, drawn through right
                // to left - the sheet parts as the blade comes out the far side -
                // and taken back out to the right at the same height
                yield return StepGate($"② {++pass}번째 포뜨기 (밑면에서 {(y - b.min.y) / wpc:0.#}cm)");
                yield return KnifeFlatFromRight(rig, b, y, handCtl, knifeR, slow);
                SplitChunks(chunks, 1, y, rig, mats);
                yield return KnifeFlatWithdraw(rig, b, y, handCtl, knifeR, slow);
                if (!float.IsNaN(topLeftover) && Mathf.Approximately(y, topLeftover))
                {
                    MeatChunk top = chunks.Find(c => c.mesh.Bounds.min.y >= y - 1e-4f);
                    if (top != null)
                    {
                        chunks.Remove(top);
                        float topGrams = Mathf.Min(gramsPerVolume * top.mesh.Volume, amountGrams);
                        yield return SlideChunkAway(top, rig, new Vector3(0f, 0.05f, -0.3f), 0.4f * slow);
                        DropScrap("자투리", OwnColor, topGrams);
                        amountGrams -= topGrams;
                    }
                }
            }
            // ③ [준비] the left hand lets go; the knife, out on the right, turned
            //    back and brought up upright (blade down, its flat side across
            //    the board, the cut line front-back); the left hand's fingers
            //    curled, holding the meat left of the cut.
            yield return StepGate("③ 왼손 떼기 · 칼 세우기");
            SupportHandController.Instance?.Hide();
            b = UnionBounds(chunks);
            handCtl?.SlideTo(handBase + Vector3.right * (b.extents.x + 0.45f) + Vector3.up * 0.2f, 0.25f * slow);
            if (knife != null && knife.HoldTurn != Quaternion.identity) yield return knife.UnturnFlat(0.25f * slow);
            knife?.RaiseUpright(0.3f * slow);
            yield return new WaitForSeconds(0.35f * slow);
            float W = b.size.x;
            int columns = Mathf.Max(1, Mathf.FloorToInt((W + SizeTolerance * wideW) / wideW));
            // from the right end in, 1 cm at a time (the knife moving left)
            var lines = new List<float>();
            for (int k = 1; k < columns; k++) lines.Add(b.max.x - k * wideW);
            bool leftoverStrip = W - columns * wideW > SizeTolerance * wideW;
            if (leftoverStrip) lines.Add(b.max.x - columns * wideW);
            int chop = 0;
            foreach (float x in lines)
            {
                yield return StepGate($"③ {++chop}번째 수직 썰기 (오른쪽 끝에서 {(b.max.x - x) / wpc:0.#}cm)");
                SupportHandController.Instance?.ShowNear(rig.TransformPoint(new Vector3(Mathf.Max(b.min.x + wideW * 0.5f, x - wideW * 1.6f), b.max.y, b.center.z)));
                float cutX = x;
                yield return VerticalCut(bladeRefX, handBase, rig.TransformPoint(new Vector3(cutX, b.max.y, b.center.z)).x, handCtl, knife, slow,
                    () => SplitChunks(chunks, 0, cutX, rig, mats));
            }
            yield return StepGate("③ 칼 들기 · 왼손 떼기");
            SupportHandController.Instance?.Hide();

            // the strips: each its own piece, a hair apart, each with its share
            FoodPieceGroup group = GetComponent<FoodPieceGroup>();
            if (group == null) group = gameObject.AddComponent<FoodPieceGroup>();
            Transform holder = PieceHolder.For(transform);
            chunks.Sort((p, q) =>
            {
                Bounds bp = p.mesh.Bounds, bq = q.mesh.Bounds;
                int c = bp.center.x.CompareTo(bq.center.x);
                return c != 0 ? c : bp.center.y.CompareTo(bq.center.y);
            });
            int n = 0, rest = 0;
            float piecesGrams = 0f;
            // what's left after the scraps, shared out by volume - all of it, exactly
            float piecesVolume = 0f;
            foreach (MeatChunk c in chunks) piecesVolume += c.mesh.Volume;
            gramsPerVolume = amountGrams / Mathf.Max(piecesVolume, 1e-9f);
            foreach (MeatChunk c in chunks)
            {
                Bounds cb = c.mesh.Bounds;
                int col = Mathf.Clamp(Mathf.FloorToInt((cb.center.x - b.min.x) / wideW), 0, columns);
                c.go.transform.localPosition += Vector3.right * (0.004f * col); // (just apart along the cuts)
                c.go.name = $"Piece_{n++}";
                bool leftover = leftoverStrip && col >= columns;
                if (leftover) rest++;
                BoxCollider box = c.go.AddComponent<BoxCollider>();
                box.center = Vector3.zero;
                box.size = cb.size;
                float grams = gramsPerVolume * c.mesh.Volume;
                piecesGrams += grams;
                FoodPiece fp = c.go.AddComponent<FoodPiece>();
                fp.Init(this, grams, 0f, cb.size);
                c.go.AddComponent<CutResultPiece>().Init(ingredientName, AppliedCut,
                    new CutSpec(cb.size.x / wpc, cb.size.z / wpc, cb.size.y / wpc));
                group.Add(fp);
                c.go.transform.SetParent(holder, true);
            }
            // the original's box no longer covers anything: the pieces' own do
            if (GetComponent<Collider>() is Collider own) own.enabled = false;
            amountGrams = piecesGrams;
            job.cutX = null;

            // Left where they were cut, as on the board (the 채 standing side by
            // side) - or, with spreadMeatStrips, spread out flat in rows.
            if (spreadMeatStrips)
            {
                float step = wideW + 0.4f * wpc, rowStep = b.size.z + 0.6f * wpc;
                int perRow = Mathf.Max(1, Mathf.FloorToInt(14f * wpc / step));
                int count = chunks.Count, rowsNeeded = Mathf.CeilToInt(count / (float)perRow);
                var moves = new List<(Transform t, Vector3 from, Vector3 to)>();
                for (int i = 0; i < count; i++)
                {
                    Renderer r = chunks[i].go.GetComponent<Renderer>();
                    if (r == null) continue;
                    int row = i / perRow, inRow = Mathf.Min(perRow, count - row * perRow);
                    Vector3 now = rig.InverseTransformPoint(r.bounds.center);
                    float h = chunks[i].mesh.Bounds.size.y;
                    Vector3 local = new Vector3(b.center.x + ((i % perRow) - (inRow - 1) * 0.5f) * step,
                                                b.min.y + h * 0.5f,
                                                now.z + (row - (rowsNeeded - 1) * 0.5f) * rowStep);
                    Vector3 delta = rig.TransformPoint(local) - r.bounds.center;
                    Transform tr = chunks[i].go.transform;
                    moves.Add((tr, tr.position, tr.position + delta));
                }
                for (float t = 0f; t < 0.35f * slow; t += Time.deltaTime)
                {
                    float k = Mathf.SmoothStep(0f, 1f, t / (0.35f * slow));
                    foreach (var m in moves) if (m.t != null) m.t.position = Vector3.Lerp(m.from, m.to, k);
                    yield return null;
                }
                foreach (var m in moves) if (m.t != null) m.t.position = m.to;
            }
            Debug.Log($"[고기 채썰기] 길이 {(b.size.z / wpc):0.#}cm × 폭 {(W / wpc):0.#}cm × 두께 {(H / wpc):0.#}cm → 판 {sheets}장 × {columns}줄 = {n - rest}조각" +
                      $"{(rest > 0 ? $" + 나머지 {rest}조각" : "")} ({wideW / wpc:0.#} × {thickW / wpc:0.#} × {b.size.z / wpc:0.#} cm), 조각 합 {piecesGrams:0.#}g");
        }
        finally
        {
            diceSlowness = slowBefore;
        }
    }

    /// <summary>다듬기 for meat: step ① on its own - the ends cut off so it's
    /// meatTrimLengthCm long (Z); the meat stays one piece, shorter, its cut
    /// faces showing (the model's mesh replaced with the cut one).</summary>
    private IEnumerator MeatTrimRoutine()
    {
        isCutting = true;
        HandController handCtl = HandController.Instance;
        ToolItem knife = HandCarry.Instance?.Current as ToolItem;
        Vector3 handBase = handCtl != null ? handCtl.RestPosition : Vector3.zero;
        yield return LayFlatOnBoard(0.35f * diceSlowness); // 기울어진 자세로 재지 않게 먼저 도마에 바로 눕힌다
        var rig = new GameObject("TrimRig").transform;
        rig.SetParent(PieceHolder.For(transform), false);
        rig.SetPositionAndRotation(VisibleBounds.center, BoardFrame());
        float knifeHoldBefore = knife != null ? knife.ChopHoldSeconds : 0f;
        if (knife != null && handCtl != null)
        {
            knife.PlayChopMotion();
            yield return new WaitForSeconds(0.08f);
            if (BladeXAt(knife, rig.position.y, out float bladeX)) handBase.x += rig.position.x - bladeX;
        }
        Material[] mats = MeatMaterials();
        var start = new MeatChunk { mesh = SliceMesh.From(GetComponent<MeshFilter>(), rig) };
        float gramsPerVolume = amountGrams / Mathf.Max(start.mesh.Volume, 1e-6f);
        ShowChunk(start, rig, mats);
        rend.enabled = false;
        var held = new MeatHolder { block = start };
        yield return MeatEnds(rig, held, meatTrimLengthCm * worldPerCm, gramsPerVolume, mats, handBase, handCtl, knife,
            knife != null ? knife.GetComponentInChildren<Renderer>() : null, diceSlowness);
        if (held.block != null)
        {
            // the shorter block becomes the model (in its own space), cut faces and all
            MeatChunk kept = held.block;
            Mesh local = new Mesh { name = sharedMesh != null ? sharedMesh.name + "_다듬음" : "Meat_Trimmed" };
            var lv = new List<Vector3>();
            var ln = new List<Vector3>();
            for (int i = 0; i < kept.mesh.v.Count; i++)
            {
                lv.Add(transform.InverseTransformPoint(rig.TransformPoint(kept.mesh.v[i])));
                ln.Add(transform.InverseTransformDirection(rig.TransformDirection(kept.mesh.n[i])).normalized);
            }
            local.SetVertices(lv);
            local.SetNormals(ln);
            local.SetUVs(0, kept.mesh.uv);
            local.subMeshCount = kept.mesh.sub.Count;
            for (int s = 0; s < kept.mesh.sub.Count; s++) local.SetTriangles(kept.mesh.sub[s], s);
            local.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = local;
            sharedMesh = local;
            rend.sharedMaterials = mats;
            if (GetComponent<Collider>() is BoxCollider box) { box.center = local.bounds.center; box.size = local.bounds.size; }
            Destroy(kept.go);
            meatEndsTrimmed = true;
            IsTrimmed = true;
            Debug.Log($"{ingredientName}: 다듬기 완료 - 앞뒤 끝 잘라 길이 {meatTrimLengthCm:0.#}cm (남은 양 {amountGrams:0.#}g)");
        }
        rend.enabled = true;
        Destroy(rig.gameObject);
        handCtl?.SlideTo(handBase, 0.15f);
        if (knife != null) knife.ChopHoldSeconds = knifeHoldBefore;
        SupportHandController.Instance?.Hide();
        isCutting = false;
    }

    /// <summary>Step ①: the front and back ends (Z) cut off evenly so what's left
    /// is lengthW long - each end a scrap of its own (food waste), with its share
    /// of the amount. Too short for it: said so, and held.block comes back null.</summary>
    private IEnumerator MeatEnds(Transform rig, MeatHolder held, float lengthW, float gramsPerVolume, Material[] mats,
        Vector3 handBase, HandController handCtl, ToolItem knife, Renderer knifeR, float slow)
    {
        Bounds b = held.block.mesh.Bounds;
        float L = b.size.z;
        if (L + 0.05f * worldPerCm < lengthW)
        {
            Notice($"고기 길이가 {L / worldPerCm:0.0}cm라 {lengthW / worldPerCm:0.#}cm로 다듬을 수 없어요 ({(lengthW - L) / worldPerCm:0.0}cm 부족)");
            held.block = null;
            yield break;
        }
        float end = (L - lengthW) * 0.5f;
        if (end < 0.05f * worldPerCm) yield break; // already that long
        float bladeRefX = rig.position.x; // (where handBase puts the upright blade - before the meat is turned)
        if (knife != null) knife.ChopHoldSeconds = 4f * slow;

        // No room to turn it a quarter round (others close by on the board): it
        // isn't turned - it keeps lying as it is, and the ends come off with the
        // knife drawn along them instead.
        Vector3 bc = rig.TransformPoint(b.center);
        float turnSide = Mathf.Max(b.size.x, b.size.z) * 1.05f;
        bool canTurn = !(CuttingBoardController.Instance is CuttingBoardController board) || board.Items.Count < 2
                       || board.IsClear(new Rect(bc.x - turnSide * 0.5f, bc.y - turnSide * 0.5f, turnSide, turnSide), this, 0f);
        if (!canTurn)
        {
            Debug.Log("[고기 다듬기] 돌릴 공간이 없어 돌리지 않고 양 끝을 자름");
            if (knife != null) knife.LayFlatInHand();
            yield return null;
            var along = new List<MeatChunk> { held.block };
            foreach (float sign in new[] { 1f, -1f })
            {
                float z = sign > 0f ? b.max.z - end : b.min.z + end;
                SupportHandController.Instance?.ShowNear(rig.TransformPoint(new Vector3(b.center.x - b.extents.x * 0.4f, b.max.y, b.center.z)));
                yield return KnifeAlongPass(rig, b.size.x, new Vector3(b.center.x, b.center.y, z), handCtl, knifeR, slow);
                SplitChunks(along, 2, z, rig, mats);
                MeatChunk cut = along.Find(c => sign > 0f ? c.mesh.Bounds.min.z >= z - 1e-4f : c.mesh.Bounds.max.z <= z + 1e-4f);
                if (cut == null) continue;
                along.Remove(cut);
                float g = gramsPerVolume * cut.mesh.Volume;
                yield return SlideChunkAway(cut, rig, new Vector3(0f, 0.03f, sign * 0.3f), 0.4f * slow);
                DropScrap("자투리", OwnColor, g);
                amountGrams = Mathf.Max(0f, amountGrams - g);
            }
            SupportHandController.Instance?.Hide();
            handCtl?.SlideTo(handBase, 0.2f * slow);
            held.block = along.Count > 0 ? along[0] : null;
            meatEndsTrimmed = true;
            yield break;
        }

        // 1-2) The knife up and out of the way; the left hand takes the meat by
        //      its middle and turns it a quarter round, flat on the board (its
        //      bottom stays down) - its length (Z) now across the board (X).
        yield return StepGate("① 칼 들어 올리기");
        handCtl?.SlideTo(handBase + Vector3.right * (b.extents.x + 0.35f) + Vector3.up * 0.25f, 0.3f * slow);
        knife?.RaiseUpright(0.3f * slow);
        yield return new WaitForSeconds(0.35f * slow);
        yield return StepGate("① 고기를 수평으로 90도 돌리기");
        yield return TurnMeat(rig, b, 90f, 0.6f * slow);

        // 5-10) Each end: the knife upright over the cut line, down through it,
        //       back up above the meat; the left hand on the middle, away from the cut.
        var chunks = new List<MeatChunk> { held.block };
        var ends = new List<MeatChunk>();
        foreach (float sign in new[] { 1f, -1f }) // the back end (now on the right), then the front one
        {
            float z = sign > 0f ? b.max.z - end : b.min.z + end;
            yield return StepGate(sign > 0f ? "① 첫 번째 끝 자르기" : "① 반대쪽 끝 자르기");
            SupportHandController.Instance?.ShowNear(rig.TransformPoint(new Vector3(b.center.x, b.max.y, b.center.z - sign * b.extents.z * 0.35f)));
            Vector3 line = rig.TransformPoint(new Vector3(b.center.x, b.max.y, z));
            yield return VerticalCut(bladeRefX, handBase, line.x, handCtl, knife, slow, () =>
            {
                SplitChunks(chunks, 2, z, rig, mats);
                MeatChunk cut = chunks.Find(c => sign > 0f ? c.mesh.Bounds.min.z >= z - 1e-4f : c.mesh.Bounds.max.z <= z + 1e-4f);
                if (cut != null) { chunks.Remove(cut); ends.Add(cut); }
            });
        }

        // 11) The knife up and out of the way (to the right, above); the two end
        //     pieces taken off the body - scraps.
        yield return StepGate("① 칼 빼고 자투리 떼어내기");
        handCtl?.SlideTo(handBase + Vector3.right * (b.extents.z + 0.35f) + Vector3.up * 0.25f, 0.3f * slow);
        yield return new WaitForSeconds(0.3f * slow);
        foreach (MeatChunk cut in ends)
        {
            float grams = gramsPerVolume * cut.mesh.Volume;
            float side = cut.mesh.Bounds.center.z > b.center.z ? 1f : -1f;
            yield return SlideChunkAway(cut, rig, new Vector3(0f, 0.03f, side * 0.3f), 0.35f * slow);
            DropScrap("자투리", OwnColor, grams);
            amountGrams = Mathf.Max(0f, amountGrams - grams);
        }

        // 12) Turned back the quarter round: the 4 cm now front-back (Z), and
        //     it isn't turned again from here.
        yield return StepGate("① 고기를 원래 방향으로 돌려놓기");
        SupportHandController.Instance?.ShowNear(rig.TransformPoint(b.center + Vector3.up * b.extents.y));
        yield return TurnMeat(rig, b, -90f, 0.6f * slow);
        SupportHandController.Instance?.Hide();
        held.block = chunks.Count > 0 ? chunks[0] : null;
        meatEndsTrimmed = true;
        Debug.Log($"[고기 다듬기] 고기를 돌려 양 끝 각 {end / worldPerCm:0.0}cm 잘라 길이 {(held.block != null ? held.block.mesh.Bounds.size.z / worldPerCm : 0f):0.0}cm");
    }

    // ---- 고기 썰기: poses and cuts ----

    [Tooltip("고기 썰기를 한 단계씩: each pose stops until N is pressed (to check the poses).")]
    [SerializeField] private bool meatCutStepMode;

    /// <summary>Each step of the meat cut, named in the console; in step mode
    /// it waits here until N is pressed.</summary>
    private IEnumerator StepGate(string step)
    {
        Debug.Log($"[고기 썰기 단계] {step}");
        if (!meatCutStepMode) yield break;
        var kb = UnityEngine.InputSystem.Keyboard.current;
        yield return null;
        stepAdvance = false;
        while (meatCutStepMode && !stepAdvance && kb != null && !kb.nKey.wasPressedThisFrame) yield return null;
        stepAdvance = false;
    }

    /// <summary>단계 모드에서 N 키 대신 다음 단계로 (에디터 테스트용, 리플렉션으로 설정).</summary>
    private static bool stepAdvance;

    /// <summary>The meat turned flat on the board by degrees (about the board's
    /// up through its middle - not tipped over), the left hand holding it by its
    /// middle as it goes. The knife is out of it meanwhile.</summary>
    private IEnumerator TurnMeat(Transform rig, Bounds b, float degrees, float seconds)
    {
        Vector3 pivot = rig.TransformPoint(b.center);
        Vector3 up = Carryable.CounterUp;
        Quaternion r0 = rig.rotation;
        Vector3 p0 = rig.position;
        for (float e = 0f; e < seconds; e += Time.deltaTime)
        {
            Quaternion turn = Quaternion.AngleAxis(degrees * Mathf.SmoothStep(0f, 1f, e / seconds), up);
            rig.SetPositionAndRotation(pivot + turn * (p0 - pivot), turn * r0);
            SupportHandController.Instance?.ShowNear(rig.TransformPoint(b.center + Vector3.up * b.extents.y));
            yield return null;
        }
        Quaternion end = Quaternion.AngleAxis(degrees, up);
        rig.SetPositionAndRotation(pivot + end * (p0 - pivot), end * r0);
    }

    /// <summary>One cut straight down (the knife upright): moved over the line
    /// (world x) above the meat, down through it to the board - the pieces part
    /// right there (atBottom) - and back up above it before it goes anywhere else.</summary>
    private IEnumerator VerticalCut(float bladeRefX, Vector3 handBase, float lineX, HandController handCtl, ToolItem knife, float slow, System.Action atBottom)
    {
        handCtl?.SlideTo(handBase + Vector3.right * (lineX - bladeRefX), 0.3f * slow);
        yield return new WaitForSeconds(0.4f * slow);       // (over the line, up)
        handCtl?.PlayChopGesture();
        knife?.PlayChopMotion();                              // (down...)
        yield return new WaitForSeconds(0.12f);
        atBottom?.Invoke();                                   // (...through to the board)
        if (IsOnCuttingBoard) CookingMess.Chop(OwnColor);
        yield return new WaitForSeconds(0.35f * slow);      // (and up again)
    }

    /// <summary>The left hand flat on top of the meat, palm down (포 뜨기): the
    /// fingers along it, not round its side where the knife comes in.</summary>
    private static void PalmOnTop(Vector3 topPoint)
    {
        SupportHandController s = SupportHandController.Instance;
        if (s == null) return;
        Vector3 up = Carryable.CounterUp;
        Vector3 fingers = Vector3.ProjectOnPlane(new Vector3(0.35f, 1f, 0f), up).normalized;
        Vector3 thumb = Quaternion.AngleAxis(90f, up) * fingers;
        s.BeginHolding(fingers, thumb, 0.08f);
        s.HoldKnucklesAt(topPoint + up * 0.03f);
    }

    /// <summary>The knife lying flat (parallel to the board), its leading end
    /// brought in from the right side of the meat at height y (rig) and drawn
    /// through to the left and out - steered every frame by where the blade
    /// actually is; the left hand flat on top of the meat meanwhile.</summary>
    // ---- 포 뜨기 동작 설정 (Inspector에서 조정) ----
    [Header("포 뜨기 (칼 앞뒤 왕복 + 왼쪽 전진)")]
    [Tooltip("앞뒤 왕복 거리 (cm, 도마 위 실제 거리): 시작점 → 앞으로 이만큼 → 다시 시작점. ±가 아니라 전체 범위.")]
    [SerializeField] private float poStrokeCm = 5f;
    [Tooltip("왕복 한 번(앞으로 갔다 돌아오기)에 걸리는 시간 (초).")]
    [SerializeField] private float poStrokeSeconds = 0.7f;
    [Tooltip("왼쪽으로 전진하는 속도 (cm/초, 도마 위 실제 거리).")]
    [SerializeField] private float poAdvanceCmPerSecond = 2.2f;
    [Tooltip("칼 길이 중 고기 가운데에 맞출 지점 - 칼끝에서부터의 비율 (0 = 칼끝, 1 = 손잡이 끝). 칼날 가운데쯤.")]
    [SerializeField, Range(0f, 1f)] private float poBladeAimFromTip = 0.3f;

    /// <summary>칼(눕혀서 길이가 도마 앞뒤 방향, TurnFlatAlongDepth)을 높이 y(rig)에
    /// 맞춰 고기 오른쪽 바깥에 대고, 앞뒤 왕복(poStrokeCm)을 하면서 동시에
    /// 왼쪽으로 전진시켜 칼날 왼쪽 끝이 고기 왼쪽 끝을 지나면 끝난다.
    /// 칼 높이는 내내 그대로 - 왕복은 칼 길이 방향(도마 앞뒤)으로만.
    /// 왼손은 고기 윗면에 고정 (칼을 따라 흔들리지 않음).</summary>
    private IEnumerator KnifeFlatFromRight(Transform rig, Bounds b, float y, HandController handCtl, Renderer knifeR, float slow)
    {
        float wpc = worldPerCm;
        float sx = ScreenRightX;                                                      // 화면 오른쪽이 월드 x 의 어느 쪽인가
        float right = rig.TransformPoint(new Vector3(sx > 0f ? b.max.x : b.min.x, y, b.center.z)).x; // 칼이 들어가는 화면 오른쪽 끝
        float left = rig.TransformPoint(new Vector3(sx > 0f ? b.min.x : b.max.x, y, b.center.z)).x;  // 칼이 빠져나오는 화면 왼쪽 끝
        Vector3 press = PalmPoint(rig, b);
        float startX = right + 0.04f * sx, endX = left - 0.06f * sx;                  // 바깥으로 조금씩 더

        // 1) 고기 오른쪽 바깥에서 높이·위치 맞추기 (칼이 고기 밖에 있을 때만 높이를 바꾼다)
        for (float e = 0f, dur = 0.4f * slow; e < dur; e += Time.deltaTime)
        {
            SteerFlatKnife(rig, b, y, startX, 0f, handCtl, knifeR, 0.06f);
            PalmOnTop(press);
            yield return null;
        }

        // 2) 앞뒤 왕복 + 왼쪽 전진 동시에
        float advance = Mathf.Max(0.1f, poAdvanceCmPerSecond) * wpc / Mathf.Max(slow, 0.01f);
        float period = Mathf.Max(0.1f, poStrokeSeconds) * slow;
        float stroke = poStrokeCm * wpc;
        float x = startX, t = 0f;
        int doneCycle = -1;
        while (true)
        {
            t += Time.deltaTime;
            int cycle = Mathf.FloorToInt(t / period);
            // 끝까지 통과한 뒤에는 지금 하던 왕복만 마저 하고 (시작점으로 돌아와서) 끝
            if (doneCycle >= 0 && cycle > doneCycle) break;
            x = Mathf.MoveTowards(x, endX, advance * Time.deltaTime);                 // 화면 왼쪽으로 전진 (월드 방향은 카메라가 정한다)
            // 0 → stroke → 0, 양 끝에서 부드럽게 방향 전환 (코사인)
            float saw = stroke * 0.5f * (1f - Mathf.Cos(t / period * Mathf.PI * 2f));
            SteerFlatKnife(rig, b, y, x, saw, handCtl, knifeR, 0.03f);
            PalmOnTop(press);
            // 칼의 '화면 왼쪽' 끝이 고기의 화면 왼쪽 끝을 지났나
            bool through = knifeR == null || (sx > 0f ? knifeR.bounds.min.x <= left - 0.02f : knifeR.bounds.max.x >= left + 0.02f);
            if (doneCycle < 0 && Mathf.Approximately(x, endX) && through) doneCycle = cycle;
            if (t > 30f) break; // (안전장치)
            yield return null;
        }
        SteerFlatKnife(rig, b, y, x, 0f, handCtl, knifeR, 0.05f);
    }

    /// <summary>포 뜨기 한 번 뒤: 같은 높이 그대로 칼을 오른쪽 바깥으로 완전히
    /// 빼낸다 (칼 전체가 고기에서 벗어날 때까지).</summary>
    private IEnumerator KnifeFlatWithdraw(Transform rig, Bounds b, float y, HandController handCtl, Renderer knifeR, float slow)
    {
        float sx = ScreenRightX;                                                      // 화면 오른쪽의 월드 x 부호
        float right = rig.TransformPoint(new Vector3(sx > 0f ? b.max.x : b.min.x, y, b.center.z)).x; // 빼낼 화면 오른쪽 바깥
        Vector3 press = PalmPoint(rig, b);
        float from = knifeR != null ? (sx > 0f ? knifeR.bounds.min.x : knifeR.bounds.max.x) : right; // 칼의 화면 왼쪽 끝
        for (float e = 0f, dur = 0.6f * slow; e < dur; e += Time.deltaTime)
        {
            float lead = Mathf.Lerp(from, right + 0.06f * sx, Mathf.SmoothStep(0f, 1f, e / dur));
            SteerFlatKnife(rig, b, y, lead, 0f, handCtl, knifeR, 0.03f);
            PalmOnTop(press);
            yield return null;
        }
    }

    /// <summary>화면 오른쪽이 월드 x 의 어느 쪽인지 (+1 / -1). 카메라가 180도 돌아
    /// 있어 월드 +x 가 화면 왼쪽이다 - 월드 x 를 화면 좌우로 그냥 쓰면 포 뜨기가
    /// 거울처럼 뒤집혀 왼쪽에서 오른쪽으로 썰게 된다.</summary>
    private static float ScreenRightX => Camera.main != null && Camera.main.transform.right.x < 0f ? -1f : 1f;

    /// <summary>왼손 누르는 자리: 고기 윗면, 칼이 들어오는 화면 오른쪽에서 먼 쪽.</summary>
    private static Vector3 PalmPoint(Transform rig, Bounds b) =>
        rig.TransformPoint(new Vector3(b.center.x + b.extents.x * 0.35f * -ScreenRightX, b.max.y, b.center.z));

    /// <summary>눕힌 칼을 손으로 움직여: 칼날 왼쪽 끝(진행 방향 끝)을 leadX(월드 x)에,
    /// 칼 길이의 조준점(poBladeAimFromTip)을 높이 y · 고기 앞뒤 가운데 + saw(앞으로,
    /// 도마 뒤쪽 방향)에 둔다. 화면(직교 카메라) 기준 x·y만 맞추고, 손 위치로 움직인다.</summary>
    private void SteerFlatKnife(Transform rig, Bounds b, float y, float leadX, float saw, HandController handCtl, Renderer knifeR, float seconds)
    {
        if (handCtl == null || knifeR == null) return;
        Bounds kb = knifeR.bounds;
        // 칼끝은 화면 위(max.y), 손잡이 끝은 아래(min.y)
        float aimY = Mathf.Lerp(kb.max.y, kb.min.y, poBladeAimFromTip);
        Vector3 target = rig.TransformPoint(new Vector3(b.center.x, y, b.center.z + saw));
        // (깊이 z도 절단면에 맞춘다 - 고기 안에 들어간 칼날은 윗장에 가려지고,
        // 윗면을 누르는 왼손이 칼 위에 보이게: 칼이 손·고기를 뚫고 지나가 보이지 않게)
        float leadEdge = ScreenRightX > 0f ? kb.min.x : kb.max.x; // 진행 방향(화면 왼쪽) 쪽 칼 끝 - 월드 x 의 어느 끝인지는 카메라가 정한다
        Vector3 err = new Vector3(leadX - leadEdge, target.y - aimY, target.z - kb.center.z);
        handCtl.SlideTo(handCtl.RestPosition + err, seconds);
    }

    /// <summary>What the strips are cut from should already be whole sizes of
    /// them (4 × 4 × 3 cm for 1 × 1 × 4): if the model isn't, it's said - a
    /// setting to fix - and cut as it is (never stretched to fit).</summary>
    private void CheckMeatSize(Bounds b, float wideW, float thickW, float lengthW)
    {
        float wpc = worldPerCm;
        float Off(float size, float unit) { float n = Mathf.Max(1f, Mathf.Round(size / unit)); return Mathf.Abs(size - n * unit) / unit; }
        bool lenOff = Mathf.Abs(b.size.z - lengthW) > SizeTolerance * lengthW;
        bool widthOff = Off(b.size.x, wideW) > SizeTolerance;
        bool heightOff = Off(b.size.y, thickW) > SizeTolerance;
        string size = $"가로(X) {b.size.x / wpc:0.##} × 세로(Z) {b.size.z / wpc:0.##} × 높이(Y) {b.size.y / wpc:0.##} cm";
        if (lenOff || widthOff || heightOff)
            Debug.LogWarning($"[고기 규격 확인] 다듬은 고기가 {size} - 채({wideW / wpc:0.#}×{thickW / wpc:0.#}×{lengthW / wpc:0.#}cm)로 나누어떨어지지 않음" +
                             $"{(widthOff ? " · 가로" : "")}{(heightOff ? " · 높이" : "")}{(lenOff ? " · 길이" : "")} - 모델/설정 확인 필요 (늘리지 않고 그대로 자름)");
        else Debug.Log($"[고기 규격 확인] 다듬은 고기 {size}");
    }

    /// <summary>Every chunk the plane (axis, at) runs through, cut in two there.</summary>
    private void SplitChunks(List<MeatChunk> chunks, int axis, float at, Transform rig, Material[] mats)
    {
        foreach (MeatChunk c in chunks.ToArray())
        {
            Bounds cb = c.mesh.Bounds;
            if (at <= cb.min[axis] + 1e-4f || at >= cb.max[axis] - 1e-4f) continue;
            c.mesh.Split(axis, at, out SliceMesh below, out SliceMesh above);
            if (below == null || above == null) continue;
            chunks.Remove(c);
            Destroy(c.go);
            foreach (SliceMesh part in new[] { below, above })
            {
                var nc = new MeatChunk { mesh = part };
                ShowChunk(nc, rig, mats);
                chunks.Add(nc);
            }
        }
    }

    /// <summary>A chunk shown: its own object, pivot at its middle, in the rig.</summary>
    private void ShowChunk(MeatChunk c, Transform rig, Material[] mats)
    {
        Bounds cb = c.mesh.Bounds;
        c.go = new GameObject("MeatPart");
        c.go.transform.SetParent(rig, false);
        c.go.transform.localPosition = cb.center;
        c.go.AddComponent<MeshFilter>().sharedMesh = c.mesh.ToMesh(cb.center, "MeatPart");
        c.go.AddComponent<MeshRenderer>().sharedMaterials = mats;
    }

    private IEnumerator SlideChunkAway(MeatChunk c, Transform rig, Vector3 by, float seconds)
    {
        Vector3 p0 = c.go.transform.localPosition;
        for (float e = 0f; e < seconds; e += Time.deltaTime)
        {
            if (c.go == null) yield break;
            c.go.transform.localPosition = p0 + by * Mathf.SmoothStep(0f, 1f, e / seconds);
            yield return null;
        }
        if (c.go != null) Destroy(c.go);
    }

    private static Bounds UnionBounds(List<MeatChunk> chunks)
    {
        Bounds b = chunks[0].mesh.Bounds;
        foreach (MeatChunk c in chunks) b.Encapsulate(c.mesh.Bounds);
        return b;
    }

    /// <summary>The model's own materials, and one more for the faces the cuts
    /// make (the meat's cut-surface material - its texture laid flat on them).</summary>
    private Material[] MeatMaterials()
    {
        Material[] own = rend.sharedMaterials;
        var all = new Material[own.Length + 1];
        for (int i = 0; i < own.Length; i++) all[i] = own[i];
        all[own.Length] = own.Length > 0 && own[0] != null ? own[0] : new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.85f, 0.5f, 0.48f) };
        return all;
    }

    /// <summary>The frame the meat is cut in - the board's: X across it, Y up off
    /// it (the counter's up), Z front-back - whatever way the meat itself is
    /// turned. (The models lie on the tilted board already tilted with it.)</summary>
    private static Quaternion BoardFrame()
    {
        Vector3 up = Carryable.CounterUp;
        Vector3 boardX = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
        return Quaternion.LookRotation(Vector3.Cross(boardX, up).normalized, up);
    }

    /// <summary>재료를 도마에 바로 눕힌다 - 재료 선반 자세(41도 기울어짐)로 들려 온 것을
    /// 도마 축에 맞춰 놓는다. 모델 치수를 그대로 재서 자르는 고기 채썰기에서는, 기울어진
    /// 채 재면 높이가 실제 두께보다 커 보여 포를 필요 이상으로 여러 번 뜨게 된다.
    /// 가로·세로 자리와 도마에 닿은 높이는 그대로 두고 자세만 바꾼다.</summary>
    private IEnumerator LayFlatOnBoard(float seconds)
    {
        Quaternion flat = BoardFrame() * Quaternion.Euler(0f, 180f, 0f); // 제 축이 도마 축과 나란하게 (원래 자세의 180도 돌림은 유지)
        Quaternion from = transform.rotation;                            // 기울어진 지금 자세
        if (Quaternion.Angle(from, flat) < 1f) yield break;              // 이미 눕혀져 있으면 그냥
        Bounds was = VisibleBounds;                                      // 지금 자리 (중심과 바닥 높이)
        Vector3 keep = was.center;
        float bottom = was.min.y;
        for (float e = 0f; e < seconds; e += Time.deltaTime)
        {
            transform.rotation = Quaternion.Slerp(from, flat, Mathf.SmoothStep(0f, 1f, e / seconds)); // 기울기를 푼다
            Bounds now = VisibleBounds;                                  // 돌면서 벗어난 만큼
            transform.position += new Vector3(keep.x - now.center.x, bottom - now.min.y, keep.z - now.center.z); // 제자리로 되돌린다
            yield return null;
        }
        transform.rotation = flat;
        Bounds end = VisibleBounds;
        transform.position += new Vector3(keep.x - end.center.x, bottom - end.min.y, keep.z - end.center.z); // 마지막 자리 보정
    }

    /// <summary>A short note over the ingredient (why something can't be done).</summary>
    private void Notice(string text)
    {
        Debug.Log($"[{ingredientName}] {text}");
        var go = new GameObject("IngredientNote");
        go.transform.position = VisibleBounds.center + new Vector3(0f, 0.7f, -1.5f);
        TextMesh tm = go.AddComponent<TextMesh>();
        KoreanFontFallback.Apply(tm); // 웹에서도 한글이 보이게 한글 글꼴
        tm.text = text;
        tm.fontSize = 36;
        tm.characterSize = 0.07f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = new Color(0.75f, 0.15f, 0.1f);
        Destroy(go, 3f);
    }

    /// <summary>The knife lying along the meat (its length along the board),
    /// lined up just left of it at the given line (rig-local: its middle) and
    /// sawn through to the right and out the far end - the hand steered every
    /// frame by where the blade's tip actually is, the other hand on top. Used
    /// for the edges (①) and for 포 뜨기 (②).</summary>
    private IEnumerator KnifeAlongPass(Transform rig, float len, Vector3 lineMid, HandController handCtl, Renderer knifeR, float slow)
    {
        float blockLeft = rig.TransformPoint(lineMid - new Vector3(len * 0.5f, 0f, 0f)).x;
        float blockRight = rig.TransformPoint(lineMid + new Vector3(len * 0.5f, 0f, 0f)).x;
        Vector3 cutAt = rig.TransformPoint(lineMid);
        SupportHandController.Instance?.ShowNear(cutAt + Carryable.CounterUp * 0.08f);
        for (float e = 0f, dur = 1.3f * slow; e < dur; e += Time.deltaTime)
        {
            float k = Mathf.Clamp01((e / dur - 0.25f) / 0.75f);
            float tipX = Mathf.Lerp(blockLeft - 0.05f, blockRight + 0.08f, k) + Mathf.Sin(k * Mathf.PI * 6f) * 0.03f;
            if (handCtl != null && knifeR != null)
            {
                Vector3 err = new Vector3(tipX - knifeR.bounds.max.x, cutAt.y - knifeR.bounds.center.y, 0f);
                handCtl.SlideTo(handCtl.RestPosition + err, 0.03f);
            }
            yield return null;
        }
    }

    private MeatBox MakeMeatBox(Transform rig, Vector3 pos, Vector3 size, Material m) =>
        new MeatBox { t = Block(rig, "Meat", pos, size, m).transform, size = size };

    /// <summary>Every box the line (rig x) runs through, cut in two there.</summary>
    private void SplitMeatX(List<MeatBox> boxes, float line, Transform rig, Material m)
    {
        foreach (MeatBox b in boxes.ToArray())
        {
            Vector3 p = b.t.localPosition;
            float left = p.x - b.size.x * 0.5f, right = p.x + b.size.x * 0.5f;
            if (line <= left + 0.003f || line >= right - 0.003f) continue;
            boxes.Remove(b);
            boxes.Add(MakeMeatBox(rig, new Vector3((left + line) * 0.5f, p.y, p.z), new Vector3(line - left, b.size.y, b.size.z), m));
            boxes.Add(MakeMeatBox(rig, new Vector3((line + right) * 0.5f, p.y, p.z), new Vector3(right - line, b.size.y, b.size.z), m));
            Destroy(b.t.gameObject);
        }
    }

    /// <summary>Every box the height (rig y) runs through, cut in two there (포 뜨기).</summary>
    private void SplitMeatY(List<MeatBox> boxes, float y, Transform rig, Material m)
    {
        foreach (MeatBox b in boxes.ToArray())
        {
            Vector3 p = b.t.localPosition;
            float lo = p.y - b.size.y * 0.5f, hi = p.y + b.size.y * 0.5f;
            if (y <= lo + 0.003f || y >= hi - 0.003f) continue;
            boxes.Remove(b);
            boxes.Add(MakeMeatBox(rig, new Vector3(p.x, (lo + y) * 0.5f, p.z), new Vector3(b.size.x, y - lo, b.size.z), m));
            boxes.Add(MakeMeatBox(rig, new Vector3(p.x, (y + hi) * 0.5f, p.z), new Vector3(b.size.x, hi - y, b.size.z), m));
            Destroy(b.t.gameObject);
        }
    }

    /// <summary>From the right end in: the knife comes down between the pieces,
    /// and the freed one tips over onto the fan (the first ends up farthest out).</summary>
    private IEnumerator SliceStackFromRight(SliceJob job, Vector3 handBase, HandController handCtl, ToolItem knife)
    {
        int n = job.stack.Count;
        for (int i = n - 1; i >= 0; i--)
        {
            if (i > 0) yield return DiceChop(job.rig, handBase, job.cutX[i], handCtl, knife);
            int f = n - 1 - i;
            Transform s = job.stack[i];
            Vector3 from = s.localPosition;
            Quaternion r0 = s.localRotation;
            Quaternion lean = Quaternion.AngleAxis(job.lean, Vector3.forward)
                            * (job.turnLying ? Quaternion.AngleAxis(Random.Range(-10f, 10f), Vector3.up) : Quaternion.identity) * r0;
            Vector3 to = new Vector3(job.rightEdge + 0.08f + (n - 1 - f) * job.fanStep, 0f, from.z * 0.3f);
            s.localRotation = lean;
            to.y = job.restY(s) + f * 0.002f;
            s.localRotation = r0;
            float dur = 0.35f * diceSlowness;
            for (float e = 0f; e < dur; e += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, e / dur);
                s.localPosition = Vector3.Lerp(from, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.03f);
                s.localRotation = Quaternion.Slerp(r0, lean, k);
                yield return null;
            }
            s.localPosition = to;
            s.localRotation = lean;
        }
    }

    private static IEnumerator Turn1(Transform t, Quaternion from, Quaternion to, float seconds)
    {
        for (float e = 0f; e < seconds; e += Time.deltaTime)
        {
            t.localRotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, e / seconds));
            yield return null;
        }
        t.localRotation = to;
    }

    /// <summary>A cut-off part slides away and is gone (it's a scrap now).</summary>
    private static IEnumerator SlideAway(Transform t, Vector3 by, float seconds)
    {
        Vector3 p0 = t.localPosition;
        for (float e = 0f; e < seconds; e += Time.deltaTime)
        {
            t.localPosition = p0 + by * Mathf.SmoothStep(0f, 1f, e / seconds);
            yield return null;
        }
        Destroy(t.gameObject);
    }

    private static Material SolidMaterial(Color c, float smooth)
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(lit != null ? lit : Shader.Find("Standard")) { color = c };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        return m;
    }

    private static GameObject Block(Transform parent, string name, Vector3 localPos, Vector3 size, Material m)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = size;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    /// <summary>A cylinder lying along the rig's x (its own axis is y).</summary>
    private static Transform Cylinder(Transform parent, string name, Vector3 localPos, Vector3 size, Material m)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        g.name = name;
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = Quaternion.AngleAxis(90f, Vector3.forward);
        g.transform.localScale = size;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g.transform;
    }

    /// <summary>A half disc (radius r, flat edge down, in the local Y-Z plane)
    /// with thickness along X - a cucumber slice: submesh 0 the flesh, 1 the
    /// skin (its curved edge and a rim on each face, skinFrac of the radius),
    /// 2 the seedy middle (coreFrac of the radius) a hair in front of the flesh.</summary>
    private static Mesh DiscSliceMesh(float r, float thickness, float skinFrac, float coreFrac, int segments)
    {
        var v = new List<Vector3>();
        var sub = new[] { new List<int>(), new List<int>(), new List<int>() };
        float hx = thickness * 0.5f;
        void Quad(int which, Vector3 a, Vector3 b, Vector3 c, Vector3 dd, Vector3 outward)
        {
            int o = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(dd);
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f;
            sub[which].AddRange(flip ? new[] { o, o + 2, o + 1, o, o + 3, o + 2 } : new[] { o, o + 1, o + 2, o, o + 2, o + 3 });
        }
        float rCore = r * coreFrac, rSkin = r * (1f - skinFrac);
        for (int s = 0; s < segments; s++)
        {
            float a0 = Mathf.PI * s / segments, a1 = Mathf.PI * (s + 1) / segments;
            Vector3 d0 = new Vector3(0f, Mathf.Sin(a0), Mathf.Cos(a0)), d1 = new Vector3(0f, Mathf.Sin(a1), Mathf.Cos(a1));
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 x = Vector3.right * hx * side, xc = Vector3.right * (hx + 0.0008f) * side;
                Vector3 n = Vector3.right * side;
                Quad(0, x, x + d0 * rSkin, x + d1 * rSkin, x, n); // flesh (fan to the middle)
                Quad(1, x + d0 * rSkin, x + d0 * r, x + d1 * r, x + d1 * rSkin, n); // skin rim
                Quad(2, xc, xc + d0 * rCore, xc + d1 * rCore, xc, n); // seeds
            }
            Vector3 mid = (d0 + d1).normalized;
            Quad(1, -Vector3.right * hx + d0 * r, Vector3.right * hx + d0 * r, Vector3.right * hx + d1 * r, -Vector3.right * hx + d1 * r, mid);
        }
        // the flat bottom
        Quad(0, new Vector3(-hx, 0f, -r), new Vector3(hx, 0f, -r), new Vector3(hx, 0f, r), new Vector3(-hx, 0f, r), Vector3.down);
        var m = new Mesh();
        m.SetVertices(v);
        m.subMeshCount = 3;
        for (int i = 0; i < 3; i++) m.SetTriangles(sub[i], i);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // ---- 고추 다듬기 (chiliTrim) ----

    /// <summary>고추 다듬기: the green cap cut off (a scrap); the chili stood up
    /// lengthwise like the leek and cut in half down the middle; the halves
    /// laid open, cut side up; the knife set aside and the seeds scraped out of
    /// each half with the measuring spoon (a scrap). The halves stay on the
    /// board, ready to cut.</summary>
    private IEnumerator ChiliTrimRoutine()
    {
        isCutting = true;
        HandController handCtl = HandController.Instance;
        ToolItem knife = HandCarry.Instance?.Current as ToolItem;
        Vector3 handBase = handCtl != null ? handCtl.RestPosition : Vector3.zero;

        Vector3 ms = sharedMesh != null ? sharedMesh.bounds.size : Vector3.one;
        Vector3 ls = transform.lossyScale;
        float[] dims = { Mathf.Abs(ms.x * ls.x), Mathf.Abs(ms.y * ls.y), Mathf.Abs(ms.z * ls.z) };
        System.Array.Sort(dims);
        float L = dims[2], d = dims[1], r = d * 0.5f, cap = L * 0.12f, len = L - cap;
        float slow = diceSlowness;

        // Drawn in onto the board first: stood up it reaches its full length up and down.
        Vector3 center = VisibleBounds.center;
        if (CuttingBoardController.Instance is CuttingBoardController board0 && IsOnCuttingBoard)
        {
            Vector3 pull = RoomShift(board0.VisibleTopRect(), new Bounds(center, new Vector3(L + 0.2f, L * 0.75f + 0.2f, 0f)));
            if (pull != Vector3.zero)
            {
                yield return SlideBy(pull, 0.2f);
                center += pull;
                handBase += new Vector3(pull.x, pull.y, 0f);
                handCtl?.SlideTo(handBase, 0.15f);
            }
        }
        Transform holder = PieceHolder.For(transform);
        Vector3 up = Carryable.CounterUp, back = Vector3.Cross(Vector3.right, up).normalized;
        var rig = new GameObject("TrimRig").transform;
        rig.SetParent(holder, false);
        rig.SetPositionAndRotation(center - up * r, Quaternion.LookRotation(back, up));

        float knifeHoldBefore = knife != null ? knife.ChopHoldSeconds : 0f;
        if (knife != null) knife.ChopHoldSeconds = 4f * slow;
        if (knife != null && handCtl != null)
        {
            knife.PlayChopMotion();
            yield return new WaitForSeconds(0.08f);
            if (BladeXAt(knife, rig.position.y, out float bladeX)) handBase.x += rig.position.x - bladeX;
        }

        Material red = SolidMaterial(new Color(0.82f, 0.1f, 0.07f), 0.6f);
        Material green = SolidMaterial(new Color(0.33f, 0.55f, 0.2f), 0.5f);
        Material inside = SolidMaterial(new Color(0.96f, 0.86f, 0.72f), 0.4f);
        Material seedM = SolidMaterial(new Color(0.98f, 0.92f, 0.62f), 0.4f);
        rend.enabled = false;
        Transform body = Cylinder(rig, "ChiliBody", new Vector3(-cap * 0.5f, r, 0f), new Vector3(d, len * 0.5f, d), red);
        Transform capT = Cylinder(rig, "ChiliCap", new Vector3(L * 0.5f - cap * 0.5f, r, 0f), new Vector3(d * 0.8f, cap * 0.5f, d * 0.8f), green);

        // 1) The green cap off.
        yield return DiceChop(rig, handBase, L * 0.5f - cap, handCtl, knife);
        yield return SlideAway(capT, new Vector3(0.15f, 0f, 0f), 0.4f * slow);
        DropScrap("꼭지", green.color);

        // 2) Stood up lengthwise (pointing back up the board), centered.
        Quaternion along = body.localRotation, upright = Quaternion.AngleAxis(90f, Vector3.right);
        Vector3 p0 = body.localPosition, p1 = new Vector3(0f, r, 0f);
        for (float e = 0f, dur = 0.5f * slow; e < dur; e += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, e / dur);
            body.localRotation = Quaternion.Slerp(along, upright, k);
            body.localPosition = Vector3.Lerp(p0, p1, k);
            yield return null;
        }
        body.localRotation = upright;
        body.localPosition = p1;

        // 3) Cut in half down the middle...
        yield return DiceChop(rig, handBase, 0f, handCtl, knife);
        Destroy(body.gameObject);
        var halves = new List<Transform>();
        var seeds = new List<Transform>[2];
        for (int h = 0; h < 2; h++)
        {
            float side = h == 0 ? -1f : 1f;
            var half = new GameObject($"Piece_ChiliHalf{h}").transform;
            half.SetParent(rig, false);
            // half a tube along z, its round side down and the cut side up
            half.localRotation = Quaternion.AngleAxis(180f, Vector3.forward) * Quaternion.AngleAxis(90f, Vector3.up);
            half.localPosition = new Vector3(0f, r, 0f);
            half.gameObject.AddComponent<MeshFilter>().sharedMesh = HalfMoonMesh(r, len, 1, 12);
            half.gameObject.AddComponent<MeshRenderer>().sharedMaterial = new Material(red);
            GameObject face = Block(half, "Face", new Vector3(0f, -0.002f, 0f), new Vector3(len * 0.96f, 0.003f, d * 0.88f), inside);
            face.transform.localRotation = Quaternion.identity;
            seeds[h] = new List<Transform>();
            for (int s = 0; s < 6; s++)
            {
                GameObject seed = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(seed.GetComponent<Collider>());
                seed.transform.SetParent(rig, false);
                seed.transform.localScale = Vector3.one * Mathf.Max(d * 0.18f, 0.012f);
                seed.GetComponent<Renderer>().sharedMaterial = seedM;
                seed.name = "Seed";
                seeds[h].Add(seed.transform);
            }
            halves.Add(half);
        }
        // ...and laid open side by side, cut side up.
        float gapX = r + 0.012f;
        for (float e = 0f, dur = 0.4f * slow; e < dur; e += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, e / dur);
            for (int h = 0; h < 2; h++)
            {
                float x = (h == 0 ? -1f : 1f) * gapX * k;
                halves[h].localPosition = new Vector3(x, r, 0f);
                for (int s = 0; s < seeds[h].Count; s++)
                    seeds[h][s].localPosition = new Vector3(x + (s % 2 == 0 ? -0.006f : 0.006f), r + 0.006f, Mathf.Lerp(-len * 0.4f, len * 0.4f, s / 5f));
            }
            yield return null;
        }

        // 4) The knife set aside; the seeds scraped out of each half with the
        //    measuring spoon (the other hand holding the half).
        handCtl?.SlideTo(handBase + rig.right * (d + 1.1f), 0.35f * slow); // (well clear: the blade leans over it)
        yield return new WaitForSeconds(0.35f * slow);
        ToolItem spoon = ToolItem.MeasuringSpoon;
        if (spoon != null && !spoon.IsHeld)
        {
            Transform sp = spoon.transform;
            Transform oldParent = sp.parent;
            Vector3 oldLocalPos = sp.localPosition, oldLocalScale = sp.localScale;
            Quaternion oldLocalRot = sp.localRotation;
            Collider[] cols = System.Array.FindAll(spoon.GetComponentsInChildren<Collider>(), c => c.enabled);
            foreach (Collider c in cols) c.enabled = false;
            sp.SetParent(null, true);
            Vector3 headOffset = spoon.SpoonBowlWorld() - sp.position;

            for (int h = 0; h < 2; h++)
            {
                float x = (h == 0 ? -1f : 1f) * gapX;
                SupportHandController.Instance?.ShowNear(rig.TransformPoint(new Vector3(x, r, -len * 0.45f)));
                Vector3 from = rig.TransformPoint(new Vector3(x, r + 0.02f, len * 0.5f));
                Vector3 to = rig.TransformPoint(new Vector3(x, r + 0.02f, -len * 0.5f));
                float dur = 0.9f * slow;
                for (float e = 0f; e < dur; e += Time.deltaTime)
                {
                    float k = Mathf.SmoothStep(0f, 1f, e / dur);
                    Vector3 head = Vector3.Lerp(from, to, k);
                    sp.position = head - headOffset + Vector3.back * 0.05f;
                    // the seeds it's passed go along in front of it
                    foreach (Transform seed in seeds[h])
                        if (seed != null && rig.InverseTransformPoint(seed.position).z > rig.InverseTransformPoint(head).z)
                            seed.position = head + new Vector3(Random.Range(-0.008f, 0.008f), Random.Range(-0.008f, 0.008f), -0.01f);
                    yield return null;
                }
                // flicked off to the side - the scraps
                foreach (Transform seed in seeds[h]) if (seed != null) Destroy(seed.gameObject);
                DropScrap("씨", seedM.color);
            }

            sp.SetParent(oldParent, false);
            sp.localPosition = oldLocalPos;
            sp.localRotation = oldLocalRot;
            sp.localScale = oldLocalScale;
            foreach (Collider c in cols) if (c != null) c.enabled = true;
        }
        else
        {
            foreach (var list in seeds) foreach (Transform seed in list) if (seed != null) Destroy(seed.gameObject);
            DropScrap("씨", seedM.color);
        }

        SupportHandController.Instance?.Hide();
        handCtl?.SlideTo(handBase, 0.2f);
        if (knife != null) knife.ChopHoldSeconds = knifeHoldBefore;
        foreach (Transform h in halves) h.SetParent(holder, true);
        Destroy(rig.gameObject);
        FitColliderToVisible();
        IsTrimmed = true;
        isCutting = false;
        Debug.Log($"{ingredientName}: 다듬기 완료 (꼭지 떼고 반 갈라 씨 제거)");
    }

    /// <summary>What trimming left showing (the chili's opened halves) makes way
    /// for the cut pieces.</summary>
    private void ClearTrimVisuals()
    {
        Transform holder = transform.Find("Pieces");
        if (holder == null) return;
        foreach (Transform t in holder.GetComponentsInChildren<Transform>())
            if (t.name.StartsWith("Piece_ChiliHalf") || t.name.StartsWith("Piece_Trimmed")) Destroy(t.gameObject); // 고추 반쪽 · 야채 다듬은 몸통
    }

    // ---- 양파 반달썰기 (crescentSlices) ----

    /// <summary>The half onion (cut face down, dome up) sliced across, the knife
    /// working in from its right end: the onion is shown as the stack of
    /// half-moon slices it's about to become, each cut frees the next one, which
    /// tips over onto the ones before it - a fan of slices to the right, like
    /// it's done on the board. 양파는 조각의 폭(규격 1cm) 간격으로 칼이 들어가고 한 칼에
    /// 그 폭 안의 결이 여러 장 떨어진다; 다른 재료는 두께 간격으로 한 장씩.</summary>
    private IEnumerator CrescentRoutine(CutSpec spec)
    {
        HandController handCtl = HandController.Instance;
        ToolItem knife = HandCarry.Instance?.Current as ToolItem;
        Vector3 handBase = handCtl != null ? handCtl.RestPosition : Vector3.zero;

        // (the radius as it shows - the onion as it lies on the board)
        Bounds shown = VisibleBounds;
        float R = Mathf.Max(shown.extents.x, shown.extents.y);
        // 조각 한 개의 규격: 가장 긴 값이 길이, 가장 짧은 값이 두께, 남은 값이 폭
        // (오이·당근·대파 편썰기가 규격을 읽는 방식과 같다).
        float lenCm = Mathf.Max(spec.widthCm, Mathf.Max(spec.lengthCm, spec.thicknessCm));  // 가장 긴 값 = 길이
        float thinCm = Mathf.Min(spec.widthCm, Mathf.Min(spec.lengthCm, spec.thicknessCm)); // 가장 짧은 값 = 두께
        float midCm = spec.widthCm + spec.lengthCm + spec.thicknessCm - lenCm - thinCm;     // 남은 값 = 폭
        if (lenCm <= 0f) { lenCm = 4f; midCm = 1f; thinCm = VeggieSliceThicknessCm; }       // 규격을 안 적었을 때
        float t = Mathf.Max(thinCm * worldPerCm, 0.012f);                                   // 조각 두께
        // 양파는 결을 가로질러 조각의 폭(1cm) 간격으로 칼이 들어가고, 한 칼에 그 폭 안의
        // 결이 여러 장 떨어진다. 다른 재료(반달)는 예전처럼 두께 간격으로 한 장씩.
        bool onionPlanks = ingredientName == "양파";
        float stripW = onionPlanks ? Mathf.Max(midCm, 0.1f) * worldPerCm : t;               // 칼이 들어가는 간격
        int cuts = Mathf.Clamp(Mathf.RoundToInt(2f * R / stripW), onionPlanks ? 2 : 6, onionPlanks ? 4 : 14); // 칼질 횟수 (양파는 접시에 너무 많아 상한을 절반으로)
        float strip = 2f * R / cuts;                                                        // 끝이 남지 않게 고르게 나눈 간격
        int layers = onionPlanks ? Mathf.Clamp(Mathf.FloorToInt(strip / t), 1, 2) : 1;       // 한 칼에 떨어지는 결 장수 (같은 이유로 상한 절반)
        int n = cuts * layers;                                                              // 조각 수
        float step = strip / layers;                                                        // 조각들이 나란히 서는 간격
        float thick = Mathf.Clamp(t, 0.012f, step * (onionPlanks ? 1f : 0.85f));            // 조각 두께
        float fanStep = Mathf.Max(thick * 0.8f, 0.025f); // (overlapping, kept together)

        // Room on the board for the onion and its fan of slices to the right.
        Vector3 center = VisibleBounds.center;
        if (CuttingBoardController.Instance is CuttingBoardController board0 && IsOnCuttingBoard)
        {
            Vector3 pull = RoomShift(board0.VisibleTopRect(), new Bounds(center + new Vector3(fanStep * n * 0.5f, 0f, 0f),
                new Vector3(2f * R + fanStep * n + 0.3f, 1.6f * R, 0f)));
            if (pull != Vector3.zero)
            {
                yield return SlideBy(pull, 0.2f);
                center += pull;
                handBase += new Vector3(pull.x, pull.y, 0f);
                handCtl?.SlideTo(handBase, 0.15f);
                SupportHandController.Instance?.ShowNear(center);
            }
        }

        Vector3 up = Carryable.CounterUp;
        Vector3 back = Vector3.Cross(Vector3.right, up).normalized;
        var rig = new GameObject("SliceRig").transform;
        rig.SetParent(transform, false);
        rig.SetPositionAndRotation(center - up * (R * 0.5f), Quaternion.LookRotation(back, up));
        rig.localScale = Vector3.one / Mathf.Max(Mathf.Abs(transform.lossyScale.x), 1e-4f);

        // Knife up, lined up with the cut lines, and kept up between the cuts.
        float knifeHoldBefore = knife != null ? knife.ChopHoldSeconds : 0f;
        if (knife != null) knife.ChopHoldSeconds = 4f * diceSlowness;
        if (knife != null && handCtl != null)
        {
            knife.PlayChopMotion();
            yield return new WaitForSeconds(0.08f);
            if (BladeXAt(knife, rig.position.y, out float bladeX)) handBase.x += rig.position.x - bladeX;
        }

        // 안 다듬었으면 다듬기 동작(뒤집기 · 위아래 자르기 · 심 빼기 · 90도 돌리기)부터 이어서
        if (!IsTrimmed) yield return OnionTrim(rig, handBase, handCtl, knife, R, lenCm * worldPerCm);
        else rend.enabled = false;                                   // 다듬어 둔 상태면 통째 모델만 숨긴다
        Transform trimmedBody = rig.Find("Piece_Trimmed");           // 다듬은 몸통은 이제 조각들로 바뀐다
        if (trimmedBody != null) Destroy(trimmedBody.gameObject);

        // The onion as its slices, standing side by side (together, its dome).
        Material onion = OnionSliceMaterial();
        var slices = new List<Transform>();
        for (int g = 0; g < cuts; g++)
            for (int l = 0; l < layers; l++)
            {
                float x = -R + strip * g + step * (l + 0.5f);        // 칼금 사이에 결 장수만큼 나란히
                float r = Mathf.Sqrt(Mathf.Max(R * R - x * x, 0f));
                var go = new GameObject($"Piece_{slices.Count}");
                go.transform.SetParent(rig, false);
                go.transform.localPosition = new Vector3(x, 0f, 0f);
                // 탕수육 양파: 둥근 반달 단면 대신 결 방향의 네모 편 - 규격대로 길이 x 폭 x 두께.
                go.AddComponent<MeshFilter>().sharedMesh = onionPlanks
                    ? OnionPlankMesh(lenCm * worldPerCm, Mathf.Max(midCm, 0.1f) * worldPerCm, thick)
                    : HalfMoonMesh(Mathf.Max(r, 0.03f), thick, 4, 14);
                go.AddComponent<MeshRenderer>().sharedMaterial = new Material(onion);
                go.AddComponent<CutResultPiece>().Init(ingredientName, AppliedCut, spec);
                slices.Add(go.transform);
            }

        // From the right end in: each cut frees a strip, whose layers tip onto the fan.
        int freed = 0;                                               // 지금까지 떨어진 조각 수
        for (int g = cuts - 1; g >= 0; g--)
        {
            if (g > 0) yield return DiceChop(rig, handBase, -R + strip * g, handCtl, knife); // 칼금 자리에 칼질
            for (int l = layers - 1; l >= 0; l--)                    // 칼 쪽 결부터 한 장씩
            {
                Transform s = slices[g * layers + l];
                Vector3 from = s.localPosition;
                // (the first one off ends up farthest out, each next one leaning on it)
                Vector3 to = new Vector3(R + 0.1f + (n - 1 - freed) * fanStep, thick * 0.5f + freed * 0.002f, 0f);
                Quaternion lean = Quaternion.AngleAxis(-72f, Vector3.forward);
                float dur = 0.35f * diceSlowness / layers;           // 한 칼에 여러 장이 떨어지니 장당 더 빠르게
                for (float e = 0f; e < dur; e += Time.deltaTime)
                {
                    float k = Mathf.SmoothStep(0f, 1f, e / dur);
                    s.localPosition = Vector3.Lerp(from, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.04f);
                    s.localRotation = Quaternion.Slerp(Quaternion.identity, lean, k);
                    yield return null;
                }
                s.localPosition = to;
                s.localRotation = lean;
                freed++;                                             // 다음 장은 그 앞에 기대 눕는다
            }
        }

        handCtl?.SlideTo(handBase, 0.15f);
        if (knife != null) knife.ChopHoldSeconds = knifeHoldBefore;

        foreach (Transform s in slices)
        {
            s.SetParent(transform, true);
            if (ingredientName == "양파")
            {
                Mesh mesh = s.GetComponent<MeshFilter>().mesh;
                Vector3 pieceCenter = mesh.bounds.center;
                Vector3 worldCenter = s.TransformPoint(pieceCenter);
                Quaternion turn = Quaternion.Euler(0f, 0f, -90f);
                Vector3[] vertices = mesh.vertices;
                for (int k = 0; k < vertices.Length; k++) vertices[k] = turn * (vertices[k] - pieceCenter);
                mesh.vertices = vertices;
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                s.rotation *= Quaternion.Inverse(turn);
                s.position = worldCenter; // 조각의 중심을 기준으로 회전하고 바닥에 눕게 한다.
            }
        }
        Destroy(rig.gameObject);
        // (조각을 규격대로 만들므로 더 줄이지 않는다 - 줄이면 4 x 1 이 어긋난다)
        FitColliderToVisible();
        DropScrap("자투리", OwnColor);
        Debug.Log($"{ingredientName}: 반달썰기 완료 (칼질 {cuts}번 x 결 {layers}장 = {n}조각, {lenCm:0.0} x {midCm:0.0} x {thick / worldPerCm:0.0} cm)");
    }

    // X는 얇은 두께, Y는 긴 결, Z는 폭. 얇은 층의 틈이 양파 결을 보여 준다.
    private static Mesh OnionPlankMesh(float length, float width, float thickness)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int i = vertices.Count;
            vertices.AddRange(new[] { a, b, c, d });
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) >= 0)
                triangles.AddRange(new[] { i, i+1, i+2, i, i+2, i+3 });
            else triangles.AddRange(new[] { i, i+2, i+1, i, i+3, i+2 });
        }
        for (int layer = 0; layer < 3; layer++)
        {
            float x0 = -thickness * 0.5f + thickness * layer / 3f;
            float x1 = x0 + thickness * 0.30f;
            float z0 = -width * 0.5f, z1 = width * 0.5f;
            Vector3 a = new Vector3(x0, 0, z0), b = new Vector3(x0, length, z0);
            Vector3 c = new Vector3(x0, length, z1), d = new Vector3(x0, 0, z1);
            Vector3 offset = Vector3.right * (x1-x0);
            Face(a,b,c,d,Vector3.left); Face(a+offset,b+offset,c+offset,d+offset,Vector3.right);
            Face(a,a+offset,b+offset,b,Vector3.back); Face(d,d+offset,c+offset,c,Vector3.forward);
            Face(a,d,d+offset,a+offset,Vector3.down); Face(b,c,c+offset,b+offset,Vector3.up);
        }
        var mesh = new Mesh { name = "Onion_4x1_LayeredPlank" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }

    private static Material onionSliceMaterial;

    private static Material OnionSliceMaterial()
    {
        if (onionSliceMaterial != null) return onionSliceMaterial;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        onionSliceMaterial = new Material(lit != null ? lit : Shader.Find("Standard")) { color = new Color(0.96f, 0.94f, 0.86f) };
        if (onionSliceMaterial.HasProperty("_Smoothness")) onionSliceMaterial.SetFloat("_Smoothness", 0.55f);
        return onionSliceMaterial;
    }

    /// <summary>A half-moon slice of onion standing on its flat edge: the half
    /// disc (radius r, in the local Y-Z plane, curved edge up) made of `layers`
    /// rings with a hairline between them (the onion's layers), thickness along
    /// local X. hollowCore: 가운데 심(가장 안쪽 링)을 빼고 그 자리를 비운다 - 다듬기에서
    /// 심을 뽑아낸 뒤의 몸통. sweep: 단면이 도는 각 - 기본 180°(반달), 90°면 1/4쪽
    /// (지급되는 양파가 1/4조각이라 다듬은 몸통은 이쪽).</summary>
    private static Mesh HalfMoonMesh(float r, float thickness, int layers, int segments, bool hollowCore = false, float sweep = Mathf.PI)
    {
        var v = new List<Vector3>();
        var tri = new List<int>();
        float hx = thickness * 0.5f, gap = r * 0.035f;
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            int o = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f;
            if (!flip) tri.AddRange(new[] { o, o + 1, o + 2, o, o + 2, o + 3 });
            else tri.AddRange(new[] { o, o + 2, o + 1, o, o + 3, o + 2 });
        }
        for (int k = hollowCore ? 1 : 0; k < layers; k++) // 심을 뺐으면 가장 안쪽 링(k=0)은 그리지 않는다
        {
            float ri = k == 0 ? 0f : r * k / layers + gap;
            float ro = r * (k + 1) / layers;
            for (int s = 0; s < segments; s++)
            {
                float a0 = sweep * s / segments, a1 = sweep * (s + 1) / segments; // 도는 각만큼만 (반달 180° · 1/4쪽 90°)
                Vector3 d0 = new Vector3(0f, Mathf.Sin(a0), Mathf.Cos(a0)), d1 = new Vector3(0f, Mathf.Sin(a1), Mathf.Cos(a1));
                Vector3 px = Vector3.right * hx, nx = -px;
                Quad(px + d0 * ri, px + d0 * ro, px + d1 * ro, px + d1 * ri, Vector3.right);
                Quad(nx + d0 * ri, nx + d0 * ro, nx + d1 * ro, nx + d1 * ri, Vector3.left);
                Vector3 mid = (d0 + d1).normalized;
                Quad(nx + d0 * ro, px + d0 * ro, px + d1 * ro, nx + d1 * ro, mid);
                if (ri > 0f) Quad(nx + d0 * ri, px + d0 * ri, px + d1 * ri, nx + d1 * ri, -mid);
            }
        }
        var m = new Mesh();
        m.SetVertices(v);
        m.SetTriangles(tri, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>One box collider round everything that shows of it (its pieces).</summary>
    private void FitColliderToVisible()
    {
        Bounds b = VisibleBounds;
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) box = gameObject.AddComponent<BoxCollider>();
        box.center = transform.InverseTransformPoint(b.center);
        Vector3 s = transform.InverseTransformVector(b.size);
        box.size = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Max(Mathf.Abs(s.z), 0.1f));
    }

    /// <summary>The chosen thickness (cm), or the method's usual one.</summary>
    private float SpecCm() => AppliedSpec.thicknessCm > 0f ? AppliedSpec.thicknessCm : ReferenceThicknessCm(CutMethod.Cube);

    /// <summary>Two cuts, a spec apart, centered on the block across the knife's direction.</summary>
    private IEnumerator CutStep(Transform rig, Vector3 handBase, HandController handCtl, ToolItem knife,
        List<CutPiece> pieces, Bounds mb, float spacing, string label, int expect)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (CutPiece p in pieces)
        {
            float x = RigPos(rig, p).x, e = RigExtent(rig, p, Vector3.right) * 0.5f;
            min = Mathf.Min(min, x - e);
            max = Mathf.Max(max, x + e);
        }
        float mid = (min + max) * 0.5f;
        float[] lines = { mid - spacing * 0.5f, mid + spacing * 0.5f };
        DiceStep(rig, label, lines, pieces);
        for (int i = 0; i < lines.Length; i++)
        {
            yield return DiceChop(rig, handBase, lines[i], handCtl, knife);
            SplitAt(pieces, lines[i], rig, mb);
            RemoveGuide(0);
            // a hairline apart at the cut - they stay together
            foreach (CutPiece p in pieces)
                p.T.position += rig.right * (RigPos(rig, p).x > lines[i] ? 0.003f : -0.003f);
            if (i + 1 < lines.Length) lines[i + 1] += 0.003f;
            Debug.Log($"[두부] 절단 → {pieces.Count}조각");
        }
        if (pieces.Count != expect) Debug.LogWarning($"[두부] {expect}조각이어야 하는데 {pieces.Count}조각");
    }

    private static Mesh dicedBlockMesh; // 모든 두부 조각이 같은 직육면체 메시를 공유한다.
    private static Mesh DicedBlockMesh() // 면별 정점을 분리해서 모서리 법선이 섞이지 않게 한다.
    {
        if (dicedBlockMesh != null) return dicedBlockMesh; // 최초 한 번만 만든다.
        var vertices = new Vector3[24]; // 면마다 네 정점을 독립적으로 둔다.
        var normals = new Vector3[24]; // 한 면의 법선은 모두 같게 유지한다.
        var uv = new Vector2[24]; // 기존 두부 재질을 적용할 텍스처 좌표
        var triangles = new int[36]; // 여섯 면에 각각 두 삼각형
        Vector3[] faces = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back }; // 바깥 방향
        for (int face = 0; face < 6; face++) // 평평한 여섯 면을 만든다.
        {
            Vector3 n = faces[face]; // 이번 면의 바깥 법선
            Vector3 u = Vector3.Cross(Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up, n).normalized; // 가로 방향
            Vector3 v = Vector3.Cross(n, u); // 세로 방향
            int first = face * 4; // 이번 면의 정점 시작점
            vertices[first] = (n - u - v) * 0.5f;
            vertices[first + 1] = (n + u - v) * 0.5f;
            vertices[first + 2] = (n + u + v) * 0.5f;
            vertices[first + 3] = (n - u + v) * 0.5f;
            for (int j = 0; j < 4; j++) normals[first + j] = n; // 모서리에서 이웃 면과 평균내지 않는다.
            uv[first] = Vector2.zero; uv[first + 1] = Vector2.right; uv[first + 2] = Vector2.one; uv[first + 3] = Vector2.up;
            int t = face * 6; // 바깥쪽을 바라보는 삼각형 순서
            triangles[t] = first; triangles[t + 1] = first + 1; triangles[t + 2] = first + 2;
            triangles[t + 3] = first; triangles[t + 4] = first + 2; triangles[t + 5] = first + 3;
        }
        dicedBlockMesh = new Mesh { name = "Tofu_FlatCutBlock", vertices = vertices, normals = normals, uv = uv, triangles = triangles }; // 각진 절단 조각
        dicedBlockMesh.RecalculateBounds(); // 표시와 충돌에 사용할 경계
        dicedBlockMesh.RecalculateTangents(); // 재질의 표면 방향도 평평한 면에 맞춘다.
        return dicedBlockMesh;
    }

    private CutPiece MakeCutPiece(Transform rig, Vector3 rigPos, Quaternion rigRot, Vector3 size, Bounds mb)
    {
        var go = new GameObject("Piece");
        go.transform.SetParent(rig, false);
        go.transform.localPosition = rigPos;
        go.transform.localRotation = rigRot;
        go.AddComponent<MeshFilter>().sharedMesh = sharedMesh; // 원본 메시를 그대로 써서 자르는 연출을 살린다.
        go.AddComponent<MeshRenderer>().sharedMaterials = rend.sharedMaterials;
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = mb.center;
        box.size = mb.size;
        var p = new CutPiece { piece = go.AddComponent<FoodPiece>(), size = size };
        SetCutSize(p, size, mb);
        return p;
    }

    private static void SetCutSize(CutPiece p, Vector3 size, Bounds mb)
    {
        p.size = size;
        // (the rig and so the piece's parent has world scale 1)
        p.T.localScale = new Vector3(size.x / Mathf.Max(mb.size.x, 1e-4f), size.y / Mathf.Max(mb.size.y, 1e-4f), size.z / Mathf.Max(mb.size.z, 1e-4f));
    }

    private static Vector3 RigPos(Transform rig, CutPiece p) => rig.InverseTransformPoint(p.T.position); // (world scale 1)

    /// <summary>A piece's length along a rig axis (its own axes turned by 90° steps).</summary>
    private static float RigExtent(Transform rig, CutPiece p, Vector3 rigAxis)
    {
        Quaternion q = Quaternion.Inverse(rig.rotation) * p.T.rotation;
        float e = 0f;
        for (int a = 0; a < 3; a++)
        {
            Vector3 axis = Vector3.zero; axis[a] = 1f;
            e += Mathf.Abs(Vector3.Dot(q * axis, rigAxis)) * p.size[a];
        }
        return e;
    }

    /// <summary>Every piece the cut line (rig x) runs through is cut in two
    /// there - each half a new object, the old one removed.</summary>
    private void SplitAt(List<CutPiece> pieces, float line, Transform rig, Bounds mb)
    {
        foreach (CutPiece p in pieces.ToArray())
        {
            Vector3 pos = RigPos(rig, p);
            float half = RigExtent(rig, p, Vector3.right) * 0.5f;
            float left = pos.x - half, rightEdge = pos.x + half;
            if (line <= left + 0.004f || line >= rightEdge - 0.004f) continue;

            Quaternion q = Quaternion.Inverse(rig.rotation) * p.T.rotation;
            int along = 0;
            float best = 0f;
            for (int a = 0; a < 3; a++)
            {
                Vector3 axis = Vector3.zero; axis[a] = 1f;
                float d = Mathf.Abs(Vector3.Dot(q * axis, Vector3.right));
                if (d > best) { best = d; along = a; }
            }
            float wl = line - left, wr = rightEdge - line;
            Vector3 sl = p.size; sl[along] = wl;
            Vector3 sr = p.size; sr[along] = wr;
            pieces.Remove(p);
            pieces.Add(MakeCutPiece(rig, new Vector3(left + wl * 0.5f, pos.y, pos.z), q, sl, mb));
            pieces.Add(MakeCutPiece(rig, new Vector3(line + wr * 0.5f, pos.y, pos.z), q, sr, mb));
            Destroy(p.T.gameObject);
        }
    }

    /// <summary>Turns all the pieces together 90° about a rig axis (round their
    /// middle); tipped over, they're set back down on the board.</summary>
    private static IEnumerator Turn(List<CutPiece> pieces, Transform rig, Vector3 rigAxis, float seconds, bool settle)
    {
        Vector3 mid = Vector3.zero;
        float bottom = float.MaxValue;
        foreach (CutPiece p in pieces)
        {
            mid += RigPos(rig, p);
            bottom = Mathf.Min(bottom, RigPos(rig, p).y - RigExtent(rig, p, Vector3.up) * 0.5f);
        }
        mid /= Mathf.Max(pieces.Count, 1);

        var pivot = new GameObject("Turn").transform;
        pivot.SetParent(rig, false);
        pivot.localPosition = mid; // (the rig is world scale 1)
        pivot.localRotation = Quaternion.identity;
        foreach (CutPiece p in pieces) p.T.SetParent(pivot, true);
        for (float t = 0f; t <= seconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            pivot.localRotation = Quaternion.AngleAxis(90f * k, rigAxis);
            if (settle) pivot.localPosition = mid + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.04f);
            yield return null;
        }
        pivot.localRotation = Quaternion.AngleAxis(90f, rigAxis);
        foreach (CutPiece p in pieces) p.T.SetParent(rig, true);
        Destroy(pivot.gameObject);

        // Each box set back upright at its turned size - the tofu model is
        // only rounded along its top edges, and lying on its side that showed
        // as slanted, diamond-shaped faces.
        foreach (CutPiece p in pieces)
        {
            Vector3 turned = new Vector3(RigExtent(rig, p, Vector3.right), RigExtent(rig, p, Vector3.up), RigExtent(rig, p, Vector3.forward));
            p.T.localRotation = Quaternion.identity;
            SetCutSize(p, turned, new Bounds(Vector3.zero, p.T.GetComponent<MeshFilter>().sharedMesh.bounds.size));
        }

        if (!settle) yield break;
        float newBottom = float.MaxValue;
        foreach (CutPiece p in pieces) newBottom = Mathf.Min(newBottom, RigPos(rig, p).y - RigExtent(rig, p, Vector3.up) * 0.5f);
        foreach (CutPiece p in pieces) p.T.position += rig.up * (bottom - newBottom);
    }

    /// <summary>A side slice slides off outward, falls flat and goes.</summary>
    private static IEnumerator SlideOff(CutPiece p, Transform rig, float sign, float seconds)
    {
        Vector3 p0 = p.T.position;
        Quaternion r0 = p.T.rotation;
        for (float t = 0f; t <= seconds; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / seconds);
            p.T.position = p0 + rig.right * (sign * 0.12f * k) - rig.up * (p.size.y * 0.3f * k);
            p.T.rotation = Quaternion.AngleAxis(-sign * 80f * k, rig.forward) * r0;
            yield return null;
        }
        Destroy(p.T.gameObject);
    }

    /// <summary>The step's label over the board, and a guide line on top of the
    /// block at each place the knife is about to come down.</summary>
    private void DiceStep(Transform rig, string label, float[] lines, List<CutPiece> pieces)
    {
        Label(rig, label);
        ClearGuides();
        float top = float.MinValue, zmin = float.MaxValue, zmax = float.MinValue;
        foreach (CutPiece p in pieces)
        {
            Vector3 pos = RigPos(rig, p);
            top = Mathf.Max(top, pos.y + RigExtent(rig, p, Vector3.up) * 0.5f);
            float ez = RigExtent(rig, p, Vector3.forward) * 0.5f;
            zmin = Mathf.Min(zmin, pos.z - ez);
            zmax = Mathf.Max(zmax, pos.z + ez);
        }
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        foreach (float x in lines)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            g.name = "CutGuide";
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(rig, false);
            g.transform.position = rig.position + rig.right * x + rig.up * (top + 0.004f) + rig.forward * ((zmin + zmax) * 0.5f);
            g.transform.rotation = rig.rotation * Quaternion.Euler(90f, 0f, 0f);
            g.transform.localScale = new Vector3(0.012f, (zmax - zmin) * 1.15f, 1f) / Mathf.Max(rig.lossyScale.x, 1e-4f);
            Renderer r = g.GetComponent<Renderer>();
            if (unlit != null) r.material = new Material(unlit);
            r.material.color = new Color(0.9f, 0.15f, 0.1f);
            if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", new Color(0.9f, 0.15f, 0.1f));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            diceGuides.Add(g);
        }
    }

    private void Label(Transform rig, string text)
    {
        // (no text on screen - the step goes to the log only; the red guide
        // lines on the block show where the knife comes down)
        Debug.Log($"[두부] {text}");
    }

    private void RemoveGuide(int i)
    {
        if (i >= diceGuides.Count) return;
        if (diceGuides[i] != null) Destroy(diceGuides[i]);
        diceGuides.RemoveAt(i);
    }

    private void ClearGuides()
    {
        foreach (GameObject g in diceGuides) if (g != null) Destroy(g);
        diceGuides.Clear();
    }

    private void ClearDiceMarks()
    {
        ClearGuides();
        if (diceLabel != null) Destroy(diceLabel.gameObject);
        diceLabel = null;
    }

    /// <summary>What shows of it (world): itself, or once cut its pieces.</summary>
    public Bounds VisibleBounds
    {
        get
        {
            Renderer own = GetComponent<Renderer>();
            if (own != null && own.enabled) return own.bounds;
            bool any = false;
            Bounds all = new Bounds(transform.position, Vector3.zero);
            foreach (Transform c in GetComponentsInChildren<Transform>())
            {
                if (!c.name.StartsWith("Piece_") || !c.TryGetComponent(out Renderer r) || !r.enabled) continue;
                if (!any) { all = r.bounds; any = true; } else all.Encapsulate(r.bounds);
            }
            return all;
        }
    }

    /// <summary>Cut pieces laid out wider than radius (world, on screen) are
    /// gathered into a loose heap round the middle, e.g. tipped into a pot or
    /// onto a towel. Already that small: left as they are.</summary>
    public void GatherPieces(float radius)
    {
        if (GetComponent<FoodPieceGroup>() is FoodPieceGroup group) { group.Gather(radius); return; }

        var pieces = new List<Transform>();
        Bounds all = default;
        foreach (Transform c in GetComponentsInChildren<Transform>()) // (under a "Pieces" holder too)
        {
            if (!c.name.StartsWith("Piece_") || !c.TryGetComponent(out Renderer r)) continue;
            if (pieces.Count == 0) all = r.bounds; else all.Encapsulate(r.bounds);
            pieces.Add(c);
        }
        // 조각들은 그릇 '바닥 평면'에 펼쳐진다. 옛 코드는 x·y(화면 가로세로)만 재서,
        // 수평 그릇에서는 깊이(z)로 넓게 퍼진 더미가 '이미 작다'고 판정돼 모이지 않고
        // 그릇 밖으로 삐져나왔다.
        float wide = Vector3.Dot(Carryable.CounterUp, Vector3.up) > 0.999f   // 수평 조리대인가
                   ? Mathf.Max(all.extents.x, all.extents.z)                 // 수평 그릇: 펼쳐지는 평면이 X·Z 다
                   : Mathf.Max(all.extents.x, all.extents.y);                // 기울어진 옛 프레임: 화면 가로세로
        if (pieces.Count < 2 || wide <= radius) return;

        Vector3 up = Carryable.CounterUp, right = Vector3.right, back = Vector3.Cross(right, up).normalized;
        float spread = Mathf.Max(radius * 0.8f, 0.05f);
        for (int k = 0; k < pieces.Count; k++)
        {
            float r = spread * Mathf.Sqrt((k + 0.5f) / pieces.Count);
            float a = k * 2.39996f;
            Vector3 target = all.center + right * (Mathf.Cos(a) * r) + back * (Mathf.Sin(a) * r)
                           + up * ((1f - r / spread) * 0.04f + Random.Range(0f, 0.02f));
            // (by what shows of it: a slice's pivot is its edge, not its middle)
            Vector3 shown = pieces[k].GetComponent<Renderer>().bounds.center;
            pieces[k].position += target - shown;
        }
        FitColliderToVisible();
    }

    /// <summary>How far to move something with these (world) bounds to get it
    /// inside the board's top - along the tilted counter, keeping its height
    /// above it.</summary>
    private Vector3 RoomShift(Rect top, Bounds all)
    {
        // (with others on the board: only if it doesn't push this one into them -
        // those already there are never moved; otherwise it's cut where it lies)
        if (Vector3.Dot(Carryable.CounterUp, Vector3.up) > 0.999f && CuttingBoardController.Instance != null)
        {
            Bounds boardBounds = CuttingBoardController.Instance.GetComponent<Renderer>().bounds;
            float halfX = Mathf.Min(all.extents.x, boardBounds.extents.x);
            float halfZ = Mathf.Min(Mathf.Max(all.extents.z, all.extents.y), boardBounds.extents.z);
            float x = Mathf.Clamp(all.center.x, boardBounds.min.x + halfX, boardBounds.max.x - halfX);
            float z = Mathf.Clamp(all.center.z, boardBounds.min.z + halfZ, boardBounds.max.z - halfZ);
            return new Vector3(x - all.center.x, 0f, z - all.center.z); // 작업 공간 보정은 도마 평면에서만 이동한다.
        }
        Vector3 s = FitShift(top, all);
        if (s == Vector3.zero || !(CuttingBoardController.Instance is CuttingBoardController board) || board.Items.Count < 2) return s;
        Bounds vb = VisibleBounds;
        return board.IsClear(new Rect(vb.min.x + s.x, vb.min.y + s.y, vb.size.x, vb.size.y), this, 0.05f) ? s : Vector3.zero;
    }

    /// <summary>Room round it for the knife's work (and, for the meat, for
    /// turning it a quarter round) - with others on the board, the knife
    /// mustn't go through them. Not enough: the cut doesn't start (the board
    /// outlines it red a moment; nothing's said).</summary>
    private bool HasRoomToCut(bool turning, float fanRight = 0f, float spread = 0f)
    {
        if (!(CuttingBoardController.Instance is CuttingBoardController board) || !IsOnCuttingBoard || board.Items.Count < 2) return true;
        Bounds vb = VisibleBounds;
        float side = Mathf.Max(vb.size.x, vb.size.y);
        // (with the slices' fan to the right, or the cut pieces' spread round it, as this cut lays them out)
        Rect work = new Rect(vb.min.x - 0.12f - spread, vb.min.y - 0.03f - spread * 0.5f, vb.size.x + 0.24f + 2f * spread + fanRight, vb.size.y + 0.06f + spread);
        bool ok = board.IsClear(work, this, 0f);
        if (ok && turning)
            ok = board.IsClear(new Rect(vb.center.x - side * 0.5f, vb.center.y - side * 0.5f, side, side), this, 0f);
        if (!ok)
        {
            Debug.Log($"[도마] {ingredientName}: 주변 재료와 가까워 칼질할 공간이 없음 - 시작 안 함");
            board.FlashBlocked(this);
        }
        return ok;
    }

    private static Vector3 FitShift(Rect top, Bounds all)
    {
        const float margin = 0.05f;
        Vector3 shift = Vector3.zero;
        if (all.size.x > top.width - 2f * margin) shift.x = top.center.x - all.center.x;
        else if (all.min.x < top.xMin + margin) shift.x = top.xMin + margin - all.min.x;
        else if (all.max.x > top.xMax - margin) shift.x = top.xMax - margin - all.max.x;
        if (all.size.y > top.height - 2f * margin) shift.y = top.center.y - all.center.y;
        else if (all.min.y < top.yMin + margin) shift.y = top.yMin + margin - all.min.y;
        else if (all.max.y > top.yMax - margin) shift.y = top.yMax - margin - all.max.y;
        shift.z = HandCarry.CounterSlope * shift.y;
        return shift;
    }

    private IEnumerator SlideBy(Vector3 shift, float seconds)
    {
        Vector3 from = transform.position;
        for (float e = 0f; e <= seconds; e += Time.deltaTime)
        {
            transform.position = from + shift * Mathf.SmoothStep(0f, 1f, e / seconds);
            yield return null;
        }
        transform.position = from + shift;
    }

    /// <summary>Where the knife's blade line (its long axis, upright in the chop
    /// stance) crosses height y on screen.</summary>
    private static bool BladeXAt(ToolItem knife, float y, out float x)
    {
        x = 0f;
        Renderer r = knife.GetComponent<Renderer>();
        if (r == null) return false;
        Bounds lb = r.localBounds;
        Vector3 s = lb.size;
        Vector3 axis = s.x >= s.y && s.x >= s.z ? Vector3.right : s.y >= s.z ? Vector3.up : Vector3.forward;
        float half = Vector3.Dot(s, axis) * 0.5f;
        Vector3 a = knife.transform.TransformPoint(lb.center - axis * half);
        Vector3 b = knife.transform.TransformPoint(lb.center + axis * half);
        x = Mathf.Abs(b.y - a.y) < 1e-3f ? (a.x + b.x) * 0.5f : Mathf.LerpUnclamped(a.x, b.x, (y - a.y) / (b.y - a.y));
        return true;
    }

    /// <summary>The knife moves over to the cut line, lines up a moment, comes
    /// down once and is lifted off - at a hand's pace (about a second a cut),
    /// not a machine's.</summary>
    private IEnumerator DiceChop(Transform rig, Vector3 handBase, float line, HandController handCtl, ToolItem knife)
    {
        Vector3 offset = rig.right * line;
        handCtl?.SlideTo(handBase + offset, 0.3f * diceSlowness); // 도마 방향의 세 좌표를 모두 반영한다.
        yield return new WaitForSeconds(0.45f * diceSlowness);
        handCtl?.PlayChopGesture();
        knife?.PlayChopMotion();
        CookingMess.Chop(OwnColor); // (now and then a bit flicked off beside the board)
        yield return new WaitForSeconds(0.5f * diceSlowness);
    }

    /// <summary>The original collider was sized for the whole, uncut shape - once
    /// cutting scatters the visible Piece_i children out across spreadRange (which
    /// can exceed the original footprint, especially for thin ingredients), clicks
    /// aimed at a piece the player can actually see can land outside that stale
    /// collider and silently miss the ingredient entirely. Piece_i objects have no
    /// colliders of their own (they're purely visual), so this swaps in one box
    /// sized to cover all of them instead.</summary>
    private void ExpandColliderToCoverPieces()
    {
        Collider oldCollider = GetComponent<Collider>();
        if (oldCollider == null) return;

        Bounds local = default;
        bool any = false;

        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith("Piece_")) continue;

            Vector3 c = child.localPosition;
            Vector3 size = new Vector3(Mathf.Abs(child.localScale.x), Mathf.Abs(child.localScale.y), 0.1f);
            Bounds b = new Bounds(c, size);

            if (!any) { local = b; any = true; }
            else local.Encapsulate(b);
        }

        if (!any) return;

        Destroy(oldCollider);
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.center = local.center;
        box.size = local.size + new Vector3(0.05f, 0.05f, 0.05f);
    }

    /// <summary>The scale a pulse squashes from and returns to - its scale as it
    /// is where it sits (not BaseLocalScale: inside the strainer, scaled 4.2,
    /// resetting to that made the drained tofu four times its size).</summary>
    private Vector3? pulseBase;

    private IEnumerator ChopPulse()
    {
        if (IsOnCuttingBoard) CookingMess.Chop(OwnColor); // (now and then a bit flicked off beside the board)
        // (a pulse cut short by the next one left it squashed: start from the
        // scale that one started from - kept until a pulse runs to its end)
        if (!pulseBase.HasValue) pulseBase = transform.localScale;
        Vector3 s = pulseBase.Value;
        float dur = 0.05f;
        float t = 0f;

        while (t < dur)
        {
            t += Time.deltaTime;
            transform.localScale = new Vector3(s.x, s.y * Mathf.Lerp(1f, 0.85f, t / dur), s.z);
            yield return null;
        }

        t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            transform.localScale = new Vector3(s.x, s.y * Mathf.Lerp(0.85f, 1f, t / dur), s.z);
            yield return null;
        }

        transform.localScale = s;
        pulseBase = null;
    }

    /// <summary>Moved somewhere new mid-pulse (picked up, placed - which set its
    /// scale for there): an unfinished pulse's base no longer applies.</summary>
    public override void OnPlaced()
    {
        base.OnPlaced();
        pulseBase = null;
    }
}
