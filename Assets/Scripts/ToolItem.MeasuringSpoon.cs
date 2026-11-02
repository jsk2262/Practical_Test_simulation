using System.Collections;
using UnityEngine;

/// <summary>
/// 계량스푼 - two-ended: 큰술 1T (15 mL) at one end, 작은술 1t (5 mL) at the
/// other (1T = 3t). Picked up, it's held by the 큰술 end; held down again
/// (flipHoldSeconds - ClickDetector) it's turned round in hand to the other
/// end, and back. What it holds is counted in mL: it takes up to the end's
/// size from a bottle, a dish or a bowl (less if less is left) and gives all
/// of it to whatever it's tipped into. While in hand a label shows the end in
/// use (and what's on it). Set down it keeps its dose and its end; put away
/// empty (rack, tray, slot) it's back to 큰술.
/// </summary>
public partial class ToolItem
{
    public const float TablespoonMl = 15f, TeaspoonMl = 5f;

    [Header("계량스푼 (큰술 1T 15mL / 작은술 1t 5mL)")]
    [Tooltip("The 큰술 end, in the spoon mesh's own local space.")]
    [SerializeField] private Vector3 tablespoonEndLocal = new Vector3(0f, 0f, -0.42f);
    [Tooltip("The 작은술 end, in the spoon mesh's own local space.")]
    [SerializeField] private Vector3 teaspoonEndLocal = new Vector3(0f, 0f, 0.42f);
    [Tooltip("How long the button is held down (spoon in hand) to turn it round to the other end.")]
    [SerializeField, Min(0.1f)] private float flipHoldSeconds = 0.6f;
    [SerializeField, Min(0.05f)] private float flipSeconds = 0.35f;

    private const float LoadedSpoonScaleBulge = 1.15f;
    private string loadedSeasoning;
    private GameObject spoonContent;

    /// <summary>mL on the spoon now.</summary>
    public float LoadedMl { get; private set; }
    /// <summary>Held by the 작은술 (1t) end.</summary>
    public bool UsingTeaspoon { get; private set; }
    public bool IsFlipping { get; private set; }
    public float FlipHoldSeconds => flipHoldSeconds;
    /// <summary>What the end in use holds, level: 15 mL or 5 mL.</summary>
    public float SpoonCapacityMl => UsingTeaspoon ? TeaspoonMl : TablespoonMl;
    public string SpoonSizeLabel => UsingTeaspoon ? "작은술 1t · 5mL" : "큰술 1T · 15mL";
    private Vector3 ActiveEndLocal => UsingTeaspoon ? teaspoonEndLocal : tablespoonEndLocal;

    /// <summary>Holding something, waiting to be tipped in.</summary>
    public bool IsSpoonLoaded => loadedSeasoning != null && LoadedMl > 0.001f;
    public string LoadedSeasoning => IsSpoonLoaded ? loadedSeasoning : null;
    public SaucePortion LoadedSauce { get; private set; }

    // 이름만 옮기지 않고 실제로 덜어낸 기름의 성분/추출/탄 정도를 보관한다.
    public bool TryLoadSauce(PanSauce source)
    {
        if (source == null || !TryLoadUpTo(source.LiquidName, source.LiquidAmount * TablespoonMl)) return false;
        LoadedSauce = source.Take(LoadedMl / TablespoonMl, true);
        return true;
    }

    /// <summary>A level spoonful (the end in use) of it, from a source that
    /// doesn't run out. The mL taken.</summary>
    public float LoadSeasoning(string seasoningName) => TryLoadUpTo(seasoningName, float.MaxValue) ? LoadedMl : 0f;

    /// <summary>A spoonful of the end in use (15 or 5 mL) - or, with less than
    /// that left where it comes from (availableMl), just what's left, as in a
    /// real kitchen (no message). An already loaded spoon takes nothing more.
    /// True if anything was taken - the caller then takes LoadedMl out of the
    /// source (never more than there was: nothing is made up).</summary>
    public bool TryLoadUpTo(string seasoningName, float availableMl)
    {
        if (string.IsNullOrEmpty(seasoningName) || IsSpoonLoaded || !(availableMl > 0.05f)) return false;
        float ml = Mathf.Min(SpoonCapacityMl, availableMl);
        if (ml < SpoonCapacityMl) Debug.Log($"[계량] {seasoningName}: 남은 {ml:0.#}mL만 떠짐 ({SpoonSizeLabel})");
        loadedSeasoning = seasoningName;
        LoadedSauce = null;
        LoadedMl = ml;
        transform.localScale = BaseLocalScale * LoadedSpoonScaleBulge;
        ShowSpoonContent(seasoningName);
        return true;
    }

    /// <summary>All of it tipped out (into whatever it was used on): the mL it
    /// held, for the caller to add there. The spoon is empty after.</summary>
    public float ConsumeSeasoning()
    {
        float ml = IsSpoonLoaded ? LoadedMl : 0f;
        loadedSeasoning = null;
        LoadedSauce = null;
        LoadedMl = 0f;
        transform.localScale = BaseLocalScale;
        if (spoonContent != null) Destroy(spoonContent);
        return ml;
    }

    /// <summary>The dose sitting in the end in use: a dry heap for a powder, a
    /// thin glossy pool for a liquid, a dollop for a thick sauce - smaller when
    /// it's less than a level spoonful.</summary>
    private void ShowSpoonContent(string seasoningName)
    {
        if (spoonContent != null) Destroy(spoonContent);
        MeshFilter mf = GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        if (!TrySpoonBowl(mf, out Vector3 center, out Vector3 up, out float radius)) return;
        Condiments.Info info = Condiments.Get(seasoningName);
        float full = Mathf.Clamp01(LoadedMl / Mathf.Max(SpoonCapacityMl, 0.01f));
        spoonContent = Condiments.BuildContent(mf.transform, center + up * radius * 0.15f, up,
            radius * 0.85f * Mathf.Max(0.45f, Mathf.Sqrt(full)), info.form, info.color, "SpoonContent");
    }

    // ---- 큰술 ↔ 작은술 (turned round in hand) ----

    private float spoonSpin;               // degrees about the counter's up, on top of the held pose
    private Quaternion gripBaseRot = Quaternion.identity;
    private Vector3 gripBaseOffset;
    private IClickable flipAction;

    /// <summary>What a long press with the spoon in hand does.</summary>
    public IClickable FlipAction => flipAction ?? (flipAction = new FlipSpoon(this));

    private sealed class FlipSpoon : IClickable
    {
        private readonly ToolItem spoon;
        public FlipSpoon(ToolItem s) { spoon = s; }
        public void OnClicked() => spoon.TryFlip();
    }

    /// <summary>Turns the spoon in hand round to its other end - not with
    /// anything on it (that'd spill).</summary>
    public bool TryFlip()
    {
        if (kind != ToolKind.MeasuringSpoon || !IsHeld || IsFlipping) return false;
        if (IsSpoonLoaded)
        {
            Debug.Log("[계량스푼] 담긴 채로는 뒤집지 않음 (내용물 먼저 비우기)"); // (no on-screen message)
            return false;
        }
        StartCoroutine(FlipRoutine());
        return true;
    }

    private IEnumerator FlipRoutine()
    {
        IsFlipping = true;
        HandCarry hand = HandCarry.Instance;
        bool wasLocked = hand != null && hand.IsLocked;
        if (hand != null) hand.IsLocked = true; // (no scooping, pouring or a second flip meanwhile)
        UsingTeaspoon = !UsingTeaspoon;
        float from = spoonSpin, to = ChooseSpin();
        if (Mathf.Abs(Mathf.DeltaAngle(from, to)) < 1f) to = from + 180f; // (always a visible turn)
        float delta = Mathf.DeltaAngle(from, to);
        if (Mathf.Abs(delta) > 179f) delta = 180f;                      // (the same way round each time)
        for (float t = 0f; t < flipSeconds; t += Time.deltaTime)
        {
            ApplySpin(from + delta * Mathf.SmoothStep(0f, 1f, t / flipSeconds));
            yield return null;
        }
        spoonSpin = Mathf.Repeat(from + delta, 360f);
        ApplySpin(spoonSpin);
        if (hand != null) hand.IsLocked = wasLocked;
        IsFlipping = false;
        Debug.Log($"[계량스푼] {SpoonSizeLabel} 쪽으로 잡음");
    }

    /// <summary>Picked up: held by the end in use (the 큰술 first), that end out
    /// away from the hand.</summary>
    private void AfterSpoonPickedUp()
    {
        if (kind != ToolKind.MeasuringSpoon) return;
        gripBaseRot = HoldTurn;
        gripBaseOffset = TempHoldOffset;
        spoonSpin = ChooseSpin();
        ApplySpin(spoonSpin);
    }

    /// <summary>Set down: as it rests. Put away empty (rack hook, tray, slot):
    /// back to 큰술 for next time.</summary>
    private void AfterSpoonPlaced()
    {
        if (kind != ToolKind.MeasuringSpoon) return;
        // (it can't be set down mid-turn: the hand is locked meanwhile)
        spoonSpin = 0f;
        SetTempHoldRotation(Quaternion.identity);
        SetTempHoldOffset(Vector3.zero);
        bool putAway = CurrentContainer is HangingRack || CurrentContainer is TraySlot || CurrentContainer is PlaceSlot
                       || ((Vector2)(transform.position - spoonHome)).magnitude < spoonHomeRadius;
        if (!IsSpoonLoaded && putAway)
        {
            if (UsingTeaspoon) Debug.Log("[계량스푼] 제자리에 반납 - 큰술로 돌아감");
            UsingTeaspoon = false;
        }
    }

    /// <summary>Where it's kept (its spot in the scene at the start).</summary>
    private Vector3 spoonHome;
    [Tooltip("Set down this close (on screen) to where it's kept counts as put away: back to 큰술.")]
    [SerializeField] private float spoonHomeRadius = 0.7f;

    /// <summary>The held pose turned deg about the counter's up - round the
    /// spoon's own middle, not its pivot.</summary>
    private void ApplySpin(float deg)
    {
        Quaternion r = Quaternion.AngleAxis(deg, Carryable.CounterUp);
        Vector3 c = gripBaseRot * RestingRotation * ScaledRootLocal(MeshCenterLocal());
        SetTempHoldRotation(r * gripBaseRot);
        SetTempHoldOffset(gripBaseOffset + c - r * c);
    }

    /// <summary>As held (0) or turned round (180): whichever puts the end in use
    /// farther out from the hand.</summary>
    private float ChooseSpin()
    {
        MeshFilter mf = GetComponentInChildren<MeshFilter>();
        if (mf == null) return 0f;
        Vector3 baseHold = HandHoldOffset - TempHoldOffset; // the item's pivot from the hand, unturned
        Vector3 c = gripBaseRot * RestingRotation * ScaledRootLocal(MeshCenterLocal());
        Vector3 end = ScaledRootLocal(ActiveEndLocal);
        float best = 0f, bestD = -1f;
        foreach (float deg in new[] { 0f, 180f })
        {
            Quaternion r = Quaternion.AngleAxis(deg, Carryable.CounterUp);
            Vector3 pivot = baseHold + gripBaseOffset + c - r * c;
            Vector3 endWorld = pivot + r * gripBaseRot * RestingRotation * end;
            float d = ((Vector2)endWorld).sqrMagnitude;
            if (d > bestD) { bestD = d; best = deg; }
        }
        return best;
    }

    /// <summary>The mesh's middle, in the spoon mesh's local space.</summary>
    private Vector3 MeshCenterLocal()
    {
        MeshFilter mf = GetComponentInChildren<MeshFilter>();
        return mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.center : Vector3.zero;
    }

    /// <summary>A point in the spoon mesh's local space, as an offset from this
    /// object's pivot in its own (unrotated, scaled) frame.</summary>
    private Vector3 ScaledRootLocal(Vector3 meshLocal)
    {
        MeshFilter mf = GetComponentInChildren<MeshFilter>();
        if (mf == null) return Vector3.zero;
        Vector3 rootLocal = transform.InverseTransformPoint(mf.transform.TransformPoint(meshLocal));
        return Vector3.Scale(rootLocal, transform.lossyScale);
    }

    // ---- 큰술 1T · 15mL label while held ----

    [Tooltip("The 큰술 1T · 15mL text over the spoon in hand.")]
    [SerializeField] private bool showSpoonLabel = false;
    private TextMesh spoonLabel;

    private void UpdateSpoonLabel()
    {
        // (off by request - the spoon's end and what's on it show on the spoon itself)
        bool show = showSpoonLabel && IsHeld && HandCarry.Instance != null && HandCarry.Instance.Current == this;
        if (!show)
        {
            if (spoonLabel != null && spoonLabel.gameObject.activeSelf) spoonLabel.gameObject.SetActive(false);
            return;
        }
        if (spoonLabel == null)
        {
            var go = new GameObject("SpoonSizeLabel");
            spoonLabel = go.AddComponent<TextMesh>();
            KoreanFontFallback.Apply(spoonLabel); // 웹에서도 한글이 보이게 한글 글꼴
            spoonLabel.fontSize = 40;
            spoonLabel.characterSize = 0.045f;
            spoonLabel.anchor = TextAnchor.LowerCenter;
            spoonLabel.alignment = TextAlignment.Center;
            spoonLabel.color = new Color(0.15f, 0.15f, 0.15f);
        }
        if (!spoonLabel.gameObject.activeSelf) spoonLabel.gameObject.SetActive(true);
        Bounds b = new Bounds(transform.position, Vector3.zero);
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) if (r.enabled) b.Encapsulate(r.bounds);
        // 화면 기준으로 스푼 바로 위에 띄우고 카메라를 정면으로 보게 한다 (TextMesh 는 로컬 +Z 쪽에서 읽힌다).
        Camera labelCam = Camera.main; // 라벨을 세울 기준
        if (labelCam != null)
        {
            spoonLabel.transform.position = b.center + labelCam.transform.up * (b.extents.magnitude + 0.25f) - labelCam.transform.forward * 0.3f; // 스푼 위, 카메라 쪽으로 당겨서
            spoonLabel.transform.rotation = Quaternion.LookRotation(-labelCam.transform.forward, labelCam.transform.up); // 글자가 화면을 향하게
        }
        else spoonLabel.transform.position = new Vector3(b.center.x, b.max.y + 0.08f, b.min.z - 0.8f);
        spoonLabel.text = IsSpoonLoaded ? $"{SpoonSizeLabel}\n{loadedSeasoning} {LoadedMl:0.#}mL" : SpoonSizeLabel;

        // a light card behind it (read over the dark cabinet or other labels too)
        if (spoonLabelCard == null)
        {
            spoonLabelCard = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            spoonLabelCard.name = "Card";
            Destroy(spoonLabelCard.GetComponent<Collider>());
            spoonLabelCard.SetParent(spoonLabel.transform, false);
            Shader s = Shader.Find("Sprites/Default");
            spoonLabelCard.GetComponent<Renderer>().sharedMaterial = new Material(s) { color = new Color(1f, 0.98f, 0.92f, 0.88f) };
        }
        Bounds tb = spoonLabel.GetComponent<Renderer>().bounds;
        // 카드는 글자 '뒤'에 깔려야 한다. 옛 코드는 +Z 를 뒤로 봤지만 지금 카메라는 +Z 쪽에
        // 있어서, 그대로 두면 카드가 글자를 덮어 흰 네모만 보인다.
        if (labelCam != null)
        {
            spoonLabelCard.position = tb.center + labelCam.transform.forward * 0.02f; // 시선 방향으로 더 멀리 = 글자 뒤
            spoonLabelCard.rotation = Quaternion.LookRotation(labelCam.transform.forward, labelCam.transform.up); // Quad 앞면(-Z)이 카메라를 향하게
        }
        else spoonLabelCard.position = new Vector3(tb.center.x, tb.center.y, tb.center.z + 0.02f);
        spoonLabelCard.localScale = new Vector3(tb.size.x + 0.12f, tb.size.y + 0.06f, 1f);
    }

    private Transform spoonLabelCard;

    /// <summary>A short note over the spoon (why it can't be turned round, or filled).</summary>
    public void SpoonNotice(string text)
    {
        Debug.Log($"[계량스푼] {text}");
        var go = new GameObject("SpoonNote");
        Bounds b = new Bounds(transform.position, Vector3.zero);
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) if (r.enabled) b.Encapsulate(r.bounds);
        Camera noteCam = Camera.main; // 알림도 화면을 보고 떠야 한다
        go.transform.position = noteCam != null
            ? b.center + noteCam.transform.up * (b.extents.magnitude + 0.6f) - noteCam.transform.forward * 0.3f // 스푼 위, 카메라 쪽으로
            : new Vector3(b.center.x, b.max.y + 0.45f, b.min.z - 0.9f);
        if (noteCam != null) go.transform.rotation = Quaternion.LookRotation(-noteCam.transform.forward, noteCam.transform.up); // 글자가 화면을 향하게
        TextMesh tm = go.AddComponent<TextMesh>();
        KoreanFontFallback.Apply(tm); // 웹에서도 한글이 보이게 한글 글꼴
        tm.text = text;
        tm.fontSize = 36;
        tm.characterSize = 0.06f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = new Color(0.75f, 0.15f, 0.1f);
        Destroy(go, 2f);
    }
}
