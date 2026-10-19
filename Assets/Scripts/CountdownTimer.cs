using UnityEngine;

[RequireComponent(typeof(TextMesh))]
public class CountdownTimer : MonoBehaviour
{
    public static CountdownTimer Instance { get; private set; }

    [SerializeField] private float totalSeconds = 55f * 60f;

    private float remaining;
    private TextMesh label;
    private bool started;

    /// <summary>시험 시계가 돌기 시작함 (감독관 안내가 끝난 뒤).</summary>
    public bool Started => started;

    void Awake()
    {
        Instance = this;
        label = GetComponent<TextMesh>();
        remaining = totalSeconds;
        UpdateLabel();
    }

    void Update()
    {
        if (!started || remaining <= 0f) return;

        // (빠른 모드: real time - not the sped-up animations)
        remaining -= SpeedMode.On ? Time.unscaledDeltaTime : Time.deltaTime;
        if (remaining < 0f) remaining = 0f;

        UpdateLabel();
        if (remaining <= 0f) ExamFlow.TimeUp(); // time's up: what's plated is handed in, then the final evaluation (once)
    }

    /// <summary>Called once the pre-start 순서도 planning screen's "시작하세요" button
    /// is clicked - the exam clock doesn't run before that.</summary>
    public void BeginCountdown()
    {
        started = true;
    }

    private void UpdateLabel()
    {
        int m = Mathf.FloorToInt(remaining / 60f);
        int s = Mathf.FloorToInt(remaining % 60f);
        label.text = $"{m:00}:{s:00}";
    }
}
