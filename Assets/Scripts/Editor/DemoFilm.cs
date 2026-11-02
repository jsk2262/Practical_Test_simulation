using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

/// <summary>
/// 시연 영상 녹화: 플레이 모드에서 장면을 차례로 코드로 진행하며(집기·썰기·데치기·
/// 튀기기·볶기·담기·정리·결과) Unity Recorder로 MP4를 남긴다. 장면마다 3분용·1분용
/// 길이가 있고, 1분용 길이가 0인 장면은 1분 영상에서 빠진다. 카메라는 장면마다
/// 정한 곳으로 부드럽게 옮긴다. 자막은 DemoCaption.
/// 사용: 플레이 모드에서 DemoFilm.Run(true/false, 녹화 여부).
/// </summary>
public static partial class DemoFilm
{
    private class Scene
    {
        public string title, desc;
        public float d3, d1;
        public Action begin;
        public Action<float> tick; // (장면 안에서 지난 초)
        /// <summary>있으면: 정한 길이가 지나도 이 일이 끝날 때까지 (길이의 3배까지) 기다린다.</summary>
        public Func<bool> until;
        /// <summary>1분 티저에서 이 장면을 몇 배 빨리 돌릴지 (1 = 그대로).</summary>
        public float teaserSpeed = 1f;
    }

    /// <summary>지금 장면의 재생 배속 - 게임 시간 · 기다림 · 카메라 이동이 모두 이만큼 빨라진다.</summary>
    private static float spd = 1f;
    /// <summary>1분 티저로 찍는 중 (장면마다 teaserSpeed로 빨리 감기).</summary>
    private static bool teaser;

    /// <summary>게임 속도 = 장면에서 정한 값 × 지금 배속 (유니티 한계 100배).</summary>
    private static void TS(float x) => Time.timeScale = Mathf.Min(100f, x * spd);

    private static List<Scene> scenes;
    private static int index;
    private static float sceneStart;
    private static bool shortCut;
    private static RecorderController recorder;
    public static bool Running { get; private set; }
    /// <summary>지금 장면 번호와 제목 (확인용).</summary>
    public static string Now => Running && index < scenes.Count ? $"{index}: {scenes[index].title}" : "-";

    // ---- 카메라 ----
    private static Vector3 camFrom, camTo;
    private static float sizeFrom, sizeTo, camT0, camDur;

    private static void Cam(float x, float y, float size, float seconds = 1.2f)
    {
        Camera c = Camera.main;
        if (c == null) return;
        if (KitchenCamera.Instance != null) KitchenCamera.Instance.enabled = false;
        camFrom = c.transform.position;
        sizeFrom = c.orthographicSize;
        camTo = new Vector3(x, y, -13.8f);
        sizeTo = size;
        camT0 = Time.unscaledTime;
        camDur = Mathf.Max(0.01f, seconds / spd);
    }

    private static void UpdateCam()
    {
        Camera c = Camera.main;
        if (c == null || camDur <= 0f) return;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.unscaledTime - camT0) / camDur));
        c.transform.position = Vector3.Lerp(camFrom, camTo, t);
        c.orthographicSize = Mathf.Lerp(sizeFrom, sizeTo, t);
        // 화면 고정 UI(정리 탭 · 체크리스트 · 경고 자막 · 타이머)도 카메라를 따라오게 (KitchenCamera는 꺼 둔 상태라 직접 부름)
        if (KitchenCamera.Instance != null) placeHuds.Invoke(KitchenCamera.Instance, null); // 화면 고정 UI 다시 배치
    }

    private static readonly System.Reflection.MethodInfo placeHuds = typeof(KitchenCamera).GetMethod("PlaceHuds",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); // 화면 고정 UI 배치 함수

    // ---- 도우미 ----
    private static HandCarry Hand => HandCarry.Instance;
    /// <summary>이름으로 그릇 찾기 - GameObject.Find는 같은 이름의 다른 오브젝트(계량컵 모델 등)를 잡을 수 있어 그릇 중에서 찾는다.</summary>
    private static MovableBowl Bowl(string n)
    {
        foreach (MovableBowl b in UnityEngine.Object.FindObjectsByType<MovableBowl>(FindObjectsSortMode.None))
            if (b.name == n) return b;
        return null;
    }
    private static StoveBurner BurnerR => GameObject.Find("Burner_R")?.GetComponent<StoveBurner>();

    private static IngredientItem Ing(string key)
    {
        foreach (IngredientItem i in UnityEngine.Object.FindObjectsByType<IngredientItem>(FindObjectsSortMode.None))
            if (i.IngredientName == key || i.name.Contains(key)) return i;
        return null;
    }

    private static ToolItem Tool(ToolKind kind)
    {
        foreach (ToolItem t in UnityEngine.Object.FindObjectsByType<ToolItem>(FindObjectsSortMode.None))
            if (t.Kind == kind && !(t is FryingChopsticks) && !(t is Strainer)) return t;
        return null;
    }

    private static void PutDown(float x, float y)
    {
        if (Hand != null && Hand.Current != null) Hand.PlaceFreeAt(new Vector3(x, y, 0f));
    }

    private static void Take(Carryable c)
    {
        if (c == null || Hand == null) return;
        if ((object)Hand.Current == c) return;
        if (Hand.Current != null) Hand.PlaceFreeAt(Hand.Current.transform.position);
        Hand.PickUp(c);
    }

    private static void SetField(object o, string field, object value) =>
        o?.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(o, value);

    private static void Burner(bool on)
    {
        StoveBurner b = BurnerR;
        if (b != null && b.IsOn != on) b.SetOn(on);
    }

    // ---- 장면들 ----
    private static List<Scene> Build()
    {
        var list = new List<Scene>();
        void Add(string title, string desc, float d3, float d1, Action begin, Action<float> tick = null) =>
            list.Add(new Scene { title = title, desc = desc, d3 = d3, d1 = d1, begin = begin, tick = tick });

        Add(null, null, 6f, 4f, () =>
        {
            UnityEngine.Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
            Cam(0f, 0.6f, 6.8f, 0.01f);
            DemoCaption.Card("중식조리기능사 실기 시뮬레이터", "탕수육 · 마파두부 — 실제 시험장처럼 연습하고, 공식 기준으로 채점받기");
        });

        Add("시험장 전체", "실제 시험장 배치 그대로: 도마 · 개수대 · 가스레인지 · 그릇 선반 · 제출대", 7f, 3f, () =>
        {
            Cam(0f, 0.6f, 6.8f, 0.01f);
        }, t => { if (t > 1.5f && camTo.x == 0f) Cam(0.6f, 0.1f, 6.0f, 4.5f); });

        Add("조리 순서 계획", "시작 전에 순서 카드를 배치해 작업 계획을 세웁니다", 8f, 0f, () =>
        {
            UnityEngine.Object.FindFirstObjectByType<CookingPlanManager>()?.Show();
            Cam(0f, 0.6f, 6.8f, 0.8f);
        });

        Add("시험 시작 안내", "감독관 음성 안내와 자막으로 지급재료와 유의사항을 듣습니다", 9f, 0f, () =>
        {
            CookingPlanManager plan = UnityEngine.Object.FindFirstObjectByType<CookingPlanManager>();
            typeof(CookingPlanManager).GetMethod("OnStartClicked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(plan, null);
        });

        Add("손 씻기 (위생)", "시작 전 손 씻기 — 안 씻고 재료를 만지면 위생 감점. 처음과 마지막 정리 때는 전체 과정으로 씻어요", 16f, 5f, () =>
        {
            EndBriefing();
            UnityEngine.Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
            Cam(-5.45f, 0.35f, 1.9f, 1.2f);
            WaterTap tap = UnityEngine.Object.FindFirstObjectByType<WaterTap>();
            if (tap != null && !tap.IsOn) tap.ForceActivate();
            ((IClickable)UnityEngine.Object.FindFirstObjectByType<WashBasin>())?.OnClicked();
            Time.timeScale = shortCut ? 3f : 1.2f;
        });

        Add("두부 썰기 — 1.5cm 주사위", "칼을 쥐고 도마 위에서 실제 칼질 순서대로 썹니다 (요구사항 규격)", 16f, 6f, () =>
        {
            Time.timeScale = shortCut ? 2.5f : 1.5f;
            HandWashSequence seq = UnityEngine.Object.FindFirstObjectByType<HandWashSequence>();
            if (seq != null && seq.IsPlaying) seq.CancelWash();
            WaterTap tap = UnityEngine.Object.FindFirstObjectByType<WaterTap>();
            if (tap != null && tap.IsOn) tap.ForceActivate();
            HandHygiene.Washed();
            Cam(0.0f, -0.55f, 1.7f, 1.2f);
            IngredientItem tofu = Ing("두부");
            Take(tofu);
            CuttingBoardController.Instance?.ForceActivate();
            Take(Tool(ToolKind.Knife));
            HandController.Instance?.GoTo(tofu.transform.position + new Vector3(0f, -0.2f, -0.6f),
                () => tofu.PerformCut(IngredientItem.CutMethod.Cube, IngredientItem.BuildSpec(IngredientItem.CutMethod.Cube, 1.5f)));
        });

        Add("돼지고기 썰기 — 4cm × 1cm", "탕수육 고기는 요구사항대로 길이 4cm, 두께 1cm 막대 모양으로 손질합니다", 12f, 5f, () =>
        {
            Time.timeScale = shortCut ? 9f : 5f;
            IngredientItem tofu = Ing("두부");
            PutDown(-1.6f, -0.5f);
            if (tofu != null) { Take(tofu); PutDown(-2.2f, 0.4f); }
            IngredientItem pork = Ing("돼지등심");
            Take(pork);
            CuttingBoardController.Instance?.ForceActivate();
            Take(Tool(ToolKind.Knife));
            pork?.PerformCut(IngredientItem.CutMethod.Julienne, new CutSpec(1f, 4f, 1f));
        });

        Add("데치기 — 체망은 손에", "끓는 물에 완두콩만 넣고 체망은 손에 듭니다 (체망째 담가 두면 위생 감점 소지)", 11f, 0f, () =>
        {
            Time.timeScale = 1f;
            PutDown(-1.4f, -0.6f);
            Cam(5.45f, -0.25f, 1.9f, 1.2f);
            MovableBowl pot = Bowl("PotSmall") ?? Bowl("FreePotSmall");
            Take(pot);
            BurnerR?.ForceActivate();
            pot?.SetFilledWithWater(true);
            Burner(true);
            SetField(pot, "boilProgress", 400f);
            Strainer st = UnityEngine.Object.FindFirstObjectByType<Strainer>();
            Take(st);
            IngredientItem peas = Ing("완두콩");
            if (st != null && peas != null) st.TakeIn(peas);
        }, t =>
        {
            if (t > 2.5f && !dipped)
            {
                dipped = true;
                MovableBowl pot = Bowl("PotSmall") ?? Bowl("FreePotSmall");
                ((IClickable)pot)?.OnClicked();
            }
        });

        Add("튀김 — 화구는 하나만", "냄비를 내리고 웍을 같은 화구에 (위쪽 화구를 켜면 실격). 고기가 익으면 기름 위로 떠오릅니다", 19f, 7f, () =>
        {
            Time.timeScale = 1f;
            Cam(5.45f, 0.05f, 1.6f, 1.2f);
            Strainer st = UnityEngine.Object.FindFirstObjectByType<Strainer>();
            if (st != null && (object)Hand.Current == st) PutDown(2.2f, 0.9f);
            if (st != null) st.MarkWet();
            MovableBowl pot = Bowl("PotSmall") ?? Bowl("FreePotSmall");
            if (pot != null && pot.CurrentBurner != null) { Take(pot); PutDown(3.2f, -2.4f); }
            MovableBowl wok = Bowl("Wok");
            Take(wok);
            BurnerR?.ForceActivate();
            Burner(true);
            SetField(wok, "oilHeat", 185f);
            Take(Ing("돼지등심"));
        }, t =>
        {
            if (t > 1.5f && !fried)
            {
                fried = true;
                ((IClickable)Bowl("Wok"))?.OnClicked();
                Time.timeScale = shortCut ? 5f : 3f;
            }
        });

        Add("실격 — 젖은 체망을 뜨거운 기름에", "데칠 때 쓴 젖은 체망을 기름에 넣으면 기름이 크게 튀며 안전사고로 실격 처리됩니다", 9f, 7f, () =>
        {
            Time.timeScale = 1f;
            Cam(5.45f, 0.2f, 2.3f, 0.8f);
            Strainer st = UnityEngine.Object.FindFirstObjectByType<Strainer>();
            Take(st);
        }, t =>
        {
            if (t > 1.5f && !splashed)
            {
                splashed = true;
                Strainer st = UnityEngine.Object.FindFirstObjectByType<Strainer>();
                st?.ScoopFrom(Bowl("Wok"));
            }
        });

        Add("마파두부 — 팬에 볶기", "데친 두부를 팬에 넣고 두반장 · 간장으로 볶습니다. 두부가 부서지지 않게!", 17f, 7f, () =>
        {
            Cam(5.45f, 0.05f, 1.6f, 1f);
            PutDown(2.2f, 0.9f);
            MovableBowl wok = Bowl("Wok");
            if (wok != null && wok.CurrentBurner != null) { Take(wok); PutDown(3.0f, -2.9f); }
            MovableBowl pan = Bowl("FryingPan");
            Take(pan);
            BurnerR?.ForceActivate();
            Burner(true);
            IngredientItem tofu = Ing("두부");
            Take(tofu);
            ((IClickable)pan)?.OnClicked();
            pan?.AddSeasoning("두반장");
            pan?.AddSeasoning("두반장");
            pan?.AddSeasoning("간장");
            ToolItem spatula = Tool(ToolKind.Spatula);
            Take(spatula);
        }, t =>
        {
            int want = Mathf.FloorToInt(t / 1.6f);
            if (t > 1f && want < 7 && want >= stirred)
            {
                stirred = want + 1;
                Bowl("FryingPan")?.ForceActivate();
            }
        });

        Add("불 조절 실패 — 타는 연기", "수분 없이 센 불에 오래 두면 소스가 졸아붙어 타고 연기가 납니다 (작품 평가 · 실격 기준)", 8f, 0f, () =>
        {
            Cam(5.45f, 0.35f, 2.0f, 0.8f);
            float t0 = Time.unscaledTime;
            // (연기는 매 프레임 불러야 보여서 게임 쪽 프레임 훅으로)
            DemoCaption.PerFrame = () =>
            {
                MovableBowl pan = Bowl("FryingPan");
                if (pan != null) BurnSmoke.For(pan)?.Emit(Mathf.Clamp01((Time.unscaledTime - t0) / 1.5f));
            };
        });

        Add("완성 접시에 담기", "팬째 들고 완성 접시로 옮겨 담습니다", 8f, 4f, () =>
        {
            DemoCaption.PerFrame = null;
            Cam(4.6f, 0.6f, 2.6f, 1f);
            IngredientItem tofu = Ing("두부");
            CookableItem ck = tofu != null ? tofu.GetComponent<CookableItem>() : null;
            if (ck != null) SetField(ck, "<CurrentStage>k__BackingField", CookableItem.Stage.Done);
            MovableBowl pan = Bowl("FryingPan");
            Take(pan);
            PlatingDestination dest = GameObject.Find("PlatingPlate1")?.GetComponent<PlatingDestination>();
            pan?.GetComponent<FryingPanPlating>()?.PourInto(dest);
        });

        Add("제출대에 올리기", "완성 접시를 제출대에 올리면 제출 — 이후 마무리 정리", 7f, 4f, () =>
        {
            Burner(false);
            PutDown(3.6f, -1.5f);
            Cam(8.4f, -0.55f, 2.4f, 1.2f);
            MovableBowl plate = Bowl("PlatingPlate1");
            Take(plate);
            SubmitShelf.Instance?.ForceActivate();
        });

        Add("마무리 정리 — 설거지 · 선반 진열", "씻은 그릇을 접시 선반(PlateShelf)에 진열합니다", 14f, 0f, () =>
        {
            ExamFlow.SubmitDishes();
            Cam(-4.4f, -1.9f, 3.0f, 1.2f);
            WashBasin sink = UnityEngine.Object.FindFirstObjectByType<WashBasin>();
            MovableBowl b = Bowl("plateSize1");
            Take(b);
            sink?.ForceActivate();
        }, t =>
        {
            if (t > 4f && !shelved)
            {
                shelved = true;
                GameObject.Find("PlateShelf1")?.GetComponent<DishShelfController>()?.ForceActivate();
            }
        });

        Add("최종 평가", "문제지 배점(위생 5 · 조리기술 30 · 작품 15)으로 채점 — 이번 시연은 체망 사고 · 탕수육 미제출로 실격 예시", 19f, 7f, () =>
        {
            PutDown(-2f, -0.5f);
            Cam(0f, 0.6f, 6.8f, 0.6f);
            ExamEvaluation.RunFinal();
        }, t =>
        {
            // 결과를 천천히 내려 보기
            ExamResultView v = UnityEngine.Object.FindFirstObjectByType<ExamResultView>();
            if (v != null && t > 4f) SetField(v, "scroll", new Vector2(0f, (t - 4f) * 70f));
        });

        Add(null, null, 6f, 3f, () =>
        {
            DemoCaption.Card("실제 시험처럼 연습하고,\n기준대로 피드백 받기", "Unity 6 · URP  |  탕수육 · 마파두부  |  위생 · 안전 · 실격 기준 반영");
        });
        return list;
    }

    private static int stirred;
    private static bool splashed, shelved, dipped, fried;

    private static void EndBriefing()
    {
        // 안내가 아직이면 끝까지 넘김 (시계는 안내가 끝나야 시작)
        ExamBriefing b = UnityEngine.Object.FindFirstObjectByType<ExamBriefing>();
        if (b == null) return;
        for (int i = 0; i < 60 && ExamBriefing.IsPlaying; i++) SetField(b, "skip", true);
    }

    // ---- 진행 ----

    /// <summary>플레이 모드에서 시작: oneMinute면 짧은 판, record면 MP4로 녹화.</summary>
    public static void Run(bool oneMinute, bool record, string fileName, bool small = false)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[DemoFilm] 플레이 모드에서 실행하세요"); return; }
        shortCut = oneMinute;
        teaser = false;
        spd = 1f;
        stirred = 0;
        splashed = shelved = dipped = fried = false;
        DemoCaption.PerFrame = null;
        scenes = Build().FindAll(s => (oneMinute ? s.d1 : s.d3) > 0f);
        index = -1;
        Running = true;
        foreach (ContextControlHint h in UnityEngine.Object.FindObjectsByType<ContextControlHint>(FindObjectsSortMode.None)) h.enabled = false;
        if (record) StartRecording(fileName, small);
        Next();
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static float Dur(Scene s) => (shortCut ? s.d1 : s.d3) / spd;

    private static void Next()
    {
        if (Running && recorder != null)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Recordings", "chapters.tsv"),
                (Time.frameCount - filmStartFrame).ToString() + "\t" + (index + 1).ToString() + "\n");
        index++;
        sceneStart = Time.unscaledTime;
        freshScene = true; // (시작 시각은 다음 틱에 다시 잼 - 녹화 첫 프레임처럼 시간이 크게 건너뛰어도 장면이 바로 끝나지 않게)
        if (index >= scenes.Count) { Stop(); return; }
        Scene s = scenes[index];
        spd = teaser ? Mathf.Max(1f, s.teaserSpeed) : 1f;
        if (s.title != null) DemoCaption.Show(s.title, teaser ? "" : s.desc);
        DemoCaption.FastForward(spd > 1.5f ? spd : 0f);
        try { s.begin?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[DemoFilm] 장면 '{s.title}' 시작 오류: {e}"); }
        Debug.Log($"[DemoFilm] 장면 {index + 1}/{scenes.Count}: {s.title ?? "(제목 카드)"} t={Time.unscaledTime:0.0}");
    }

    private static void Tick()
    {
        if (!Running) { EditorApplication.update -= Tick; return; }
        if (!Application.isPlaying) { Stop(); return; }
        if (freshScene) { freshScene = false; sceneStart = Time.unscaledTime; camT0 = Time.unscaledTime; }
        UpdateCam();
        Scene s = scenes[index];
        float t = Time.unscaledTime - sceneStart;
        try { s.tick?.Invoke(t); }
        catch (Exception e) { Debug.LogError($"[DemoFilm] 장면 '{s.title}' 진행 오류: {e}"); s.tick = null; }
        if (t >= Dur(s) && (s.until == null || s.until() || t >= Dur(s) * 8f)) Next();
    }

    public static void Stop()
    {
        Running = false;
        EditorApplication.update -= Tick;
        Time.timeScale = 1f;
        if (recorder != null && recorder.IsRecording())
        {
            recorder.StopRecording();
            Debug.Log("[DemoFilm] 녹화 끝");
        }
        recorder = null;
        DemoCaption.Clear();
    }

    private static int filmStartFrame;
    private static bool freshScene;

    private static void StartRecording(string fileName, bool small)
    {
        var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "Demo";
        movie.Enabled = true;
        movie.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingQuality = small ? CoreEncoderSettings.VideoEncodingQuality.Low : CoreEncoderSettings.VideoEncodingQuality.High,
        };
        // (small: 발표자료에 넣을 작은 판 - 480p · 낮은 화질로 15MB 안쪽)
        movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = small ? 854 : 1920, OutputHeight = small ? 480 : 1080 };
        movie.AudioInputSettings.PreserveAudio = true;
        movie.OutputFile = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Recordings", fileName);
        controllerSettings.AddRecorderSettings(movie);
        controllerSettings.SetRecordModeToManual();
        controllerSettings.FrameRate = 30f;
        RecorderOptions.VerboseMode = false;
        recorder = new RecorderController(controllerSettings);
        recorder.PrepareRecording();
        recorder.StartRecording();
        filmStartFrame = Time.frameCount;
        System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Recordings", "chapters.tsv"), "");
        Debug.Log($"[DemoFilm] 녹화 시작: {movie.OutputFile}.mp4");
    }
}
