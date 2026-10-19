using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 전체 과정 3분 영상: 탕수육과 마파두부를 실제 시험 순서대로 함께 만드는 과정을
/// 빠르게 돌려 보여 준다 - 순서 카드(머릿속 계획) → 재료 나누기 → 손 씻기 → 썰기 →
/// 고추기름 → 고기 밑간 · 튀김옷 → 기름 온도 확인 · 두 번 튀기기 → 탕수육 소스 →
/// 마파두부 → 제출 → 정리 → 채점. 자막은 "왜 이 과정이 들어가 있는지"를 설명한다.
/// 감독관 음성(TTS)은 앞과 중간에 짧게 잘라 넣는다.
/// 사용: 플레이 모드에서 DemoFilm.RunFull(녹화 여부, 파일 이름).
/// </summary>
public static partial class DemoFilm
{
    // ---- 감독관 음성 (TTS) 짧게 ----
    private static AudioSource tts;

    /// <summary>Assets/Audio/TTS의 음성을 from초부터 seconds초만 튼다.</summary>
    private static void Say(string clipName, float from, float seconds)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/TTS/{clipName}.wav");
        if (clip == null) return;
        if (tts == null)
        {
            tts = new GameObject("DemoTTS").AddComponent<AudioSource>();
            tts.playOnAwake = false;
            tts.spatialBlend = 0f;
        }
        tts.Stop();
        tts.clip = clip;
        tts.time = Mathf.Clamp(from, 0f, clip.length - 0.05f);
        tts.volume = 1f;
        tts.Play();
        sayStopAt = Time.unscaledTime + seconds;
    }

    private static float sayStopAt = -1f;

    private static void UpdateSay()
    {
        if (tts == null || !tts.isPlaying || sayStopAt < 0f) return;
        // (끝에서 살짝 줄여 끊김 없이)
        float left = sayStopAt - Time.unscaledTime;
        tts.volume = Mathf.Clamp01(left / 0.3f);
        if (left <= 0f) { tts.Stop(); sayStopAt = -1f; }
    }

    private static ExamBriefing Briefing => UnityEngine.Object.FindFirstObjectByType<ExamBriefing>();

    /// <summary>시작 안내 음성의 다음 자막으로 넘긴다 (한 번에 하나).</summary>
    private static void SkipBriefingLine()
    {
        ExamBriefing b = Briefing;
        if (b != null && ExamBriefing.IsPlaying) SetField(b, "skip", true);
    }

    private static void PlanAnnouncerOn(bool on)
    {
        PlanAnnouncer pa = UnityEngine.Object.FindFirstObjectByType<PlanAnnouncer>();
        if (pa != null) pa.enabled = on;
        AudioSource src = pa != null ? pa.GetComponent<AudioSource>() : null;
        if (src != null && !on) src.Stop();
    }

    private static List<PlanStepCard> PlanCardsInOrder()
    {
        var list = new List<PlanStepCard>(UnityEngine.Object.FindObjectsByType<PlanStepCard>(FindObjectsSortMode.None));
        list.RemoveAll(c => c == null || c.SlotIndex >= 0);
        list.Sort((a, b) => a.Phase != b.Phase ? a.Phase.CompareTo(b.Phase) : string.CompareOrdinal(a.name, b.name));
        return list;
    }

    // ---- 재료 나누기 ----
    private static readonly string[] TangIngredients = { "돼지등심", "달걀", "당근", "오이", "양파", "목이버섯", "완두콩", "대파" };
    private static readonly string[] MapoIngredients = { "두부", "돼지고기", "마늘", "생강", "고추", "대파" };

    /// <summary>이름이 같은 재료(대파 두 개)는 아직 안 옮긴 것부터.</summary>
    private static IngredientItem IngNotIn(string key, MovableBowl notIn1, MovableBowl notIn2)
    {
        foreach (IngredientItem i in UnityEngine.Object.FindObjectsByType<IngredientItem>(FindObjectsSortMode.None))
            if (i.IngredientName == key && (object)i.CurrentContainer != notIn1 && (object)i.CurrentContainer != notIn2) return i;
        return null;
    }

    private static Queue<Action> sortQueue;
    private static int skipsDone;

    private static void BuildSortQueue()
    {
        sortQueue = new Queue<Action>();
        MovableBowl tangPlate = Bowl("plateSize2"), mapoPlate = Bowl("plateSize2_");
        // 넓은 접시 두 개를 꺼내 조리대에 (왼쪽 탕수육, 오른쪽 마파두부)
        sortQueue.Enqueue(() => { Take(tangPlate); PutDown(-3.3f, -0.2f); });
        sortQueue.Enqueue(() => { Take(mapoPlate); PutDown(-1.6f, 0.9f); });
        int a = 0, b = 0;
        while (a < TangIngredients.Length || b < MapoIngredients.Length)
        {
            if (a < TangIngredients.Length) { string k = TangIngredients[a++]; sortQueue.Enqueue(() => MoveInto(k, tangPlate, mapoPlate)); }
            if (b < MapoIngredients.Length) { string k = MapoIngredients[b++]; sortQueue.Enqueue(() => MoveInto(k, mapoPlate, tangPlate)); }
        }
    }

    private static void MoveInto(string key, MovableBowl plate, MovableBowl other)
    {
        IngredientItem ing = IngNotIn(key, plate, other);
        if (ing == null || plate == null) return;
        Take(ing);
        ((IClickable)plate).OnClicked();
        if ((object)Hand.Current == ing) PutDown(plate.transform.position.x, plate.transform.position.y);
    }

    // ---- 전체 장면 ----
    private static List<Scene> BuildFull()
    {
        var list = new List<Scene>();
        void Add(string title, string desc, float d, float fast, Action begin, Action<float> tick = null) =>
            list.Add(new Scene { title = title, desc = desc, d3 = d, d1 = d, teaserSpeed = fast, begin = begin, tick = tick });

        Add(null, null, 4f, 1.4f, () =>
        {
            UnityEngine.Object.FindFirstObjectByType<CookingPlanManager>()?.Show();
            PlanAnnouncerOn(false);
            Cam(0f, 0.6f, 6.8f, 0.01f);
            DemoCaption.Card("기능사 실기\n시뮬레이션", "중식조리기능사 · 탕수육 + 마파두부를 실제 시험처럼 함께");
        });

        float lastCard = 0f;
        Add("① 순서 정리 — 감독관 안내(TTS) 동안 머릿속 순서를 카드로", "왜? 유의사항 · 요구사항을 듣는 동안 수험자는 머릿속으로 작업 순서를 정리합니다. 그 마인드맵 같은 과정을 카드로 보이게 했어요", 8f, 4f, () =>
        {
            lastCard = 0f;
            Say("tts1", 0f, teaser ? 2.5f : 7.6f); // (유의사항 안내 앞부분)
        }, t =>
        {
            float gt = t * spd;
            if (gt - lastCard < 0.3f || gt < 0.6f) return;
            lastCard = gt;
            List<PlanStepCard> left = PlanCardsInOrder();
            if (left.Count > 0) CookingPlanManager.Instance?.PlaceInNextSlot(left[0]);
        });

        Add("② 재료 확인 — 접시 세팅 · 요리별로 나누기", "왜? 재료 안내(TTS) 중엔 화구 · 개수대 · 칼은 만지지 않고, 선반에서 넓은 접시 두 개를 꺼내 탕수육 · 마파두부 재료를 나눠요 (실제로는 수량 · 이상 유무 확인)", 11f, 4f, () =>
        {
            CookingPlanManager plan = CookingPlanManager.Instance;
            plan?.Hide();
            typeof(CookingPlanManager).GetMethod("OnStartClicked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(plan, null);
            Cam(-2.2f, 0.4f, 3.2f, 1.2f);
            BuildSortQueue();
            lastCard = 0f;
            skipsDone = 0;
        }, t =>
        {
            float gt = t * spd;
            // 안내 음성: 처음 몇 초만 듣고 한 줄씩 넘겨 "문제없다면 시험 시작하겠습니다"로
            if (skipsDone < 4 && gt > 6.0f + skipsDone * 0.5f) { SkipBriefingLine(); skipsDone++; }
            if (gt < 0.6f || gt - lastCard < 0.42f || sortQueue == null || sortQueue.Count == 0) return;
            lastCard = gt;
            try { sortQueue.Dequeue()(); } catch (Exception e) { Debug.LogWarning("[DemoFilm] 나누기: " + e.Message); }
        });

        Add("③ 손 씻기 — 가장 먼저", "왜? 손을 안 씻고 재료를 만지면 위생 감점. 처음과 마지막 정리 때는 전체 과정으로, 중간에는 간단히 씻어요", 8f, 2f, () =>
        {
            while (sortQueue != null && sortQueue.Count > 0) { try { sortQueue.Dequeue()(); } catch { } }
            EndBriefing();
            PutDown(-1.0f, -0.6f);
            Cam(-5.45f, 0.35f, 1.9f, 1.0f);
            WaterTap tap = UnityEngine.Object.FindFirstObjectByType<WaterTap>();
            if (tap != null && !tap.IsOn) tap.ForceActivate();
            ((IClickable)UnityEngine.Object.FindFirstObjectByType<WashBasin>())?.OnClicked();
            TS(2.0f);
        });

        BuildFullCooking(list);
        return list;
    }

    /// <summary>실격 장면 짧은 클립: 데칠 때 쓴 젖은 체망을 뜨거운 기름에 → 기름 튐 · 실격 (발표자료용).</summary>
    public static void RunSplashClip(bool record, string fileName)
    {
        if (!Application.isPlaying) return;
        shortCut = false; teaser = false; spd = 1f; splashed = false;
        DemoCaption.PerFrame = null;
        scenes = new List<Scene>
        {
            new Scene
            {
                title = "실격 — 데칠 때 쓴 젖은 체망을 뜨거운 기름에", desc = "물이 순간 끓으며 기름이 크게 튀어요. 화상 위험 → 안전사고로 실격 처리됩니다",
                d3 = 9f, d1 = 9f,
                begin = () =>
                {
                    UnityEngine.Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
                    Cam(5.45f, 0.2f, 1.9f, 0.01f);
                    OnBurner(Wok, StoveBurner.FlameLevel.Medium);
                    SetField(Wok, "oilHeat", 185f);
                    Strainer s = SmallStrainer;
                    Take(s);
                    s?.MarkWet();
                },
                tick = t =>
                {
                    if (t > 2.5f && !splashed) { splashed = true; SmallStrainer?.ScoopFrom(Wok); }
                },
            },
        };
        index = -1;
        Running = true;
        foreach (ContextControlHint h in UnityEngine.Object.FindObjectsByType<ContextControlHint>(FindObjectsSortMode.None)) h.enabled = false;
        if (record) StartRecording(fileName, false);
        Next();
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    /// <summary>플레이 모드에서 전체 과정 영상 시작. teaserMode면 장면마다 빨리 감아 1분 티저로.</summary>
    public static void RunFull(bool record, string fileName, bool teaserMode = false, bool small = false)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[DemoFilm] 플레이 모드에서 실행하세요"); return; }
        shortCut = false;
        teaser = teaserMode;
        spd = 1f;
        stirred = 0;
        splashed = shelved = dipped = fried = false;
        stirPan = null;
        DemoCaption.PerFrame = UpdateSay;
        scenes = BuildFull();
        index = -1;
        Running = true;
        foreach (ContextControlHint h in UnityEngine.Object.FindObjectsByType<ContextControlHint>(FindObjectsSortMode.None)) h.enabled = false;
        if (record) StartRecording(fileName, small);
        Next();
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }
}
