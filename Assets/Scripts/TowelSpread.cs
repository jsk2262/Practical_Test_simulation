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
    private Transform leftHinge, rightHinge;
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

        // How far in front of the sheet the flaps must come to lie over the piece.
        float lift = 0.02f;
        foreach (IngredientItem p in pieces)
            lift = Mathf.Max(lift, transform.position.z - p.VisibleBounds.min.z + 0.02f);
        float liftLocal = lift / Mathf.Max(Mathf.Abs(transform.lossyScale.z), 1e-4f);

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
            foreach (IngredientItem p in pieces) p.BlotWithTowel();
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
        folded = close;
        anim = null;
    }

    private void Pose(float k, float liftLocal)
    {
        leftHinge.localRotation = Quaternion.AngleAxis(-180f * k, Vector3.up);
        rightHinge.localRotation = Quaternion.AngleAxis(180f * k, Vector3.up);
        float z = -liftLocal * k;
        leftHinge.localPosition = new Vector3(-1f / 6f, 0f, z);
        rightHinge.localPosition = new Vector3(1f / 6f, 0f, z - 0.001f);
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
    }
}
