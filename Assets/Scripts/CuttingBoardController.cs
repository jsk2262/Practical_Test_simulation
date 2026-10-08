using System.Collections;
using UnityEngine;

[RequireComponent(typeof(HygieneTracker))]
public class CuttingBoardController : PlaceSlot, ITowelLined
{
    /// <summary>A paper towel spread on the board (under where ingredients go),
    /// for meat/tofu to drain on.</summary>
    public ToolItem Liner { get; private set; }
    public bool HasTowelLiner => Liner != null;

    /// <summary>A trimming/cutting scrap set aside in the board's left corner
    /// (자투리는 왼쪽 구석에 모으기) - beside the work, not taking the board's spot.</summary>
    public void AddScrap(FoodScrap scrap)
    {
        Rect top = VisibleTopRect();
        Transform root = IngredientRoot;
        // the upper-left corner: the lower one holds the 뒤집기 marker
        Vector3 corner = new Vector3(top.xMin + 0.35f + Random.Range(-0.15f, 0.15f), top.yMax - 0.3f + Random.Range(-0.12f, 0.1f), root.position.z);
        scrap.transform.SetParent(root, true);
        Vector3 local = root.InverseTransformPoint(corner);
        scrap.transform.localPosition = new Vector3(local.x, local.y, -0.06f);
        scrap.CurrentContainer = this;
    }

    public void AdoptLiner(ToolItem towel)
    {
        if (Liner != null) return;
        Liner = towel;
        towel.CurrentContainer = this;
    }

    public override void Release(Carryable item)
    {
        if ((object)item == Liner) Liner = null;
        base.Release(item);
    }

    /// <summary>The held paper towel goes down flat in the middle of the
    /// board, sized to most of its surface, behind whatever is placed on it.</summary>
    private void LayLiner(ToolItem towel)
    {
        Transform root = IngredientRoot;
        Rect top = VisibleTopRect();
        Vector3 c = root.InverseTransformPoint(new Vector3(top.center.x, top.center.y, root.position.z));
        Vector3 local = new Vector3(c.x, c.y, -0.01f);
        if (!HandCarry.Instance.PlaceAtFreeLocalPosition(root, local)) return;

        towel.CurrentContainer = this;
        Liner = towel;
        towel.SpreadAsLiner(this, root, top.size * 0.4f, local); // one sheet: about half the board
    }

    /// <summary>The board's top face as seen on screen (world XY). The board is
    /// a tilted box, so its collider bounds also take in the front edge and
    /// thickness - centering on them put things low, hanging off the bottom.
    /// Sampled with camera rays and grouped by which face they hit: the face
    /// covering the most of the screen is the top (on a steeply tilted board
    /// the front edge can face the camera more squarely, so "most head-on"
    /// picked a thin strip instead).</summary>
    public Rect VisibleTopRect()
    {
        Collider col = GetComponent<Collider>();
        Camera cam = Camera.main;
        Bounds b = col != null ? col.bounds : new Bounds(transform.position, Vector3.one);
        Rect whole = new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
        if (col == null || cam == null) return whole;

        var faces = new System.Collections.Generic.Dictionary<Vector3Int, Rect>();
        var counts = new System.Collections.Generic.Dictionary<Vector3Int, int>();
        const int n = 16;
        for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
            {
                Vector3 p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, i / (float)n), Mathf.Lerp(b.min.y, b.max.y, j / (float)n), b.min.z);
                Ray ray = cam.ScreenPointToRay(cam.WorldToScreenPoint(p));
                if (!col.Raycast(ray, out RaycastHit hit, 1000f)) continue;

                Vector3Int key = Vector3Int.RoundToInt(hit.normal * 4f);
                if (faces.TryGetValue(key, out Rect r))
                {
                    r.xMin = Mathf.Min(r.xMin, hit.point.x); r.xMax = Mathf.Max(r.xMax, hit.point.x);
                    r.yMin = Mathf.Min(r.yMin, hit.point.y); r.yMax = Mathf.Max(r.yMax, hit.point.y);
                    faces[key] = r;
                    counts[key]++;
                }
                else
                {
                    faces[key] = new Rect(hit.point.x, hit.point.y, 0f, 0f);
                    counts[key] = 1;
                }
            }

        Vector3Int best = default;
        int bestCount = 0;
        foreach (var kv in counts)
            if (kv.Value > bestCount) { bestCount = kv.Value; best = kv.Key; }
        return bestCount >= 4 ? faces[best] : whole;
    }

    public static CuttingBoardController Instance { get; private set; }

    /// <summary>Whether the board itself shows at this point (world x, y as
    /// seen) - its click box, a tilted box, reaches well past the top face, over
    /// a plate set down beside it: food dropped on that plate went onto the
    /// board instead (and later went missing with it).</summary>
    public bool ShowsAt(Vector3 world) => VisibleTopRect().Contains(new Vector2(world.x, world.y));

    /// <summary>The ray's hits nearest first, the board's skipped where it
    /// doesn't actually show (see ShowsAt) - what a click/drop there means.</summary>
    public static RaycastHit[] HitsShowing(Ray ray, RaycastHit[] hits)
    {
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        if (Instance == null) return hits;
        Vector3 at = ray.origin;
        var kept = new System.Collections.Generic.List<RaycastHit>(hits.Length);
        foreach (RaycastHit h in hits)
            if (h.collider.GetComponent<CuttingBoardController>() == null || Instance.ShowsAt(at)) kept.Add(h);
        return kept.ToArray();
    }

    /// <summary>Half-extents (real-world-like units) an ingredient may land within,
    /// measured from the board's center.</summary>
    [SerializeField] private Vector2 placementExtents = new Vector2(1.1f, 0.75f);

    public HygieneTracker Hygiene { get; private set; }

    private Transform ingredientRoot;
    private Vector3 rootOffset;

    /// <summary>Under the board (tilted, stretched 2.4 x 2) the root couldn't
    /// cancel its scale and rotation both - its world transform came out
    /// skewed (scale 1, 1.4, 0.8), and so did anything on it: cut pieces turned
    /// 90 degrees came out squashed into slanted shapes. So it sits beside the
    /// board instead (under the board's unscaled parent), square and level, and
    /// follows the board's position (washing carries the board to the sink).</summary>
    private void CleanRoot()
    {
        if (ingredientRoot.parent == transform && transform.parent != null)
        {
            ingredientRoot.SetParent(transform.parent, true);
            ingredientRoot.rotation = Quaternion.identity;
            Vector3 ps = transform.parent.lossyScale;
            ingredientRoot.localScale = new Vector3(1f / Mathf.Max(ps.x, 1e-4f), 1f / Mathf.Max(ps.y, 1e-4f), 1f / Mathf.Max(ps.z, 1e-4f));
        }
        rootOffset = ingredientRoot.position - transform.position;
    }

    void LateUpdate()
    {
        if (ingredientRoot != null && ingredientRoot.parent != transform)
            ingredientRoot.position = transform.position + rootOffset;
        if (!anchorCleaned) CleanAnchor();
        if (Anchor != transform && Anchor.parent != transform)
            Anchor.position = transform.position + anchorOffset;
    }

    private bool anchorCleaned;
    private Vector3 anchorOffset;

    /// <summary>The knife's rest spot (Anchor) had the same trouble as the
    /// ingredient root: under the tilted, stretched board it was skewed, and the
    /// board's flip turned it over too - the knife ended up under the board.
    /// It's moved beside the board (square, level, following the board's
    /// position, untouched by the flip), to the middle of the board's bottom
    /// edge (6 o'clock): the bottom-left corner is the flip marker's.</summary>
    private void CleanAnchor()
    {
        anchorCleaned = true;
        Transform a = Anchor;
        if (a == transform || transform.parent == null) return;

        ToolItem tool = a.childCount > 0 ? a.GetChild(0).GetComponent<ToolItem>() : null;
        Renderer toolRend = tool != null ? tool.GetComponentInChildren<Renderer>() : null;
        Vector3 bodyOffset = toolRend != null ? toolRend.bounds.center - a.position : Vector3.zero;
        float halfHeight = toolRend != null ? toolRend.bounds.extents.y : 0.2f;
        if (tool != null) tool.transform.SetParent(null, true); // (kept out of the re-scale below)

        a.SetParent(transform.parent, true);
        a.rotation = Quaternion.identity;
        Vector3 ps = transform.parent.lossyScale;
        a.localScale = new Vector3(1f / Mathf.Max(ps.x, 1e-4f), 1f / Mathf.Max(ps.y, 1e-4f), 1f / Mathf.Max(ps.z, 1e-4f));

        Rect top = VisibleTopRect();
        Vector3 body = new Vector3(top.center.x, top.yMin + halfHeight + 0.06f, 0f);
        a.position = new Vector3(body.x - bodyOffset.x, body.y - bodyOffset.y, FrontZ(a.position.z));
        anchorOffset = a.position - transform.position;

        if (tool != null)
        {
            tool.transform.SetParent(a, true);
            tool.transform.localPosition = Vector3.zero;
            tool.transform.rotation = tool.RestingRotation;
            // (its resting turn moves the blade off its pivot differently - measured as it lies now)
            Vector3 shown = WholeBounds(tool).center;
            a.position += new Vector3(body.x - shown.x, top.yMin + WholeBounds(tool).extents.y + 0.06f - shown.y, 0f);
            anchorOffset = a.position - transform.position;
        }
    }

    private static Bounds WholeBounds(Component c)
    {
        Bounds b = new Bounds(c.transform.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in c.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    /// <summary>A depth on or in front of the board's top face.</summary>
    private float FrontZ(float z)
    {
        Collider c = GetComponent<Collider>();
        return c != null ? Mathf.Min(z, c.bounds.min.z - 0.05f) : z;
    }

    private Transform IngredientRoot
    {
        get
        {
            if (ingredientRoot == null)
            {
                // Runtime-only field, resets to null on every reload even though the
                // child itself (if this board already held something before) is
                // still there - reuse it instead of creating a duplicate.
                Transform existing = transform.Find("IngredientRoot");
                if (existing == null && transform.parent != null) existing = transform.parent.Find("IngredientRoot");
                if (existing != null)
                {
                    ingredientRoot = existing;
                    CleanRoot();
                }
                else
                {
                    var go = new GameObject("IngredientRoot");
                    ingredientRoot = go.transform;
                    ingredientRoot.SetParent(transform, false);

                    // Cancel the board's own scale/rotation so an ingredient placed
                    // here keeps its natural size and stays screen-aligned instead
                    // of inheriting the board's own stretch/tilt.
                    Vector3 s = transform.lossyScale;
                    ingredientRoot.localScale = new Vector3(
                        Mathf.Approximately(s.x, 0f) ? 1f : 1f / s.x,
                        Mathf.Approximately(s.y, 0f) ? 1f : 1f / s.y,
                        Mathf.Approximately(s.z, 0f) ? 1f : 1f / s.z);
                    ingredientRoot.rotation = Quaternion.identity;
                    CleanRoot();
                    PullIngredientsInFront();
                }
            }

            return ingredientRoot;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
        Hygiene = GetComponent<HygieneTracker>();
    }

    protected override void Activate()
    {
        // Away from its spot (carried / leaning in the sink): a click only picks it up.
        if (GetComponent<BoardSinkWash>() is BoardSinkWash sinkWash && sinkWash.IsAway)
        {
            sinkWash.OnBoardClicked();
            return;
        }

        HandCarry hand = HandCarry.Instance;
        if (hand?.Current is ToolItem tool && (tool.Kind == ToolKind.Rag || tool.Kind == ToolKind.Cloth || tool.Kind == ToolKind.PaperTowel))
        {
            // A towel currently wrapping an ingredient is being set down, not
            // used to wipe - without this check there was no way to ever let
            // go of a wrapped towel here, since this branch always intercepted
            // the click first (the knife's own placement branch below, and
            // ResetToBaseScale from PlaceAtAnchor undoing the wrap bulge, only
            // run in the else case).
            if (tool.IsWrapping)
            {
                if (hand.PlaceAtAnchor(Anchor)) tool.CurrentContainer = this;
                return;
            }

            // A fresh paper towel on an empty board is spread out to drain meat/tofu
            // on (put it down first, then the ingredient); otherwise it wipes.
            if (tool.Kind == ToolKind.PaperTowel && Liner == null && Current == null)
            {
                LayLiner(tool);
                return;
            }

            Hygiene.Wash();
            ProcessStageManager.Instance?.MarkWiped();
            return;
        }

        // The knife's resting spot on the board is tracked independently of
        // PlaceSlot's own Current (reserved for the ingredient being cut) -
        // going through the normal Accepts()/Current-gated path here would
        // either reject the knife outright (accepts is IngredientOnly) or,
        // if allowed, let it occupy the one slot and block every ingredient
        // drop until it's picked back up.
        if (hand?.Current is ToolItem knife && knife.Kind == ToolKind.Knife)
        {
            if (hand.PlaceAtAnchor(Anchor)) knife.CurrentContainer = this;
            return;
        }

        // Ingredients land wherever they were actually dropped (clamped to the
        // board's surface) rather than snapping to Anchor's one fixed spot -
        // PlaceSlot's default Activate() always uses Anchor, which is also
        // where the knife rests (off toward one corner), so every ingredient
        // used to end up there regardless of where the player aimed.
        if (hand?.Current is IngredientItem ingredient && Current == null && Accepts(ingredient))
        {
            PlaceIngredientFree(ingredient);
            return;
        }

        base.Activate();
    }

    /// <summary>Where the item's pivot goes so its visible middle lands under the
    /// mouse cursor (fallback: the board's middle).</summary>
    private static Vector3 CursorPivotFor(IngredientItem item, Vector3 fallback)
    {
        Camera cam = Camera.main;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (cam == null || mouse == null) return fallback;
        Vector3 cursor = cam.ScreenToWorldPoint(new Vector3(mouse.position.ReadValue().x, mouse.position.ReadValue().y, fallback.z - cam.transform.position.z));
        return PivotFor(item, cursor, fallback.z);
    }

    /// <summary>Where the item's pivot goes so what shows of it (its cut pieces,
    /// once cut) is centered on point.</summary>
    private static Vector3 PivotFor(IngredientItem item, Vector3 point, float z)
    {
        Vector3 offset = item.VisibleBounds.center - item.transform.position;
        return new Vector3(point.x - offset.x, point.y - offset.y, z);
    }

    private void PlaceIngredientFree(IngredientItem item)
    {
        Transform root = IngredientRoot;

        // Wherever the player puts it (나이나이 레시피 style): the piece's middle
        // under the cursor, for a click as for a drag - and turned however the
        // mouse wheel turned it in hand (no automatic lengthwise turn).
        // The board's visible top face, less the item's own half size so it
        // stays fully on the board - the fixed placementExtents is only a
        // fallback, it no longer matched a board resized in the scene (its outer
        // parts pulled every drop back toward the middle).
        Rect top = VisibleTopRect();
        Vector3 topCenter = new Vector3(top.center.x, top.center.y, root.position.z);
        // A paper towel spread here: it goes on the towel (to drain), whatever spot was clicked.
        TowelSpread towelSheet = Liner != null ? root.GetComponentInChildren<TowelSpread>() : null;
        // (cut pieces heaped within the towel's middle third, so its folded sides cover them)
        if (towelSheet != null && item.CurrentState == IngredientItem.State.Cut)
            item.GatherPieces(Mathf.Abs(towelSheet.transform.lossyScale.x) / 6f * 0.75f);
        Vector3 aim = towelSheet != null
            ? PivotFor(item, towelSheet.transform.position, topCenter.z)
            : CursorPivotFor(item, topCenter);
        Vector3 desired = root.InverseTransformPoint(aim);

        Vector2 center = Vector2.zero, extents = placementExtents;
        if (GetComponent<Collider>() != null)
        {
            Vector3 c = root.InverseTransformPoint(topCenter);
            Vector3 itemHalf = item.VisibleBounds.extents;
            center = new Vector2(c.x, c.y);
            extents = new Vector2(Mathf.Max(top.width * 0.5f - itemHalf.x, 0f), Mathf.Max(top.height * 0.5f - itemHalf.y, 0f));
        }

        Vector3 clamped = new Vector3(
            Mathf.Clamp(desired.x, center.x - extents.x, center.x + extents.x),
            Mathf.Clamp(desired.y, center.y - extents.y, center.y + extents.y),
            -0.05f);

        if (HandCarry.Instance.PlaceAtFreeLocalPosition(root, clamped))
        {
            item.CurrentContainer = this;
            Current = item;
            PullIngredientsInFront(); // a thick ingredient mustn't sink into the board
        }
    }

    /// <summary>Every ingredient is cut lying lengthwise across the board (long
    /// side left-right), with the knife coming down vertically across it - a
    /// green onion or carrot that would land standing tall on screen gets a
    /// quarter turn. Applied as the item's own spin (Carryable.UserSpin), so it
    /// stays lying that way when picked up and carried on, like a mouse-wheel
    /// turn. Left alone if the player already turned it themselves.</summary>
    private static void LayLengthwise(IngredientItem item)
    {
        Renderer rend = item.GetComponent<Renderer>();
        MeshFilter mf = item.GetComponent<MeshFilter>();
        if (rend == null || !rend.enabled || mf == null || mf.sharedMesh == null) return;

        // The model's long side, as it lies on the (tilted) counter: turned about
        // the counter's up until it runs left-right (whichever way is nearer).
        Vector3 size = mf.sharedMesh.bounds.size;
        Vector3 lenLocal = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
        Vector3 len = Vector3.ProjectOnPlane(item.transform.TransformDirection(lenLocal), Carryable.CounterUp);
        if (len.sqrMagnitude < 1e-6f) return;
        float angle = Vector3.SignedAngle(len, Vector3.ProjectOnPlane(Vector3.right, Carryable.CounterUp), Carryable.CounterUp);
        if (angle > 90f) angle -= 180f; else if (angle < -90f) angle += 180f;
        if (Mathf.Abs(angle) < 5f) return;

        Vector3 center = rend.bounds.center;
        item.Spin(angle);
        item.transform.rotation = item.RestingRotation;
        // Turn about its visual center, not its pivot, so it stays where it landed.
        item.transform.position += center - rend.bounds.center;
    }

    public override bool CanAcceptDrop(Carryable item)
    {
        if (item is ToolItem tool && (tool.Kind == ToolKind.Rag || tool.Kind == ToolKind.Cloth || tool.Kind == ToolKind.PaperTowel))
            return true;

        if (item is ToolItem knife && knife.Kind == ToolKind.Knife)
            return true;

        return base.CanAcceptDrop(item);
    }

    private bool isWashing;

    /// <summary>Washed at the sink by hand (BoardSinkWash) - both faces clean.</summary>
    public void MarkWashedBothSides()
    {
        Hygiene.Wash();
        otherSideLastCategory = IngredientCategory.None;
        ProcessStageManager.Instance?.MarkWiped();
    }

    /// <summary>"씻기": the board (and the knife resting on it) is carried to
    /// the sink, scrubbed clean, and carried back to its spot - both sides'
    /// hygiene history is cleared. Input is locked meanwhile. False (nothing
    /// happens) if an ingredient is still on the board or it's busy.</summary>
    public bool WashAtSink(Transform sink, HygieneTracker knifeHygiene)
    {
        if (isWashing || isFlipping || Current != null || sink == null) return false;
        StartCoroutine(WashRoutine(sink, knifeHygiene));
        return true;
    }

    private IEnumerator WashRoutine(Transform sink, HygieneTracker knifeHygiene)
    {
        isWashing = true;
        HandCarry hand = HandCarry.Instance;
        if (hand != null) hand.IsLocked = true;

        Vector3 home = transform.position;
        Collider sinkCollider = sink.GetComponent<Collider>();
        Vector3 sinkCenter = sinkCollider != null ? sinkCollider.bounds.center : sink.position;
        // Board stays at its own depth (it's drawn over the counter there).
        Vector3 atSink = new Vector3(sinkCenter.x, sinkCenter.y, home.z);

        yield return MoveTo(home, atSink, 0.45f);

        HandController.Instance?.GoTo(new Vector3(atSink.x, atSink.y, HandController.Instance.transform.position.z), null);
        HandController.Instance?.PlayWashGesture(1.0f);
        Vector3 baseScale = transform.localScale;
        for (float t = 0f; t < 1.0f; t += Time.deltaTime)
        {
            float wobble = Mathf.Sin(t * Mathf.PI * 10f) * 0.03f * (1f - t);
            transform.localScale = baseScale * (1f + wobble);
            yield return null;
        }
        transform.localScale = baseScale;

        Hygiene.Wash();
        otherSideLastCategory = IngredientCategory.None; // the whole board was washed
        knifeHygiene?.Wash();
        ProcessStageManager.Instance?.MarkWiped();

        yield return MoveTo(atSink, home, 0.45f);

        if (hand != null) hand.IsLocked = false;
        isWashing = false;
    }

    private IEnumerator MoveTo(Vector3 from, Vector3 to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            transform.position = Vector3.Lerp(from, to, k);
            yield return null;
        }
        transform.position = to;
    }

    private bool isFlipping;

    /// <summary>The side NOT currently facing up - starts clean (unused) the
    /// first time the board is ever flipped, and from then on keeps whatever
    /// mess it was left with, independently of the active side.</summary>
    private IngredientCategory otherSideLastCategory = IngredientCategory.None;

    /// <summary>Flips the board onto its other side - a quicker alternative to
    /// carrying it to the sink when you're mid-something and need a clean
    /// surface right now. Unlike actually washing it, this doesn't erase the
    /// mess: the side just turned away keeps whatever category dirtied it, and
    /// flipping back to it later still carries that same violation risk until
    /// it's properly washed.</summary>
    public void Flip()
    {
        if (isFlipping || isWashing) return;
        StartCoroutine(FlipRoutine());
    }

    private IEnumerator FlipRoutine()
    {
        isFlipping = true;

        // Anchor sits off-center now (the knife's rest spot, moved out to the
        // board's corner) - unlike a tool sitting exactly at the board's own
        // pivot, an off-center child DOES drift in world space when its parent
        // rotates 180 degrees (the local offset that used to read as "toward
        // the camera" now reads as "toward the player" or vice versa). Pinning
        // Anchor's world position back afterward keeps the knife's rest spot a
        // fixed physical place beside the board, the way flipping a real board
        // never relocates whatever's sitting next to it.
        Vector3 anchorWorldPosBeforeFlip = Anchor.position;

        // Tips end-over-end around the board's own local X axis - the way a
        // flat board actually flips to show its other face, rather than
        // spinning in place around the axis facing the camera.
        Quaternion start = transform.rotation;
        Quaternion end = start * Quaternion.Euler(180f, 0f, 0f);
        const float duration = 0.4f;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(start, end, t / duration);
            yield return null;
        }

        transform.rotation = end;
        if (Anchor != transform && Anchor.parent == transform) Anchor.position = anchorWorldPosBeforeFlip;
        // (a re-homed Anchor didn't turn with the board - just kept in front of its new top face)
        if (Anchor != transform && Anchor.parent != transform)
        {
            Vector3 ap = Anchor.position;
            Anchor.position = new Vector3(ap.x, ap.y, FrontZ(ap.z));
            anchorOffset = Anchor.position - transform.position;
        }

        // Swap which side's history is active - the side just turned away
        // keeps its own mess untouched, and whatever the newly-up side was
        // last left with (clean, the first time) comes back as the active
        // state.
        IngredientCategory justLeft = Hygiene.LastCategory;
        Hygiene.SetState(otherSideLastCategory);
        otherSideLastCategory = justLeft;

        // IngredientRoot's own rotation was pinned to world-identity once, at
        // creation time, to keep contents screen-aligned - the board rotating
        // out from under it here would otherwise leave that pin stale (it'd
        // now be rotated right along with the board), misaligning whatever
        // gets placed next.
        if (ingredientRoot != null)
        {
            ingredientRoot.rotation = Quaternion.identity;
            PullIngredientsInFront();
        }

        ReseatToolsAfterFlip();

        isFlipping = false;
    }

    /// <summary>The board's collider isn't symmetric front-to-back around its
    /// pivot, so after a flip its new top face can sit closer to the camera
    /// than IngredientRoot (which stays at the pivot) - whatever is on the
    /// board, and the off-hand holding it (placed relative to the ingredient),
    /// then ended up behind the board while the knife (re-seated in front by
    /// ReseatToolsAfterFlip) chopped at nothing. Keeps the root on or in front
    /// of the current top face, and every ingredient on it fully in front.</summary>
    private void PullIngredientsInFront()
    {
        Collider boardCollider = GetComponent<Collider>();
        if (boardCollider == null || ingredientRoot == null) return;

        float surfaceZ = boardCollider.bounds.min.z;
        Vector3 p = ingredientRoot.position;
        if (p.z > surfaceZ) p.z = surfaceZ;
        ingredientRoot.position = p;

        float overlap = 0f;
        foreach (Renderer r in ingredientRoot.GetComponentsInChildren<Renderer>())
            overlap = Mathf.Max(overlap, r.bounds.max.z - (surfaceZ - 0.01f));
        if (overlap > 0f) ingredientRoot.position -= new Vector3(0f, 0f, overlap);
        rootOffset = ingredientRoot.position - transform.position;
    }

    /// <summary>A tool resting on the board tumbles right along with its
    /// 180-degree flip: its facing changes, and since the board's own
    /// collider isn't symmetric front-to-back around its pivot, the flip can
    /// leave the board's new front face closer to the camera than the
    /// now-backwards-facing tool - silently burying it behind the board and
    /// making it unclickable. Re-facing the tool and pulling it in front of
    /// the board's own (post-flip) surface fixes both.
    /// Searches the whole hierarchy (not just direct children) since Anchor is
    /// itself a child object the tool rests under, one level deeper than the
    /// board's own transform - a shallow direct-children scan missed it
    /// entirely, leaving a flipped knife stuck on the board's back face.</summary>
    private void ReseatToolsAfterFlip()
    {
        Collider boardCollider = GetComponent<Collider>();
        float safeZ = boardCollider != null ? boardCollider.bounds.min.z - 0.05f : transform.position.z - 0.2f;

        foreach (ToolItem tool in GetComponentsInChildren<ToolItem>(true))
        {
            if (ingredientRoot != null && tool.transform.IsChildOf(ingredientRoot)) continue;

            // A tool resting at a dedicated anchor (not directly on the
            // board's own transform) belongs at that anchor's exact position -
            // any local drift it picked up (from earlier pickup/place cycles)
            // would otherwise ride along through the flip's rotation math and
            // land somewhere else on the board entirely (reproduced: the knife
            // sliding to the board's bottom edge after a flip). Snapping home
            // to local zero is safe here since PlaceAtAnchor always parks a
            // tool there in the first place.
            if (tool.transform.parent != transform)
            {
                tool.transform.localPosition = Vector3.zero;
                tool.transform.rotation = tool.RestingRotation;
                continue;
            }

            // Restore the tool's own correct resting rotation (the knife's isn't
            // identity - see Carryable.BaseRotation) rather than assuming identity.
            tool.transform.rotation = tool.RestingRotation;
            Vector3 wp = tool.transform.position;
            wp.z = safeZ;
            tool.transform.position = wp;
        }
    }
}
