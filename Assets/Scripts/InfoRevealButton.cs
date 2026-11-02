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

    private Vector3 popupBaseScale; // 창의 전체 보기 기준 크기

    /// <summary>창을 카메라 앞 얼마에 띄울지 - 조리대·선반(깊이 14 이상)보다 앞.</summary>
    private const float PopupViewDistance = 12f;

    public void OnClicked()
    {
        if (popup == null) BuildPopup();
        bool willOpen = !popup.activeSelf;

        if (willOpen)
        {
            if (currentlyOpen != null && currentlyOpen != this) currentlyOpen.ClosePopup();
            currentlyOpen = this;
            // 힌트처럼 화면 가운데에 띄움 (버튼 바로 아래가 아니라)
            Camera cam = Camera.main;                                                       // 지금 보는 카메라
            if (cam != null)
            {
                // 화면 한가운데에 띄운다. 옛 프레임은 카메라가 조리대와 같은 높이라
                // (cam.x, cam.y) 가 곧 화면 중앙이었지만, 지금 카메라는 위·앞에 있어
                // 그 자리에 두면 창이 화면 위쪽 바깥으로 나가 안 열리는 것처럼 보였다.
                popup.transform.position = cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, PopupViewDistance)); // 화면 중앙, 카메라 앞
                popup.transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);     // 창이 화면과 나란하게
            }
            if (popupBaseScale == Vector3.zero) popupBaseScale = popup.transform.localScale;      // 전체 보기 기준 크기 기억
            popup.transform.localScale = popupBaseScale * KitchenCamera.ZoomScale;               // 확대 중이면 화면에 맞게 줄임
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
        KoreanFontFallback.Apply(titleTm); // 웹에서도 한글이 보이게 한글 글꼴
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
        KoreanFontFallback.Apply(bodyTm); // 웹에서도 한글이 보이게 한글 글꼴
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
        KoreanFontFallback.Apply(xTm); // 웹에서도 한글이 보이게 한글 글꼴
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
