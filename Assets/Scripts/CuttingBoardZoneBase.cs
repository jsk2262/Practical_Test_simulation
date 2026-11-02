using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shared look/feel of the small buttons at the cutting board's corners
/// (CuttingBoardFlipZone, CuttingBoardWashZone): a shimmering marker, a
/// translucent label ("뒤집기"/"씻기") that shows while the cursor is near it,
/// and the same label as a brief toast after a click.
/// </summary>
public abstract class CuttingBoardZoneBase : MonoBehaviour, IClickable
{
    /// <summary>Offset from this zone down into the empty counter space below
    /// the board - the board's own Y extent varies as it gets repositioned, so
    /// this is a small downward nudge from wherever the zone itself already
    /// sits (already low, near the board's bottom edge) rather than a fixed
    /// world position.</summary>
    [SerializeField] private Vector3 promptOffset = new Vector3(0f, -0.55f, -0.2f);
    [SerializeField] private float promptVisibleSeconds = 1f;
    /// <summary>How close (world units, on screen) the cursor must come for the label to show.</summary>
    [SerializeField] private float hoverRadius = 0.55f;

    protected abstract string Label { get; }
    protected abstract Color MarkerColor { get; }
    protected abstract void OnZoneClicked();

    /// <summary>Whether the button is there at all right now - hidden (and not
    /// clickable, so clicks reach the board) otherwise.</summary>
    protected virtual bool IsAvailable => true;

    private GameObject promptRoot;
    private TextMesh promptText;
    private Coroutine hideRoutine;
    private GameObject marker;
    private Collider ownCollider;
    private bool shown = true;

    protected virtual void Awake()
    {
        ownCollider = GetComponent<Collider>();
        BuildMarker();
    }

    private void SetShown(bool show)
    {
        if (shown == show) return;
        shown = show;
        if (marker != null) marker.SetActive(show);
        if (ownCollider != null) ownCollider.enabled = show;
        if (!show && promptRoot != null && hideRoutine == null) promptRoot.SetActive(false);
    }

    public void OnClicked()
    {
        ShowToast(Label);
        OnZoneClicked(); // may replace the toast with a reason it couldn't
    }

    void Update()
    {
        SetShown(IsAvailable);
        if (!shown) return;
        if (hideRoutine != null) return; // a click toast is showing
        bool near = CursorIsNear();
        if (near && (promptRoot == null || !promptRoot.activeSelf)) ShowPrompt(Label);
        else if (!near && promptRoot != null && promptRoot.activeSelf) promptRoot.SetActive(false);
    }

    private bool CursorIsNear()
    {
        Camera cam = Camera.main;
        if (cam == null || Mouse.current == null) return false;
        Vector2 screen = Mouse.current.position.ReadValue();
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, transform.position.z - cam.transform.position.z));
        return ((Vector2)(world - transform.position)).sqrMagnitude <= hoverRadius * hoverRadius;
    }

    /// <summary>Brief feedback after a click (also used for messages like
    /// "clear the board first").</summary>
    protected void ShowToast(string text)
    {
        ShowPrompt(text);
        if (hideRoutine != null) StopCoroutine(hideRoutine);
        hideRoutine = StartCoroutine(HideAfterDelay());
    }

    private void ShowPrompt(string text)
    {
        if (promptRoot == null) BuildPrompt();
        promptText.text = text;
        promptRoot.transform.position = transform.position + promptOffset;
        promptRoot.SetActive(true);
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(promptVisibleSeconds);
        promptRoot.SetActive(false);
        hideRoutine = null;
    }

    /// <summary>This trigger has no visual of its own - just an invisible
    /// collider - so a small shimmering dot sits on top of it as the
    /// "click here" affordance.</summary>
    private void BuildMarker()
    {
        marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
        marker.name = "Marker";
        marker.transform.SetParent(transform, false);
        marker.transform.localPosition = new Vector3(0f, 0f, -0.1f);
        marker.transform.localScale = new Vector3(0.3f, 0.3f, 1f);
        Object.Destroy(marker.GetComponent<Collider>());
        marker.GetComponent<Renderer>().material = TransparentMaterial(MarkerColor);
        marker.AddComponent<ShimmerEffect>();
    }

    /// <summary>Translucent (background alpha well below the marker's own) so
    /// it reads as a passing note, not a dialog demanding attention.</summary>
    private void BuildPrompt()
    {
        promptRoot = new GameObject("Prompt");
        promptRoot.transform.SetParent(transform, false);

        GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "Background";
        bg.transform.SetParent(promptRoot.transform, false);
        bg.transform.localPosition = new Vector3(0f, 0f, 0.02f);
        bg.transform.localScale = new Vector3(1.3f, 0.5f, 1f);
        bg.GetComponent<Renderer>().material = TransparentMaterial(new Color(0.9f, 0.88f, 0.8f, 0.5f));
        Object.Destroy(bg.GetComponent<Collider>());

        GameObject labelGo = new GameObject("Label");
        labelGo.transform.SetParent(promptRoot.transform, false);
        labelGo.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        promptText = labelGo.AddComponent<TextMesh>();
        KoreanFontFallback.Apply(promptText); // 웹에서도 한글이 보이게 한글 글꼴
        promptText.fontSize = 28;
        promptText.characterSize = 0.16f;
        promptText.anchor = TextAnchor.MiddleCenter;
        promptText.color = new Color(0.15f, 0.15f, 0.15f, 0.85f);

        promptRoot.SetActive(false);
    }

    private static Material TransparentMaterial(Color color)
    {
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetFloat("_Surface", 1f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.color = color;
        return mat;
    }
}
