using UnityEngine;

/// <summary>A small clickable marker (matches SequenceHintButton's yellow-sphere
/// look) that toggles a plain text note nearby - for exam cheat-sheet reminders
/// (cutting specs, sauce ratios) that would otherwise clutter the counter. Click
/// again, or its own "닫기" button, to hide it.</summary>
public class InfoRevealButton : MonoBehaviour, IClickable
{
    [SerializeField] private string title = "정보";
    [TextArea(2, 6)]
    [SerializeField] private string body = "";
    [SerializeField] private Vector2 popupSize = new Vector2(10f, 3.4f);
    [SerializeField] private Vector3 popupLocalOffset = new Vector3(0f, -1.7f, -0.3f);

    private GameObject popup;

    /// <summary>Whichever of these buttons (CutSpecification, RecipesSauce, any
    /// future one) currently has its popup open - opening a different one
    /// closes this first, so at most one cheat-sheet note ever covers the
    /// counter at a time instead of them stacking up and blocking clicks to
    /// whatever's underneath.</summary>
    private static InfoRevealButton currentlyOpen;

    public void Init(string title, string body)
    {
        this.title = title;
        this.body = body;
    }

    public void OnClicked()
    {
        if (popup == null) BuildPopup();
        bool willOpen = !popup.activeSelf;
        // (창은 전체 화면 기준 자리에 뜬다 - 확대 중이면 먼저 전체 보기로, 다시 누르면 열림)
        if (willOpen && KitchenCamera.IsZoomed) { KitchenCamera.Instance.ShowOverview(); return; }

        if (willOpen)
        {
            if (currentlyOpen != null && currentlyOpen != this) currentlyOpen.ClosePopup();
            currentlyOpen = this;
        }
        else if (currentlyOpen == this)
        {
            currentlyOpen = null;
        }

        popup.SetActive(willOpen);
    }

    private void ClosePopup()
    {
        if (popup != null) popup.SetActive(false);
        if (currentlyOpen == this) currentlyOpen = null;
    }

    private void BuildPopup()
    {
        popup = new GameObject("InfoPopup");
        popup.transform.SetParent(transform, false);

        // World-space offset and a scale that cancels this button's own -
        // imported marker assets aren't necessarily authored at scale 1 (e.g.
        // CutSpecification/RecipesSauce sit at 0.1), and naively parenting with
        // a local offset/identity scale would shrink and mis-place the whole
        // popup right along with it.
        popup.transform.position = transform.position + popupLocalOffset;
        popup.transform.rotation = Quaternion.identity;
        Vector3 parentScale = transform.lossyScale;
        popup.transform.localScale = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? 1f : 1f / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? 1f : 1f / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? 1f : 1f / parentScale.z);

        GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "Bg";
        bg.transform.SetParent(popup.transform, false);
        bg.transform.localPosition = new Vector3(0f, 0f, 0.05f);
        bg.transform.localScale = new Vector3(popupSize.x, popupSize.y, 1f);
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = new Color(0.1f, 0.1f, 0.08f, 0.97f);
        bg.GetComponent<Renderer>().sharedMaterial = mat;
        // Keeps its own collider (unlike a cosmetic-only quad) so this note
        // doesn't let clicks fall through to whatever kitchen prop sits behind it.

        GameObject titleGo = new GameObject("Title");
        titleGo.transform.SetParent(popup.transform, false);
        titleGo.transform.localPosition = new Vector3(0f, popupSize.y * 0.5f - 0.35f, -0.02f);
        TextMesh titleTm = titleGo.AddComponent<TextMesh>();
        titleTm.text = title;
        titleTm.fontSize = 28;
        titleTm.fontStyle = FontStyle.Bold;
        titleTm.characterSize = 0.1f;
        titleTm.anchor = TextAnchor.MiddleCenter;
        titleTm.color = new Color(1f, 0.85f, 0.3f);

        GameObject bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(popup.transform, false);
        bodyGo.transform.localPosition = new Vector3(0f, -0.15f, -0.02f);
        TextMesh bodyTm = bodyGo.AddComponent<TextMesh>();
        bodyTm.text = body;
        bodyTm.fontSize = 23;
        bodyTm.characterSize = 0.1f;
        bodyTm.anchor = TextAnchor.MiddleCenter;
        bodyTm.color = Color.white;

        GameObject btn = GameObject.CreatePrimitive(PrimitiveType.Quad);
        btn.name = "Btn_Close";
        btn.transform.SetParent(popup.transform, false);
        btn.transform.localPosition = new Vector3(popupSize.x * 0.5f - 0.35f, popupSize.y * 0.5f - 0.3f, -0.06f);
        btn.transform.localScale = new Vector3(0.6f, 0.35f, 1f);
        Material btnMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        btnMat.color = new Color(0.8f, 0.3f, 0.3f);
        btn.GetComponent<Renderer>().sharedMaterial = btnMat;

        GameObject xGo = new GameObject("X");
        xGo.transform.SetParent(btn.transform, false);
        xGo.transform.localPosition = new Vector3(0f, 0f, -0.05f);
        TextMesh xTm = xGo.AddComponent<TextMesh>();
        xTm.text = "X";
        xTm.fontSize = 20;
        xTm.characterSize = 0.1f;
        xTm.anchor = TextAnchor.MiddleCenter;
        xTm.color = Color.white;
        xGo.transform.localScale = new Vector3(1f / 0.6f, 1f / 0.35f, 1f);

        btn.AddComponent<SimpleActionButton>().Init(ClosePopup);

        popup.SetActive(false);
    }
}
