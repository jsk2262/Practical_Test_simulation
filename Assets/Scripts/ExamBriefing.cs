using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The examiner's briefing between the 순서도 planning screen and the start of
/// the clock: each dish's supplied ingredients read out as subtitles along the
/// bottom of the screen ("각자 확인해주세요. 탕수육 재료입니다. ..."), then
/// "문제없다면 시험 시작하겠습니다." - and only then does the exam time start
/// running. Meanwhile the player checks the ingredients and sorts them into
/// bowls per dish, so the kitchen stays usable (plates too, without the 정리
/// tab); Space/Enter skips to the next line (a click is for the kitchen).
/// Voice (TTS) later: subscribe to LineStarted, and set VoiceSeconds to how long
/// a line's audio runs so the subtitle stays up that long.
/// </summary>
public class ExamBriefing : MonoBehaviour
{
    /// <summary>Raised as each subtitle line appears (for TTS).</summary>
    public static event Action<string> LineStarted;

    /// <summary>Optional: how long the voice for a line lasts (seconds, 0 = unknown).</summary>
    public static Func<string, float> VoiceSeconds;

    public static bool IsPlaying { get; private set; }

    /// <summary>지급재료 per dish, as the examiner reads them out. Dishes not
    /// listed fall back to the seasonings on their exam sheet.</summary>
    private static readonly Dictionary<string, string> Supplied = new Dictionary<string, string>
    {
        { "탕수육", "돼지등심 200g, 달걀 1개, 대파, 당근, 오이, 완두콩, 목이버섯, 양파, 청주, 식초, 식용유, 설탕, 간장, 녹말가루" },
        { "마파두부", "두부 150g, 다진 돼지등심 50g, 마늘 2쪽, 생강, 대파, 홍고추, 두반장, 고춧가루, 간장, 식용유, 참기름, 설탕, 검은후춧가루, 녹말가루" },
    };

    private const int CharsPerRow = 34;
    private static ExamBriefing instance;

    private TextMesh text;
    private GameObject bar;
    private bool skip;

    /// <summary>Plays the briefing, then calls onDone (starting the clock).</summary>
    public static void Run(Action onDone)
    {
        if (instance == null) instance = new GameObject("ExamBriefing").AddComponent<ExamBriefing>();
        instance.StartCoroutine(instance.Play(onDone));
    }

    private IEnumerator Play(Action onDone)
    {
        IsPlaying = true;
        Build();

        // Voiced (BriefingVoice): the recording plays and each subtitle shows
        // at its own time in it - not at the reading-pace guess below, which
        // ran ahead of the voice.
        BriefingVoice voiced = BriefingVoice.Find();
        if (voiced != null && voiced.Ready)
        {
            yield return PlayVoiced(voiced);
            bar.SetActive(false);
            IsPlaying = false;
            onDone?.Invoke();
            yield break;
        }

        foreach (string line in Lines())
        {
            text.text = Wrap(line);
            LineStarted?.Invoke(line);
            lineAt = Time.time; // 이 줄을 띄운 시각 (빠른 모드 자동 넘기기용)
            float voice = VoiceSeconds != null ? VoiceSeconds(line) : 0f;
            float seconds = voice > 0f ? voice + 0.4f : SpeechSeconds(line);
            skip = false;
            yield return null;
            for (float t = 0f; t < seconds && !skip; t += Time.deltaTime) yield return null;
        }

        bar.SetActive(false);
        IsPlaying = false;
        onDone?.Invoke();
    }

    private AudioSource voice;

    /// <summary>The recording through, its subtitles changing at their times;
    /// Space/Enter skips to the next subtitle (the voice jumps there too).</summary>
    private IEnumerator PlayVoiced(BriefingVoice v)
    {
        if (voice == null)
        {
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
        }
        voice.clip = v.Clip;
        voice.time = 0f;
        voice.Play();
        int shown = -2;
        skip = false;
        while (voice.isPlaying)
        {
            int at = v.CueAt(voice.time);
            if (at != shown)
            {
                shown = at;
                string line = at >= 0 ? v.Cues[at].text : "";
                text.text = Wrap(line.Replace("\n", " "));
                if (at >= 0) LineStarted?.Invoke(line);
                lineAt = Time.time; // 이 줄을 띄운 시각 (빠른 모드 자동 넘기기용)
                skip = false;       // (줄이 바뀐 프레임에 남은 넘기기는 버림 - 두 줄씩 넘어가지 않게)
            }
            if (skip)
            {
                skip = false;
                if (at + 1 < v.Cues.Count) voice.time = Mathf.Min(v.Cues[at + 1].start, v.Clip.length - 0.05f);
                else break;
            }
            yield return null;
        }
        voice.Stop();
        yield return new WaitForSeconds(0.4f);
    }

    void Update()
    {
        if (!IsPlaying) return;
        Keyboard k = Keyboard.current;
        if (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame))
            skip = true;
        if (SpeedMode.On && Time.time - lineAt >= FastLineSeconds) skip = true; // 빠른 모드: 한 줄을 1초만 보여 주고 넘김
    }

    private float lineAt; // 지금 줄을 띄운 시각
    private const float FastLineSeconds = 1f; // 빠른 모드에서 한 줄을 보여 주는 시간

    /// <summary>Korean read aloud at an examiner's pace (≈4 syllables a second):
    /// how long a line will take once it's voiced - numbers and units read out
    /// in full ("200g" = 이백 그램), a short breath at each comma and a pause at
    /// each sentence end, plus a moment before the next line. Used until the
    /// TTS reports its real length (VoiceSeconds).</summary>
    public const float SyllablesPerSecond = 4f;

    public static float SpeechSeconds(string line)
    {
        float syllables = 0f;
        int commas = 0, stops = 0;
        foreach (char ch in line)
        {
            if (ch >= '가' && ch <= '힣') syllables += 1f;     // 한글 음절
            else if (char.IsDigit(ch)) syllables += 1.3f;   // 2 → 두, 150 → 백오십
            else if (ch == 'g') syllables += 2f;            // 그램
            else if (ch == ',') commas++;
            else if (ch == '.' || ch == '?') stops++;
        }
        return syllables / SyllablesPerSecond + commas * 0.25f + stops * 0.4f + 0.5f;
    }

    private static List<string> Lines()
    {
        var lines = new List<string> { "각자 확인해주세요." };
        var pages = new List<ExamPaperFlip>(FindObjectsByType<ExamPaperFlip>(FindObjectsSortMode.None));
        pages.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x)); // left sheet first
        if (pages.Count == 0)
            foreach (var kv in Supplied) lines.Add($"{kv.Key} 재료입니다. {kv.Value}입니다.");
        foreach (ExamPaperFlip page in pages)
        {
            string list = Supplied.TryGetValue(page.DishName, out string s) ? s
                : page.RequiredSeasonings != null ? string.Join(", ", page.RequiredSeasonings) : "";
            lines.Add($"{page.DishName} 재료입니다. {list}입니다.");
        }
        lines.Add("재료가 없거나 상태가 좋지 않다면 말해주세요.");
        lines.Add("문제없다면 시험 시작하겠습니다.");
        return lines;
    }

    /// <summary>Breaks a long line into rows at ", " / spaces (TextMesh doesn't wrap).</summary>
    private static string Wrap(string line)
    {
        var sb = new System.Text.StringBuilder();
        int row = 0;
        foreach (string word in line.Split(' '))
        {
            if (row > 0 && row + word.Length + 1 > CharsPerRow) { sb.Append('\n'); row = 0; }
            else if (row > 0) { sb.Append(' '); row++; }
            sb.Append(word);
            row += word.Length;
        }
        return sb.ToString();
    }

    private void Build()
    {
        if (bar != null) { bar.SetActive(true); return; }
        Camera cam = Camera.main;
        float half = cam != null && cam.orthographic ? cam.orthographicSize : 6.8f;
        Vector3 c = cam != null ? cam.transform.position : Vector3.zero;

        bar = new GameObject("BriefingSubtitle");
        bar.transform.SetParent(transform, false);
        // Up over the seasoning shelf, out of the way of the counter where the
        // ingredients are being sorted.
        bar.transform.rotation = cam != null ? cam.transform.rotation : Quaternion.identity; // 자막을 현재 화면과 평행하게 한다.
        bar.transform.position = c + bar.transform.forward * 3f + bar.transform.up * (half * 0.82f); // 카메라 정면 상단

        GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "Bg";
        Destroy(bg.GetComponent<Collider>());
        bg.transform.SetParent(bar.transform, false);
        bg.transform.localScale = new Vector3(half * 2.05f, half * 0.34f, 1f);
        Shader sh = Shader.Find("Sprites/Default");
        bg.GetComponent<Renderer>().sharedMaterial = new Material(sh) { color = new Color(0.05f, 0.05f, 0.05f, 0.9f) };

        GameObject label = new GameObject("Text");
        label.transform.SetParent(bar.transform, false);
        label.transform.localPosition = new Vector3(0f, 0f, -0.05f);
        text = label.AddComponent<TextMesh>();
        KoreanFontFallback.Apply(text); // 웹에서도 한글이 보이게 한글 글꼴
        text.fontSize = 44;
        text.characterSize = 0.08f;
        text.lineSpacing = 1.1f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = Color.white;
    }
}
