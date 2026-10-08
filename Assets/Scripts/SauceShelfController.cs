using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A shared shelf that accepts any seasoning bottle dropped onto it, letting it
/// rest wherever it was actually placed along the shelf's width - unlike the old
/// setup (one dedicated PlaceSlot "home" per bottle name), any bottle can land
/// anywhere on this one shelf. At scene start it also gathers up whatever
/// SeasoningBottles already exist in the scene and spreads them across itself,
/// so bottles don't need to be hand-positioned one at a time while their
/// placeholder art gets swapped for real assets later.
/// </summary>
public class SauceShelfController : Interactable, ISlotContainer
{
    [SerializeField] private float halfWidth = 3.4f;

    // The old placeholder bottles were center-pivoted, so this needed to be
    // well above the shelf's own transform to clear its surface. The real
    // jar/bottle FBX models are base-pivoted instead (their origin sits right
    // at their own bottom), so this now only needs to reach the shelf's top
    // surface (half its own thickness above its center) plus a hair of
    // clearance - anything more just floats every bottle above the shelf.
    [SerializeField] private float shelfLocalY = 0.11f;
    [SerializeField] private float shelfLocalZ = -0.3f;

    private Transform contentRoot;
    private const string ContentRootName = "SauceShelf_ContentRoot";

    private Transform ContentRoot
    {
        get
        {
            if (contentRoot == null)
            {
                // A sibling of the shelf, not a child: the shelf mesh's own
                // non-uniform scale (8, 0.2, 1) can't be cancelled by a child's
                // inverse scale once the shelf is tilted - the axes no longer
                // line up, and every bottle came out stretched toward the camera
                // (~2.5x deep) as soon as play started. Unparented from the
                // shelf, the bottles just keep their own size.
                // Runtime-only field, resets to null on every reload even though the
                // object itself is still there - reuse it instead of creating a duplicate.
                Transform parent = transform.parent;
                Transform existing = parent != null ? parent.Find(ContentRootName) : GameObject.Find(ContentRootName)?.transform;
                if (existing != null)
                {
                    contentRoot = existing;
                }
                else
                {
                    var go = new GameObject(ContentRootName);
                    contentRoot = go.transform;
                    contentRoot.SetParent(parent, false);

                    // World-space offset, the same distance above/in front of the
                    // shelf as before.
                    contentRoot.position = transform.position + new Vector3(0f, shelfLocalY, shelfLocalZ);
                    contentRoot.rotation = Quaternion.identity;
                    Vector3 ps = parent != null ? parent.lossyScale : Vector3.one;
                    contentRoot.localScale = new Vector3(1f / ps.x, 1f / ps.y, 1f / ps.z);
                }
            }

            return contentRoot;
        }
    }

    void Start()
    {
        HideBottlesNotOnExamSheet();
        ScatterExistingBottles();
    }

    /// <summary>Only seasonings actually listed as supplied material (지급재료)
    /// on today's exam sheet(s) belong on the shelf at all - using anything
    /// else is grounds for disqualification in the real exam, so a bottle that
    /// isn't on the sheet shouldn't even be available to reach for. Reads
    /// every ExamPaperFlip in the scene (one per dish) rather than hardcoding
    /// a list, so a different day's menu automatically shows a different set
    /// of bottles without this script needing to change.</summary>
    private void HideBottlesNotOnExamSheet()
    {
        var required = new HashSet<string>();
        foreach (ExamPaperFlip page in FindObjectsByType<ExamPaperFlip>(FindObjectsSortMode.None))
            foreach (string seasoning in page.RequiredSeasonings)
                required.Add(seasoning);

        // No exam page data configured yet - safer to show every bottle than
        // to silently hide the entire shelf.
        if (required.Count == 0) return;

        foreach (SeasoningBottle bottle in FindObjectsByType<SeasoningBottle>(FindObjectsSortMode.None))
        {
            if (!required.Contains(bottle.SeasoningName))
                bottle.gameObject.SetActive(false);
        }
    }

    /// <summary>One-time "activate onto the shelf" pass - spreads every
    /// SeasoningBottle already in the scene evenly across this shelf instead of
    /// relying on each one's hand-authored starting position.</summary>
    private void ScatterExistingBottles()
    {
        SeasoningBottle[] bottles = FindObjectsByType<SeasoningBottle>(FindObjectsSortMode.None);
        // 종지 dishes (CondimentDish) stay on the counter - only real bottles go up here.
        bottles = System.Array.FindAll(bottles, b => !(b is CondimentDish));
        if (bottles.Length == 0) return;

        Transform root = ContentRoot;
        float step = bottles.Length <= 1 ? 0f : (halfWidth * 2f) / (bottles.Length - 1);

        for (int i = 0; i < bottles.Length; i++)
        {
            SeasoningBottle bottle = bottles[i];
            if (bottle.IsHeld) continue;

            bottle.CurrentContainer?.Release(bottle);

            float x = bottles.Length <= 1 ? 0f : -halfWidth + step * i;
            bottle.transform.SetParent(root, false);
            bottle.transform.localPosition = new Vector3(x, 0f, 0f);
            bottle.transform.rotation = bottle.BaseRotation;
            bottle.transform.localScale = bottle.BaseLocalScale;
            bottle.CurrentContainer = this;
        }
    }

    public override bool CanAcceptDrop(Carryable item) => item is SeasoningBottle && !(item is CondimentDish);

    protected override void Activate()
    {
        HandCarry hand = HandCarry.Instance;
        if (hand == null || !(hand.Current is SeasoningBottle bottle) || bottle is CondimentDish) return;

        Transform root = ContentRoot;

        // Lands right where the cursor is, click or drag alike (나이나이 레시피
        // style) - a plain click used to drop every bottle at the shelf's
        // center, stacking them all on top of each other there.
        Vector3 desired = root.InverseTransformPoint(CursorWorldPoint(root.position.z, bottle.transform.position));

        Vector3 clamped = new Vector3(Mathf.Clamp(desired.x, -halfWidth, halfWidth), 0f, 0f);

        // PlaceAtFreeLocalPosition only handles the transform/parenting side -
        // unlike PlaceAt(PlaceSlot), it never touches CurrentContainer itself.
        if (hand.PlaceAtFreeLocalPosition(root, clamped))
        {
            bottle.CurrentContainer = this;
        }
    }

    private static Vector3 CursorWorldPoint(float worldZ, Vector3 fallback)
    {
        Camera cam = Camera.main;
        if (cam == null || Mouse.current == null) return fallback;

        Vector2 screen = Mouse.current.position.ReadValue();
        return cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, worldZ - cam.transform.position.z));
    }

    public void Release(Carryable item)
    {
        // No single-occupant slot to clear - each bottle just tracks itself,
        // but it does remember the spot it was taken from, so it can be sent
        // back there after scooping (see SeasoningBottle).
        if (item is SeasoningBottle bottle) bottle.SetShelfHome(this, bottle.transform.localPosition);
    }

    /// <summary>Puts the held bottle back at the given spot on this shelf -
    /// the end of a scoop, when the hand trades the bottle for the spoon.</summary>
    public bool ReturnBottle(SeasoningBottle bottle, Vector3 localPosition)
    {
        HandCarry hand = HandCarry.Instance;
        if (hand == null || hand.Current != bottle) return false;

        Vector3 clamped = new Vector3(Mathf.Clamp(localPosition.x, -halfWidth, halfWidth), 0f, 0f);
        if (!hand.PlaceAtFreeLocalPosition(ContentRoot, clamped)) return false;

        bottle.CurrentContainer = this;
        return true;
    }

    /// <summary>World position of a spot on this shelf (for animating toward it).</summary>
    public Vector3 ShelfWorldPoint(Vector3 localPosition) => ContentRoot.TransformPoint(localPosition);
}
