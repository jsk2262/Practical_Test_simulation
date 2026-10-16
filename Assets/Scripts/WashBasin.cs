using UnityEngine;

public class WashBasin : Interactable
{
    /// <summary>The full hand-wash routine (washing your own hands, empty-handed).</summary>
    [SerializeField] private HandWashSequence handWash;

    protected override void Activate()
    {
        if (washing) return;
        HandCarry hand = HandCarry.Instance;
        // (with something in hand it's that thing that gets washed - under the
        // running tap - never the empty-handed hand-washing motion)
        if (hand?.Current is IngredientItem ing)
        {
            CookableItem cooked = ing.GetComponent<CookableItem>();
            if (cooked != null && cooked.CurrentStage == CookableItem.Stage.Done)
            {
                StartCoroutine(RinseHeld(ing, false, false, cooked.ColdShock));
            }
            else if (ing.CurrentState == IngredientItem.State.Raw)
            {
                if (ing.NeedsSoaking) ing.StartSoaking();
                else StartCoroutine(RinseHeld(ing, false, false, ing.Wash));
            }
        }
        else if (hand?.Current is Cheesecloth cloth)
        {
            // 면보 빨기: rinsed under the tap - clean again (the chilli left in
            // it goes in the food waste first).
            if (cloth.HasResidue) cloth.Say("찌꺼기를 먼저 음식물쓰레기에 버리세요");
            else StartCoroutine(RinseHeld(cloth, false, false, cloth.Wash));
        }
        else if (hand?.Current is Strainer strainer)
        {
            // 찬물 헹구기: blanched pieces scooped out with the strainer get a
            // cold-water rinse right in it.
            if (strainer.HasContents) StartCoroutine(RinseHeld(strainer, false, false, strainer.RinseInColdWater));
        }
        else if (hand?.Current is ToolItem tool && tool.Kind == ToolKind.Knife)
        {
            // 칼 씻기: water down the blade, one wipe with the sponge, turned
            // over and the same on the other side.
            StartCoroutine(RinseHeld(tool, true, true, () => tool.GetComponent<HygieneTracker>()?.Wash()));
        }
        else if (hand?.Current is ToolItem measuringSpoon && measuringSpoon.Kind == ToolKind.MeasuringSpoon)
        {
            // 계량스푼 씻기: 담겨 있던 것은 흘려 버리고 물에 헹군다 (들고 있는 그대로).
            if (measuringSpoon.IsSpoonLoaded)
            {
                Debug.Log($"[세척] 계량스푼에 남은 {measuringSpoon.LoadedSeasoning} 버림");
                measuringSpoon.ConsumeSeasoning();
            }
            StartCoroutine(RinseHeld(measuringSpoon, false, false, () =>
            {
                measuringSpoon.GetComponent<HygieneTracker>()?.Wash();
                measuringSpoon.SpoonNotice("계량스푼 세척 완료");
            }));
        }
        else if (hand?.Current is ToolItem towel && towel.IsTowelKind)
        {
            // A wrapped towel needs somewhere to unwrap, and a plain one
            // (e.g. picked up with nothing on the board to wrap) needs
            // somewhere to rest too - without the plain case, a towel picked
            // up empty-handed had nowhere it could ever be set back down
            // again, leaving it stuck in hand for the rest of the session.
            HandCarry.Instance.PlaceAtAnchor(transform);
            towel.CurrentContainer = null;
        }
        else if (hand?.Current is MovableBowl starchBowl && starchBowl.GetComponent<PortionedCondiment>() is PortionedCondiment starch
                 && starch.TryDrain())
        {
            // 앙금녹말: the clear water over the settled starch poured off into
            // the sink; the bowl stays in hand.
            HandController.Instance?.PlayChopGesture();
        }
        else if (hand?.Current is MovableBowl wet && wet.WaterAmount > 0f && !CleanupMode.IsOn)
        {
            // 물 버리기: the first click shows the pour arrow over the sink, a
            // second (on the sink or the arrow) tips the water out.
            if ((object)pourPending == wet) PourOut(wet);
            else ShowPourArrow(wet);
        }
        else if (hand?.Current is MovableBowl dirtyDish && dirtyDish.TryWash())
        {
            // 설거지: a used, emptied dish held at the sink gets washed and
            // stays in hand, ready to go up on a PlateShelf.
            StartCoroutine(RinseHeld(dirtyDish, true, false, null));
        }
        else if (hand?.Current is MovableBowl bowl)
        {
            // Just resting the bowl at the sink (e.g. to fill it under the faucet
            // right there) - not itself a wash action, unlike the ingredient/knife
            // cases above.
            HandCarry.Instance.PlaceAtAnchor(transform);
            bowl.CurrentContainer = null;
            CenterInBasin(bowl);

            // A pot is held by its handle in the sink, left hand on the handle
            // (it stays there - following the pot - until the pot is taken again).
            bowl.ShowHandleHeldInPlace();
        }
        else if (hand?.Current is PotLid lid)
        {
            // A lid taken off one pot and not immediately handed to another
            // needs somewhere to rest too, or it's stuck in hand for good -
            // same reasoning as the empty-towel fix above.
            HandCarry.Instance.PlaceAtAnchor(transform);
            lid.CurrentContainer = null;
        }
        else if (hand?.Current == null)
        {
            // Clicking the sink with nothing in hand is the player washing their
            // own hands - the real exam's opening step, and one of the 순서도
            // planning cards. With the 15-second wash sequence set up (Tools >
            // Kitchen > Setup Hand Wash) that plays the full 1→2→3→5→6→9→10
            // routine; otherwise just the short rub gesture as before.
            // 전체 과정은 처음(시험 시작) 한 번과 마지막 정리 때 한 번만 - 그 사이
            // 중간중간 손씻기는 1단계(적시기)만 하는 간단 버전.
            if (handWash != null)
            {
                bool full = !handWash.HandsWashed || (CleanupMode.IsOn && !cleanupWashDone);
                if (full && CleanupMode.IsOn) WatchCleanupWash();
                handWash.BeginWash(!full);
            }
            else HandController.Instance?.PlayWashGesture();
        }
    }

    /// <summary>마지막 정리 때 전체 손씻기를 끝까지 했는지 (중간에 끊기면 다시 전체).</summary>
    private bool cleanupWashDone, watchingCleanupWash;

    private void WatchCleanupWash()
    {
        if (watchingCleanupWash) return;
        watchingCleanupWash = true;
        handWash.onCompleted.AddListener(() =>
        {
            if (CleanupMode.IsOn && !handWash.IsShortWash) cleanupWashDone = true;
        });
    }

    // ---- washing what's in hand ----

    private bool washing;
    private WaterTap tap;
    private WaterTap Tap => tap != null ? tap : (tap = FindFirstObjectByType<WaterTap>());
    private GameObject sponge;
    private static Material dropMaterial;

    /// <summary>What's in hand is held under the tap (the water turned on for
    /// it and back off after, if it was off), the water running over it. With
    /// sponge: one wipe along it (a knife: from the handle end to the tip).
    /// With flip: turned over and the same again on the other side. done runs
    /// at the end (the item counted as washed).</summary>
    private System.Collections.IEnumerator RinseHeld(Carryable item, bool withSponge, bool flip, System.Action done)
    {
        washing = true;
        HandCarry hand = HandCarry.Instance;
        HandController hc = HandController.Instance;
        if (hand != null) hand.IsLocked = true;
        WaterTap t = Tap;
        bool wasOn = t != null && t.IsOn;

        // The item's middle just under the stream.
        Vector3 stream = t != null ? t.StreamPoint : BasinCenter() + Vector3.up;
        // Down in the middle of the basin, over the drain (the round one in the
        // middle of the sink), with the tap's water falling onto it.
        // The hand where hands are washed (HandWashSequence's WashOrigin, right
        // under the tap), the item held in it there.
        // 화면에서 보이는 물체의 중심을 배수구보다 위쪽에 맞춘다.
        // 손잡이/피벗 위치가 달라도 팬 몸통과 칼날이 개수대 밖으로 벗어나지 않는다.
        item.transform.rotation = item.HandRotation;
        Vector3 target = ItemWashTarget(stream);
        Vector3 visibleCenter = item is MovableBowl dish
            ? dish.BodyCenterWorld() : ItemBounds(item).center;
        Vector3 centerFromHand = visibleCenter - item.transform.position + item.HandHoldOffset;
        Vector3 handAt = target - centerFromHand;
        if (hc != null) hc.GoTo(new Vector3(handAt.x, handAt.y, hc.transform.position.z), null);
        yield return new WaitForSeconds(0.4f);
        t?.SetRunning(true);

        Vector3 axis = LongAxis(item);
        // (들 때 이미 돌려 둔 자세 - 뜰채를 세우고 뒤집은 것 등 - 위에 얹어서 돌린다;
        // 헹군 뒤에도 그 자세로 돌아오게)
        Quaternion baseTurn = item.HoldTurn;
        for (int side = 0; side < (flip ? 2 : 1); side++)
        {
            Quaternion turn = (side == 0 ? Quaternion.identity : Quaternion.AngleAxis(180f, axis)) * baseTurn;
            if (side == 1)
            {
                // turned over (about its own length) to wash the other face
                for (float k = 0f; k < 0.5f; k += Time.deltaTime)
                {
                    item.SetHoldTurn(Quaternion.AngleAxis(180f * Mathf.SmoothStep(0f, 1f, k / 0.5f), axis) * baseTurn);
                    yield return null;
                }
                item.SetHoldTurn(turn);
            }
            yield return Water(item, turn, withSponge ? 1.0f : 1.8f, !withSponge, stream);
            if (withSponge) yield return SpongeWipe(item, 1.0f, stream);
        }
        if (flip)
        {
            for (float k = 0f; k < 0.4f; k += Time.deltaTime)
            {
                item.SetHoldTurn(Quaternion.AngleAxis(180f * (1f - Mathf.SmoothStep(0f, 1f, k / 0.4f)), axis) * baseTurn);
                yield return null;
            }
        }
        item.SetHoldTurn(baseTurn);
        if (!wasOn) t?.SetRunning(false);
        SupportHandController.Instance?.Hide();
        done?.Invoke();
        if (hand != null) hand.IsLocked = false;
        washing = false;
    }

    /// <summary>The water running onto it and off: drops come down on its top,
    /// run along it (the knife: down the blade to the tip) and fall off. A
    /// plain item is turned back and forth under the stream meanwhile.</summary>
    private System.Collections.IEnumerator Water(Carryable item, Quaternion turn, float seconds, bool wiggle, Vector3 stream)
    {
        float nextDrop = 0f;
        for (float k = 0f; k < seconds; k += Time.deltaTime)
        {
            if (wiggle)
            {
                item.SetHoldTurn(turn * Quaternion.AngleAxis(Mathf.Sin(k / seconds * Mathf.PI * 4f) * 25f, Vector3.forward));
                // the other hand rubs it under the water - both hands at it
                Bounds ib = ItemBounds(item);
                float a = k / seconds * Mathf.PI * 2f * 3f;
                // (ShowNear already sets the knuckles just beside the point, fingers on it)
                SupportHandController.Instance?.ShowNear(new Vector3(ib.center.x + Mathf.Cos(a) * 0.04f,
                    ib.center.y + Mathf.Sin(a) * 0.04f, ib.min.z - 0.1f));
            }
            else
            {
                // (the knife: the other hand ready by the blade, the sponge in it)
                Bounds ib = ItemBounds(item);
                SupportHandController.Instance?.ShowNear(new Vector3(ib.center.x + ib.extents.x * 0.3f, ib.max.y + 0.08f, ib.min.z - 0.1f));
            }
            if (k >= nextDrop)
            {
                nextDrop = k + 0.05f;
                StartCoroutine(Drop(item, stream));
            }
            yield return null;
        }
        item.SetHoldTurn(turn);
    }

    private System.Collections.IEnumerator Drop(Carryable item, Vector3 stream)
    {
        if (dropMaterial == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            dropMaterial = new Material(sh != null ? sh : Shader.Find("Standard")) { color = new Color(0.62f, 0.82f, 1f) };
            if (dropMaterial.HasProperty("_BaseColor")) dropMaterial.SetColor("_BaseColor", new Color(0.62f, 0.82f, 1f));
        }
        GameObject d = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(d.GetComponent<Collider>());
        d.GetComponent<Renderer>().sharedMaterial = dropMaterial;
        d.transform.localScale = Vector3.one * Random.Range(0.025f, 0.045f);

        Bounds b = ItemBounds(item);
        float front = b.min.z - 0.03f;
        // (from the tap down onto it - it's held a little off the stream's line, by the drain)
        Vector3 start = new Vector3(Mathf.Lerp(stream.x, b.center.x, 0.85f) + Random.Range(-0.04f, 0.04f), stream.y, front);
        // lands on its top, runs along it toward its lower/far end, then falls off
        Vector3 land = new Vector3(start.x, b.max.y - b.size.y * 0.25f, front);
        Vector3 run = RunDirection(item);
        Vector3 off = land + run * Random.Range(0.25f, 0.6f) * Mathf.Max(b.extents.x, 0.2f);
        Vector3 gone = off + Vector3.down * 0.7f;
        Vector3[] path = { start, land, off, gone };
        float[] times = { 0.12f, 0.25f, 0.3f };
        for (int seg = 0; seg < 3; seg++)
            for (float k = 0f; k < times[seg]; k += Time.deltaTime)
            {
                if (d == null) yield break;
                d.transform.position = Vector3.Lerp(path[seg], path[seg + 1], k / times[seg]);
                yield return null;
            }
        Destroy(d);
    }

    /// <summary>One wipe of the sponge, held by the other hand: along the blade
    /// from near the handle to the tip for a knife, round the middle for a dish.</summary>
    private System.Collections.IEnumerator SpongeWipe(Carryable item, float seconds, Vector3 stream)
    {
        if (sponge == null) sponge = BuildSponge();
        sponge.SetActive(true);
        bool knife = item is ToolItem tk && tk.Kind == ToolKind.Knife;
        BladeEnds(item, out Vector3 handleEnd, out Vector3 tip);
        Bounds b = ItemBounds(item);
        for (float k = 0f; k < seconds; k += Time.deltaTime)
        {
            float f = k / seconds;
            Vector3 at = knife
                ? Vector3.Lerp(Vector3.Lerp(handleEnd, tip, 0.3f), Vector3.Lerp(handleEnd, tip, 0.95f), Mathf.SmoothStep(0f, 1f, f))
                : b.center + new Vector3(Mathf.Cos(f * Mathf.PI * 2f), Mathf.Sin(f * Mathf.PI * 2f) * 0.6f, 0f) * Mathf.Min(b.extents.x, b.extents.y) * 0.5f;
            at.z = b.min.z - 0.08f;
            sponge.transform.position = at;
            SupportHandController.Instance?.ShowNear(at + new Vector3(0f, 0.12f, -0.05f));
            yield return null;
        }
        sponge.SetActive(false);
    }

    private static GameObject BuildSponge()
    {
        var root = new GameObject("WashSponge");
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        void Part(string n, Vector3 local, Vector3 size, Color c)
        {
            GameObject p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = n;
            Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(root.transform, false);
            p.transform.localPosition = local;
            p.transform.localScale = size;
            Renderer r = p.GetComponent<Renderer>();
            if (lit != null) r.material = new Material(lit);
            r.material.color = c;
        }
        Part("Sponge", Vector3.zero, new Vector3(0.24f, 0.12f, 0.08f), new Color(0.97f, 0.86f, 0.3f));
        Part("Scrub", new Vector3(0f, 0.08f, 0f), new Vector3(0.24f, 0.04f, 0.08f), new Color(0.2f, 0.55f, 0.25f));
        root.SetActive(false);
        return root;
    }

    // 손 씻기 원점은 유지하고, 물건만 화면 위쪽으로 올린다.
    private Vector3 ItemWashTarget(Vector3 stream)
    {
        Vector3 origin = WashSpot(stream);
        return new Vector3(BasinCenter().x, origin.y + 0.65f, origin.z);
    }
    /// <summary>Where hands are washed: the hand-wash routine's origin, else just under the tap.</summary>
    private Vector3 WashSpot(Vector3 stream)
    {
        if (handWash != null)
        {
            var so = handWash.GetType().GetField("washOrigin", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (so?.GetValue(handWash) is Transform origin && origin != null) return origin.position;
        }
        return stream + Vector3.down * 0.3f;
    }

    private static Bounds ItemBounds(Carryable item)
    {
        if (item is IngredientItem ing) return ing.VisibleBounds;
        Bounds b = new Bounds(item.transform.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in item.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    /// <summary>The item's long axis (world) - what the knife is turned over about.</summary>
    private static Vector3 LongAxis(Carryable item)
    {
        BladeEnds(item, out Vector3 a, out Vector3 b);
        Vector3 d = b - a;
        return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.right;
    }

    /// <summary>The two ends of its longest side (world): the one nearer the hand
    /// (the knife's handle) first.</summary>
    private static void BladeEnds(Carryable item, out Vector3 handleEnd, out Vector3 tip)
    {
        Renderer r = item.GetComponent<Renderer>();
        if (r == null) r = item.GetComponentInChildren<Renderer>();
        handleEnd = tip = item.transform.position;
        if (r == null) return;
        Bounds lb = r.localBounds;
        Vector3 s = lb.size;
        Vector3 ax = s.x >= s.y && s.x >= s.z ? Vector3.right : s.y >= s.z ? Vector3.up : Vector3.forward;
        float half = Vector3.Dot(s, ax) * 0.5f;
        Vector3 a = r.transform.TransformPoint(lb.center - ax * half);
        Vector3 b = r.transform.TransformPoint(lb.center + ax * half);
        Vector3 hand = HandController.Instance != null ? HandController.Instance.transform.position : a;
        bool aNearer = (a - hand).sqrMagnitude <= (b - hand).sqrMagnitude;
        handleEnd = aNearer ? a : b;
        tip = aNearer ? b : a;
    }

    /// <summary>Which way water runs off it: down the knife's blade toward the
    /// tip; off whichever side of anything else.</summary>
    private static Vector3 RunDirection(Carryable item)
    {
        if (item is ToolItem t && t.Kind == ToolKind.Knife)
        {
            BladeEnds(item, out Vector3 h, out Vector3 tip);
            Vector3 d = tip - h;
            d.z = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.right;
        }
        return Random.value < 0.5f ? Vector3.left : Vector3.right;
    }

    private MovableBowl pourPending;
    private PlatingArrow pourArrow;
    private bool pouring;

    private void ShowPourArrow(MovableBowl vessel)
    {
        pourPending = vessel;
        if (pourArrow == null) pourArrow = PlatingArrow.Create("PourWaterArrow", () => { if (pourPending != null) PourOut(pourPending); });
        Vector3 c = BasinCenter();
        pourArrow.Place(new Vector3(c.x, c.y + 0.9f, -3.2f));
        pourArrow.gameObject.SetActive(true);
    }

    void Update()
    {
        // The arrow only stands while that vessel (still holding water) is in hand.
        if (pourPending == null || pouring) return;
        if (!pourPending.IsHeld || pourPending.WaterAmount <= 0f) HidePour();
    }

    private void HidePour()
    {
        pourPending = null;
        if (pourArrow != null) pourArrow.gameObject.SetActive(false);
    }

    private void PourOut(MovableBowl vessel)
    {
        if (pouring) return;
        if (pourArrow != null) pourArrow.gameObject.SetActive(false);
        StartCoroutine(Pour(vessel));
    }

    /// <summary>Tipped over the sink, the water runs out (whatever else is in it -
    /// blanched peas, tofu - stays), then upright again.</summary>
    private System.Collections.IEnumerator Pour(MovableBowl vessel)
    {
        pouring = true;
        HandCarry hand = HandCarry.Instance;
        if (hand != null) hand.IsLocked = true;
        Vector3 c = BasinCenter();
        HandController.Instance?.GoTo(new Vector3(c.x + 0.5f, c.y + 0.6f, HandController.Instance.transform.position.z), null);
        yield return new WaitForSeconds(0.25f);
        for (float t = 0f; t < 0.3f; t += Time.deltaTime) { vessel.SetPourTilt(Quaternion.AngleAxis(-60f * t / 0.3f, Vector3.forward)); yield return null; }
        float start = vessel.WaterAmount;
        for (float t = 0f; t < 0.6f; t += Time.deltaTime) { vessel.SetWaterLevel(Mathf.Lerp(start, 0f, t / 0.6f)); yield return null; }
        vessel.PourOffWater();
        for (float t = 0f; t < 0.3f; t += Time.deltaTime) { vessel.SetPourTilt(Quaternion.AngleAxis(-60f * (1f - t / 0.3f), Vector3.forward)); yield return null; }
        vessel.SetPourTilt(Quaternion.identity);
        if (hand != null) hand.IsLocked = false;
        pouring = false;
        HidePour();
    }

    /// <summary>A bowl/pot set in the sink sits with its body in the middle of
    /// the basin, under the faucet (the sink's own pivot is at the basin's far
    /// bottom edge, too far from the water). The basin is the 3D station's
    /// Sink_* parts when there are any, else this sink's own collider.</summary>
    private void CenterInBasin(MovableBowl bowl)
    {
        Vector3 c = BasinCenter();
        Vector3 body = bowl.BodyCenterWorld();
        bowl.transform.position += new Vector3(c.x - body.x, c.y - body.y, 0f);
    }

    private Vector3? basinCenter;

    private Vector3 BasinCenter()
    {
        if (basinCenter.HasValue) return basinCenter.Value;
        // Seen from the tilted front view, the dark middle of the basin right
        // under the faucet is its back wall - the bottom shows lower, by the
        // front edge, too far from the water.
        GameObject back = GameObject.Find("Sink_WallB");
        Renderer r = back != null ? back.GetComponent<Renderer>() : null;
        Collider own = GetComponent<Collider>();
        basinCenter = r != null ? r.bounds.center : own != null ? own.bounds.center : transform.position;
        return basinCenter.Value;
    }

    public override bool CanAcceptDrop(Carryable item)
    {
        if (item is IngredientItem ing)
        {
            CookableItem cooked = ing.GetComponent<CookableItem>();
            if (cooked != null && cooked.CurrentStage == CookableItem.Stage.Done) return true;
            return ing.CurrentState == IngredientItem.State.Raw;
        }

        if (item is Strainer strainer) return strainer.HasContents;
        if (item is Cheesecloth) return true;
        if (item is ToolItem tool) return tool.Kind == ToolKind.Knife || tool.IsTowelKind;
        if (item is PotLid) return true;

        return item is MovableBowl;
    }
}
