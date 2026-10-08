using UnityEngine;

/// <summary>
/// The tissue box the kitchen towel is pulled from: clicking it empty-handed
/// puts one fresh sheet in the hand (a copy of sheetTemplate - the old towel
/// roll, kept hidden as the template), and the box stays where it is.
/// Set up with Tools > Kitchen > Setup Kitchen Towel (TissueBox).
/// </summary>
public class KitchenTowelDispenser : Interactable
{
    [SerializeField] private ToolItem sheetTemplate;
    /// <summary>Sheet size relative to the template.</summary>
    [SerializeField] private float sheetScale = 0.8f;

    private int count;

    protected override void Activate()
    {
        HandCarry hand = HandCarry.Instance;
        if (hand == null || hand.Current != null || sheetTemplate == null) return;

        // Sized and placed before it wakes up, so its Awake records this as its
        // own size and resting depth.
        bool wasActive = sheetTemplate.gameObject.activeSelf;
        sheetTemplate.gameObject.SetActive(false);
        ToolItem sheet = Instantiate(sheetTemplate, transform.position, sheetTemplate.transform.rotation);
        sheetTemplate.gameObject.SetActive(wasActive);

        sheet.name = $"KitchenTowel_Sheet_{++count}";
        sheet.transform.SetParent(null, true);
        sheet.transform.localScale = sheetTemplate.transform.lossyScale * sheetScale;
        sheet.gameObject.SetActive(true);
        foreach (Renderer r in sheet.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        foreach (Collider c in sheet.GetComponentsInChildren<Collider>(true)) c.enabled = true;

        // Straight into the hand - pulling a sheet never wraps whatever is on the
        // board (that's what a sheet already in hand does when used on it).
        hand.PickUp(sheet);
    }

    public override bool CanAcceptDrop(Carryable item) => false;
}
