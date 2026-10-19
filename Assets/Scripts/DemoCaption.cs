using UnityEngine;

/// <summary>
/// 시연 영상용 자막: 화면 아래 장면 설명(제목 + 한 줄 설명)과, 처음·끝의 큰
/// 제목 카드. 시연 녹화(Editor/DemoFilm)가 켜고 끄며, 평소 게임에는 없다.
/// </summary>
public class DemoCaption : MonoBehaviour
{
    private static DemoCaption instance;
    private string title = "", line = "";
    private string cardTitle, cardSub;
    private float shownAt;
    private GUIStyle titleStyle, lineStyle, cardStyle, cardSubStyle, tagStyle;
    private Texture2D bg, cardBg;

    private static DemoCaption Get()
    {
        if (instance == null) instance = new GameObject("DemoCaption").AddComponent<DemoCaption>();
        return instance;
    }

    /// <summary>아래쪽 장면 자막.</summary>
    public static void Show(string sceneTitle, string description)
    {
        DemoCaption c = Get();
        c.title = sceneTitle ?? "";
        c.line = description ?? "";
        c.cardTitle = null;
        c.shownAt = Time.unscaledTime;
    }

    /// <summary>화면 전체를 덮는 제목 카드 (null이면 끔).</summary>
    public static void Card(string bigTitle, string sub)
    {
        DemoCaption c = Get();
        c.cardTitle = bigTitle;
        c.cardSub = sub ?? "";
        c.title = c.line = "";
        c.shownAt = Time.unscaledTime;
    }

    public static void Clear()
    {
        PerFrame = null;
        if (instance != null) Destroy(instance.gameObject);
    }

    /// <summary>녹화 중 매 프레임 할 일 (예: 연기 뿜기 - 에디터 쪽 틱은 프레임마다 오지 않음).</summary>
    public static System.Action PerFrame;

    void Update() => PerFrame?.Invoke();

    void OnDestroy()
    {
        if (bg != null) Destroy(bg);
        if (cardBg != null) Destroy(cardBg);
        if (vignette != null) Destroy(vignette);
        if (badgeBg != null) Destroy(badgeBg);
    }

    private static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void OnGUI()
    {
        GUI.depth = -100; // (다른 화면 위에)
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            titleStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);
            lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            lineStyle.normal.textColor = Color.white;
            cardStyle = new GUIStyle(GUI.skin.label) { fontSize = 76, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            cardStyle.normal.textColor = Color.white;
            cardSubStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            cardSubStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);
            tagStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleRight };
            tagStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            bg = Solid(new Color(0.06f, 0.07f, 0.09f, 0.82f));
            cardBg = Solid(new Color(0.07f, 0.08f, 0.1f, 0.94f));
        }
        // 1920x1080 기준으로 배치하고 화면 크기에 맞춰 늘림
        Matrix4x4 old = GUI.matrix;
        float s = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
        GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1920f * s) * 0.5f, (Screen.height - 1080f * s) * 0.5f, 0f), Quaternion.identity, Vector3.one * s);
        float fade = Mathf.Clamp01((Time.unscaledTime - shownAt) / 0.35f);
        Color prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, fade);

        if (!string.IsNullOrEmpty(cardTitle))
        {
            GUI.DrawTexture(new Rect(-200f, -200f, 2320f, 1480f), cardBg);
            GUI.Label(new Rect(160f, 330f, 1600f, 260f), cardTitle, cardStyle);
            GUI.Label(new Rect(160f, 600f, 1600f, 160f), cardSub, cardSubStyle);
        }
        else
        {
            // 핵심 장면: 가장자리를 어둡게 해 가운데로 시선을 모으고 배지 · 단계 자막
            if (!string.IsNullOrEmpty(spotBadge))
            {
                if (vignette == null) vignette = MakeVignette();
                GUI.color = new Color(1f, 1f, 1f, fade);
                GUI.DrawTexture(new Rect(-60f, -60f, 2040f, 1200f), vignette);
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 5f);
                GUI.color = new Color(1f, 1f, 1f, fade * pulse);
                GUI.DrawTexture(new Rect(60f, 50f, 300f, 74f), badgeBg);
                GUI.Label(new Rect(60f, 50f, 300f, 74f), spotBadge, badgeStyle);
                GUI.color = new Color(1f, 1f, 1f, fade);
                if (!string.IsNullOrEmpty(spotStep)) GUI.Label(new Rect(60f, 135f, 1200f, 64f), spotStep, stepStyle);
            }
            if (!string.IsNullOrEmpty(title))
            {
                Rect box = new Rect(0f, 890f, 1920f, 150f);
                GUI.DrawTexture(box, bg);
                GUI.Label(new Rect(70f, 900f, 1780f, 56f), title, titleStyle);
                GUI.Label(new Rect(70f, 955f, 1780f, 76f), line, lineStyle);
                GUI.Label(new Rect(1320f, 900f, 540f, 56f), "중식조리기능사 실기 시뮬레이터", tagStyle);
            }
            // 빨리 감기 중
            if (ffSpeed > 0f)
            {
                GUI.DrawTexture(new Rect(1600f, 50f, 260f, 64f), bg);
                GUI.Label(new Rect(1600f, 50f, 260f, 64f), $"▶▶ ×{ffSpeed:0}", badgeStyle);
            }
        }
        GUI.color = prev;
        GUI.matrix = old;
    }

    // ---- 핵심 장면 강조 · 빨리 감기 표시 ----
    private string spotBadge, spotStep;
    private float ffSpeed;
    private Texture2D vignette, badgeBg;
    private GUIStyle badgeStyle, stepStyle;

    /// <summary>핵심 장면 강조 (badge가 null이면 끔). step = 지금 하는 단계 자막.</summary>
    public static void Spotlight(string badge, string step = null)
    {
        DemoCaption c = Get();
        c.EnsureSpotStyles();
        c.spotBadge = badge;
        c.spotStep = step;
    }

    /// <summary>빨리 감기 배속 표시 (0이면 끔).</summary>
    public static void FastForward(float speed)
    {
        DemoCaption c = Get();
        c.EnsureSpotStyles();
        c.ffSpeed = speed;
    }

    private void EnsureSpotStyles()
    {
        if (badgeStyle != null) return;
        badgeStyle = new GUIStyle { fontSize = 36, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        badgeStyle.normal.textColor = new Color(0.12f, 0.1f, 0.06f);
        stepStyle = new GUIStyle { fontSize = 38, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        stepStyle.normal.textColor = Color.white;
        badgeBg = Solid(new Color(1f, 0.78f, 0.25f, 0.95f));
    }

    /// <summary>가운데는 투명, 가장자리로 갈수록 어두워지는 원형 그라데이션.</summary>
    private static Texture2D MakeVignette()
    {
        const int n = 128;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                t.SetPixel(x, y, new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 0.72f, (r - 0.45f) / 0.65f)));
            }
        t.Apply();
        return t;
    }
}
