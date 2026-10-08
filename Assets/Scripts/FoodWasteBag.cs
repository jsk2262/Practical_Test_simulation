using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 음식물쓰레기 봉투 (FoodWasteBag) at a corner of the sink, mouth open. With food
/// in hand - an ingredient, a scrap, a bowl/pot/pan with food in it, the knife
/// with pieces or scraps on its blade, the strainer with pieces in it - pointing
/// at the bag lights it up with "음식물 버리기"; a click (or a drag onto it) throws
/// the food in. Containers and tools stay in hand, only their contents go (their
/// washed/dirty state is left alone). Never automatic: only on a click.
/// Built by Tools > Kitchen > Setup Food Waste Bag (a stand-in model under
/// "Model" - replace that child to swap the bag's look).
/// </summary>
public class FoodWasteBag : Interactable
{
    [SerializeField] private Transform mouth;
    [SerializeField] private TextMesh hint;
    [SerializeField] private Renderer rimRenderer;
    [SerializeField] private float dropSeconds = 0.35f;

    /// <summary>Pieces on their way into the bag - a second click can't throw them twice.</summary>
    private readonly HashSet<Carryable> falling = new HashSet<Carryable>();
    private Color rimColor;
    private bool lit;

    private Vector3 MouthPoint => mouth != null ? mouth.position : transform.position;

    void Awake()
    {
        if (rimRenderer != null) rimColor = rimRenderer.material.color;
        if (hint != null) hint.gameObject.SetActive(false);
        FitToModel();
    }

    /// <summary>The click box, the mouth and the hint put where the bag's model
    /// actually is - its parts can be moved (or the model swapped) in the scene,
    /// and the click box stayed behind at the old spot, so the open bag itself
    /// couldn't be clicked.</summary>
    private void FitToModel()
    {
        Transform model = transform.Find("Model");
        if (model == null) return;
        // Each part's own (mesh-local) box corners, taken into the bag root's
        // local space: the click box then hugs the tilted cylinder itself.
        bool any = false;
        Bounds local = default, b = default;
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
        {
            Bounds lb = r.localBounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = transform.InverseTransformPoint(r.transform.TransformPoint(c));
                if (!any) { local = new Bounds(p, Vector3.zero); any = true; } else local.Encapsulate(p);
            }
            if (b.size == Vector3.zero) b = r.bounds; else b.Encapsulate(r.bounds);
        }
        if (!any) return;

        if (GetComponent<BoxCollider>() is BoxCollider box)
        {
            box.center = local.center;
            box.size = local.size;
        }
        // the mouth: the opening's middle (else the top of it all)
        Transform opening = FindDeep(model, "Opening");
        Vector3 mouthAt = opening != null && opening.GetComponent<Renderer>() is Renderer or ? or.bounds.center : new Vector3(b.center.x, b.max.y, b.center.z);
        if (mouth != null) mouth.position = mouthAt;
        if (hint != null) hint.transform.position = new Vector3(b.center.x, b.max.y + 0.35f, b.min.z - 0.4f);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    // Only in clean-up (정리): food isn't thrown out in the middle of cooking.
    public override bool CanAcceptDrop(Carryable item) => CleanupMode.IsOn && (WasteRules.IsFood(item) || WasteRules.HasFoodToDump(item));

    void Update()
    {
        // Lit up (with the hint) only while pointing at it with food in hand.
        bool show = false;
        Camera cam = Camera.main;
        HandCarry hand = HandCarry.Instance;
        if (cam != null && hand != null && Mouse.current != null && CleanupMode.IsOn && WasteRules.HasFoodToDump(hand.Current))
        {
            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            foreach (RaycastHit h in Physics.RaycastAll(ray, 200f))
                if (h.collider.GetComponentInParent<FoodWasteBag>() == this) { show = true; break; }
        }
        if (show == lit) return;
        lit = show;
        if (hint != null) hint.gameObject.SetActive(show);
        if (rimRenderer != null) rimRenderer.material.color = show ? Color.Lerp(rimColor, new Color(1f, 0.85f, 0.3f), 0.7f) : rimColor;
    }

    protected override void Activate()
    {
        HandCarry hand = HandCarry.Instance;
        Carryable held = hand?.Current;
        if (held == null || !WasteRules.HasFoodToDump(held)) return; // empty hand, empty container, bare tool: nothing
        if (!CleanupMode.IsOn) { CleanupMode.NotifyBlocked(); return; } // (the 정리 tab first)

        var food = new List<Carryable>();
        if (WasteRules.IsFood(held))
        {
            hand.Detach();
            food.Add(held);
        }
        else if (held is MovableBowl bowl)
        {
            // Emptied into the bag; the bowl/pot/pan itself stays in hand as it was.
            foreach (Carryable c in new List<Carryable>(bowl.Contents))
                if (WasteRules.IsFood(c)) { bowl.Release(c); c.CurrentContainer = null; food.Add(c); }
            if (bowl.GetComponent<PortionedCondiment>() is PortionedCondiment pc && !pc.Removed)
            {
                WasteLog.Add(pc.CondimentName, pc.CurrentStage == PortionedCondiment.Stage.Drained ? PortionedCondiment.SettledStarch : "덜어둔 것");
                pc.Remove();
            }
        }
        else if (held is Strainer strainer) food.AddRange(strainer.TakeAllForDisposal());
        else if (held is Cheesecloth cloth) cloth.EmptyResidue(); // (the cloth stays in hand, used)
        else if (held is ToolItem knife) food.AddRange(knife.TakeLoadForDisposal());

        HandController.Instance?.PlayChopGesture();
        foreach (Carryable c in food) Throw(c);
    }

    /// <summary>Into the bag: let go of by everything that still refers to it,
    /// a short drop into the mouth, then gone.</summary>
    public void Throw(Carryable food)
    {
        if (food == null || !WasteRules.IsFood(food) || falling.Contains(food)) return;
        falling.Add(food);

        if (food.CurrentContainer != null) { food.CurrentContainer.Release(food); food.CurrentContainer = null; }
        if (food.GetComponent<CookableItem>() is CookableItem cook)
        {
            cook.SetBurner(null);
            cook.SetWok(null);
            cook.SetVessel(null);
            cook.enabled = false;
        }
        // Only the food goes: anything else riding on it (a towel wrapped round
        // it, a tool) is put down where it is, never thrown away with it.
        foreach (Transform t in food.GetComponentsInChildren<Transform>(true))
        {
            if (t == food.transform) continue;
            if ((t.GetComponent<Carryable>() is Carryable other && !WasteRules.IsFood(other)) || t.GetComponent<TowelSpread>() != null)
                t.SetParent(null, true);
        }
        foreach (Collider c in food.GetComponentsInChildren<Collider>()) c.enabled = false;

        string name = food is IngredientItem ing ? ing.IngredientName : ((FoodScrap)food).SourceName;
        string kind = food is FoodScrap scrap ? scrap.Kind
                    : ((IngredientItem)food).CurrentState == IngredientItem.State.Cut ? "손질한 재료"
                    : food.GetComponent<CookableItem>() is CookableItem ck && ck.CurrentStage != CookableItem.Stage.Raw ? "조리된 음식" : "식재료";
        float grams = food is FoodScrap sg ? sg.Grams : 0f;
        if (food.GetComponent<FoodPieceGroup>() is FoodPieceGroup pg) foreach (FoodPiece p in pg.Pieces) grams += p.Grams;
        WasteLog.Add(name, kind, grams);
        StartCoroutine(Drop(food));
    }

    private IEnumerator Drop(Carryable food)
    {
        food.transform.SetParent(null, true);
        Vector3 from = food.transform.position;
        Vector3 scale = food.transform.localScale;
        Vector3 into = MouthPoint + new Vector3(Random.Range(-0.05f, 0.05f), -0.15f, 0.25f);
        for (float t = 0f; t < dropSeconds; t += Time.deltaTime)
        {
            float k = t / dropSeconds;
            if (food == null) yield break;
            food.transform.position = Vector3.Lerp(from, MouthPoint, Mathf.Min(1f, k * 1.6f)) + (into - MouthPoint) * Mathf.Max(0f, k * 1.6f - 0.6f);
            food.transform.localScale = scale * Mathf.Lerp(1f, 0.4f, k);
            yield return null;
        }
        falling.Remove(food);
        if (food != null) Destroy(food.gameObject);
    }
}
