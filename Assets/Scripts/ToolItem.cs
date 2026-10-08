using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ToolKind { Knife, CuttingBoard, Wok, Pot, Ladle, Spatula, Spoon, Chopsticks, Bowl, MeasuringCup, Strainer, SmallDish, Rag, Cloth, PaperTowel, Whisk, MeasuringSpoon }

public class ToolItem : Carryable
{
    [SerializeField] protected ToolKind kind;

    public ToolKind Kind => kind;

    private bool CanWrap => kind == ToolKind.PaperTowel || kind == ToolKind.Cloth || kind == ToolKind.Rag;

    /// <summary>Same towel/cloth/rag check as CanWrap, exposed publicly so other
    /// Interactables (the sink, a bowl) can offer this tool a place to be set
    /// down regardless of whether it's currently wrapping anything.</summary>
    public bool IsTowelKind => CanWrap;

    /// <summary>Whether this towel/cloth/rag currently has an ingredient
    /// wrapped inside - lets other Interactables (the cutting board, a bowl)
    /// tell "set this down" apart from their normal reaction to this tool.</summary>
    public bool IsWrapping => wrapped != null;

    private const float WrappedScaleBulge = 1.35f;

    private IngredientItem wrapped;
    private Collider wrappedCollider;
    private Renderer wrappedRenderer;

    private const float LoadedSpoonScaleBulge = 1.15f;
    private string loadedSeasoning;

    /// <summary>The scene's measuring spoon, registered by itself - a seasoning
    /// bottle "used" on something summons it into the hand for one scoop
    /// without needing a hand-wired reference on every bottle.</summary>
    public static ToolItem MeasuringSpoon { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        if (kind == ToolKind.MeasuringSpoon) MeasuringSpoon = this;
    }

    void OnDestroy()
    {
        if (MeasuringSpoon == this) MeasuringSpoon = null;
    }

    /// <summary>Whether this measuring spoon currently holds one scooped dose,
    /// waiting to be poured into a bowl.</summary>
    public bool IsSpoonLoaded => loadedSeasoning != null;
    public string LoadedSeasoning => loadedSeasoning;

    /// <summary>Scoops one dose of the given seasoning onto this (empty)
    /// measuring spoon - called by SeasoningBottle.OnClickedWhileHandFull when
    /// this spoon, held in hand, clicks a bottle resting anywhere in the scene.</summary>
    public void LoadSeasoning(string seasoningName)
    {
        if (IsSpoonLoaded && loadedSeasoning == seasoningName) return;
        // A spoon still holding a different dose is emptied first (tipped back)
        // rather than mixing two seasonings in one spoonful.
        loadedSeasoning = seasoningName;
        transform.localScale = BaseLocalScale * LoadedSpoonScaleBulge;
        ShowSpoonContent(seasoningName);
    }

    /// <summary>Empties the spoon after its one loaded dose has been poured into
    /// a bowl - ready to scoop again.</summary>
    public void ConsumeSeasoning()
    {
        loadedSeasoning = null;
        transform.localScale = BaseLocalScale;
        if (spoonContent != null) Destroy(spoonContent);
    }

    private GameObject spoonContent;

    /// <summary>The scooped spoonful, sitting in the spoon's bowl: a dry heap for
    /// a powder, a thin glossy pool for a liquid, a dollop for a thick sauce.</summary>
    private void ShowSpoonContent(string seasoningName)
    {
        if (spoonContent != null) Destroy(spoonContent);
        MeshFilter mf = GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        if (!TrySpoonBowl(mf, out Vector3 center, out Vector3 up, out float radius)) return;
        Condiments.Info info = Condiments.Get(seasoningName);
        spoonContent = Condiments.BuildContent(mf.transform, center + up * radius * 0.15f, up, radius * 0.85f, info.form, info.color, "SpoonContent");
    }

    /// <summary>The spoon's bowl in its mesh's own space: the end of the long
    /// axis away from the grip; its depth axis (the thinnest there) turned
    /// toward the camera, i.e. the open side.</summary>
    /// <summary>How much more to move the hand this frame (on screen) to bring
    /// the spoon's bowl over target - eased, so it follows the hand still on its
    /// way there.</summary>
    public static Vector3 TowardSpoonHead(ToolItem spoon, Vector3 target, float dt)
    {
        Vector3 err = target - spoon.SpoonBowlWorld();
        err.z = 0f;
        return err * Mathf.Clamp01(dt * 12f);
    }

    /// <summary>Where the measuring spoon's bowl is right now (world).</summary>
    public Vector3 SpoonBowlWorld()
    {
        MeshFilter mf = GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null || !TrySpoonBowl(mf, out Vector3 c, out _, out _)) return transform.position;
        return mf.transform.TransformPoint(c);
    }

    private bool TrySpoonBowl(MeshFilter mf, out Vector3 center, out Vector3 up, out float radius)
    {
        Bounds lb = mf.sharedMesh.bounds;
        Vector3 size = lb.size;
        int len = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        // The bowl is the end away from the grip: in hand, the end farther from
        // the hand holding it (also mid-scoop, when the bottle is what's "held");
        // otherwise the end opposite gripLocalDir.
        int side;
        HandController handCtl = HandController.Instance;
        if (handCtl != null && ((Vector2)(transform.position - handCtl.transform.position)).magnitude < 2.5f)
        {
            Vector3 maxEnd = lb.center, minEnd = lb.center;
            maxEnd[len] = lb.max[len]; minEnd[len] = lb.min[len];
            Vector3 hp = handCtl.transform.position;
            Vector2 dMax = (Vector2)(mf.transform.TransformPoint(maxEnd) - hp);
            Vector2 dMin = (Vector2)(mf.transform.TransformPoint(minEnd) - hp);
            side = dMax.sqrMagnitude >= dMin.sqrMagnitude ? 1 : -1;
        }
        else
        {
            Vector3 gripInMesh = mf.transform.InverseTransformDirection(transform.TransformDirection(gripLocalDir));
            side = gripInMesh[len] > 0f ? -1 : 1;
        }

        Bounds bowl = new Bounds();
        bool any = false;
        if (mf.sharedMesh.isReadable)
        {
            float cut = side > 0 ? lb.max[len] - size[len] * 0.3f : lb.min[len] + size[len] * 0.3f;
            foreach (Vector3 v in mf.sharedMesh.vertices)
            {
                if (side > 0 ? v[len] < cut : v[len] > cut) continue;
                if (!any) { bowl = new Bounds(v, Vector3.zero); any = true; }
                else bowl.Encapsulate(v);
            }
        }
        if (!any)
        {
            Vector3 c = lb.center;
            c[len] = side > 0 ? lb.max[len] - size[len] * 0.15f : lb.min[len] + size[len] * 0.15f;
            Vector3 s = size; s[len] *= 0.3f;
            bowl = new Bounds(c, s);
        }

        int a = (len + 1) % 3, b = (len + 2) % 3;
        int depth = bowl.size[a] <= bowl.size[b] ? a : b, across = depth == a ? b : a;
        up = Vector3.zero;
        up[depth] = 1f;
        Vector3 toCamera = new Vector3(0f, 0.5f, -1f);
        if (Vector3.Dot(mf.transform.TransformDirection(up), toCamera) < 0f) up = -up;
        center = bowl.center;
        radius = Mathf.Min(bowl.size[across], bowl.size[len]) * 0.5f;
        return radius > 0f;
    }

    /// <summary>Wiping the knife with a held towel resets its cross-contamination
    /// state, the same relief the cutting board already gets from a towel wipe -
    /// without this, the only way to clear a dirtied knife was carrying it all
    /// the way to the sink.</summary>
    /// <summary>Something dropped onto a spread towel is meant for the board/plate under it.</summary>
    public override bool CanAcceptDrop(Carryable item) =>
        IsLiner && CurrentContainer is Interactable under && under.CanAcceptDrop(item);

    protected override void OnClickedWhileHandFull(Carryable held)
    {
        // On a spread towel or hanging on a rack: a click with something else in
        // hand is meant for what this sits on (another ingredient onto the board,
        // another utensil onto the next hook).
        if ((IsLiner || CurrentContainer is HangingRack) && CurrentContainer is Interactable under)
        {
            under.ForceActivate();
            return;
        }

        if (kind == ToolKind.Knife && held is ToolItem towel && towel.IsTowelKind)
        {
            GetComponent<HygieneTracker>()?.Wash();
            ProcessStageManager.Instance?.MarkWiped();
        }
    }

    /// <summary>Picking up a cloth/towel while an ingredient sits on the cutting
    /// board scoops that ingredient up wrapped inside it - the real exam move of
    /// carrying something in a towel instead of bare hands. Tucked away (hidden,
    /// uninteractable) as a child until the towel is set back down somewhere.</summary>
    /// <summary>A paper towel spread flat on a cutting board or plate (see
    /// ITowelLined) for meat/tofu to drain on.</summary>
    public bool IsLiner => spread != null;
    private TowelSpread spread;

    /// <summary>Lays this (already placed) paper towel out flat: its own
    /// carried look is hidden and a flat white sheet of the given on-screen
    /// size (world XY) shows instead, just behind whatever gets placed on it.</summary>
    public void SpreadAsLiner(Interactable under, Transform root, Vector2 worldSize, Vector3 localPos)
    {
        EndLiner();
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name + "_Spread";
        Destroy(go.GetComponent<Collider>());
        go.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 0.02f);
        go.transform.SetParent(root, false);
        go.transform.localPosition = localPos;
        go.transform.rotation = Quaternion.identity;
        Vector3 rs = root.lossyScale;
        go.transform.localScale = new Vector3(worldSize.x / rs.x, worldSize.y / rs.y, 1f);

        Renderer rend = go.GetComponent<Renderer>();
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit != null) rend.material = new Material(lit);
        rend.material.color = new Color(0.97f, 0.97f, 0.95f);
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        spread = go.AddComponent<TowelSpread>();
        spread.Init(this, under);
    }

    /// <summary>Back to the carried look (picked up off the board/plate).</summary>
    private void EndLiner()
    {
        if (spread == null) return;
        Destroy(spread.gameObject);
        spread = null;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        foreach (Collider c in GetComponentsInChildren<Collider>(true)) c.enabled = true;
    }

    protected override bool OnClickedWhileHandEmpty()
    {
        if (!CanWrap) return false;

        CuttingBoardController board = CuttingBoardController.Instance;
        IngredientItem ingredient = board != null ? board.Current as IngredientItem : null;
        if (ingredient == null || (object)ingredient.CurrentContainer != board) return false;

        board.Release(ingredient);
        ingredient.CurrentContainer = null;

        wrapped = ingredient;
        wrappedCollider = ingredient.GetComponent<Collider>();
        wrappedRenderer = ingredient.GetComponent<Renderer>();
        if (wrappedCollider != null) wrappedCollider.enabled = false;
        if (wrappedRenderer != null) wrappedRenderer.enabled = false;

        ingredient.transform.SetParent(transform, false);
        ingredient.transform.localPosition = Vector3.zero;

        HandCarry.Instance.PickUp(this);

        // PickUp() just reset localScale to BaseLocalScale - bulge it out
        // afterward so a wrapped towel visibly looks like it's carrying
        // something instead of being indistinguishable from an empty one.
        transform.localScale = BaseLocalScale * WrappedScaleBulge;
        return true;
    }

    private bool IsUtensil => kind == ToolKind.Ladle || kind == ToolKind.Spatula || kind == ToolKind.Spoon
                           || kind == ToolKind.Chopsticks || kind == ToolKind.Whisk || kind == ToolKind.MeasuringSpoon;

    /// <summary>Which way the handle end points in the model's own space (the
    /// end that goes up on a rack hook) - the utensil models stand head-up, grip down.</summary>
    [SerializeField] private Vector3 gripLocalDir = Vector3.down;
    public Vector3 GripLocalDir => gripLocalDir;

    /// <summary>Learned from a pose: whichever of the model's own axes points
    /// up in the given rotation is its grip end (used for utensils posed
    /// hanging in the scene, whose models aren't all authored head-up).</summary>
    public void LearnGripFromHangingPose(Quaternion hangingRotation)
    {
        Vector3 v = Quaternion.Inverse(hangingRotation) * Vector3.up;
        Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        gripLocalDir = a.x >= a.y && a.x >= a.z ? new Vector3(Mathf.Sign(v.x), 0f, 0f)
                     : a.y >= a.z ? new Vector3(0f, Mathf.Sign(v.y), 0f)
                     : new Vector3(0f, 0f, Mathf.Sign(v.z));
    }

    /// <summary>Can go on a HangingRack hook (ladles, spatulas, whisks, spoons, the strainer).</summary>
    public bool IsHangable => IsUtensil || kind == ToolKind.Strainer || this is Strainer;
    private bool laidDownInHand;

    /// <summary>Utensils stand upright in the holder; carried that way they read
    /// as being lifted straight up. In hand they lie flat instead (head to the
    /// left, handle toward the right hand) - only while held, so they still go
    /// back into the holder standing.</summary>
    public override void OnPickedUp()
    {
        // Taken back off its board/plate (which already let go of it) - restored
        // first, so base.OnPickedUp sees (and later re-enables) its colliders.
        EndLiner();
        base.OnPickedUp();

        // Strainers (뜰채) are carried upright: basket at 12 o'clock, held by the
        // end of the handle below it - however it was resting or hanging.
        if (kind == ToolKind.Strainer || this is Strainer)
        {
            Vector3 grip = RestingRotation * gripLocalDir;
            Vector2 g = new Vector2(grip.x, grip.y);
            Quaternion upright = Quaternion.AngleAxis(g.sqrMagnitude < 1e-4f ? 0f : Vector2.SignedAngle(g, Vector2.down), Vector3.forward);
            laidDownInHand = true; // undone on placing, same as the laid-down utensils
            SetTempHoldRotation(upright);
            SetTempHoldOffset(-GripPointAlong(Vector2.down, upright * RestingRotation));
            return;
        }

        Renderer rend = GetComponent<Renderer>();
        laidDownInHand = IsUtensil && rend != null && rend.bounds.size.y > rend.bounds.size.x * 1.3f;
        if (!laidDownInHand) return;

        // Lying head-left: the right hand closes on the handle's end (now its right end).
        Quaternion lay = Quaternion.AngleAxis(90f, Vector3.forward);
        SetTempHoldRotation(lay);
        SetTempHoldOffset(-GripPointAlong(Vector2.right, lay * RestingRotation));
    }

    /// <summary>Setting the towel down anywhere unwraps whatever it was carrying,
    /// leaving the ingredient at the towel's own landing spot with its collider
    /// and renderer restored.</summary>
    public override void OnPlaced()
    {
        base.OnPlaced();

        // The knife set down with cut pieces still on its blade: they're left
        // right where they are.
        if (KnifeLoad != null) SetLoadDownHere();

        if (laidDownInHand)
        {
            laidDownInHand = false;
            SetTempHoldRotation(Quaternion.identity);
            SetTempHoldOffset(Vector3.zero);
        }

        // Set down mid-chop: drop the stance so neither the knife nor the hand
        // stays turned/lifted.
        if (chopMotionRoutine != null)
        {
            StopCoroutine(chopMotionRoutine);
            chopMotionRoutine = null;
            chopBlend = 0f;
            SetTempHoldRotation(Quaternion.identity);
            SetTempHoldOffset(Vector3.zero);
            HandController.Instance?.SetChopPose(0f, Vector3.zero);
        }

        if (wrapped == null) return;

        IngredientItem ingredient = wrapped;
        wrapped = null;

        ingredient.transform.SetParent(null, true);

        // Land just clear of the towel's own collider footprint (not just its
        // transform - the two can differ, e.g. an off-pivot mesh) so the two
        // don't overlap: a fixed small nudge isn't reliably outside a collider
        // whose actual bounds are bigger than its transform suggests. Placing
        // past the box's own extent on X guarantees no overlap on any axis.
        Collider towelCollider = GetComponent<Collider>();
        Bounds towelBounds = towelCollider != null
            ? towelCollider.bounds
            : new Bounds(transform.position, Vector3.zero);
        ingredient.transform.position = new Vector3(
            towelBounds.center.x + towelBounds.extents.x + 0.15f,
            towelBounds.center.y,
            towelBounds.center.z);

        if (wrappedCollider != null) wrappedCollider.enabled = true;
        if (wrappedRenderer != null) wrappedRenderer.enabled = true;
        wrappedCollider = null;
        wrappedRenderer = null;
    }

    [Header("Knife chop stance")]
    /// <summary>Flip the upright chop pose end-for-end if the blade (instead of
    /// the handle) ends up at 12 o'clock for this knife model.</summary>
    [SerializeField] private bool flipChopUpright;
    /// <summary>How high (world units) the knife lifts before each stroke.</summary>
    [SerializeField] private float chopLift = 0.25f;
    /// <summary>Lean of the upright knife (degrees, + = tip leaning left).</summary>
    [SerializeField] private float chopTiltZ = 25f;

    /// <summary>Sets the upright knife's lean (re-measured on the next cut).</summary>
    public void SetChopTilt(float degrees)
    {
        chopTiltZ = degrees;
        if (chopMotionRoutine == null) return;
        StopCoroutine(chopMotionRoutine);
        chopMotionRoutine = null;
        chopBlend = 0f;
        SetTempHoldRotation(Quaternion.identity);
        SetTempHoldOffset(Vector3.zero);
        HandController.Instance?.SetChopPose(0f, Vector3.zero);
    }

    private Coroutine chopMotionRoutine;
    private Quaternion uprightChopRotation = Quaternion.identity;
    private float chopBlend;
    private Vector3 chopGripVector;

    /// <summary>One up-and-down chop stroke - called by IngredientItem.CutRoutine
    /// on each chop pulse. The knife first turns into the chopping stance - tip
    /// at 12 o'clock (leaning slightly left, chopTiltZ), handle down at 6
    /// o'clock in the right hand, blade over the ingredient - then lifts and
    /// comes straight down. The stroke moves the HAND (the knife rides the hand
    /// every frame via HandCarry), so hand and knife never separate; the turn
    /// is Carryable's tempHoldRotation, layered on the knife's resting pose
    /// since HandCarry.LateUpdate would overwrite a direct transform change the
    /// very next frame.</summary>
    /// <summary>How long the knife stays up in the chop stance after a stroke
    /// before lying back down - a slow, careful sequence of cuts (the tofu's)
    /// raises it so the knife isn't put down between every cut.</summary>
    public float ChopHoldSeconds { get; set; } = 0.35f;

    public void PlayChopMotion()
    {
        if (kind != ToolKind.Knife || !IsHeld) return;
        if (chopMotionRoutine != null) StopCoroutine(chopMotionRoutine);
        else ComputeChopStance();
        chopMotionRoutine = StartCoroutine(ChopMotionRoutine());
    }

    /// <summary>Measured from the knife model itself (its mesh's longest bounds
    /// axis), so a swapped knife model doesn't need re-calibrating - the old
    /// fixed -90 degree turn was tuned for a previous knife and came out upside
    /// down with the cleaver. The resting knife lies with its handle toward the
    /// bottom of the screen, so the end that points more downward at rest is taken
    /// as the handle and kept pointing down; flipChopUpright swaps that.
    /// Also finds the grip point on the handle, so the handle - not the knife's
    /// pivot - is what ends up in the hand.</summary>
    private void ComputeChopStance()
    {
        uprightChopRotation = Quaternion.identity;
        chopGripVector = Vector3.zero;

        Renderer rend = GetComponent<Renderer>();
        if (rend == null) return;

        Bounds lb = rend.localBounds;
        Vector3 size = lb.size;
        Vector3 longLocal = size.x >= size.y && size.x >= size.z ? Vector3.right
                          : size.y >= size.z ? Vector3.up : Vector3.forward;
        float halfLength = Vector3.Dot(size, longLocal) * 0.5f;

        Vector2 axis = RestingRotation * longLocal;
        if (axis.sqrMagnitude < 1e-6f) return;

        // The knife's resting pose has its blade toward the top of the screen
        // and the black handle toward the bottom (taking the upper end as the
        // handle had the hand gripping the blade end instead).
        float handleSign = axis.y >= 0f ? -1f : 1f;
        if (flipChopUpright) handleSign = -handleSign;
        Vector2 handleDir = axis * handleSign;

        uprightChopRotation = Quaternion.AngleAxis(chopTiltZ, Vector3.forward)
                            * Quaternion.AngleAxis(Vector2.SignedAngle(handleDir, Vector2.down), Vector3.forward);

        // Grip about three quarters of the way from the middle out along the handle.
        Vector3 gripLocal = lb.center + longLocal * (handleSign * halfLength * 0.75f);
        chopGripVector = uprightChopRotation * RestingRotation * Vector3.Scale(gripLocal, BaseLocalScale);
    }
    private IEnumerator ChopMotionRoutine()
    {
        yield return BlendChopPose(1f, 0.06f);
        yield return ChopStroke();

        // Grace window before easing back down to the normal carry pose - a
        // new pulse arriving before this elapses cancels it (PlayChopMotion
        // always stops the running coroutine first), so the knife stays
        // upright for an entire multi-piece cut instead of flopping back to
        // its lying pose between every single stroke.
        yield return new WaitForSeconds(ChopHoldSeconds);
        yield return BlendChopPose(0f, 0.15f);
        chopMotionRoutine = null;
    }

    /// <summary>Lift, then a quick straight-down "통" onto the board with a
    /// tiny dip past it, then settle.</summary>
    private IEnumerator ChopStroke()
    {
        yield return StrokeTo(0f, chopLift, 0.07f);
        yield return StrokeTo(chopLift, -0.03f, 0.04f);
        yield return StrokeTo(-0.03f, 0f, 0.05f);
    }

    private IEnumerator StrokeTo(float from, float to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            ApplyChopPose(Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        ApplyChopPose(to);
    }

    private IEnumerator BlendChopPose(float target, float duration)
    {
        float start = chopBlend;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            chopBlend = Mathf.Lerp(start, target, t / duration);
            ApplyChopPose(0f);
            yield return null;
        }
        chopBlend = target;
        ApplyChopPose(0f);
    }

    /// <summary>The knife shifts so its handle's grip point sits where the hand
    /// holds things, and the hand shifts the other way by the same amount - net
    /// effect: the blade stays over the ingredient while the hand drops down to
    /// the handle, like the reference grip.</summary>
    private void ApplyChopPose(float lift)
    {
        SetTempHoldRotation(Quaternion.Slerp(Quaternion.identity, uprightChopRotation, chopBlend));
        // The knife rides the hand transform, so whatever extra shift the hand
        // takes to close its fist on the handle is taken back out here.
        Vector3 fistShift = HandController.Instance != null
            ? HandController.Instance.SetChopPose(chopBlend, Vector3.up * lift + chopGripVector * chopBlend, BaseHandHoldOffset)
            : Vector3.zero;
        SetTempHoldOffset(-chopGripVector * chopBlend - fistShift);
    }

    // ---- 칼에 썬 재료 올려 옮기기 (knife as a bench scraper) ----

    /// <summary>A cut ingredient scooped up onto the flat of the knife's blade,
    /// riding along until it's tipped in somewhere.</summary>
    public IngredientItem KnifeLoad { get; private set; }
    private Collider[] loadColliders;
    private Coroutine loadRoutine;

    /// <summary>Knife in hand, click a cut ingredient on the board: the blade
    /// slides under it and it comes up lying on the blade.</summary>
    public bool TryLoadOnKnife(IngredientItem ing)
    {
        if (kind != ToolKind.Knife || !IsHeld || KnifeLoad != null || loadRoutine != null || ing == null || ing.IsHeld) return false;
        EndChopStance();

        if (ing.CurrentContainer != null)
        {
            ing.CurrentContainer.Release(ing);
            ing.CurrentContainer = null;
        }
        KnifeLoad = ing;
        loadColliders = System.Array.FindAll(ing.GetComponentsInChildren<Collider>(), c => c.enabled);
        foreach (Collider c in loadColliders) c.enabled = false;
        ing.transform.SetParent(transform, true);
        loadRoutine = StartCoroutine(SlideOntoBlade(ing));
        return true;
    }

    private System.Collections.IEnumerator SlideOntoBlade(IngredientItem ing)
    {
        // The blade sweeps under the pieces (the hand dips toward them), then
        // the pieces settle onto the blade.
        HandController hand = HandController.Instance;
        Vector3 from = ing.transform.position;
        for (float t = 0f; t < 0.22f; t += Time.deltaTime)
        {
            float k = t / 0.22f;
            hand?.SetGestureOffset(new Vector3(-0.15f, 0f, 0f) * Mathf.Sin(k * Mathf.PI));
            if (ing != null) ing.transform.position = Vector3.Lerp(from, LoadPositionOnBlade(ing), k * k);
            yield return null;
        }
        hand?.SetGestureOffset(Vector3.zero);
        loadRoutine = null;
    }

    void LateUpdate()
    {
        if (KnifeLoad != null && loadRoutine == null) KnifeLoad.transform.position = LoadPositionOnBlade(KnifeLoad);
        if (scraps.Count > 0)
        {
            Vector3 blade = BladePoint();
            for (int i = 0; i < scraps.Count; i++)
                if (scraps[i] != null) scraps[i].transform.position = ScrapSlot(blade, i);
        }
    }

    // ---- 자투리 모아 버리기 (clean-up: scraps swept onto the blade) ----

    private readonly List<FoodScrap> scraps = new List<FoodScrap>();
    public bool HasScraps => scraps.Count > 0;

    /// <summary>Every scrap lying on the board swept up onto the blade at once
    /// (칼등에 모아서), to be carried to the waste bag.</summary>
    /// <summary>How far round the clicked scrap (world, on screen) the blade takes the others too.</summary>
    [SerializeField] private float scrapSweepRadius = 0.4f;

    /// <summary>The clicked scrap and the ones lying around it swept up onto the
    /// blade in one go (칼등에 모아서) - the scraps heaped in a corner come up
    /// together; one off by itself elsewhere stays for its own click.</summary>
    public int SweepScraps(CuttingBoardController board, FoodScrap clicked)
    {
        if (kind != ToolKind.Knife || !IsHeld || board == null || clicked == null || sweepRoutine != null) return 0;
        Vector2 at = clicked.transform.position;
        // (the scraps lie on the board's ingredient root, which sits beside the
        // board now, not under it - found by whose they are)
        var taken = new List<FoodScrap>();
        foreach (FoodScrap s in FindObjectsByType<FoodScrap>(FindObjectsSortMode.None))
        {
            if (scraps.Contains(s) || s.IsHeld || (object)s.CurrentContainer != board) continue;
            if (s != clicked && ((Vector2)s.transform.position - at).magnitude > scrapSweepRadius) continue;
            board.Release(s);
            s.CurrentContainer = null;
            foreach (Collider c in s.GetComponentsInChildren<Collider>()) c.enabled = false;
            s.transform.SetParent(transform, true);
            taken.Add(s);
        }
        if (taken.Count == 0) return 0;
        EndChopStance();
        sweepRoutine = StartCoroutine(SweepOntoBlade(taken));
        return taken.Count;
    }

    private Coroutine sweepRoutine;

    /// <summary>The same scoop as for cut pieces: the hand dips, the blade slides
    /// in under the scraps, and they come up onto it one after another.</summary>
    private System.Collections.IEnumerator SweepOntoBlade(List<FoodScrap> taken)
    {
        HandController hand = HandController.Instance;
        int start = scraps.Count;
        var from = new List<Vector3>();
        foreach (FoodScrap s in taken) from.Add(s.transform.position);
        const float seconds = 0.45f;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = t / seconds;
            hand?.SetGestureOffset(new Vector3(-0.15f, -0.05f, 0f) * Mathf.Sin(k * Mathf.PI));
            Vector3 blade = BladePoint();
            for (int i = 0; i < taken.Count; i++)
            {
                if (taken[i] == null) continue;
                float ki = Mathf.Clamp01((k - i * 0.08f) / 0.6f); // one after another
                taken[i].transform.position = Vector3.Lerp(from[i], ScrapSlot(blade, start + i), ki * ki);
            }
            yield return null;
        }
        hand?.SetGestureOffset(Vector3.zero);
        scraps.AddRange(taken);
        sweepRoutine = null;
    }

    /// <summary>Where the i-th scrap rides on the blade (a little heap).</summary>
    private static Vector3 ScrapSlot(Vector3 blade, int i)
    {
        float a = i * 2.4f;
        return blade + new Vector3(Mathf.Cos(a) * 0.12f, Mathf.Sin(a) * 0.05f, -0.02f) * Mathf.Sqrt(i);
    }

    /// <summary>Whatever rides on the blade - a cut piece, swept scraps - handed
    /// over to be thrown away (the knife keeps nothing). The knife now wants a wash.</summary>
    public List<Carryable> TakeLoadForDisposal()
    {
        var all = new List<Carryable>();
        if (KnifeLoad != null) { IngredientItem ing = KnifeLoad; ReleaseLoad(); all.Add(ing); }
        foreach (FoodScrap s in scraps) if (s != null) { s.transform.SetParent(null, true); all.Add(s); }
        scraps.Clear();
        if (all.Count > 0) GetComponent<HygieneTracker>()?.MarkNeedsWash();
        return all;
    }

    /// <summary>Where the ingredient goes so its pieces (not its pivot - the cut
    /// pieces spread out around it) sit in the middle of the blade.</summary>
    private Vector3 LoadPositionOnBlade(IngredientItem ing)
    {
        Vector3 pivot = ing.transform.position;
        bool any = false;
        Bounds b = new Bounds(pivot, Vector3.zero);
        foreach (Renderer r in ing.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        Vector3 offset = any ? b.center - pivot : Vector3.zero;
        offset.z = 0f;
        return BladePoint() - offset;
    }

    /// <summary>Middle of the blade's flat, just in front of it: along the knife's
    /// long axis, toward the end away from the hand (the handle's in the hand).</summary>
    private Vector3 BladePoint()
    {
        Renderer r = GetComponentInChildren<Renderer>();
        if (r == null) return transform.position;
        Bounds b = r.bounds;
        Vector2 size = b.size;
        Vector3 c = b.center;
        Vector3 axis = size.x >= size.y ? Vector3.right : Vector3.up;
        Vector3 hp = HandController.Instance != null ? HandController.Instance.transform.position : c;
        float sign = Vector3.Dot(c - hp, axis) >= 0f ? 1f : -1f;
        Vector3 p = c + axis * sign * Vector3.Dot(b.extents, axis) * 0.4f;
        p.z = b.min.z - 0.05f;
        return p;
    }

    /// <summary>Tips the pieces off the blade into target, as if carried there by
    /// hand (its own placing rules apply). False (still on the blade) if the
    /// target doesn't take them.</summary>
    public bool TryDropLoadOn(Interactable target)
    {
        HandCarry hand = HandCarry.Instance;
        IngredientItem ing = KnifeLoad;
        if (ing == null || loadRoutine != null || target == null || hand == null || (object)hand.Current != this) return false;
        Interactable dest = target.CanAcceptDrop(ing) ? target
            : target is Carryable c && c.CurrentContainer is Interactable under && under.CanAcceptDrop(ing) ? under : null;
        if (dest == null) return false;

        ReleaseLoad(); // first - setting the knife down (Detach) drops whatever's still on it
        hand.Detach();
        hand.PickUp(ing);
        hand.DropIsFromDrag = true; // lands where it is (off the blade), not snapped to center
        dest.ForceActivate();
        hand.DropIsFromDrag = false;

        if ((object)hand.Current == ing) // not taken after all - back on the blade
        {
            hand.Detach();
            hand.PickUp(this);
            KnifeLoad = ing;
            loadColliders = System.Array.FindAll(ing.GetComponentsInChildren<Collider>(), cc => cc.enabled);
            foreach (Collider cc in loadColliders) cc.enabled = false;
            ing.transform.SetParent(transform, true);
            return false;
        }

        hand.PickUp(this);
        StartCoroutine(TipGesture());
        return true;
    }

    /// <summary>Clicked on an empty spot of the counter: the pieces are slid off
    /// the blade right there.</summary>
    public void DropLoadAt(Vector3 world)
    {
        if (KnifeLoad == null || loadRoutine != null) return;
        IngredientItem ing = KnifeLoad;
        HandCarry hand = HandCarry.Instance;
        if (hand == null || (object)hand.Current != this) { SetLoadDownHere(); return; }
        ReleaseLoad(); // first - setting the knife down (Detach) drops whatever's still on it
        hand.Detach();
        hand.PickUp(ing);
        hand.PlaceFreeAt(world);
        hand.PickUp(this);
        StartCoroutine(TipGesture());
    }

    private void SetLoadDownHere()
    {
        IngredientItem ing = KnifeLoad;
        ReleaseLoad();
        if (ing == null) return;
        ing.transform.SetParent(null, true);
        ing.transform.localScale = ing.BaseLocalScale;
        ing.OnPlaced();
    }

    private void ReleaseLoad()
    {
        if (loadRoutine != null) { StopCoroutine(loadRoutine); loadRoutine = null; HandController.Instance?.SetGestureOffset(Vector3.zero); }
        if (loadColliders != null) foreach (Collider c in loadColliders) if (c != null) c.enabled = true;
        loadColliders = null;
        if (KnifeLoad != null) KnifeLoad.transform.SetParent(null, true);
        KnifeLoad = null;
    }

    /// <summary>The blade tips over a moment as the pieces slide off.</summary>
    private System.Collections.IEnumerator TipGesture()
    {
        for (float t = 0f; t < 0.25f; t += Time.deltaTime)
        {
            SetTempHoldRotation(Quaternion.AngleAxis(Mathf.Sin(t / 0.25f * Mathf.PI) * -25f, Vector3.forward));
            yield return null;
        }
        SetTempHoldRotation(Quaternion.identity);
    }

    /// <summary>Out of the upright chop stance, lying flat in the hand again -
    /// for 포 뜨기, slicing sideways through meat with the blade flat.</summary>
    public void LayFlatInHand() => EndChopStance();

    private void EndChopStance()
    {
        if (chopMotionRoutine == null) return;
        StopCoroutine(chopMotionRoutine);
        chopMotionRoutine = null;
        chopBlend = 0f;
        SetTempHoldRotation(Quaternion.identity);
        SetTempHoldOffset(Vector3.zero);
        HandController.Instance?.SetChopPose(0f, Vector3.zero);
    }
}
