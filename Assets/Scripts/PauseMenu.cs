using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Esc 일시정지: 게임 시간을 멈추고(소리도) 가운데에 "계속하기 / 게임 종료" 창을 띄운다.
/// 다른 창 · 조작 · 확대 보기가 Esc를 쓰는 중이면 그쪽에 양보한다 (확대 중이면 Esc 한 번은
/// 전체 보기, 그다음 Esc가 일시정지). 멈춘 동안 주방 클릭은 막힌다 (ClickDetector 등이 IsPaused 확인).
/// </summary>
[DefaultExecutionOrder(-1000)] // (다른 스크립트가 같은 Esc로 창을 닫기 전에, 이번 프레임 상태로 판단)
public class PauseMenu : MonoBehaviour
{
    public static PauseMenu Instance { get; private set; } // 관리자 하나
    public static bool IsPaused => Instance != null && Instance.paused; // 지금 멈춤

    private bool paused;            // 멈춤 여부
    private float timeScaleBefore;  // 멈추기 전 시간 배속 (빠른 모드면 1이 아닐 수 있음)
    private GUIStyle head, button;  // 글자 · 버튼 모양
    private Texture2D shade, panel; // 뒤 어둡게 · 창 바탕

    void Awake() => Instance = this; // 관리자 등록

    void OnDestroy()
    {
        if (paused) Resume();                 // 씬이 바뀌어도 시간이 멈춘 채 남지 않게
        if (Instance == this) Instance = null; // 등록 해제
        if (shade != null) Destroy(shade);     // 만든 그림 정리
        if (panel != null) Destroy(panel);
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;                          // 키보드
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return; // Esc를 눌렀을 때만
        if (paused) { Resume(); return; }                        // 멈춤 중이면 다시 시작
        if (KitchenCamera.IsZoomed || KitchenCamera.SomethingOpenOrBusy()) return; // 다른 곳이 Esc를 씀 - 양보
        Pause();                                                 // 일시정지
    }

    /// <summary>게임 시간 · 소리를 멈추고 창을 띄움.</summary>
    public void Pause()
    {
        if (paused) return;                 // 이미 멈춤
        paused = true;
        timeScaleBefore = Time.timeScale;   // 원래 배속 기억
        Time.timeScale = 0f;                // 시간 멈춤 (가열 · 익힘 · 시험 시간)
        AudioListener.pause = true;         // 소리 멈춤 (안내 음성 포함)
    }

    /// <summary>멈추기 전 상태로 되돌림.</summary>
    public void Resume()
    {
        if (!paused) return;                // 멈춘 적 없음
        paused = false;
        Time.timeScale = timeScaleBefore > 0f ? timeScaleBefore : 1f; // 원래 배속으로
        AudioListener.pause = false;        // 소리 다시
    }

    /// <summary>게임을 끝냄 (빌드: 프로그램 종료, 에디터: 플레이 중지).</summary>
    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // 에디터에서는 플레이만 멈춤
#else
        Application.Quit();                              // 빌드에서는 프로그램 종료
#endif
    }

    private static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1); // 한 색 그림
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void OnGUI()
    {
        if (!paused) return; // 멈췄을 때만 창
        if (head == null)    // 처음 한 번 모양 만들기
        {
            head = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { fontSize = 22 };
            shade = Solid(new Color(0f, 0f, 0f, 0.55f));
            panel = Solid(new Color(0.12f, 0.12f, 0.14f, 0.97f));
        }
        GUI.depth = -200;                                                    // 다른 창보다 위
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), shade); // 뒤 화면 어둡게
        float w = Mathf.Min(420f, Screen.width - 40f), h = 260f;              // 창 크기
        Rect r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h); // 가운데
        GUI.DrawTexture(r, panel);                                           // 창 바탕
        GUILayout.BeginArea(new Rect(r.x + 30f, r.y + 24f, r.width - 60f, r.height - 48f));
        GUILayout.Label("일시정지", head);                                    // 제목
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("계속하기 (Esc)", button, GUILayout.Height(52f))) Resume(); // 다시 시작
        GUILayout.Space(14f);
        if (GUILayout.Button("게임 종료", button, GUILayout.Height(52f))) QuitGame();    // 종료
        GUILayout.EndArea();
    }
}
