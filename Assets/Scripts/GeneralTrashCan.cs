using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 일반쓰레기통 (TrashCan_01e): general waste - used paper towel sheets and the
/// like (GeneralTrashCan.IsGeneralWaste) - goes in here, in clean-up (정리) only.
/// Holding one and pointing at the can lights the hint "일반쓰레기 버리기"; a click
/// (or a drag onto it) drops it in. Food goes in the FoodWasteBag instead.
/// Gives the can its own click box if it has none.
/// </summary>
public class GeneralTrashCan : Interactable
{
    [SerializeField] private float dropSeconds = 0.35f;

    private TextMesh hint;
    private bool lit;
    private bool busy;

    /// <summary>What counts as general waste: a paper towel (키친타올) sheet that
    /// isn't wrapped round something.</summary>
    public static bool IsGeneralWaste(Carryable c) =>
        (c is ToolItem t && t.Kind == ToolKind.PaperTowel && !t.IsWrapping) || IsEggShell(c);

    /// <summary>The empty shell of an egg broken into a bowl (EggCracker) - 달걀 껍데기는 일반쓰레기.</summary>
    public static bool IsEggShell(Carryable c) => c != null && c.GetComponent<EggCracker>() is EggCracker e && e.IsCracked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void SetUpScene()
    {
        // (renamed in the scene - "TrashCan_01e" became "TrashCan" - so any
        // object named TrashCan..., or showing the TrashCan_01e model, will do)
        GameObject can = null;
        foreach (MeshFilter mf in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            if (mf.name.StartsWith("TrashCan") || (mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("TrashCan")))
            {
                can = mf.gameObject;
                break;
            }
        if (can == null || can.GetComponent<GeneralTrashCan>() != null) return;
        // (an Interactable needs its collider first: the model came without one)
        if (can.GetComponent<Collider>() == null)
        {
            BoxCollider box = can.AddComponent<BoxCollider>();
            if (can.GetComponent<Renderer>() is Renderer r)
            {
                box.center = r.localBounds.center;
                box.size = r.localBounds.size;
            }
        }
        can.AddComponent<GeneralTrashCan>();
    }

    void Awake()
    {
        var go = new GameObject("TrashHint");
        go.transform.SetParent(transform, true);
        Renderer rend = GetComponent<Renderer>();
        Vector3 top = rend != null ? new Vector3(rend.bounds.center.x, rend.bounds.max.y + 0.3f, rend.bounds.min.z - 0.5f) : transform.position;
        go.transform.SetPositionAndRotation(top, Quaternion.identity);
        hint = go.AddComponent<TextMesh>();
        hint.text = "일반쓰레기 버리기";
        hint.fontSize = 48;
        hint.characterSize = 0.04f;
        hint.anchor = TextAnchor.MiddleCenter;
        hint.color = new Color(1f, 0.9f, 0.4f);
        go.SetActive(false);
    }

    public override bool CanAcceptDrop(Carryable item) => IsGeneralWaste(item) && CleanupMode.IsOn;

    void Update()
    {
        bool show = false;
        Camera cam = Camera.main;
        HandCarry hand = HandCarry.Instance;
        if (cam != null && hand != null && Mouse.current != null && IsGeneralWaste(hand.Current) && CleanupMode.IsOn)
        {
            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            foreach (RaycastHit h in Physics.RaycastAll(ray, 200f))
                if (h.collider.GetComponentInParent<GeneralTrashCan>() == this) { show = true; break; }
        }
        if (show == lit) return;
        lit = show;
        if (hint != null) hint.gameObject.SetActive(show);
    }

    protected override void Activate()
    {
        HandCarry hand = HandCarry.Instance;
        Carryable held = hand?.Current;
        if (held == null || !IsGeneralWaste(held) || busy) return;
        if (!CleanupMode.IsOn) { CleanupMode.NotifyBlocked(); return; }

        hand.Detach();
        if (held.CurrentContainer != null) { held.CurrentContainer.Release(held); held.CurrentContainer = null; }
        foreach (Collider c in held.GetComponentsInChildren<Collider>()) c.enabled = false;
        Debug.Log($"[일반쓰레기] {held.name} 버림");
        StartCoroutine(Drop(held));
    }

    private IEnumerator Drop(Carryable item)
    {
        busy = true;
        item.transform.SetParent(null, true);
        Renderer r = GetComponent<Renderer>();
        Vector3 mouth = r != null ? new Vector3(r.bounds.center.x, r.bounds.max.y, r.bounds.min.z) : transform.position;
        Vector3 from = item.transform.position, scale = item.transform.localScale;
        for (float t = 0f; t < dropSeconds; t += Time.deltaTime)
        {
            if (item == null) break;
            float k = t / dropSeconds;
            item.transform.position = Vector3.Lerp(from, mouth, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.3f;
            item.transform.localScale = scale * Mathf.Lerp(1f, 0.3f, k);
            yield return null;
        }
        if (item != null) Destroy(item.gameObject);
        busy = false;
    }
}
