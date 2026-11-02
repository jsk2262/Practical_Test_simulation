using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The flat look of a paper towel spread on a cutting board or plate (see
/// ToolItem.SpreadAsLiner) - stands in for the towel while it lies there:
/// empty-handed, clicking it picks the towel back up; anything else clicked
/// or dropped on it goes to the board/plate underneath.
/// With meat or tofu lying on it, an empty-handed click folds the towel's left
/// and right sides over it and presses - 핏물/물기 빼기. Folded, a click picks
/// the whole bundle up (towel round the piece) to carry it elsewhere; held
/// down (press-and-hold) it opens back out flat, wherever it is.
/// </summary>
public class TowelSpread : Interactable
{
    [SerializeField] private float foldSeconds = 0.45f;

    private ToolItem towel;
    private Interactable under;

    private bool folded;
    public bool IsFolded => folded;

    /// <summary>The piece it's wrapped round while carried as a bundle.</summary>
    private IngredientItem bundledWith;
    private bool bundleWasHeld;

    /// <summary>What a press-and-hold on the folded towel does: open it out.</summary>
    public IClickable UnfoldAction => unfold ??= new Unfold(this);
    private Unfold unfold;

    private sealed class Unfold : IClickable
    {
        private readonly TowelSpread spread;
        public Unfold(TowelSpread s) { spread = s; }
        public void OnClicked() => spread.Open();
    }

    private void Open()
    {
        if (!folded || anim != null) return;
        anim = StartCoroutine(Fold(false));
    }
    private Coroutine anim;
    private Transform leftHinge, rightHinge, topHinge, bottomHinge;
    private GameObject center;

    public void Init(ToolItem towel, Interactable under)
    {
        this.towel = towel;
        this.under = under;
    }

    protected override void Activate()
    {
        HandCarry hand = HandCarry.Instance;
        if (hand == null || towel == null) return;
        if (hand.Current != null) { under?.ForceActivate(); return; }
        if (anim != null) return;

        if (folded) { PickUpBundle(hand); return; }
        if (PiecesOnIt().Count > 0) { anim = StartCoroutine(Fold(true)); return; }
        hand.PickUp(towel);
    }

    public override bool CanAcceptDrop(Carryable item) => under != null && under.CanAcceptDrop(item);

    /// <summary>Folded round the piece, the two go together: the towel lets go of
    /// its board/plate and rides on the piece, which is what the hand carries.</summary>
    private void PickUpBundle(HandCarry hand)
    {
        List<IngredientItem> pieces = PiecesOnIt();
        if (pieces.Count == 0) return;
        IngredientItem piece = pieces[0];
        if (towel.CurrentContainer is ISlotContainer old) old.Release(towel);
        towel.CurrentContainer = null;
        transform.SetParent(piece.transform, true);
        GetComponent<Collider>().enabled = false;
        bundledWith = piece;
        hand.PickUp(piece);
        bundleWasHeld = true;
    }

    void Update()
    {
        if (bundledWith != null)
        {
            // Set down: the towel's here now - this board/plate's liner.
            if (bundleWasHeld && !bundledWith.IsHeld)
            {
                bundleWasHeld = false;
                GetComponent<Collider>().enabled = true;
                under = bundledWith.CurrentContainer as Interactable;
                if (bundledWith.CurrentContainer is ITowelLined lined) lined.AdoptLiner(towel);
            }
            else if (!bundleWasHeld && bundledWith.IsHeld) bundleWasHeld = true;
            return;
        }

        // The piece was taken off while it was wrapped: the towel falls open.
        if (folded && anim == null && PiecesOnIt().Count == 0) anim = StartCoroutine(Fold(false));
    }

    private List<IngredientItem> PiecesOnIt()
    {
        var list = new List<IngredientItem>();
        if (bundledWith != null) { list.Add(bundledWith); return list; }
        if (under is PlaceSlot slot && slot.Current is IngredientItem onBoard) list.Add(onBoard);
        else if (under is MovableBowl bowl)
            foreach (Carryable c in bowl.Contents) if (c is IngredientItem ing) list.Add(ing);
        return list;
    }

    /// <summary>Left and right thirds of the sheet hinge over the middle (in front
    /// of the piece lying there), press for a moment - drawing off its blood or
    /// water - and stay folded; or open back out.</summary>
    private IEnumerator Fold(bool close)
    {
        Renderer flat = GetComponent<Renderer>();
        BuildFlaps(flat);
        List<IngredientItem> pieces = PiecesOnIt();
        if (close) FitTo(pieces);

        // How far in front of the sheet the flaps must come to lie over the piece.
        float lift = 0.02f;
        foreach (IngredientItem p in pieces) // 시트 바깥 방향으로 재료를 덮을 높이를 구한다.
        {
            Bounds b = p.VisibleBounds;
            Vector3 outward = -transform.forward;
            Vector3 abs = new Vector3(Mathf.Abs(outward.x), Mathf.Abs(outward.y), Mathf.Abs(outward.z));
            lift = Mathf.Max(lift, Vector3.Dot(b.center - transform.position, outward) + Vector3.Dot(b.extents, abs) + 0.02f);
        }
        float liftLocal = lift / Mathf.Max(transform.TransformVector(Vector3.forward).magnitude, 1e-4f); // 회전된 부모의 실제 축 배율

        flat.enabled = false;
        SetFlapsVisible(true);
        for (float t = 0f; t <= foldSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / foldSeconds);
            if (!close) k = 1f - k;
            Pose(k, liftLocal);
            yield return null;
        }
        Pose(close ? 1f : 0f, liftLocal);

        if (close)
        {
            HandController.Instance?.PlayChopGesture(); // press down on it
            foreach (IngredientItem p in pieces) p.BlotWithTowel(); // 실제 물기·핏물 제거 상태를 기록한다.
            foreach (IngredientItem p in pieces)
                if (p.Category == IngredientCategory.Meat && p.DrainOnTowel == IngredientItem.DrainNeed.Blood)
                {
                    Material paper = center.GetComponent<Renderer>().material; // 핏물이 묻은 타올의 색
                    Color absorbed = new Color(0.86f, 0.70f, 0.68f);
                    paper.color = absorbed;
                    if (paper.HasProperty("_BaseColor")) paper.SetColor("_BaseColor", absorbed);
                    foreach (Renderer flap in GetComponentsInChildren<Renderer>(true))
                        if (flap != flat) flap.sharedMaterial = paper;
                    break;
                }
        }
        else
        {
            SetFlapsVisible(false);
            flat.enabled = true;
            // Opened out: it lies on the board/plate now, not on the piece.
            if (bundledWith != null)
            {
                transform.SetParent(bundledWith.transform.parent, true);
                bundledWith = null;
                bundleWasHeld = false;
            }
        }
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null)
        {
            box.center = close ? new Vector3((coverMinX + coverMaxX) * 0.5f,
                (coverMinY + coverMaxY) * 0.5f, -liftLocal * 0.5f) : Vector3.zero;
            box.size = close ? new Vector3(coverMaxX - coverMinX, coverMaxY - coverMinY,
                liftLocal + 0.02f) : new Vector3(1f, 1f, 0.02f);
        }
        folded = close;
        anim = null;
    }

    // 접혔을 때 덮는 영역 (시트 로컬 좌표, 시트 전체가 -0.5~0.5)
    private float coverMinX = -1f / 6f, coverMaxX = 1f / 6f, coverMinY = -0.5f, coverMaxY = 0.5f;

    /// <summary>접기 전에, 재료가 어디에 어떤 크기로 놓였든 접힌 타월이 재료
    /// 전체를 덮도록 가운데 면과 양쪽 날개의 크기·위치를 맞춘다 (재료의 보이는
    /// 범위를 시트 평면에 투영하고 여유를 둔다). 날개는 각각 가운데 면 전체
    /// 너비라서 한쪽만으로도 다 덮인다.</summary>
    private void FitTo(List<IngredientItem> pieces)
    {
        float minX = -1f / 6f, maxX = 1f / 6f, minY = -0.5f, maxY = 0.5f;
        bool any = false;
        float pMinX = 0f, pMaxX = 0f, pMinY = 0f, pMaxY = 0f;
        foreach (IngredientItem p in pieces)
        {
            Bounds b = p.VisibleBounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 l = transform.InverseTransformPoint(corner);
                if (!any) { pMinX = pMaxX = l.x; pMinY = pMaxY = l.y; any = true; }
                else { pMinX = Mathf.Min(pMinX, l.x); pMaxX = Mathf.Max(pMaxX, l.x); pMinY = Mathf.Min(pMinY, l.y); pMaxY = Mathf.Max(pMaxY, l.y); }
            }
        }
        if (any)
        {
            // (가장자리가 살짝 보이지 않게 크기의 15% + 고정 여유)
            float padX = (pMaxX - pMinX) * 0.05f + 0.01f, padY = (pMaxY - pMinY) * 0.05f + 0.01f; // 재료에 바짝 붙게 덮는다.
            minX = pMinX - padX; maxX = pMaxX + padX;
            minY = pMinY - padY; maxY = pMaxY + padY;
        }
        coverMinX = minX; coverMaxX = maxX; coverMinY = minY; coverMaxY = maxY;

        float w = maxX - minX, h = maxY - minY, cy = (minY + maxY) * 0.5f;
        center.transform.localPosition = new Vector3((minX + maxX) * 0.5f, cy, 0f);
        center.transform.localScale = new Vector3(w, h, 1f);
        Transform lf = leftHinge.GetChild(0), rf = rightHinge.GetChild(0);
        lf.localPosition = new Vector3(-w * 0.5f, cy, 0f);
        lf.localScale = new Vector3(w, h, 1f);
        rf.localPosition = new Vector3(w * 0.5f, cy, 0f);
        rf.localScale = new Vector3(w, h, 1f);
        float cx = (minX + maxX) * 0.5f;
        Transform tf = topHinge.GetChild(0), bf = bottomHinge.GetChild(0);
        tf.localPosition = new Vector3(cx, h * 0.25f, 0f);
        bf.localPosition = new Vector3(cx, -h * 0.25f, 0f);
        tf.localScale = bf.localScale = new Vector3(w, h * 0.5f, 1f);
    }

    private void Pose(float k, float liftLocal)
    {
        float sides = Mathf.Clamp01(k * 1.5f); // 양옆이 조금 앞서 접힌다.
        leftHinge.localRotation = Quaternion.AngleAxis(-180f * sides, Vector3.up);
        rightHinge.localRotation = Quaternion.AngleAxis(180f * sides, Vector3.up);
        float z = -liftLocal * k; // 접히는 내내 재료 높이를 따라 올라온다.
        leftHinge.localPosition = new Vector3(coverMinX, 0f, z);
        rightHinge.localPosition = new Vector3(coverMaxX, 0f, z - 0.001f);
        float ends = Mathf.Clamp01((k - 0.35f) / 0.65f); // 위아래는 조금 늦게 따라 접힌다.
        topHinge.localRotation = Quaternion.AngleAxis(-180f * ends, Vector3.right);
        bottomHinge.localRotation = Quaternion.AngleAxis(180f * ends, Vector3.right);
        topHinge.localPosition = new Vector3(0f, coverMaxY, -liftLocal * ends - 0.003f);
        bottomHinge.localPosition = new Vector3(0f, coverMinY, -liftLocal * ends - 0.005f);
    }

    private void BuildFlaps(Renderer flat)
    {
        if (center != null) return;
        // Unlit, both faces: a flap shows its back once folded over, and a lit
        // back face came out a dull blue-grey instead of paper white.
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        Material m = unlit != null ? new Material(unlit) : new Material(flat.sharedMaterial);
        Color paper = new Color(0.93f, 0.93f, 0.91f);
        m.color = paper;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", paper);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);

        center = Part("Middle", transform, new Vector3(0f, 0f, 0f), 1f / 3f, m);
        leftHinge = new GameObject("LeftHinge").transform;
        leftHinge.SetParent(transform, false);
        rightHinge = new GameObject("RightHinge").transform;
        rightHinge.SetParent(transform, false);
        Part("LeftFlap", leftHinge, new Vector3(-1f / 6f, 0f, 0f), 1f / 3f, m);
        Part("RightFlap", rightHinge, new Vector3(1f / 6f, 0f, 0f), 1f / 3f, m);
        topHinge = new GameObject("TopHinge").transform;
        bottomHinge = new GameObject("BottomHinge").transform;
        topHinge.SetParent(transform, false);
        bottomHinge.SetParent(transform, false);
        Part("TopFlap", topHinge, Vector3.zero, 1f, m);
        Part("BottomFlap", bottomHinge, Vector3.zero, 1f, m);
        Pose(0f, 0f);
    }

    private static GameObject Part(string name, Transform parent, Vector3 local, float width, Material m)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = new Vector3(width, 1f, 1f);
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.SetActive(false);
        return go;
    }

    private void SetFlapsVisible(bool on)
    {
        center.SetActive(on);
        leftHinge.GetChild(0).gameObject.SetActive(on);
        rightHinge.GetChild(0).gameObject.SetActive(on);
        topHinge.GetChild(0).gameObject.SetActive(on);
        bottomHinge.GetChild(0).gameObject.SetActive(on);
    }
}
