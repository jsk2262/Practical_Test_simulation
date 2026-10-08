using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A bowl/dish that can itself be picked up and carried (like a tool), while also
/// acting as a container that holds several ingredients freely across its surface -
/// like a real plate, items land near wherever they're dropped instead of snapping
/// into a fixed row of slots. If that spot is already taken (a player dragging
/// several things just aims at "the plate" each time, not a precise sub-position),
/// the new item is nudged to the nearest free spot instead of stacking exactly on
/// top of - and fully hiding - whatever's already there. Whatever is sitting in the
/// bowl moves with it since ingredients are parented under it.
/// </summary>
public class MovableBowl : Carryable, ISlotContainer, ITowelLined
{
    [SerializeField] private SlotAccepts accepts = SlotAccepts.IngredientOnly;
    [SerializeField] private int capacity = 6;

    /// <summary>Only a deep dish (a real bowl, not a flat plate) makes sense to fill
    /// with water - set on the bowlSize variants, left off on the flat plateSize ones.</summary>
    [SerializeField] private bool canHoldWater = false;
    /// <summary>How much of a full fill one measuring-spoon dip takes out.</summary>
    [SerializeField] private float spoonOfWater = 0.1f;
    public bool CanHoldWater => canHoldWater;

    public bool IsFilledWithWater { get; private set; }
    private GameObject waterVisual;

    [Header("Boiling (water-filled vessels on a lit burner)")]
    /// <summary>Seconds on a strong flame for the exam pot's 0.5-1 L of water
    /// to come to the boil (a lid shortens it); a medium/weak flame takes longer.</summary>
    [SerializeField] private float timeToBoilNoLid = 300f;
    [SerializeField] private float timeToBoilWithLid = 210f;

    /// <summary>Seconds on a strong flame for the wok's ~1 L of frying oil to
    /// reach frying heat (test: a drop of starch batter comes straight back up).</summary>
    [SerializeField] private float timeToHeatOil = 420f;
    private float oilHeat;
    private GameObject oilSurface;

    /// <summary>The frying oil's heat, 0 (cold) .. 1 (hot enough to fry).</summary>
    public float OilHeat01 => HasFryingOil ? Mathf.Clamp01(oilHeat / Mathf.Max(timeToHeatOil, 0.01f)) : 0f;
    public bool IsOilHot => HasFryingOil && oilHeat >= timeToHeatOil;

    /// <summary>How fast the burner under it heats: 강 1, 중 0.7, 약 0.45.</summary>
    private float FlameRate => CurrentBurner == null ? 0f
        : CurrentBurner.Level == StoveBurner.FlameLevel.Strong ? 1f
        : CurrentBurner.Level == StoveBurner.FlameLevel.Medium ? 0.7f : 0.45f;
    [SerializeField] private GameObject bubbleEffectPrefab;

    /// <summary>True once this vessel's water has been on a lit burner long
    /// enough to boil - a lid on top (see PotLid/HasLid) shortens that wait,
    /// the real-kitchen reason to actually put a lid on. Purely about the
    /// water itself, separate from CookableItem's own per-ingredient cook
    /// timer.</summary>
    public bool IsBoiling { get; private set; }
    private float boilProgress;
    private GameObject bubbleInstance;

    [Header("Water look")]
    /// <summary>Optional - a material using Kitchen/PotWater. Left empty, one is
    /// made from that shader at runtime (fine in the editor; assign it here for
    /// builds so the shader actually gets included).</summary>
    [SerializeField] private Material waterMaterial;
    /// <summary>Water disc diameter as a fraction of the vessel body's outer
    /// diameter - the water sits inside the wall, not over the rim.</summary>
    [SerializeField, Range(0.5f, 1f)] private float waterInnerRatio = 0.86f;
    /// <summary>Surface height once full, as a fraction of the vessel's own
    /// height (0 = bottom, 1 = rim) - every vessel fills to about half.</summary>
    [SerializeField, Range(0.1f, 1f)] private float fullWaterLevel = 0.5f;
    /// <summary>Seconds under a running faucet to fill from empty to full.</summary>
    [SerializeField] private float secondsToFill = 3f;

    /// <summary>0 = empty, 1 = full (at fullWaterLevel). Rises while under a
    /// running WaterTap; the vessel only counts as filled (and can boil) at 1.</summary>
    public float WaterAmount { get; private set; }

    private Vector3 waterBottomLocal;
    private Vector3 waterTopLocal;
    private float waterDiameter;
    private float waterThickness;
    /// <summary>Manual nudge in this vessel's own local space, for a model whose
    /// pivot isn't at the center of its round body.</summary>
    [SerializeField] private Vector3 waterLocalOffset;
    /// <summary>How fast the visible heat/boil fades in and out (per second).</summary>
    [SerializeField] private float waterLookSpeed = 0.6f;
    /// <summary>Resting slosh (0-1) - still water isn't perfectly dead flat.</summary>
    [SerializeField, Range(0f, 1f)] private float idleSlosh = 0.2f;

    private static readonly int HeatId = Shader.PropertyToID("_Heat");
    private static readonly int BoilId = Shader.PropertyToID("_Boil");
    private static readonly int SloshId = Shader.PropertyToID("_Slosh");
    private Material waterMaterialInstance;
    private float shownHeat;
    private float shownBoil;
    private float shownSlosh;

    /// <summary>Half-extents (in real-world-like units, not the plate's own squashed
    /// local scale) an item may land within, measured from the plate's center.</summary>
    [SerializeField] private Vector2 placementExtents = new Vector2(0.8f, 0.3f);

    /// <summary>How far apart (center to center, same real-world-like units as
    /// placementExtents) two items must land to not visually overlap. Set this to
    /// roughly a typical ingredient's own footprint - a fraction of placementExtents
    /// isn't reliable since it doesn't know how big the items themselves are.</summary>
    [SerializeField] private Vector2 minSeparation = new Vector2(0.4f, 0.15f);

    private Transform contentRoot;
    private readonly Dictionary<Carryable, Vector2> occupied = new Dictionary<Carryable, Vector2>();

    /// <summary>Seasonings poured directly into this bowl via a loaded measuring
    /// spoon - e.g. a sauce bowl built up from soy sauce/vinegar/sugar with no
    /// solid ingredient in it at all, distinct from IngredientItem's own
    /// per-ingredient seasoning tracking.</summary>
    private readonly Dictionary<string, int> appliedSeasonings = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> AppliedSeasonings => appliedSeasonings;

    public void AddSeasoning(string seasoningName)
    {
        appliedSeasonings.TryGetValue(seasoningName, out int count);
        appliedSeasonings[seasoningName] = count + 1;
        MarkUsed();
        // Into the frying pan/wok it's the dish's sauce - a pool the pieces get
        // coated in; into a pot/pan, what's being cooked there (식용유 + 고춧가루
        // heated: 고추기름); into a bowl, what's kept there (strained oil).
        if ((GetComponent<FryingPanPlating>() != null || IsCookingVessel || PanSauce.Of(this, false) != null) && seasoningName != Condiments.Water)
            PanSauce.Of(this, true).Add(seasoningName);
    }

    /// <summary>The inside of this vessel (world): the middle of its bottom, its
    /// up, the round body's inner radius, the rim's height above the bottom and
    /// - with water in it - the surface's; how hard it's boiling (0..1).</summary>
    public struct Interior
    {
        public Vector3 floorCenter, up;
        public float radius, rim, surface, boil;
        public bool liquid;
    }

    private bool interiorKnown;
    private Transform interiorFrame;
    private Vector3 interiorFloorLocal, interiorUpLocal, interiorAcrossA, interiorAcrossB;
    private float interiorRadiusLocal, interiorDepthLocal;

    /// <summary>Works out (once) the vessel's inside in its own model space. Which
    /// model axis is its "up" differs per model (the pot and the plates: Y; the
    /// frying pan and wok are modelled lying on their side: Z) - it's the one
    /// pointing along the counter's up as it rests. The round body across the
    /// other two (a handle stretches one of them), the bottom just above the
    /// base (a dish: about its middle), the rim at the top.</summary>
    private void LearnInterior()
    {
        // (the model that shows - Bowl_Prepped's own is a hidden one far off at its pivot)
        Renderer r = VisibleDishRenderer();
        interiorFrame = r != null ? r.transform : transform;
        Transform frame = interiorFrame;
        Bounds lb = r != null ? r.localBounds : new Bounds(Vector3.zero, Vector3.one);
        int up = 0;
        float best = -1f, sign = 1f;
        for (int a = 0; a < 3; a++)
        {
            Vector3 axis = Vector3.zero; axis[a] = 1f;
            float d = Vector3.Dot(frame.TransformDirection(axis).normalized, Carryable.CounterUp);
            if (Mathf.Abs(d) > best) { best = Mathf.Abs(d); up = a; sign = Mathf.Sign(d); }
        }
        int a1 = (up + 1) % 3, a2 = (up + 2) % 3;
        Vector3 center = lb.center;
        float radius;
        if (frame != transform)
        {
            // a child model (a plain round dish): its middle, its smaller half-width
            radius = Mathf.Min(lb.extents[a1], lb.extents[a2]);
        }
        else if (up == 1)
        {
            FindBodyCircle(lb, out center, out radius);
            center += new Vector3(waterLocalOffset.x, 0f, waterLocalOffset.z);
        }
        else
        {
            float e1 = lb.extents[a1], e2 = lb.extents[a2];
            radius = Mathf.Min(e1, e2);
            if (Mathf.Abs(e1 - e2) > radius * 0.15f)
            {
                int handle = e1 > e2 ? a1 : a2, across = handle == a1 ? a2 : a1;
                int side = BodySideFromMesh(handle, across, lb);
                if (side == 0) side = Mathf.Abs(lb.max[handle]) < Mathf.Abs(lb.min[handle]) ? 1 : -1;
                center[handle] = side > 0 ? lb.max[handle] - radius : lb.min[handle] + radius;
            }
        }
        float lo = lb.min[up], hi = lb.max[up];
        float floor = isDish ? lb.center[up] : sign > 0f ? lo + (hi - lo) * 0.08f : hi - (hi - lo) * 0.08f;
        center[up] = floor;
        interiorFloorLocal = center;
        interiorUpLocal = Vector3.zero; interiorUpLocal[up] = sign;
        interiorAcrossA = Vector3.zero; interiorAcrossA[a1] = 1f;
        interiorAcrossB = Vector3.zero; interiorAcrossB[a2] = 1f;
        interiorRadiusLocal = radius;
        interiorDepthLocal = sign > 0f ? hi - floor : floor - lo;
        interiorKnown = true;
    }

    public Interior GetInterior()
    {
        if (!interiorKnown) LearnInterior();

        Transform frame = interiorFrame != null ? interiorFrame : transform;
        var inside = new Interior();
        inside.up = frame.TransformDirection(interiorUpLocal).normalized;
        inside.floorCenter = frame.TransformPoint(interiorFloorLocal);
        float ra = frame.TransformVector(interiorAcrossA * interiorRadiusLocal).magnitude;
        float rb = frame.TransformVector(interiorAcrossB * interiorRadiusLocal).magnitude;
        inside.radius = Mathf.Min(ra, rb) * waterInnerRatio;
        inside.rim = frame.TransformVector(interiorUpLocal * interiorDepthLocal).magnitude;
        inside.liquid = canHoldWater && WaterAmount > 0f;
        if (inside.liquid)
        {
            float level = Mathf.Lerp(waterBottomLocal.y, waterTopLocal.y, WaterAmount);
            // (water is laid out along model Y - the pots that hold it are modelled upright)
            inside.surface = transform.TransformVector(Vector3.up * (level - interiorFloorLocal.y)).magnitude;
        }
        inside.boil = IsBoiling ? 1f : inside.liquid ? Mathf.Clamp01(boilProgress / Mathf.Max(timeToBoilNoLid, 1f)) * 0.25f : 0f;
        return inside;
    }

    public bool IsFull => occupied.Count >= capacity;

    /// <summary>Everything resting in/on it.</summary>
    public IEnumerable<Carryable> Contents => occupied.Keys;

    // ---- Dishwashing ----
    // A dish that has held food (ingredients placed in it, seasoning added)
    // is dirty until washed at the sink; only a used-then-washed dish goes on
    // a DishShelfController (PlateShelf) - the exam's "다 쓴 그릇은 설거지해서
    // 정리" step.

    /// <summary>Has held food since it was last washed.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Was used and has been washed since.</summary>
    public bool IsWashed { get; private set; }

    /// <summary>A plate or bowl (Tableware) - not a pot/pan/wok or the measuring
    /// cup, which are held by a handle; these go up on the PlateShelves.</summary>
    public bool IsPlateOrBowl => isDish || (!HoldsByHandle && GetComponent<WokDragHandler>() == null);

    /// <summary>Nothing in it at all - no ingredients, no water.</summary>
    public bool IsEmpty => occupied.Count == 0 && WaterAmount <= 0f && !HasPortion;

    /// <summary>Holding part of a 종지 portioned out into it (PortionedCondiment).</summary>
    public bool HasPortion => GetComponent<PortionedCondiment>() is PortionedCondiment pc && !pc.Removed;

    /// <summary>The water surface (null while there's no water yet).</summary>
    public Transform WaterSurface => waterVisual != null ? waterVisual.transform : null;

    /// <summary>Pours all the water out (e.g. the clear water off settled starch).</summary>
    public void PourOffWater() => SetWaterAmount(0f);

    /// <summary>Sets how full of water it is (0..1) - e.g. draining while poured out.</summary>
    public void SetWaterLevel(float amount) => SetWaterAmount(amount);

    private void MarkUsed()
    {
        IsDirty = true;
        IsWashed = false;
    }

    /// <summary>Washes an empty dirty dish (WashBasin, dish in hand). Returns
    /// false if there's nothing to wash or it still has something in it.</summary>
    public bool TryWash()
    {
        // In clean-up every plate and bowl can be rinsed and put away, used or
        // not - a bowl's leftover water just gets poured out first.
        if (CleanupMode.IsOn && IsPlateOrBowl && occupied.Count == 0 && WaterAmount > 0f) SetWaterAmount(0f);
        if (CleanupMode.IsOn && IsPlateOrBowl && occupied.Count == 0 && HasPortion) GetComponent<PortionedCondiment>().Remove();
        if (!IsEmpty || (!IsDirty && !(IsPlateOrBowl && CleanupMode.IsOn))) return false;

        IsDirty = false;
        IsWashed = true;
        appliedSeasonings.Clear();
        return true;
    }

    /// <summary>The burner this vessel is currently sitting on, if any - set by
    /// StoveBurner when the vessel is placed/removed. Whatever's inside gets this
    /// burner (and this vessel's own WokDragHandler, for stir-fry) wired onto its
    /// CookableItem, so cooking progress isn't tied to one fixed burner reference
    /// chosen at design time.</summary>
    public StoveBurner CurrentBurner { get; private set; }

    public void SetBurner(StoveBurner newBurner)
    {
        CurrentBurner = newBurner;
        foreach (Carryable item in occupied.Keys) PropagateBurner(item);
    }

    private void PropagateBurner(Carryable item)
    {
        if (item is IngredientItem ing)
        {
            CookableItem cooked = ing.GetComponent<CookableItem>();
            if (cooked == null) return;
            cooked.SetBurner(CurrentBurner);
            cooked.SetWok(GetComponent<WokDragHandler>());
            cooked.SetVessel(this);
        }
    }

    /// <summary>Lifts every ingredient out of this vessel (the strainer scooping
    /// blanched peas out of the water) - each is released from this vessel's
    /// bookkeeping and left unparented for the caller to take.</summary>
    public void TakeIngredients(List<IngredientItem> result)
    {
        foreach (Carryable item in occupied.Keys)
            if (item is IngredientItem ing) result.Add(ing);

        foreach (IngredientItem ing in result)
        {
            Release(ing);
            ing.CurrentContainer = null;
            ing.transform.SetParent(null, true);
        }
    }

    private Transform ContentRoot
    {
        get
        {
            if (contentRoot == null)
            {
                // Reuse an existing child (e.g. one baked into the scene at edit time
                // with items already placed in it) rather than creating a duplicate -
                // contentRoot itself is a runtime-only field, so it starts null again
                // after every domain reload / scene load even when the GameObject
                // hierarchy underneath it was already there.
                Transform existing = transform.Find("ContentRoot");
                if (existing != null)
                {
                    contentRoot = existing;
                }
                else
                {
                    var go = new GameObject("ContentRoot");
                    contentRoot = go.transform;
                    contentRoot.SetParent(transform, false);

                    // Cancel out the plate's own (often non-uniform) scale so items
                    // parented here keep their natural size instead of being squashed
                    // into the plate's flattened-oval shape.
                    Vector3 s = transform.lossyScale;
                    contentRoot.localScale = new Vector3(
                        Mathf.Approximately(s.x, 0f) ? 1f : 1f / s.x,
                        Mathf.Approximately(s.y, 0f) ? 1f : 1f / s.y,
                        Mathf.Approximately(s.z, 0f) ? 1f : 1f / s.z);

                    // Cancel out the plate's own rotation too - some plate meshes (an
                    // imported model whose native "up" axis isn't the mesh's front
                    // face) are rotated in the scene to lie flat toward the camera.
                    // PlaceFree spreads items across local X/Y and stacks along local
                    // Z; without this, that X/Y/Z mapping would be skewed by whatever
                    // rotation the plate itself carries, and items dropped anywhere on
                    // screen would land clustered onto a thin sliver instead of
                    // spreading across the plate's visible surface. Keeping contents
                    // screen-aligned also just looks right - a carrot resting on a
                    // tilted plate shouldn't tilt along with the plate's own mesh.
                    contentRoot.rotation = Quaternion.identity;

                    // A dish whose own (hidden) pivot sits away from the model that
                    // actually shows (Bowl_Prepped: pivot at the counter's front,
                    // the plate far behind it) - contents go on the visible plate.
                    // Otherwise a piece set back on it landed by the pivot and was
                    // slid way up the plate's face by RestOnSurface.
                    Renderer shown = VisibleDishRenderer();
                    if (shown != null && shown.transform != transform)
                    {
                        Vector3 c = shown.bounds.center;
                        Vector3 p = transform.position;
                        if (new Vector2(c.x - p.x, c.y - p.y).magnitude > 0.3f || Mathf.Abs(c.z - p.z) > 1f)
                            contentRoot.position = c;
                    }
                }
            }

            return contentRoot;
        }
    }

    [Header("Frying oil")]
    /// <summary>The frying pan (Pan_01a): when today's exam has a deep-fried
    /// dish (탕수육 etc.), it starts already holding cooking oil up to
    /// oilLevel of its depth, the way the exam station is set up.</summary>
    [SerializeField] private bool oilForFrying;
    [SerializeField, Range(0.1f, 1f)] private float oilLevel = 0.5f;
    [SerializeField] private Color oilColor = new Color(0.86f, 0.66f, 0.18f);
    private static readonly string[] FriedDishKeywords = { "탕수육", "튀김", "강정", "까스", "커틀릿" };

    public bool HasFryingOil { get; private set; }

    /// <summary>Center of the round body (world), not the pivot - a pan's or
    /// wok's pivot sits at the edge of its body, by the handle, so placing
    /// it by pivot put the body off the burner. Model space: the bounds'
    /// thinnest axis is the depth, the longer of the other two runs along the
    /// handle, the shorter is the body's diameter; the body is the end of the
    /// handle axis nearer the pivot.</summary>
    public Vector3 BodyCenterWorld() => BodyPointWorld(false);

    /// <summary>The middle of the body's bottom (world) - what should sit over
    /// a burner's center. On the tilted view a tall pot's mid-height center
    /// shows above its base, so centering that put the pot low on the grate.</summary>
    public Vector3 BodyBottomWorld() => BodyPointWorld(true);

    private Vector3 BodyPointWorld(bool bottom)
    {
        Renderer rend = GetComponent<Renderer>();
        if (rend == null) return transform.position;
        Bounds lb = rend.localBounds;
        Vector3 size = lb.size;
        int up = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
        int a = (up + 1) % 3, b = (up + 2) % 3;
        int handle = size[a] >= size[b] ? a : b, across = handle == a ? b : a;
        float r = size[across] * 0.5f;

        Vector3 c = lb.center;
        if (bottom) c[up] = lb.min[up];
        if (size[handle] < size[across] * 1.15f) return transform.TransformPoint(c); // round, no handle

        int bodySide = BodySideFromMesh(handle, across, lb);
        if (bodySide == 0) // mesh not readable: assume the body is the end nearer the pivot
            bodySide = Mathf.Abs(lb.max[handle]) < Mathf.Abs(lb.min[handle]) ? 1 : -1;
        c[handle] = bodySide > 0 ? lb.max[handle] - r : lb.min[handle] + r;
        return transform.TransformPoint(c);
    }

    /// <summary>The round body alone (handle left out), as world axes: its up
    /// (the model's thinnest axis), the direction from the body out along the
    /// handle, and the body's world-space box. False for a vessel without a
    /// handle (a plain bowl).</summary>
    public bool TryGetHandleFrame(out Vector3 upWorld, out Vector3 handleWorld, out Bounds bodyWorld)
    {
        upWorld = Vector3.up; handleWorld = Vector3.right; bodyWorld = default;
        Renderer rend = GetComponent<Renderer>();
        if (rend == null) return false;
        Bounds lb = rend.localBounds;
        Vector3 size = lb.size;
        int up = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
        int a = (up + 1) % 3, b = (up + 2) % 3;
        int handle = size[a] >= size[b] ? a : b, across = handle == a ? b : a;
        if (size[handle] < size[across] * 1.15f) return false;

        float r = size[across] * 0.5f;
        int bodySide = BodySideFromMesh(handle, across, lb);
        if (bodySide == 0) bodySide = Mathf.Abs(lb.max[handle]) < Mathf.Abs(lb.min[handle]) ? 1 : -1;
        Vector3 c = lb.center;
        c[handle] = bodySide > 0 ? lb.max[handle] - r : lb.min[handle] + r;

        Vector3 axisUp = Vector3.zero, axisHandle = Vector3.zero;
        axisUp[up] = 1f;
        axisHandle[handle] = -bodySide; // from the body toward the handle's end
        upWorld = transform.TransformDirection(axisUp).normalized;
        handleWorld = transform.TransformDirection(axisHandle).normalized;

        Vector3 half = Vector3.zero;
        half[up] = size[up] * 0.5f; half[handle] = r; half[across] = r;
        bodyWorld = new Bounds(transform.TransformPoint(c), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
            bodyWorld.Encapsulate(transform.TransformPoint(c + corner));
        }
        return true;
    }

    private static readonly Dictionary<Mesh, int> bodySideCache = new Dictionary<Mesh, int>();

    /// <summary>Which end of the handle axis the round body is at (+1 max, -1
    /// min), from the model itself: the wider end. 0 if the mesh can't be read
    /// (set Read/Write on its import - Tools > Kitchen > Make Pan Meshes Readable).</summary>
    private int BodySideFromMesh(int handle, int across, Bounds lb)
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        Mesh mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null || !mesh.isReadable) return 0;
        if (bodySideCache.TryGetValue(mesh, out int side)) return side;

        float len = lb.size[handle], mid = lb.center[across];
        float wideMin = 0f, wideMax = 0f;
        foreach (Vector3 v in mesh.vertices)
        {
            float t = (v[handle] - lb.min[handle]) / Mathf.Max(len, 1e-6f);
            float w = Mathf.Abs(v[across] - mid);
            if (t < 0.2f) wideMin = Mathf.Max(wideMin, w);
            else if (t > 0.8f) wideMax = Mathf.Max(wideMax, w);
        }
        side = wideMax >= wideMin ? 1 : -1;
        bodySideCache[mesh] = side;
        return side;
    }

    /// <summary>A plate/dish (not a cooking vessel or water bowl): can only be
    /// picked up in clean-up mode (CleanupMode), to wash and put away.</summary>
    [SerializeField] private bool isDish;
    public bool IsDish => isDish;

    /// <summary>A strainer can be rested across it to drain what's in the
    /// basket (bowlSize2: 데친 두부 물기 빼기).</summary>
    [SerializeField] private bool strainerRest;

    /// <summary>Takes a strainer across it now: set up for it, nothing in it, no
    /// water standing in it (beyond what's dripped off), no strainer already on it.</summary>
    private bool CanRestStrainer => strainerRest && occupied.Count == 0 && WaterAmount < 0.2f
                                    && GetComponentInChildren<Strainer>() == null;

    /// <summary>A paper towel spread in this dish (ITowelLined), under its contents.</summary>
    private ToolItem towelLiner;
    public bool HasTowelLiner => towelLiner != null;

    public void AdoptLiner(ToolItem towel)
    {
        if (towelLiner != null) return;
        towelLiner = towel;
        towel.CurrentContainer = this;
    }

    private void LayTowelLiner(ToolItem towel)
    {
        Transform root = ContentRoot;
        Vector3 local = new Vector3(0f, 0f, SurfaceZOffset(Vector2.zero) + 0.015f); // just behind the contents
        if (!HandCarry.Instance.PlaceAtFreeLocalPosition(root, local)) return;
        towel.CurrentContainer = this;
        towelLiner = towel;
        Renderer body = GetComponent<Renderer>();
        Vector2 size = body != null ? (Vector2)body.bounds.size * 0.4f : Vector2.one;
        towel.SpreadAsLiner(this, root, size, local);
    }

    void Start()
    {
        if (oilForFrying && ExamHasFriedDish()) AddFryingOil();
    }

    /// <summary>Any exam paper naming a fried dish - with no named papers, the
    /// default exam (탕수육 · 마파두부, see CookingPlanManager) counts.</summary>
    private static bool ExamHasFriedDish()
    {
        bool anyNamed = false;
        foreach (ExamPaperFlip page in FindObjectsByType<ExamPaperFlip>(FindObjectsSortMode.None))
        {
            if (string.IsNullOrEmpty(page.DishName)) continue;
            anyNamed = true;
            foreach (string k in FriedDishKeywords)
                if (page.DishName.Contains(k)) return true;
        }
        return !anyNamed;
    }

    /// <summary>A level oil surface inside the body. Unlike the water fill this
    /// doesn't assume model Y is up - the pan models aren't authored that way -
    /// so "up" is the bounds' thinnest axis (a pan's depth); of the other two,
    /// the longer one carries the handle, and the body sits at the pivot along it.</summary>
    private void AddFryingOil()
    {
        Renderer bodyRend = GetComponent<Renderer>();
        if (bodyRend == null || HasFryingOil) return;
        HasFryingOil = true;

        Bounds lb = bodyRend.localBounds;
        Vector3 size = lb.size;
        int up = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
        int a = (up + 1) % 3, b = (up + 2) % 3;
        int handleAxis = size[a] >= size[b] ? a : b, across = handleAxis == a ? b : a;
        float radius = size[across] * 0.5f;

        Vector3 center = lb.center;
        center[handleAxis] = Mathf.Clamp(0f, lb.min[handleAxis] + radius, lb.max[handleAxis] - radius);
        center[up] = lb.min[up] + size[up] * oilLevel; // at 0.5 the same whichever side is the mouth

        Vector3 upDir = Vector3.zero;
        upDir[up] = 1f;

        GameObject oil = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        oil.name = "FryingOil";
        oilSurface = oil;
        Destroy(oil.GetComponent<Collider>());
        oil.transform.SetParent(transform, false);
        oil.transform.localRotation = Quaternion.FromToRotation(Vector3.up, upDir);
        oil.transform.localPosition = center;
        // Slightly narrower than the rim: a pan's walls flare, the oil sits lower in them.
        float d = radius * 2f * waterInnerRatio * Mathf.Lerp(0.8f, 1f, oilLevel);
        oil.transform.localScale = new Vector3(d, size[up] * 0.002f, d);

        var rend = oil.GetComponent<Renderer>();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        rend.material = lit != null ? new Material(lit) : new Material(rend.sharedMaterial);
        rend.material.color = oilColor;
        if (rend.material.HasProperty("_Smoothness")) rend.material.SetFloat("_Smoothness", 0.92f);
    }

    protected override void Awake()
    {
        base.Awake();
        ReconcileExistingContents();
        ReconcileExistingLid();
    }

    void Update()
    {
        UpdateOilHeat();
        if (!canHoldWater) return;

        bool canBoil = IsFilledWithWater && !IsHeld && CurrentBurner != null && CurrentBurner.IsOn;
        float timeToBoil = HasLid ? timeToBoilWithLid : timeToBoilNoLid;
        if (!canBoil)
        {
            // Off the heat the water cools - but slowly; back on, it's soon boiling again.
            boilProgress = Mathf.Max(0f, boilProgress - Time.deltaTime * 0.5f);
            SetBoiling(false);
        }
        else
        {
            boilProgress += Time.deltaTime * FlameRate;
            if (boilProgress >= timeToBoil) SetBoiling(true);
        }

        UpdateWaterLook(canBoil ? Mathf.Clamp01(boilProgress / Mathf.Max(timeToBoil, 0.01f)) : 0f);
    }

    /// <summary>The frying oil warming on a lit burner (cooling slowly off it); as
    /// it gets hot it darkens a little and, once at frying heat, shimmers.</summary>
    private void UpdateOilHeat()
    {
        if (!HasFryingOil) return;
        bool heating = !IsHeld && CurrentBurner != null && CurrentBurner.IsOn;
        oilHeat = heating ? Mathf.Min(oilHeat + Time.deltaTime * FlameRate, timeToHeatOil * 1.5f)
                          : Mathf.Max(0f, oilHeat - Time.deltaTime * 0.3f);
        // Frying oil hot on the flame spatters the burner's floor (under its grate).
        if (heating && OilHeat01 > 0.5f) BurnerFloorDirt.Soil(CurrentBurner, BurnerFloorDirt.SoilKind.Oil, Time.deltaTime);
        if (oilSurface == null) return;
        Renderer r = oilSurface.GetComponent<Renderer>();
        Color hot = Color.Lerp(oilColor, new Color(0.78f, 0.5f, 0.1f), 0.5f);
        Color c = Color.Lerp(oilColor, hot, OilHeat01);
        if (IsOilHot) c = Color.Lerp(c, Color.white, (Mathf.Sin(Time.time * 9f) * 0.5f + 0.5f) * 0.08f);
        r.material.color = c;
    }

    /// <summary>기름 온도 확인: a little starch batter dropped in the oil. Hot
    /// enough, it bobs straight back up and sizzles; too cold, it sinks and
    /// only drifts up slowly.</summary>
    public void DropBatterTest()
    {
        if (!HasFryingOil || oilSurface == null) return;
        StartCoroutine(BatterTest());
    }

    private System.Collections.IEnumerator BatterTest()
    {
        bool hot = IsOilHot;
        Transform surf = oilSurface.transform;
        Vector3 up = surf.up.normalized;
        if (Vector3.Dot(up, new Vector3(0f, 0.6f, -1f)) < 0f) up = -up;
        float r = surf.TransformVector(Vector3.right * 0.5f).magnitude;
        Vector3 at = surf.position + surf.TransformVector(new Vector3(0.18f, 0f, -0.12f));

        GameObject blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        blob.name = "BatterDrop";
        Destroy(blob.GetComponent<Collider>());
        blob.transform.localScale = Vector3.one * r * 0.16f;
        var mr = blob.GetComponent<Renderer>();
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit != null) mr.material = new Material(lit);
        mr.material.color = new Color(0.97f, 0.96f, 0.9f);

        Vector3 under = at - up * r * 0.25f;   // below the surface (hidden)
        Vector3 floating = at + up * r * 0.04f;
        float sink = hot ? 0.12f : 0.4f, rise = hot ? 0.18f : 4f;
        for (float t = 0f; t < sink; t += Time.deltaTime) { blob.transform.position = Vector3.Lerp(floating + up * r * 0.2f, under, t / sink); yield return null; }
        if (!hot) yield return new WaitForSeconds(1.5f);
        for (float t = 0f; t < rise; t += Time.deltaTime) { blob.transform.position = Vector3.Lerp(under, floating, t / rise); yield return null; }
        Debug.Log(hot ? "기름 온도 확인: 바로 떠오름 - 튀길 수 있어요" : "기름 온도 확인: 가라앉았다 천천히 올라옴 - 아직 덜 달궈졌어요");
        // Sizzling a moment (hot: a jittery bob, going golden), then gone.
        for (float t = 0f; t < 2.5f; t += Time.deltaTime)
        {
            if (hot)
            {
                blob.transform.position = floating + Random.insideUnitSphere * r * 0.01f;
                mr.material.color = Color.Lerp(new Color(0.97f, 0.96f, 0.9f), new Color(0.9f, 0.7f, 0.35f), t / 2.5f);
            }
            yield return null;
        }
        Destroy(blob);
    }

    /// <summary>Eases the water shader's heat (burner glow, first small bubbles)
    /// and boil (full rolling bubbles) toward the vessel's actual state, so the
    /// water visibly warms up and calms down again instead of snapping.</summary>
    private void UpdateWaterLook(float targetHeat)
    {
        if (waterMaterialInstance == null || WaterAmount <= 0f) return;

        float step = waterLookSpeed * Time.deltaTime;
        float newHeat = Mathf.MoveTowards(shownHeat, targetHeat, step);
        float newBoil = Mathf.MoveTowards(shownBoil, IsBoiling ? 1f : 0f, step * 2f);
        // Sloshes hard right after filling / while being carried, then settles.
        float newSlosh = IsHeld ? Mathf.Max(shownSlosh, 0.7f)
                                : Mathf.MoveTowards(shownSlosh, idleSlosh, 0.35f * Time.deltaTime);
        if (newHeat == shownHeat && newBoil == shownBoil && newSlosh == shownSlosh) return;

        shownHeat = newHeat;
        shownBoil = newBoil;
        shownSlosh = newSlosh;
        waterMaterialInstance.SetFloat(HeatId, shownHeat);
        waterMaterialInstance.SetFloat(BoilId, shownBoil);
        waterMaterialInstance.SetFloat(SloshId, shownSlosh);
    }

    private void SetBoiling(bool boiling)
    {
        if (IsBoiling == boiling) return;
        IsBoiling = boiling;

        if (boiling)
        {
            if (bubbleInstance == null && bubbleEffectPrefab != null && waterVisual != null)
            {
                bubbleInstance = Instantiate(bubbleEffectPrefab, waterVisual.transform);
                bubbleInstance.transform.localPosition = Vector3.zero;
                bubbleInstance.transform.localRotation = Quaternion.identity;
            }
            bubbleInstance?.SetActive(true);
        }
        else
        {
            bubbleInstance?.SetActive(false);
        }
    }

    /// <summary>A lid resting on this vessel at edit time (parented directly
    /// under it, not under ContentRoot - it's not "contents") needs its
    /// CurrentContainer/currentLid bookkeeping set up the same way pre-placed
    /// ingredients get it, or picking it up wouldn't know to clear this vessel's
    /// lid slot.</summary>
    private void ReconcileExistingLid()
    {
        foreach (Transform child in transform)
        {
            PotLid lid = child.GetComponent<PotLid>();
            if (lid == null) continue;

            currentLid = lid;
            lid.CurrentContainer = this;
            break;
        }
    }

    /// <summary>Picks up any items already sitting under ContentRoot at startup (e.g.
    /// ingredients arranged on this plate at edit time, like the exam's pre-portioned
    /// tray) into the occupied bookkeeping, so capacity/collision-avoidance accounts
    /// for them from the first frame instead of only learning about them once each is
    /// picked up and re-dropped.</summary>
    private void ReconcileExistingContents()
    {
        Transform root = ContentRoot;
        foreach (Transform child in root)
        {
            Carryable c = child.GetComponent<Carryable>();
            if (c == null || occupied.ContainsKey(c)) continue;

            Vector2 spot = new Vector2(child.localPosition.x, child.localPosition.y);
            occupied[c] = spot;
            MarkUsed(); // pre-portioned contents: emptied, this dish counts as used
            c.CurrentContainer = this;
            PropagateBurner(c);

            // Edit-time-authored children may have been positioned with an older/flat
            // z-offset that the sphere-bulge-aware SurfaceZOffset now supersedes -
            // recompute so they aren't buried behind the container's own geometry.
            Vector3 lp = child.localPosition;
            lp.z = SurfaceZOffset(spot);
            child.localPosition = lp;
        }
    }

    /// <summary>A plate still holding ingredients shouldn't be pickup-able itself -
    /// clicking it with an empty hand would otherwise carry off the whole plate
    /// (and everything on it) instead of letting the player take ingredients off
    /// one at a time. Only once every ingredient has been removed does the plate
    /// itself become a normal Carryable again.</summary>
    /// <summary>Whether an empty-handed click right now would pick this vessel
    /// itself up (not refused as a dish outside clean-up, nor taking what's in it).</summary>
    public bool WouldPickUp => !IsHeld && !(isDish && !DishesMovable) && occupied.Count == 0;

    /// <summary>Plates move in clean-up (정리) - and while the opening ingredient
    /// check (ExamBriefing) runs, to sort the ingredients into them per dish.</summary>
    private static bool DishesMovable => CleanupMode.IsOn || ExamBriefing.IsPlaying;

    protected override bool OnClickedWhileHandEmpty()
    {
        // Dishes only move in clean-up mode (the 정리 tab) - or during the opening check.
        if (isDish && !DishesMovable)
        {
            CleanupMode.NotifyBlocked();
            return true;
        }
        // A cheesecloth spread over it: it stays put under the cloth (the cloth
        // is lifted off first) - moving it away broke the cloth's hold on it.
        if (Cheesecloth.On(this) is Cheesecloth cloth)
        {
            cloth.Say("면보를 먼저 치우세요");
            return true;
        }
        return occupied.Count > 0;
    }

    /// <summary>Tips the vessel in hand (담기 - FryingPanPlating's pour).</summary>
    public void SetPourTilt(Quaternion tilt) => SetTempHoldRotation(tilt);

    protected override void OnClickedWhileHandFull(Carryable held)
    {
        // A cheesecloth over this bowl: what's in hand goes to the cloth (the
        // pan of chilli oil poured through it).
        if (Cheesecloth.On(this) is Cheesecloth spreadCloth)
        {
            spreadCloth.ClickedWith(held);
            return;
        }
        // The cheesecloth in hand spread over this bowl.
        if (held is Cheesecloth cloth)
        {
            cloth.TrySpreadOn(this);
            return;
        }

        // A bowl of strained oil (or other liquid sauce) poured into this one.
        if (held is MovableBowl sauceBowl && (object)sauceBowl != this && !IsDish && !sauceBowl.IsCookingVessel
            && !sauceBowl.HasIngredients && PanSauce.Of(sauceBowl, false) is PanSauce kept && kept.Amount > 0.01f)
        {
            StartCoroutine(PourSauceFrom(sauceBowl, kept));
            return;
        }

        // A pan of finished food clicked on a serving plate: 담기.
        if (held is MovableBowl heldPan && heldPan.GetComponent<FryingPanPlating>() is FryingPanPlating plating
            && plating.CanPlateOnto(this))
        {
            plating.PourInto(GetComponent<PlatingDestination>());
            return;
        }

        // Strainer (체망): empty -> scoops everything out of this vessel (e.g.
        // blanched peas out of the boiling water); full -> tips it all in here.
        if (held is Strainer strainer)
        {
            // 물기 빼기: an empty, dry bowl that takes a strainer rested across it.
            if (CanRestStrainer && strainer.RestOn(this)) return;
            if (!strainer.HasContents) strainer.ScoopFrom(this);
            // Into water: on a lit burner it's blanching - the strainer is set
            // down in the pot with the pieces in the boiling water; cold water
            // (a bowl filled at the faucet) is the 찬물 헹구기 dip.
            else if (WaterAmount > 0f && CurrentBurner != null && CurrentBurner.IsOn) strainer.DipInto(this);
            else if (WaterAmount > 0f) strainer.RinseIn(this);
            else strainer.DumpInto(this);
            return;
        }

        if (held is ToolItem knife && knife.Kind == ToolKind.Knife)
        {
            ScoopFromBoard();
            return;
        }

        // A bowl of ingredients in hand (the blanched tofu in bowlSize2) tipped
        // straight into this pot/pan/bowl; the bowl stays in hand.
        if (held is MovableBowl fromBowl && (object)fromBowl != this && !IsDish && fromBowl.GetComponent<FryingPanPlating>() == null
            && fromBowl.HasIngredients && !IsFull)
        {
            StartCoroutine(PourFrom(fromBowl));
            return;
        }

        // The spatula (뒤집개) stirs the pieces round - and through the sauce.
        if (held is ToolItem spatula && spatula.Kind == ToolKind.Spatula)
        {
            StirWithSpatula();
            return;
        }

        if (held is PotLid lid)
        {
            PlaceLid(lid);
            return;
        }

        // 기름 온도 확인: a little starch (앙금녹말/감자전분) off the spoon into the frying oil.
        if (HasFryingOil && held is ToolItem testSpoon && testSpoon.Kind == ToolKind.MeasuringSpoon
            && (testSpoon.LoadedSeasoning == PortionedCondiment.SettledStarch || testSpoon.LoadedSeasoning == PortionedCondiment.Starch))
        {
            testSpoon.ConsumeSeasoning();
            DropBatterTest();
            return;
        }

        // 앙금녹말: 감자전분 measured into a bowl spoon by spoon...
        if (held is ToolItem starchSpoon && starchSpoon.Kind == ToolKind.MeasuringSpoon
            && starchSpoon.LoadedSeasoning == PortionedCondiment.Starch)
        {
            PortionedCondiment pc = GetComponent<PortionedCondiment>();
            if (pc == null && IsPlateOrBowl && IsEmpty)
            {
                pc = gameObject.AddComponent<PortionedCondiment>();
                pc.Init(this, PortionedCondiment.Starch);
            }
            if (pc != null && pc.AddSpoon(starchSpoon.LoadedSeasoning))
            {
                starchSpoon.ConsumeSeasoning();
                StartCoroutine(SpoonDip(starchSpoon));
                HandController.Instance?.PlayChopGesture();
                return;
            }
        }

        // ...then stirred once with the empty spoon (after the water).
        if (held is ToolItem stirSpoon && stirSpoon.Kind == ToolKind.MeasuringSpoon && !stirSpoon.IsSpoonLoaded
            && GetComponent<PortionedCondiment>() is PortionedCondiment toStir && toStir.TryStir())
            return;

        // ...and once it's 앙금녹말, the empty spoon takes a little of it up.
        if (held is ToolItem scoopSpoon && scoopSpoon.Kind == ToolKind.MeasuringSpoon && !scoopSpoon.IsSpoonLoaded
            && GetComponent<PortionedCondiment>() is PortionedCondiment settled && settled.CurrentStage == PortionedCondiment.Stage.Drained)
        {
            scoopSpoon.LoadSeasoning(PortionedCondiment.SettledStarch);
            StartCoroutine(SpoonDip(scoopSpoon));
            return;
        }

        // A spoonful of water onto the starch (3 spoons, from the measuring cup).
        if (held is ToolItem waterSpoonIn && waterSpoonIn.Kind == ToolKind.MeasuringSpoon
            && waterSpoonIn.LoadedSeasoning == Condiments.Water
            && GetComponent<PortionedCondiment>() is PortionedCondiment portion && portion.AddSpoonOfWater())
        {
            waterSpoonIn.ConsumeSeasoning();
            StartCoroutine(SpoonDip(waterSpoonIn));
            HandController.Instance?.PlayChopGesture();
            return;
        }

        if (held is ToolItem spoon && spoon.Kind == ToolKind.MeasuringSpoon && spoon.IsSpoonLoaded)
        {
            AddSeasoning(spoon.LoadedSeasoning);
            StartCoroutine(SpoonDip(spoon));
            spoon.ConsumeSeasoning();
            return;
        }

        // An empty measuring spoon dipped into a bowl of strained oil (고추기름)
        // comes out with one spoonful of it - taken from the bowl, to add to
        // the cooking with the spoon as any seasoning.
        if (held is ToolItem oilSpoon && oilSpoon.Kind == ToolKind.MeasuringSpoon && !oilSpoon.IsSpoonLoaded && !IsCookingVessel
            && Cheesecloth.On(this) == null && PanSauce.Of(this, false) is PanSauce keptOil && keptOil.LiquidAmount >= 0.5f)
        {
            string what = keptOil.LiquidName;
            keptOil.Take(1f, true);
            oilSpoon.LoadSeasoning(what);
            StartCoroutine(SpoonDip(oilSpoon));
            Debug.Log($"[계량] {name}: {what} 1스푼 (남은 양 {keptOil.LiquidAmount:0.#}스푼)");
            return;
        }

        // An empty measuring spoon dipped into water (e.g. the measuring cup)
        // comes out with one spoonful of water - for 겨자 개기 and the like.
        if (held is ToolItem waterSpoon && waterSpoon.Kind == ToolKind.MeasuringSpoon && WaterAmount > 0f)
        {
            waterSpoon.LoadSeasoning(Condiments.Water);
            StartCoroutine(SpoonDip(waterSpoon));
            SetWaterAmount(WaterAmount - spoonOfWater);
            HandController.Instance?.PlayChopGesture();
            return;
        }

        // Using a bottle on a bowl measures one spoonful into the measuring
        // spoon (bottle back to the shelf, loaded spoon in hand) - the spoon
        // branch above then adds it on the next click.
        if (held is SeasoningBottle bottle)
        {
            bottle.TryScoopWithSpoon();
            return;
        }

        // A towel (wrapping an ingredient, or empty) needs somewhere to be set
        // down too - a plate/bowl's own accepts policy (usually IngredientOnly)
        // would otherwise silently reject the ToolItem itself and leave it
        // stuck in hand, since this isn't "adding the towel as bowl content".
        // Only the wrapping case matters for handing an ingredient off, but an
        // empty towel needs a resting spot just as much or it can never be set
        // down again once picked up with nothing to wrap.
        if (held is ToolItem towel && towel.IsTowelKind)
        {
            // A water-filled vessel gets wiped dry instead of having the towel
            // set down into it - "접시 물기닦기": drying off a washed bowl/plate
            // is a real step, and dropping the towel into standing water instead
            // would be an odd result of the same click.
            if (IsFilledWithWater)
            {
                SetFilledWithWater(false);
                ProcessStageManager.Instance?.MarkWiped();
                return;
            }

            // A fresh paper towel on an empty dish is spread out first, for
            // meat/tofu to drain on (towel down first, then the ingredient).
            if (towel.Kind == ToolKind.PaperTowel && !towel.IsWrapping && towelLiner == null && occupied.Count == 0)
            {
                LayTowelLiner(towel);
                return;
            }

            if (HandCarry.Instance.PlaceAtAnchor(transform)) towel.CurrentContainer = this;
            return;
        }

        if (IsFull) return;
        if (!Accepts(held)) return;

        HandCarry.Instance.PlaceAtBowl(this);
    }

    /// <summary>A pot, pan or wok things are cooked in (not a bowl or dish).</summary>
    public bool IsCookingVessel => !isDish && (name.Contains("Pot") || System.Array.IndexOf(FryingPanPlating.PanNames, name) >= 0
                                               || GetComponent<WokDragHandler>() != null || oilForFrying);

    /// <summary>Has ingredients (or scraps) in it to tip out.</summary>
    public bool HasIngredients
    {
        get
        {
            foreach (Carryable c in occupied.Keys) if (c is IngredientItem || c is FoodScrap) return true;
            return false;
        }
    }

    private bool pouring;

    /// <summary>The bowl in hand is tipped over this one and what's in it slides
    /// in (each taking its place here - the tofu's pieces go loose in the
    /// water), then it's set upright again, still in hand.</summary>
    /// <summary>The liquid kept in a bowl (strained 고추기름) poured in here:
    /// tipped over this one, all of it moved across (as it is - its colour,
    /// taste and heat go with it), upright again.</summary>
    private IEnumerator PourSauceFrom(MovableBowl from, PanSauce kept)
    {
        if (pouring) yield break;
        pouring = true;
        HandCarry hand = HandCarry.Instance;
        if (hand != null) hand.IsLocked = true;
        HandController.Instance?.GoTo(BodyCenterWorld() + new Vector3(0.6f, 0.7f, 0f) + new Vector3(0f, 0f, HandController.Instance.transform.position.z - BodyCenterWorld().z), null);
        yield return new WaitForSeconds(0.3f);
        for (float t = 0f; t < 0.3f; t += Time.deltaTime) { from.SetPourTilt(Quaternion.AngleAxis(-70f * t / 0.3f, Vector3.forward)); yield return null; }
        float total = kept.Amount;
        PanSauce into = PanSauce.Of(this, true);
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            into.Put(kept.Take(total * Time.deltaTime / 0.6f));
            yield return null;
        }
        into.Put(kept.Take(kept.Amount)); // (the last of it)
        MarkUsed();
        Debug.Log($"[옮겨 담기] {from.name} → {name}: 소스 {total:0.#}스푼");
        for (float t = 0f; t < 0.3f; t += Time.deltaTime) { from.SetPourTilt(Quaternion.AngleAxis(-70f * (1f - t / 0.3f), Vector3.forward)); yield return null; }
        from.SetPourTilt(Quaternion.identity);
        if (hand != null) hand.IsLocked = false;
        pouring = false;
    }

    private IEnumerator PourFrom(MovableBowl from)
    {
        if (pouring) yield break;
        pouring = true;
        HandCarry hand = HandCarry.Instance;
        if (hand != null) hand.IsLocked = true;
        // over this one, then tipped
        HandController.Instance?.GoTo(BodyCenterWorld() + new Vector3(0.6f, 0.7f, 0f) + new Vector3(0f, 0f, HandController.Instance.transform.position.z - BodyCenterWorld().z), null);
        yield return new WaitForSeconds(0.3f);
        for (float t = 0f; t < 0.3f; t += Time.deltaTime) { from.SetPourTilt(Quaternion.AngleAxis(-70f * t / 0.3f, Vector3.forward)); yield return null; }

        var food = new List<Carryable>();
        foreach (Carryable c in from.Contents) if ((c is IngredientItem || c is FoodScrap) && Accepts(c)) food.Add(c);
        foreach (Carryable c in food)
        {
            if (IsFull) break;
            from.Release(c);
            c.CurrentContainer = null;
            c.transform.SetParent(null, true);
            PlaceFree(c, BodyCenterWorld());
            c.transform.localScale = c.BaseLocalScale;
            c.CurrentContainer = this;
            c.OnPlaced();
            foreach (Collider col in c.GetComponentsInChildren<Collider>(true)) col.enabled = true;
            Debug.Log($"[옮겨 담기] {c.name}: {from.name} → {name}");
            // Allowed, but into a cooking pot/pan straight from the bowl it's
            // marked down: the bowl tipped over the heat (위생), and not put in
            // properly with a strainer/ladle (조리태도) - for the score later.
            if (IsCookingVessel)
            {
                string what = c is IngredientItem ing ? ing.IngredientName : c.name;
                ExamDeductions.Add(ExamDeductions.Hygiene, $"{what}: 그릇({from.name})째 기울여 {name}에 바로 부음");
                ExamDeductions.Add(ExamDeductions.Attitude, $"{what}: 도구 없이 그릇에서 {name}에 쏟아 넣음");
            }
            yield return new WaitForSeconds(0.08f);
        }

        for (float t = 0f; t < 0.3f; t += Time.deltaTime) { from.SetPourTilt(Quaternion.AngleAxis(-70f * (1f - t / 0.3f), Vector3.forward)); yield return null; }
        from.SetPourTilt(Quaternion.identity);
        if (hand != null) hand.IsLocked = false;
        pouring = false;
    }

    /// <summary>The measuring spoon dipped in here - its bowl (the front end, the
    /// one away from the hand) brought into the middle of this bowl, a little
    /// dip, and back. The hand alone used to come here, which left the handle
    /// end over the bowl and the spoon's bowl sticking out past it.</summary>
    private IEnumerator SpoonDip(ToolItem spoon, float seconds = 0.55f)
    {
        HandController hand = HandController.Instance;
        if (hand == null || spoon == null) yield break;
        Vector3 target = BodyCenterWorld();
        Vector3 reach = Vector3.zero;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            reach += ToolItem.TowardSpoonHead(spoon, target, Time.deltaTime);
            float dip = Mathf.Sin(Mathf.Clamp01(t / seconds) * Mathf.PI) * 0.05f;
            hand.SetGestureOffset(reach + Vector3.down * dip);
            yield return null;
        }
        // (and left there, the spoon's bowl over this one - the hand's next move
        // to somewhere else clears it, see HandController.GoTo)
        hand.SetGestureOffset(reach);
    }

    /// <summary>One stroke round the pan with the spatula: the hand sweeps a
    /// circle over it and every cut ingredient's pieces get pushed about.</summary>
    public bool StirWithSpatula()
    {
        bool any = false;
        foreach (Carryable c in occupied.Keys)
            if (c.GetComponent<FoodPieceGroup>() is FoodPieceGroup g) { g.Stir(); any = true; }
        if (!any) return false;
        if (stirRoutine != null) StopCoroutine(stirRoutine);
        stirRoutine = StartCoroutine(StirGesture());
        return true;
    }

    private Coroutine stirRoutine;

    private IEnumerator StirGesture()
    {
        HandController hand = HandController.Instance;
        if (hand == null) yield break;
        Interior inside = GetInterior();
        for (float t = 0f; t < 0.45f; t += Time.deltaTime)
        {
            float a = t / 0.45f * Mathf.PI * 2f;
            hand.SetGestureOffset(new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.6f, 0f) * inside.radius * 0.5f);
            yield return null;
        }
        hand.SetGestureOffset(Vector3.zero);
        stirRoutine = null;
    }

    public bool Accepts(Carryable c)
    {
        switch (accepts)
        {
            case SlotAccepts.IngredientOnly: return c is IngredientItem || c is FoodScrap; // scraps gather in a bowl too
            case SlotAccepts.ToolOnly: return c is ToolItem;
            default: return true;
        }
    }

    public override bool CanAcceptDrop(Carryable item)
    {
        if (item is Strainer strainer) return CanRestStrainer || (strainer.HasContents ? !IsFull : occupied.Count > 0);
        if (item is ToolItem knife && knife.Kind == ToolKind.Knife) return CanScoopFromBoard();
        if (item is ToolItem spatula && spatula.Kind == ToolKind.Spatula) return occupied.Count > 0;
        if (item is MovableBowl from && (object)from != this && !IsDish && from.GetComponent<FryingPanPlating>() == null
            && from.HasIngredients && !IsFull) return true; // tipped in here
        if (item is PotLid) return currentLid == null;
        if (item is ToolItem spoon && spoon.Kind == ToolKind.MeasuringSpoon && (spoon.IsSpoonLoaded || WaterAmount > 0f || HasPortion)) return true;
        if (item is SeasoningBottle) return true;
        if (item is ToolItem towel && towel.IsTowelKind) return true;
        return !IsFull && Accepts(item);
    }

    /// <summary>Sits any pot/pan lid on top of this vessel's own actual render
    /// bounds (not a fixed offset) so it works the same for every vessel shape -
    /// generic on purpose, since more lidded dishes are coming later.</summary>
    private PotLid currentLid;

    public bool HasLid => currentLid != null;

    private void PlaceLid(PotLid lid)
    {
        if (currentLid != null) return;

        Bounds worldBounds = GetComponent<Renderer>().bounds;
        Vector3 topWorld = new Vector3(worldBounds.center.x, worldBounds.center.y + worldBounds.extents.y * 0.85f, worldBounds.center.z);
        Vector3 topLocal = transform.InverseTransformPoint(topWorld);

        if (!HandCarry.Instance.PlaceAtFreeLocalPosition(transform, topLocal)) return;

        // Down onto the mouth itself, over the round body's middle: by the
        // whole model's bounds (handle and all) it sat high and off to the
        // handle's side, floating over the pot instead of covering it.
        Interior inside = GetInterior();
        Vector3 mouth = inside.floorCenter + inside.up * inside.rim;
        Bounds lidBounds = default;
        bool any = false;
        foreach (Renderer r in lid.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (!any) { lidBounds = r.bounds; any = true; } else lidBounds.Encapsulate(r.bounds);
        }
        if (any)
        {
            Vector3 shift = mouth - lidBounds.center;
            // Resting just on the rim, not pressed into it, and tipped a little
            // so a sliver of the gap under it shows (as a lid sits on a pot).
            lid.transform.position += new Vector3(shift.x, shift.y, 0f) + inside.up * (lidBounds.extents.y * 0.35f);
            lid.transform.RotateAround(mouth + inside.up * (lidBounds.extents.y * 0.35f) + Vector3.left * lidBounds.extents.x,
                Vector3.forward, 6f); // (its right side lifted off the rim)
        }

        currentLid = lid;
        lid.CurrentContainer = this;
    }

    private bool CanScoopFromBoard()
    {
        IngredientItem cut = CuttingBoardController.Instance?.Current as IngredientItem;
        return cut != null && cut.CurrentState == IngredientItem.State.Cut && !IsFull && Accepts(cut);
    }

    /// <summary>Using the flat of the knife to push cut ingredients into a bowl - the
    /// real-kitchen move that skips having to set the knife down first just to free a
    /// hand for the ingredient. Dragging the (still-held) knife onto a bowl scoops
    /// whatever's sitting cut on the cutting board straight in.</summary>
    private void ScoopFromBoard()
    {
        if (!CanScoopFromBoard()) return;

        CuttingBoardController board = CuttingBoardController.Instance;
        IngredientItem cut = (IngredientItem)board.Current;

        Vector3 dropPos = cut.transform.position;
        board.Release(cut);
        cut.CurrentContainer = null;

        PlaceFree(cut, dropPos);
        cut.transform.localScale = cut.BaseLocalScale;
        cut.CurrentContainer = this;
        cut.OnPlaced();
    }

    /// <summary>Parents the item under this plate at (a bounds-clamped, collision-
    /// avoided version of) wherever it currently sits in the world, so it lands
    /// freely on the plate's surface instead of snapping to a fixed slot.</summary>
    public void PlaceFree(Carryable item, Vector3 worldDropPosition)
    {
        Transform root = ContentRoot;
        item.transform.SetParent(root, false);

        Vector3 desired3 = root.InverseTransformPoint(worldDropPosition);
        Vector2 desired = new Vector2(
            Mathf.Clamp(desired3.x, -placementExtents.x, placementExtents.x),
            Mathf.Clamp(desired3.y, -placementExtents.y, placementExtents.y));

        Vector2 spot = FindFreeSpot(desired);
        item.transform.localPosition = new Vector3(spot.x, spot.y, SurfaceZOffset(spot));

        occupied[item] = spot;
        MarkUsed();
        PropagateBurner(item);
        // Food goes to its own place in the dish, like ingredients set out on a
        // plate (onion to one side, cucumber and carrot round the other) - each
        // new one where it's farthest from what's already there. They used to
        // pile up together at one edge. Cut ones go in as a heap that fits there.
        if (item is IngredientItem || item is FoodScrap)
        {
            Interior inside = GetInterior();
            if (item is IngredientItem cut && cut.CurrentState == IngredientItem.State.Cut)
                cut.GatherPieces(Mathf.Min(InsideRadius(), inside.radius * 0.45f));
            // What shows of it (cut slices lie off to one side of the pivot) goes on the spot.
            Vector3 want = FreeSlot(item, inside);
            Vector3 shown = VisibleCenter(item);
            item.transform.position += new Vector3(want.x - shown.x, want.y - shown.y, 0f);
        }
        RestOnSurface(item);
        SinkIntoWater(item);

        // Into the wok/frying pan without having drained on a paper towel first
        // (blanching tofu in the pot beforehand is fine - that comes before drying).
        if (item is IngredientItem ing && ing.NeedsTowelDrain && (GetComponent<WokDragHandler>() != null || HasFryingOil))
        {
            string what = ing.DrainOnTowel == IngredientItem.DrainNeed.Blood ? "핏물" : "물기";
            ExamDeductions.Add($"{ing.IngredientName}: 키친타올로 {what} 제거 없이 조리");
        }
    }

    /// <summary>The places food can go in this dish (world): round it at half its
    /// radius - left first, then upper right, lower right, upper left, right,
    /// lower left - and its middle; the one farthest from what's already in it.
    /// A small dish (a sauce bowl) just has its middle.</summary>
    private Vector3 FreeSlot(Carryable item, Interior inside)
    {
        if (inside.radius < 0.2f) return inside.floorCenter;
        Vector3 right = Vector3.ProjectOnPlane(Vector3.right, inside.up).normalized;
        Vector3 back = Vector3.Cross(right, inside.up).normalized;
        // A deep bowl (meat put in to season, cut pieces): the middle first, and
        // the others closer in - set round the edge of a bowl, food sat on its
        // wall, half out of it. A flat plate keeps the ring, left first.
        bool deep = inside.rim > inside.radius * 0.35f;
        float r = inside.radius * (deep ? 0.35f : 0.5f);
        var slots = new List<Vector3>();
        if (deep) slots.Add(inside.floorCenter);
        foreach (float deg in new[] { 180f, 60f, -60f, 120f, 0f, -120f })
        {
            float a = deg * Mathf.Deg2Rad;
            slots.Add(inside.floorCenter + right * (Mathf.Cos(a) * r) + back * (Mathf.Sin(a) * r));
        }
        if (!deep) slots.Add(inside.floorCenter);

        var others = new List<Vector2>();
        foreach (Carryable c in occupied.Keys)
            if (c != null && (object)c != item) others.Add(VisibleCenter(c));
        if (others.Count == 0) return slots[0];

        // The first of them (in that order) that's clear of everything there;
        // all taken, the one with the most room.
        Vector3 best = slots[0];
        float bestGap = -1f;
        foreach (Vector3 s in slots)
        {
            float gap = float.MaxValue;
            foreach (Vector2 o in others) gap = Mathf.Min(gap, ((Vector2)s - o).magnitude);
            if (gap >= r * 0.8f) return s;
            if (gap > bestGap + 0.01f) { bestGap = gap; best = s; }
        }
        return best;
    }

    private static Vector3 VisibleCenter(Carryable c)
    {
        if (c is IngredientItem ing) return ing.VisibleBounds.center;
        Bounds b = new Bounds(c.transform.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in c.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b.center;
    }

    /// <summary>Sets a piece down ON the plate/bowl: moved along the dish's own up
    /// (toward the camera on the tilted view) until its lowest point sits on the
    /// dish's middle plane. The old bulge estimate (SurfaceZOffset) left small
    /// pieces - two garlic cloves - buried behind a big plate's face, out of sight.</summary>
    private void RestOnSurface(Carryable item)
    {
        Renderer dish = VisibleDishRenderer();
        if (dish == null) return;

        Vector3 up = dish.transform.TransformDirection(Vector3.up).normalized;
        if (Vector3.Dot(up, new Vector3(0f, 0.6f, -1f)) < 0f) up = -up;
        Vector3 plane = dish.bounds.center;

        float lowest = float.MaxValue;
        foreach (MeshFilter mf in item.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Renderer mr = mf.GetComponent<Renderer>();
            if (mr == null || (!mr.enabled && mf.transform != item.transform)) continue;
            foreach (Vector3 v in mf.sharedMesh.vertices)
                lowest = Mathf.Min(lowest, Vector3.Dot(mf.transform.TransformPoint(v) - plane, up));
        }
        if (lowest == float.MaxValue) return;
        // Along the camera's line of sight, not the dish's up: the piece stays on
        // screen right where it was set down (moving along up slid it up the plate).
        float need = 0.01f - lowest;
        if (Mathf.Abs(up.z) > 0.3f) item.transform.position += new Vector3(0f, 0f, need / up.z);
        else item.transform.position += up * need;
    }

    /// <summary>About how far from the middle (world, across the screen) things
    /// put in here can reach and still be inside it.</summary>
    private float InsideRadius()
    {
        if (canHoldWater && waterVisual != null)
            return waterVisual.transform.TransformVector(Vector3.right * 0.5f).magnitude * 0.6f;
        Renderer dish = VisibleDishRenderer();
        return dish != null ? dish.bounds.extents.x * 0.6f : 0.3f;
    }

    /// <summary>The dish's own renderer, or - when that's hidden - the model child that shows.</summary>
    private Renderer VisibleDishRenderer()
    {
        Renderer dish = GetComponent<Renderer>();
        if (dish != null && dish.enabled) return dish;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            if (r.enabled && r.GetComponentInParent<Carryable>() == this && !(r is ParticleSystemRenderer)) return r;
        return dish;
    }

    /// <summary>Into a pot/bowl of water, a piece goes in the water - not lying
    /// on top of the surface in front of it: it's drawn in toward the middle of
    /// the water and sunk along the vessel's up until only its top shows above
    /// the (opaque) surface.</summary>
    private void SinkIntoWater(Carryable item)
    {
        if (WaterRestOffset(item, out Vector3 move)) item.transform.position += move;
    }

    /// <summary>A floater (CookableItem.floatsWhenDone, the tofu) done blanching
    /// comes up from low in the water to ride on the surface.</summary>
    public void Refloat(Carryable item)
    {
        if (item != null && WaterRestOffset(item, out Vector3 move)) StartCoroutine(FloatUp(item, move));
    }

    private IEnumerator FloatUp(Carryable item, Vector3 move)
    {
        Vector3 done = Vector3.zero;
        for (float t = 0f; t < 1.2f; t += Time.deltaTime)
        {
            if (item == null || item.IsHeld || !item.transform.IsChildOf(transform)) yield break;
            // up past it a touch, then a little bob back
            float k = Mathf.SmoothStep(0f, 1f, t / 1.2f);
            Vector3 want = move * (k + Mathf.Sin(k * Mathf.PI) * 0.15f);
            item.transform.position += want - done;
            done = want;
            yield return null;
        }
        if (item != null && !item.IsHeld) item.transform.position += move - done;
    }

    /// <summary>How far to move an ingredient so it sits in the water: within
    /// the water's circle, its middle at the surface - the lower part under,
    /// the top showing (any deeper and the opaque surface hid it altogether);
    /// a floater lies lower until it's done (CookableItem.WaterSinkFactor).</summary>
    private bool WaterRestOffset(Carryable item, out Vector3 move)
    {
        move = Vector3.zero;
        if (!canHoldWater || WaterAmount <= 0f || waterVisual == null || !(item is IngredientItem)) return false;
        // Cut pieces show instead of the (then hidden) ingredient itself.
        bool any = false;
        Bounds b = default;
        foreach (Renderer r in item.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer || r.GetComponentInParent<TowelSpread>() != null) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) return false;

        Transform surface = waterVisual.transform;
        Vector3 up = transform.TransformDirection(Vector3.up).normalized;
        if (Vector3.Dot(up, new Vector3(0f, 0.6f, -1f)) < 0f) up = -up;

        Vector3 flat = Vector3.ProjectOnPlane(b.center - surface.position, up);
        float waterRadius = surface.TransformVector(Vector3.right * 0.5f).magnitude * 0.45f;
        if (flat.magnitude > waterRadius) move -= flat - flat.normalized * waterRadius;

        float half = Mathf.Abs(b.extents.x * up.x) + Mathf.Abs(b.extents.y * up.y) + Mathf.Abs(b.extents.z * up.z);
        float s = Vector3.Dot(b.center + move - surface.position, up);
        float factor = item.GetComponent<CookableItem>() is CookableItem ck ? ck.WaterSinkFactor : 0.15f;
        move += up * (half * factor - s);
        return true;
    }

    /// <summary>How far in front of this container's own center an item at the given
    /// (x,y) offset must sit to clear the container's own surface. Some plate meshes
    /// are a full sphere flattened into a dish shape, not a flat disc, so their front
    /// face bulges toward the camera - most near the center, less near the rim. A flat
    /// offset from the transform's own z (the old approach) only cleared that bulge
    /// right at the rim; everywhere closer to center it buried the item behind the
    /// plate's own geometry, which is why pre-placed ingredients disappeared.
    /// Uses the plate's actual world-space render bounds rather than assuming a unit
    /// sphere (radius 0.5) mesh, so this also works for an imported plate model with
    /// its own real proportions - a thin, wide mesh barely bulges at all, which is
    /// the right behavior for something that's actually closer to a flat disc.</summary>
    private float SurfaceZOffset(Vector2 localXY)
    {
        Bounds worldBounds = GetComponent<Renderer>().bounds;
        float a = Mathf.Max(worldBounds.extents.x, 0.001f);
        float b = Mathf.Max(worldBounds.extents.y, 0.001f);
        float c = worldBounds.extents.z;

        float nx = localXY.x / a;
        float ny = localXY.y / b;
        float underSqrt = 1f - nx * nx - ny * ny;
        float bulge = underSqrt > 0f ? c * Mathf.Sqrt(underSqrt) : 0f;

        // A flat base offset (not just the tiny per-item stacking increment) keeps every
        // item - including the very first one placed - solidly in front of the plate's
        // own collider, so a raycast at an item's position always resolves to the item
        // rather than the plate underneath it.
        return -(bulge + 0.03f + 0.01f * occupied.Count);
    }

    private Vector2 FindFreeSpot(Vector2 desired)
    {
        if (IsFreeSpot(desired)) return desired;

        Vector2 step = minSeparation;
        for (int ring = 1; ring <= 8; ring++)
        {
            float radius = Mathf.Max(step.x, step.y) * ring;
            int samples = 6 * ring;
            for (int i = 0; i < samples; i++)
            {
                float angle = (Mathf.PI * 2f / samples) * i;
                Vector2 candidate = desired + new Vector2(Mathf.Cos(angle) * step.x, Mathf.Sin(angle) * step.y) * ring;
                candidate.x = Mathf.Clamp(candidate.x, -placementExtents.x, placementExtents.x);
                candidate.y = Mathf.Clamp(candidate.y, -placementExtents.y, placementExtents.y);
                if (IsFreeSpot(candidate)) return candidate;
            }
        }

        return desired; // plate is packed tight enough that nothing opened up - overlap rather than get stuck
    }

    private bool IsFreeSpot(Vector2 p)
    {
        Vector2 sep = minSeparation;
        foreach (Vector2 occ in occupied.Values)
        {
            float dx = (p.x - occ.x) / Mathf.Max(sep.x, 0.0001f);
            float dy = (p.y - occ.y) / Mathf.Max(sep.y, 0.0001f);
            if (dx * dx + dy * dy < 1f) return false;
        }
        return true;
    }

    public void Release(Carryable item)
    {
        if ((object)item == towelLiner)
        {
            towelLiner = null;
            return;
        }

        if ((object)item == currentLid)
        {
            currentLid = null;
            return;
        }

        occupied.Remove(item);

        // Taken back out of the vessel - no longer sitting over any burner,
        // however this vessel itself is currently placed.
        if (item is IngredientItem ing)
        {
            CookableItem cooked = ing.GetComponent<CookableItem>();
            if (cooked != null) { cooked.SetBurner(null); cooked.SetWok(null); cooked.SetVessel(null); }
        }
    }

    /// <summary>Fills (or empties) this vessel all at once - emptying is the
    /// towel wipe; filling instantly is kept for any caller that just wants it
    /// full. The faucet fills gradually through AddWater instead.</summary>
    public void SetFilledWithWater(bool filled)
    {
        if (!canHoldWater) return;
        SetWaterAmount(filled ? 1f : 0f);
    }

    /// <summary>Called every frame by a running WaterTap while this vessel sits
    /// (or is held) under its stream - the water visibly rises to half full
    /// over secondsToFill, then stops.</summary>
    public void AddWater(float deltaTime)
    {
        if (!canHoldWater || WaterAmount >= 1f) return;
        SetWaterAmount(WaterAmount + deltaTime / Mathf.Max(secondsToFill, 0.01f));
    }

    private void SetWaterAmount(float amount)
    {
        float previous = WaterAmount;
        WaterAmount = Mathf.Clamp01(amount);
        IsFilledWithWater = WaterAmount >= 1f;

        if (!IsFilledWithWater)
        {
            boilProgress = 0f;
            SetBoiling(false);
        }

        if (WaterAmount > 0f) EnsureWaterVisual();

        // Water just started going in - fresh water sloshes, then settles.
        if (previous <= 0f && WaterAmount > 0f)
        {
            shownHeat = 0f;
            shownBoil = 0f;
            shownSlosh = 1f;
            if (waterMaterialInstance != null)
            {
                waterMaterialInstance.SetFloat(HeatId, 0f);
                waterMaterialInstance.SetFloat(BoilId, 0f);
                waterMaterialInstance.SetFloat(SloshId, shownSlosh);
            }
        }

        if (waterVisual == null) return;
        waterVisual.SetActive(WaterAmount > 0f);

        // Surface rises from just above the bottom to the full line, spreading
        // out a little as it goes (a thin puddle first, then the full surface).
        waterVisual.transform.localPosition = Vector3.Lerp(waterBottomLocal, waterTopLocal, WaterAmount);
        float d = waterDiameter * Mathf.Lerp(0.75f, 1f, WaterAmount);
        waterVisual.transform.localScale = new Vector3(d, waterThickness, d);
    }

    private void EnsureWaterVisual()
    {
        if (waterVisual != null) return;

        waterVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        waterVisual.name = "WaterFill";
        Object.DestroyImmediate(waterVisual.GetComponent<Collider>());

        // Laid out in the vessel's own local space (model Y = up), so the
        // surface sits level inside the mouth and tilts with the vessel -
        // a pot angled toward the camera shows an oval of water inside its
        // rim instead of a camera-facing circle floating off to one side.
        Bounds lb = GetComponent<Renderer>().localBounds;
        FindBodyCircle(lb, out Vector3 center, out float bodyRadius);
        center += waterLocalOffset;

        waterBottomLocal = new Vector3(center.x, lb.min.y + lb.size.y * 0.08f + waterLocalOffset.y, center.z);
        waterTopLocal = new Vector3(center.x, lb.min.y + lb.size.y * fullWaterLevel + waterLocalOffset.y, center.z);
        waterDiameter = bodyRadius * 2f * waterInnerRatio;
        waterThickness = lb.size.y * 0.002f;

        waterVisual.transform.SetParent(transform, false);
        waterVisual.transform.localRotation = Quaternion.identity;

        var rend = waterVisual.GetComponent<Renderer>();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        Shader waterShader = Shader.Find("Kitchen/PotWater");
        if (waterMaterial != null) waterMaterialInstance = new Material(waterMaterial);
        else if (waterShader != null) waterMaterialInstance = new Material(waterShader);

        if (waterMaterialInstance != null)
        {
            rend.material = waterMaterialInstance;
        }
        else
        {
            // Shader missing (e.g. stripped from a build) - plain blue tint.
            rend.material = new Material(rend.sharedMaterial.shader);
            rend.material.color = new Color(0.3f, 0.55f, 0.9f);
        }
    }

    /// <summary>The round body of this vessel in its own local XZ plane (model Y
    /// = up), ignoring any handle. A handle stretches the bounds along its own
    /// direction, so the bounds center sits off toward the handle and the water
    /// used to spill over the rim on that side. With a readable mesh, a few
    /// mean-shift steps over the vertices pull the center onto the body itself
    /// (the handle's vertices fall outside the body radius and drop out);
    /// otherwise the model's pivot - normally the body's center for a pot - is
    /// used along the handle's axis, and waterLocalOffset can nudge it.</summary>
    private void FindBodyCircle(Bounds localBounds, out Vector3 center, out float radius)
    {
        center = localBounds.center;
        float ex = localBounds.extents.x;
        float ez = localBounds.extents.z;
        // Smaller of the two horizontal extents - the handle only lengthens one.
        radius = Mathf.Min(ex, ez);

        // A handled pot/pan: the body is the round end of the handle's axis,
        // one radius in from that end - the same reading BodyCenterWorld uses.
        // (Mean-shifting the vertices from the bounds center got caught between
        // the body and the long handle and left the water off toward it.)
        if (Mathf.Abs(ex - ez) > radius * 0.15f)
        {
            int handle = ex > ez ? 0 : 2, across = handle == 0 ? 2 : 0;
            int side = BodySideFromMesh(handle, across, localBounds);
            if (side == 0) side = Mathf.Abs(localBounds.max[handle]) < Mathf.Abs(localBounds.min[handle]) ? 1 : -1;
            center[handle] = side > 0 ? localBounds.max[handle] - radius : localBounds.min[handle] + radius;
            return;
        }

        MeshFilter mf = GetComponent<MeshFilter>();
        Mesh mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null || !mesh.isReadable) return;

        Vector3[] verts = mesh.vertices;
        if (verts.Length == 0) return;

        Vector2 c = new Vector2(center.x, center.z);
        float window = radius * 1.05f;
        for (int iter = 0; iter < 8; iter++)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (Vector3 v in verts)
            {
                Vector2 w = new Vector2(v.x, v.z);
                if ((w - c).sqrMagnitude > window * window) continue;
                sum += w;
                count++;
            }
            if (count == 0) break;
            c = sum / count;
        }

        float maxDist = 0f;
        foreach (Vector3 v in verts)
        {
            float d = (new Vector2(v.x, v.z) - c).magnitude;
            if (d <= window && d > maxDist) maxDist = d;
        }
        if (maxDist > 0f) radius = maxDist;
        center.x = c.x;
        center.z = c.y;
    }

    /// <summary>While this bowl itself is being dragged, its contents ride along as
    /// attached baggage - if their colliders stay active, a raycast aimed at wherever
    /// the player drops the bowl can hit an ingredient sitting on it instead of the
    /// actual destination underneath, leaving the bowl stuck in hand. Called by
    /// Carryable around its own drag collider toggle.</summary>
    public void SetContentsCollidersEnabled(bool enabled)
    {
        foreach (Carryable item in occupied.Keys)
        {
            Collider c = item.GetComponent<Collider>();
            if (c != null) c.enabled = enabled;
            // a cut ingredient's pieces, each with its own
            if (item.GetComponent<FoodPieceGroup>() is FoodPieceGroup g)
                foreach (FoodPiece p in g.Pieces) if (p != null && p.TryGetComponent(out Collider pc)) pc.enabled = enabled;
        }

        // The spread towel too, or a drag of this dish could land on its own towel.
        if (towelLiner != null)
            foreach (TowelSpread s in ContentRoot.GetComponentsInChildren<TowelSpread>())
                if (s.TryGetComponent(out Collider sc)) sc.enabled = enabled;
    }
}
