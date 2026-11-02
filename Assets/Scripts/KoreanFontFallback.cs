using UnityEngine; // 글꼴 · 시작 시점 호출

/// <summary>
/// 웹(WebGL)에서도 한글이 보이게 한다. 게임 글자(TextMesh · OnGUI)는 글꼴을 따로 정하지
/// 않으면 Unity 기본 글꼴을 쓰는데, 여기엔 한글이 없다. Windows는 PC에 깔린 글꼴로 대신
/// 보여 주지만 브라우저는 PC 글꼴을 못 쓰므로(대체 글꼴 지정도 웹에선 안 먹음), 게임에 넣은
/// 한글 글꼴(Resources/Fonts)을 글자마다 직접 지정한다.
/// - 코드로 만드는 글자: 만든 곳에서 Apply(글자)
/// - 씬에 놓인 글자: 장면이 열린 뒤 한 번
/// - OnGUI 글자: 공용 GUI 스킨의 글꼴을 한 번 (GUIStyle은 스킨에서 글꼴을 물려받음)
/// </summary>
public static class KoreanFontFallback
{
    private const string ResourcePath = "Fonts/NotoSansKR-Regular"; // 게임에 넣은 한글 글꼴 위치 (Resources 기준) - Noto Sans KR, SIL OFL 1.1 (Assets/Fonts/NotoSansKR-OFL.txt)

    /// <summary>게임에 넣은 한글 글꼴.</summary>
    public static Font Font { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Load() // 장면이 열리기 전에 한 번
    {
        Font = Resources.Load<Font>(ResourcePath); // 빌드에 들어간 한글 글꼴을 불러온다
        if (Font == null) { Debug.LogWarning($"[글꼴] {ResourcePath} 한글 글꼴을 찾지 못함 - 웹에서 한글이 안 보일 수 있음"); return; } // 글꼴이 없으면 알림만
        var gui = new GameObject("KoreanGuiFont"); // OnGUI 스킨 글꼴을 바꿀 작은 도우미
        Object.DontDestroyOnLoad(gui); // 장면 계층에 남지 않게 (DontDestroyOnLoad 영역)
        gui.AddComponent<KoreanGuiFont>(); // 첫 OnGUI에서 스킨 글꼴을 바꾼다
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyToScene() // 씬에 미리 놓인 글자 - 장면이 열린 뒤 한 번만
    {
        foreach (TextMesh tm in Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Apply(tm); // 꺼져 있는 것까지
    }

    /// <summary>글자 하나를 한글 글꼴로 (글꼴과 그 글꼴의 재질을 함께 바꿔야 보인다).</summary>
    public static void Apply(TextMesh tm)
    {
        if (Font == null || tm == null || tm.font == Font) return; // 글꼴이 없거나 이미 한글 글꼴이면 그대로
        tm.font = Font; // 한글 글꼴로
        if (tm.TryGetComponent(out MeshRenderer r)) r.sharedMaterial = Font.material; // 글꼴 텍스처가 든 재질로
    }
}

