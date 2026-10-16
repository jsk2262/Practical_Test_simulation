using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 버무리기 by hand - both hands in the bowl working the meat (its 밑간, or its
/// 앙금녹말·흰자 튀김옷). Started by a plain click (empty hand) on a bowl of
/// meat with something on it to work in: the hands come to either side of
/// the bowl and wait. Holding the left button down kneads, over and over -
/// reach in, gather toward the middle, close the fingers round it, lift and
/// turn it, let go - the left hand a little behind the right. Each squeeze
/// works the marinade/batter in once (BatterCoating.KneadStroke, the same
/// mixing as stirring - so 6 or so is enough), whether or not the mouse
/// moves. Let go, the hands finish what they were doing and wait. A click
/// off the bowl, the right button or Esc and they come out of the bowl.
/// Raw meat (and the batter) stays on the hands until they're washed
/// (HandHygiene) - only recorded, for the final evaluation.
/// While kneading the mouse is all its own (InputCaptured).
/// </summary>
public class MeatKneader : MonoBehaviour
{
    private static MeatKneader active;
    public static bool InputCaptured => active != null;
    public static MovableBowl ActiveBowl => active != null ? active.bowl : null;
    public static bool IsKneading(MovableBowl bowl) => active != null && active.bowl == bowl;

    [Tooltip("Seconds for one squeeze (reach in - gather - squeeze - lift - let go).")]
    [SerializeField] private float cycleSeconds = 1.0f;
    [Tooltip("The left hand's squeeze comes this much (of a cycle) after the right's.")]
    [SerializeField] private float leftLag = 0.08f;
    [Tooltip("Squeezes it takes to work new seasoning in (밑간 - the batter's own recipe has its own).")]
    [SerializeField] private int marinadeStrokes = 5;
    [Tooltip("How hard the hands move the meat about (higher: more tossing and turning).")]
    [SerializeField] private float kneadStrength = 5f;

    private MovableBowl bowl;
    private float phase;           // right hand's, 0..1 (0 = waiting, open, at the side)
    private bool cycling;
    private bool endOnRelease;     // ended by a press - its release isn't a click

    /// <summary>Starts kneading the meat in this bowl (a plain click on it,
    /// hand empty). False if it can't be (something in hand, already on).</summary>
    public static bool Begin(MovableBowl bowl)
    {
        if (bowl == null || active != null || bowl.IsCookingVessel || bowl.IsHeld) return false;
        if (HandCarry.Instance == null || HandCarry.Instance.Current != null) return false;
        if (!HasMeatToKnead(bowl)) return false;
        var go = new GameObject("MeatKneader");
        active = go.AddComponent<MeatKneader>();
        active.bowl = bowl;
        Debug.Log($"[버무리기] {bowl.name}: 양손 버무리기 시작 (누르고 있는 동안 주무름)");
        return true;
    }

    /// <summary>Meat in it that kneading would do something for: 튀김옷 on it,
    /// or seasoning not worked in yet.</summary>
    public static bool HasMeatToKnead(MovableBowl bowl)
    {
        // (그릇마다 횟수 제한 - 밑간 5회, 튀김옷 10회: 다 버무린 뒤엔 버무리지 않고
        // 그릇을 누르면 집을 것 선택창 - BowlMixInteraction)
        BowlMixInteraction mix = BowlMixInteraction.For(bowl);
        return mix != null && mix.CanMix();
    }

    private static System.Collections.Generic.List<IngredientItem> MeatIn(MovableBowl bowl)
    {
        var list = new System.Collections.Generic.List<IngredientItem>();
        if (bowl == null) return list;
        foreach (IngredientItem m in bowl.GetComponentsInChildren<IngredientItem>())
            if (m.Container == bowl && m.Category == IngredientCategory.Meat) list.Add(m);
        return list;
    }

    void LateUpdate()
    {
        Mouse mouse = Mouse.current;
        if (endOnRelease)
        {
            if (mouse == null || !mouse.leftButton.isPressed) Finish();
            return;
        }
        if (bowl == null || bowl.IsHeld || mouse == null || ConfirmDialog.IsOpen || ExamResultView.IsOpen
            || (HandCarry.Instance != null && HandCarry.Instance.Current != null)
            || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            || mouse.rightButton.wasPressedThisFrame)
        {
            Stop();
            return;
        }
        if (mouse.leftButton.wasPressedThisFrame && !OverBowl(mouse.position.ReadValue()))
        {
            Stop();
            return;
        }

        bool held = mouse.leftButton.isPressed && !OverUi();
        if (held) cycling = true;
        if (cycling)
        {
            phase += Time.deltaTime / Mathf.Max(cycleSeconds, 0.1f);
            if (phase >= 1f)
            {
                // 한 번의 주무르기가 끝까지 끝났을 때 1회 (중간에 멈추면 세지 않는다)
                phase -= 1f;
                Squeeze();
                if (BowlMixInteraction.For(bowl).CountCompletedMix())
                {
                    // 이 단계 마지막 횟수: 더 버무리지 않고 손을 빼고, 집을 것 선택창
                    MovableBowl done = bowl;
                    Finish();
                    BowlMixInteraction.For(done).OpenChooser();
                    return;
                }
                if (!held) { phase = 0f; cycling = false; } // (let go: this one finished, then wait)
            }
            // 다 버무려졌고 손을 뗐으면: 양손이 그릇에서 저절로 나온다 (그다음 꾹 누르면 한 조각 집기)
            if (!cycling && !held && !HasMeatToKnead(bowl))
            {
                Debug.Log($"[버무리기] {bowl.name}: 다 버무려짐 - 손을 뺍니다 (꾹 누르면 한 조각씩 집기)");
                Finish();
                return;
            }
        }
        Pose();
    }

    private static bool OverUi() =>
        UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

    private bool OverBowl(Vector2 screen)
    {
        Camera cam = Camera.main;
        if (cam == null) return true;
        MovableBowl.Interior inside = bowl.GetInterior();
        Vector3 c = inside.floorCenter + inside.up * inside.rim * 0.5f;
        Vector2 sc = cam.WorldToScreenPoint(c);
        Vector3 across = Vector3.ProjectOnPlane(cam.transform.right, inside.up).normalized;
        float r = Vector2.Distance(sc, cam.WorldToScreenPoint(c + across * inside.radius));
        return Vector2.Distance(screen, sc) <= Mathf.Max(r * 1.3f, 40f);
    }

    // ---- the hands ----

    [Header("Hand path (fractions of the bowl's radius / depth)")]
    [SerializeField] private float outSide = 0.75f, inSide = 0.42f, gatherSide = 0.22f, liftSide = 0.28f;
    [SerializeField] private float aboveRim = 1.0f, reachDepth = 1.45f, squeezeDepth = 1.35f, liftDepth = 1.7f;
    [SerializeField] private float turnDegrees = 28f;
    [Tooltip("Which way the fingers point: in toward the middle, away from the cook (the arms come in from the front), down into the bowl.")]
    [SerializeField] private float fingersIn = 0.35f, fingersAway = 1f, fingersDown = 0.25f;
    [Tooltip("Knuckles this much (of the radius) toward the cook - the fingers reach on past them.")]
    [SerializeField] private float knucklesForward = 0.12f;
    [SerializeField] private bool flipThumbs = false;

    private struct HandPose { public float side, depth, wait, curl, turn; public bool contact, lifting; }

    /// <summary>Where a hand is in its squeeze (0..1).</summary>
    private HandPose At(float p)
    {
        HandPose h;
        h.turn = 0f;
        h.contact = false;
        h.lifting = false;
        h.wait = 0f;
        if (!cycling && p <= 0f) { h.side = outSide; h.depth = reachDepth; h.wait = 1f; h.curl = 0.12f; return h; }
        p = Mathf.Repeat(p, 1f);
        if (p < 0.2f)       // reach in
        {
            float k = Ease(p / 0.2f);
            h.side = Mathf.Lerp(outSide, inSide, k); h.depth = reachDepth; h.wait = 1f - k; h.curl = Mathf.Lerp(0.12f, 0.2f, k);
        }
        else if (p < 0.4f)  // gather toward the middle
        {
            float k = Ease((p - 0.2f) / 0.2f);
            h.side = Mathf.Lerp(inSide, gatherSide, k); h.depth = Mathf.Lerp(reachDepth, squeezeDepth, k); h.curl = Mathf.Lerp(0.2f, 0.4f, k);
            h.contact = true;
        }
        else if (p < 0.6f)  // fingers close round it
        {
            float k = Ease((p - 0.4f) / 0.2f);
            h.side = gatherSide; h.depth = squeezeDepth; h.curl = Mathf.Lerp(0.4f, 0.9f, k);
            h.contact = true;
        }
        else if (p < 0.8f)  // lifted and turned over
        {
            float k = Ease((p - 0.6f) / 0.2f);
            h.side = Mathf.Lerp(gatherSide, liftSide, k); h.depth = Mathf.Lerp(squeezeDepth, liftDepth, k); h.curl = Mathf.Lerp(0.9f, 0.6f, k);
            h.turn = Mathf.Sin(k * Mathf.PI) * turnDegrees;
            h.lifting = true;
        }
        else                // let go, back out to the side
        {
            float k = Ease((p - 0.8f) / 0.2f);
            h.side = Mathf.Lerp(liftSide, outSide, k); h.depth = liftDepth; h.wait = k; h.curl = Mathf.Lerp(0.6f, 0.12f, k);
        }
        return h;
    }

    private static float Ease(float t) => t * t * (3f - 2f * t);

    private void Pose()
    {
        Camera cam = Camera.main;
        MovableBowl.Interior inside = bowl.GetInterior();
        Vector3 up = inside.up;
        Vector3 right = Vector3.ProjectOnPlane(cam != null ? cam.transform.right : Vector3.right, up).normalized;
        Vector3 away = Vector3.ProjectOnPlane(cam != null ? cam.transform.up : Vector3.up, up).normalized;
        // (the fingers toward the top of the screen - the arms come in from below it,
        // the hand model's open wrist turned away from view)
        Vector3 screenUp = cam != null ? cam.transform.up : Vector3.up;

        // (measured while the hands are out of it - measured while they lift pieces,
        // the heap "grew" with them and the hands rose after it, the meat riding on top)
        if (heap <= 0f || !cycling || phase < 0.2f) heap = HeapHeight(inside);
        HandPose r = At(phase);
        HandPose l = At(cycling ? phase - leftLag : 0f);
        Vector3 rPos = Place(inside, right, +1f, r) - away * (knucklesForward * inside.radius);
        Vector3 lPos = Place(inside, right, -1f, l) - away * (knucklesForward * inside.radius);

        HandController hand = HandController.Instance;
        if (hand != null) { Vector3 f = Fingers(+1f, right, screenUp, up, -r.turn); hand.SetKneadPose(rPos, f, Thumb(+1f, f, up), r.curl); }
        SupportHandController support = SupportHandController.Instance;
        if (support != null)
        {
            Vector3 f = Fingers(-1f, right, screenUp, up, l.turn);
            support.BeginHolding(f, Thumb(-1f, f, up), l.curl);
            support.HoldKnucklesAt(lPos);
        }

        // the meat near each hand, while it's in it
        // (squeezed: drawn in and pushed up over each other; lifted: carried up
        // with the hand and turned over, falling back as it lets go)
        if (r.contact || l.contact || r.lifting || l.lifting)
        {
            Vector3 middle = inside.floorCenter + up * Mathf.Min(inside.rim * 0.2f, 0.05f);
            float reach = inside.radius * 0.6f;
            float strength = Time.deltaTime * kneadStrength;
            foreach (IngredientItem m in MeatIn(bowl))
            {
                if (!(m.GetComponent<FoodPieceGroup>() is FoodPieceGroup g)) continue;
                if (r.contact) g.KneadAt(rPos, middle, reach, strength);
                if (l.contact) g.KneadAt(lPos, middle, reach, strength);
                if (r.lifting) g.KneadAt(rPos, Floor(inside, rPos), reach * 0.7f, strength, 1f);
                if (l.lifting) g.KneadAt(lPos, Floor(inside, lPos), reach * 0.7f, strength, 1f);
            }
            // (raw meat - and the batter, once it's on - onto the hands)
            bool batter = false;
            foreach (IngredientItem m in MeatIn(bowl))
                if (BatterCoating.Of(m, false) is BatterCoating b && b.HasAny) batter = true;
            HandHygiene.TouchedRawMeat(batter);
        }
    }

    /// <summary>Where the hand is: across by side (of the radius), up by depth -
    /// of the meat's heap, not the bowl (the hands work on top of and into the
    /// meat, never sunk under it out of sight); the waiting spot clears the rim.</summary>
    private Vector3 Place(MovableBowl.Interior inside, Vector3 right, float sign, HandPose h)
    {
        float height = Mathf.Lerp(heap * h.depth, Mathf.Max(inside.rim, heap) * aboveRim + inside.rim * 0.15f, h.wait);
        return inside.floorCenter + right * (sign * h.side * inside.radius) + inside.up * height;
    }

    private float heap;

    /// <summary>How high the meat is heaped in the bowl (world, from its floor).</summary>
    private float HeapHeight(MovableBowl.Interior inside)
    {
        // (the top of most of it - not the one piece sitting highest)
        var tops = new System.Collections.Generic.List<float>();
        foreach (IngredientItem m in MeatIn(bowl))
            if (m.GetComponent<FoodPieceGroup>() is FoodPieceGroup g)
                foreach (FoodPiece p in g.Pieces)
                    if (p != null) tops.Add(Vector3.Dot(p.transform.position - inside.floorCenter, inside.up) + p.Radius * 0.5f);
        if (tops.Count == 0) return inside.rim * 0.3f;
        tops.Sort();
        float top = tops[Mathf.Clamp(Mathf.FloorToInt(tops.Count * 0.75f), 0, tops.Count - 1)];
        return Mathf.Clamp(top, inside.rim * 0.3f, inside.rim * 1.2f);
    }

    /// <summary>The point on the bowl's floor under a hand.</summary>
    private static Vector3 Floor(MovableBowl.Interior inside, Vector3 p) =>
        p - inside.up * Vector3.Dot(p - inside.floorCenter, inside.up);

    /// <summary>Fingers (sign: +1 the right hand, -1 the left) pointing in toward
    /// the middle, away from the cook and down into the bowl, turned about the
    /// bowl's up by turn degrees.</summary>
    private Vector3 Fingers(float sign, Vector3 right, Vector3 away, Vector3 up, float turn) =>
        Quaternion.AngleAxis(turn, up) * (-sign * right * fingersIn + away * fingersAway - up * fingersDown).normalized;

    /// <summary>Palm down, back of the hand up: the thumb side is the fingers turned a
    /// quarter round - toward the middle for either hand.</summary>
    private Vector3 Thumb(float sign, Vector3 fingers, Vector3 up)
    {
        Vector3 t = Quaternion.AngleAxis(sign > 0f ? -90f : 90f, up) * Vector3.ProjectOnPlane(fingers, up).normalized;
        return flipThumbs ? -t : t;
    }

    // ---- the squeeze ----

    private void Squeeze()
    {
        foreach (IngredientItem m in MeatIn(bowl))
        {
            if (BatterCoating.Of(m, false) is BatterCoating b && b.HasAny) b.KneadStroke();
            if (m.HasMarinade && m.MarinadeMix < 1f)
            {
                m.KneadMarinade(marinadeStrokes);
                Debug.Log($"[버무리기] {m.IngredientName} 밑간 주무름 - 고루 밴 정도 {m.MarinadeMix:0.00}");
            }
        }
    }

    // ---- out ----

    private void Stop()
    {
        if (Mouse.current != null && Mouse.current.leftButton.isPressed) { endOnRelease = true; Restore(); return; }
        Finish();
    }

    private void Restore()
    {
        HandController.Instance?.EndKneadPose();
        SupportHandController.Instance?.Hide();
    }

    private void Finish()
    {
        Restore();
        if (active == this) active = null;
        Debug.Log("[버무리기] 손을 뺌");
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (active == this) { active = null; Restore(); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay() { active = null; }
}
