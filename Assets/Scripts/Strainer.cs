using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 체망 (strainer) - add this (instead of a plain ToolItem) to the strainer
/// model once it's in the scene. The blanching flow it completes:
/// 1) Held empty, clicked on a bowl/pot: scoops only pieces inside the basket area
///    (e.g. the peas off their plate, or blanched peas out of the water); they
///    ride along in the basket.
/// 2) Held full, clicked on a pot boiling on a lit burner: 데치기 - the strainer
///    is set down in the pot, the pieces in the boiling water; taking it back
///    up lifts them out again.
/// 3) Held full, clicked on a bowl of (cold) water: they're tipped into it to
///    cool (찬물에 식히기 - CookableItem.ColdShock, locking in the blanched
///    color); the empty strainer scoops them back out. At the sink: a rinse
///    under the tap, staying in the basket.
/// 4) Held full, clicked on a dry bowl/plate: tips them all in there.
/// Picking it up, carrying and setting it down work like any other tool.
/// </summary>
public partial class Strainer : ToolItem
{
    /// <summary>How the scooped pieces are spread inside the strainer (world units).</summary>
    [SerializeField] private float contentSpread = 0.12f;

    private readonly List<IngredientItem> contents = new List<IngredientItem>();
    private readonly List<Collider> contentColliders = new List<Collider>();

    public bool HasContents => contents.Count > 0;

    protected override void Awake()
    {
        // Always a strainer - the inherited kind would otherwise default to
        // Knife and let this cut ingredients on the board.
        kind = ToolKind.Strainer;
        base.Awake();
    }

    public void ScoopFrom(MovableBowl vessel)
    {
        BeginLocalScoop(vessel);
    }

    /// <summary>The pieces spread in the basket (not at the pivot, which is the
    /// handle's end), just in front of its mesh.</summary>
    private void LayoutContents()
    {
        Vector3 basket = BasketCenterWorld(out float radius);
        float spread = Mathf.Min(contentSpread, radius * 0.35f);
        for (int i = 0; i < contents.Count; i++)
        {
            float angle = i * 2.4f; // golden-angle spiral: evenly spread, no overlap
            Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * (spread * Mathf.Sqrt(i));
            IngredientItem ing = contents[i];
            // cut pieces (the diced tofu) heaped to fit the basket, the heap - not
            // the ingredient's pivot - in the middle of it
            ing.GatherPieces(radius * 0.75f);
            Vector3 target = basket + offset + new Vector3(0f, 0f, -0.05f);
            ing.transform.position = target;
            Vector3 shown = ing.VisibleBounds.center;
            ing.transform.position += new Vector3(target.x - shown.x, target.y - shown.y, 0f);
        }
    }

    /// <summary>Middle of the basket (world): the end of the strainer's long
    /// axis away from its grip, one basket radius in.</summary>
    public Vector3 BasketCenterWorld(out float radius)
    {
        MeshFilter mf = GetComponentInChildren<MeshFilter>();
        radius = 0.2f;
        if (mf == null || mf.sharedMesh == null) return transform.position;
        Bounds lb = mf.sharedMesh.bounds;
        Vector3 size = lb.size;
        int len = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        float across = Mathf.Max(size[(len + 1) % 3], size[(len + 2) % 3]);
        Vector3 grip = mf.transform.InverseTransformDirection(transform.TransformDirection(GripLocalDir));
        Vector3 c = lb.center;
        c[len] = grip[len] > 0f ? lb.min[len] + across * 0.5f : lb.max[len] - across * 0.5f;
        Vector3 world = mf.transform.TransformPoint(c);
        radius = mf.transform.TransformVector(Vector3.one * across * 0.5f / 1.732f).magnitude;
        return world;
    }

    /// <summary>The pot it's standing in while the pieces blanch (데치기).</summary>
    private MovableBowl dippedIn;
    /// <summary>The pieces it brought into the pot - only these come back up with it.</summary>
    private readonly List<IngredientItem> dipped = new List<IngredientItem>();

    [Tooltip("데치기: 끄면 콩만 끓는 물에 넣고 체망은 손에 남는다 (위생 - 체망째 냄비에 넣어 두지 않음). 켜면 예전처럼 체망째 냄비에 세워 둔다.")]
    [SerializeField] private bool leaveInPotWhileBlanching = false;

    /// <summary>데치기: 재료(완두콩)만 끓는 물에 넣고 체망은 손에 남는다 - 다 데쳐지면
    /// 빈 체망으로 누른 자리에서 건진다 (StrainerLocalScoop). 체망째 냄비에 넣어 두면
    /// 위생 감점 소지가 있어서. (leaveInPotWhileBlanching을 켜면 예전 방식)</summary>
    public void DipInto(MovableBowl pot)
    {
        HandCarry hand = HandCarry.Instance;
        if (!HasContents || hand == null || (object)hand.Current != this) return;
        // Into the middle of the pot's water, not wherever the basket was held.
        CenterIn(pot);
        if (!leaveInPotWhileBlanching)
        {
            DumpInto(pot);
            HandController.Instance?.PlayChopGesture();
            Debug.Log($"[체망] {pot.name}: 재료만 끓는 물에 넣음 - 체망은 손에 (다 데쳐지면 빈 체망으로 건지세요)");
            return;
        }
        dipped.Clear();
        dipped.AddRange(contents);
        DumpInto(pot);

        hand.Detach();
        transform.SetParent(pot.transform, true);
        Vector3 basketDir = RestingRotation * -GripLocalDir;
        float turn = Vector2.SignedAngle(new Vector2(basketDir.x, basketDir.y), new Vector2(-0.35f, -1f));
        transform.rotation = Quaternion.AngleAxis(turn, Vector3.forward) * RestingRotation;
        Vector3 body = pot.BodyCenterWorld();
        Vector3 basket = BasketCenterWorld(out float basketRadius);
        transform.position += new Vector3(body.x - basket.x, body.y - basket.y, body.z - basket.z);
        // The basket down in the water (hidden under its surface, handle sticking
        // out) - in front of it, it covered the very pieces it put in.
        if (pot.WaterSurface != null)
        {
            Vector3 up = pot.transform.TransformDirection(Vector3.up).normalized;
            if (Vector3.Dot(up, new Vector3(0f, 0.6f, -1f)) < 0f) up = -up;
            float s = Vector3.Dot(BasketCenterWorld(out _) - pot.WaterSurface.position, up);
            transform.position += up * (-basketRadius * 0.6f - s);
        }
        dippedIn = pot;
        HandController.Instance?.PlayChopGesture();
    }

    // ---- 물기 빼기: resting across a bowl ----

    [Tooltip("Seconds resting across a bowl before what's in it has drained (물기 빼기).")]
    [SerializeField] private float drainSeconds = 4f;

    /// <summary>The bowl it's resting across, letting the pieces drain into it.</summary>
    private MovableBowl restingOn;
    private float drainTimer;
    private bool drained;

    public bool IsResting => restingOn != null;

    /// <summary>Set down across a bowl (bowlSize2): the basket in its mouth,
    /// the handle up over the rim - what's in it drips into the bowl and, after
    /// drainSeconds, has had its water drained off (like the paper towel).</summary>
    public bool RestOn(MovableBowl bowl)
    {
        HandCarry hand = HandCarry.Instance;
        if (bowl == null || hand == null || (object)hand.Current != this) return false;
        hand.Detach();
        transform.SetParent(bowl.transform, true);

        // handle toward 6 o'clock (down, over the bowl's front rim), basket up in the mouth
        Vector3 basketDir = RestingRotation * -GripLocalDir;
        float turn = Vector2.SignedAngle(new Vector2(basketDir.x, basketDir.y), Vector2.up);
        transform.rotation = Quaternion.AngleAxis(turn, Vector3.forward) * RestingRotation;
        MovableBowl.Interior inside = bowl.GetInterior();
        Vector3 mouth = inside.floorCenter + inside.up * (inside.rim * 0.85f);
        Vector3 basket = BasketCenterWorld(out _);
        transform.position += mouth - basket;
        LayoutContents();

        restingOn = bowl;
        drainTimer = 0f;
        drained = false;
        HandController.Instance?.PlayChopGesture();
        Debug.Log($"[체망] {bowl.name}에 걸쳐 놓음 - 물기 빼는 중 ({contents.Count}개)");
        return true;
    }

    void Update()
    {
        if (restingOn == null) return;
        if (IsHeld || !transform.IsChildOf(restingOn.transform)) { restingOn = null; return; }
        if (!HasContents) return;

        // The water runs off into the bowl underneath...
        drainTimer += Time.deltaTime;
        if (restingOn.CanHoldWater)
            restingOn.SetWaterLevel(Mathf.Max(restingOn.WaterAmount, Mathf.Min(0.12f, drainTimer / Mathf.Max(drainSeconds, 0.1f) * 0.12f)));
        // ...and once it's stopped, they're drained.
        if (!drained && drainTimer >= drainSeconds)
        {
            drained = true;
            foreach (IngredientItem ing in contents) if (ing != null) ing.DrainInStrainer();
        }
    }

    public override void OnPickedUp()
    {
        restingOn = null;
        base.OnPickedUp();
        // Taken back up out of the pot: the blanched pieces come up in the basket.
        if (dippedIn != null)
        {
            MovableBowl pot = dippedIn;
            dippedIn = null;
            LiftOut(pot);
        }
    }

    /// <summary>Back up out of the pot: just the pieces it went in with (not
    /// whatever else is cooking in that pot, e.g. the tofu).</summary>
    private void LiftOut(MovableBowl pot)
    {
        foreach (IngredientItem ing in dipped)
        {
            if (ing == null || (object)ing.CurrentContainer != pot) continue;
            pot.Release(ing);
            ing.CurrentContainer = null;
            foreach (Collider c in ing.GetComponentsInChildren<Collider>())
                if (c.enabled) { c.enabled = false; contentColliders.Add(c); }
            ing.transform.SetParent(transform, true);
            contents.Add(ing);
        }
        dipped.Clear();
        LayoutContents();
    }

    /// <summary>찬물에 식히기: the pieces are tipped into a bowl of cold water
    /// (in the middle of it, in the water) and cooled there - scoop them back
    /// out with the empty strainer when they're cold.</summary>
    public void RinseIn(MovableBowl coldWater)
    {
        var pieces = new List<IngredientItem>(contents);
        CenterIn(coldWater);
        DumpInto(coldWater);
        foreach (IngredientItem ing in pieces)
            if ((object)ing.CurrentContainer == coldWater) ing.GetComponent<CookableItem>()?.ColdShock();
        HandController.Instance?.PlayChopGesture();
    }

    /// <summary>Lines the pieces up in the middle of a bowl's water before they're
    /// tipped in (they land where they are, which otherwise was the basket's spot).</summary>
    private void CenterIn(MovableBowl bowl)
    {
        Vector3 mid = bowl.BodyCenterWorld();
        for (int i = 0; i < contents.Count; i++)
        {
            float angle = i * 2.4f;
            contents[i].transform.position = mid + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * 0.5f, 0f) * (0.05f * Mathf.Sqrt(i));
        }
    }

    /// <summary>One piece put in the basket: carried in by hand onto the strainer
    /// lying on the counter, or scooped up with the strainer in hand.</summary>
    public bool TakeIn(IngredientItem ing)
    {
        if (ing == null || contents.Contains(ing)) return false;
        HandCarry hand = HandCarry.Instance;
        if ((object)hand?.Current == ing) hand.Detach();
        if (ing.CurrentContainer != null) { ing.CurrentContainer.Release(ing); ing.CurrentContainer = null; }
        foreach (Collider c in ing.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled) continue;
            c.enabled = false;
            contentColliders.Add(c);
        }
        ing.transform.SetParent(transform, true);
        contents.Add(ing);
        LayoutContents();
        HandController.Instance?.PlayChopGesture();
        return true;
    }

    // A piece carried onto the strainer (wherever it's resting) goes in its basket.
    public override bool CanAcceptDrop(Carryable item) => (item is IngredientItem && !IsHeld) || base.CanAcceptDrop(item);

    protected override void OnClickedWhileHandFull(Carryable held)
    {
        if (held is IngredientItem ing && !IsHeld && TakeIn(ing)) return;
        base.OnClickedWhileHandFull(held);
    }

    /// <summary>Resting across a bowl with something in it, a plain click (no
    /// hold - that still picks it up) tips it all into the bowl underneath -
    /// drained or not (not drained: the water goes in with it).</summary>
    protected override bool OnClickedWhileHandEmpty()
    {
        if (IsResting && HasContents)
        {
            MovableBowl bowl = restingOn;
            int n = contents.Count;
            CenterIn(bowl);
            DumpInto(bowl);
            HandController.Instance?.PlayChopGesture();
            Debug.Log($"[체망] {bowl.name}에 쏟음 ({n - contents.Count}개{(drained ? "" : ", 물기 덜 빠짐")})");
            return true;
        }
        return base.OnClickedWhileHandEmpty();
    }

    /// <summary>Everything in the basket, handed over to be thrown away.</summary>
    public List<IngredientItem> TakeAllForDisposal()
    {
        var all = new List<IngredientItem>(contents);
        foreach (Collider c in contentColliders) if (c != null) c.enabled = true;
        contentColliders.Clear();
        foreach (IngredientItem ing in all) if (ing != null) ing.transform.SetParent(null, true);
        contents.Clear();
        dipped.Clear();
        return all;
    }

    /// <summary>Cold-water rinse at the sink.</summary>
    public void RinseInColdWater()
    {
        foreach (IngredientItem ing in contents)
            ing.GetComponent<CookableItem>()?.ColdShock();
        // (the rinse under the tap is WashBasin's - no hand-washing motion with the strainer in hand)
    }

    public void DumpInto(MovableBowl bowl)
    {
        if (!HasContents) return;

        foreach (Collider c in contentColliders) if (c != null) c.enabled = true;
        contentColliders.Clear();

        foreach (IngredientItem ing in contents)
        {
            if (bowl.IsFull) break;
            Vector3 at = ing.transform.position;
            bowl.PlaceFree(ing, at);
            ing.transform.rotation = ing.RestingRotation;
            ing.transform.localScale = ing.BaseLocalScale;
            ing.CurrentContainer = bowl;
            ing.OnPlaced();
        }

        // Anything that didn't fit stays in the strainer.
        contents.RemoveAll(ing => (object)ing.CurrentContainer == bowl);
        foreach (IngredientItem ing in contents)
            foreach (Collider c in ing.GetComponentsInChildren<Collider>())
                if (c.enabled) { c.enabled = false; contentColliders.Add(c); }
    }
}
