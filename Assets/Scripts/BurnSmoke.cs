using UnityEngine;

/// <summary>
/// 조리 연기: 팬 · 웍 · 냄비 위로 피어오르는 연기 (파티클).
/// - 흰 연기(김): 소스 · 물이 끓을 때, 뜨거운 팬에서 재료를 볶을 때, 튀길 때 -
///   정상적인 조리 과정에서 나는 것.
/// - 회색 연기: 그릇 속 재료가 모두 다 익음 (익힘 시간이 다 됨 - 이제 꺼낼 때).
/// - 검은 연기: 재료가 그을리거나(FoodPiece.Char) 소스가 타기 시작하면(PanSauce.Burn)
///   검게, 다 타면 더 많이 · 크게 - 검은 연기 = 타고 있다는 신호.
/// 타는 쪽(PanSauce, FoodPieceGroup)이 매 프레임 Emit(세기)를 부르고, 흰 연기는
/// 그릇 상태를 보고 여기서 정한다. 조리 그릇에는 시작할 때 저절로 붙는다.
/// 재질은 Resources/VFX/BurnSmoke (Vefects 연기 텍스처).
/// </summary>
[RequireComponent(typeof(MovableBowl))]
public class BurnSmoke : MonoBehaviour
{
    [Tooltip("세기 1일 때 1초에 뿜는 연기 수.")]
    [SerializeField] private float maxRate = 14f;
    [Tooltip("탄 정도(0..1)가 이만큼부터 연기가 회색으로 짙어지기 시작.")]
    [SerializeField] private float greyFrom = 0.12f;
    [Tooltip("탄 정도가 이만큼이면 완전히 검은 연기.")]
    [SerializeField] private float blackAt = 0.45f;
    [Tooltip("흰 연기(김: 조리 중) / 회색 연기(다 익음) / 검은 연기(탐) 색.")]
    [SerializeField] private Color whiteSmoke = new Color(0.97f, 0.97f, 0.97f, 0.55f),
                                   greySmoke = new Color(0.58f, 0.57f, 0.56f, 0.65f),
                                   blackSmoke = new Color(0.16f, 0.15f, 0.14f, 0.8f);
    [Tooltip("다 탔을 때 연기 양 배수 (타기 시작할 때는 1).")]
    [SerializeField] private float burntRateBoost = 2.5f;

    private MovableBowl bowl;
    private ParticleSystem ps;
    private float wanted, shown;
    private float shownBurn;
    private readonly System.Collections.Generic.List<FoodPieceGroup> groups = new System.Collections.Generic.List<FoodPieceGroup>(); // 재료 묶음 찾기용 재사용 리스트

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToCookingVessels()
    {
        foreach (MovableBowl b in FindObjectsByType<MovableBowl>(FindObjectsSortMode.None))
            if (b.IsCookingVessel && b.GetComponent<BurnSmoke>() == null) b.gameObject.AddComponent<BurnSmoke>();
    }

    public static BurnSmoke For(MovableBowl bowl)
    {
        if (bowl == null) return null;
        BurnSmoke s = bowl.GetComponent<BurnSmoke>();
        return s != null ? s : bowl.gameObject.AddComponent<BurnSmoke>();
    }

    /// <summary>이번 프레임 타는 세기 (0..1).</summary>
    public void Emit(float intensity) => wanted = Mathf.Max(wanted, Mathf.Clamp01(intensity));

    void Awake() => bowl = GetComponent<MovableBowl>();

    void LateUpdate()
    {
        float burn = BurnLevel();
        float cooking = CookingSmoke();
        float heavy = Mathf.InverseLerp(greyFrom, blackAt, burn);                  // 타기 시작 0 → 다 탐 1
        // 탄 정도(burn)는 한 번 타면 남는 값이라, 화구 위에 있는 동안만 연기를 낸다.
        // 이 조건이 없으면 다 쓰고 선반에 내려둔 냄비에서 계속 검은 연기가 났다.
        bool onHeat = bowl.CurrentBurner != null;                                  // 아직 화구 위인가 (불만 꺼도 연기는 남는다)
        float burning = onHeat && burn >= greyFrom ? Mathf.Lerp(0.4f, 1f, heavy) : 0f; // 타는 동안은 불을 줄여도 연기가 남
        // 화구에서 내리면(접시에 옮기면) 연기도 그친다 - 타는 조각이 Emit 을 계속 불러
        // 접시에 옮긴 뒤에도 검은 연기가 쏟아졌다 (burning 만 막혀 있었다).
        shown = Mathf.Lerp(shown, onHeat ? Mathf.Max(wanted, cooking, burning) : 0f, Mathf.Clamp01(Time.deltaTime * 4f));
        wanted = 0f;
        // 연기 단계: 흰 김(조리 중) → 회색(거의 다 됨) → 타기 시작하면 회색에서 검정으로
        // 서서히 → 많이 타면 진한 검정(실격). 예전에는 조금만 그을려도 바로 0.8 = 새까만
        // 연기여서, 재료를 넣자마자 검은 연기가 났다.
        float tone = burn >= greyFrom ? Mathf.Lerp(0.45f, 1f, heavy) // 타기 시작 회색 → 많이 타면 검정
                   : CookedThrough() ? 0.45f                         // 거의 다 되면 회색
                   : 0f;                                             // 조리 중은 흰 김
        if (SpeedMode.On) tone = Mathf.Min(tone, 0.45f);             // 빠른 모드: 회색까지만
        // (색은 천천히 - 흰 연기가 한 번에 검게 바뀌지 않게)
        shownBurn = Mathf.Lerp(shownBurn, tone, Mathf.Clamp01(Time.deltaTime * 1.5f));
        if (shown < 0.02f && ps == null) return;
        if (ps == null) Build();

        var main = ps.main;
        main.startColor = shownBurn < 0.45f
            ? Color.Lerp(whiteSmoke, greySmoke, shownBurn / 0.45f)                // 흰 → 회색
            : Color.Lerp(greySmoke, blackSmoke, (shownBurn - 0.45f) / 0.35f);     // 회색 → 검은색
        main.startSizeMultiplier = Mathf.Lerp(1f, 1.5f, heavy);                   // 탈수록 연기 덩이도 큼

        // 그릇 안 바닥 위, 가장자리 높이쯤에서 위로
        MovableBowl.Interior inside = bowl.GetInterior();
        ps.transform.position = inside.floorCenter + inside.up * inside.rim * 0.8f + Vector3.back * 0.2f;
        var shape = ps.shape;
        shape.radius = inside.radius * 0.6f;
        var em = ps.emission;
        em.rateOverTime = shown * maxRate * Mathf.Lerp(1f, burntRateBoost, heavy); // 탈수록 연기가 많이
    }

    /// <summary>그릇 속 익히는 재료가 모두 다 익었나 (익힘 시간이 다 됨).</summary>
    private bool CookedThrough()
    {
        bool any = false;                                                  // 익히는 재료가 하나라도 있나
        bowl.GetComponentsInChildren(groups);                              // 재사용 리스트에 담기
        foreach (FoodPieceGroup g in groups)                               // 재료 묶음마다
        {
            CookableItem ck = g.GetComponent<CookableItem>();              // 익힘 정보
            if (ck == null || g.Pieces.Count == 0) continue;               // 익히지 않는 재료는 빼고
            any = true;
            if (g.MinCook < ck.TimeToCooked) return false;                 // 덜 익은 게 하나라도 있으면 아직
        }
        return any;                                                        // 모두 다 익음
    }

    /// <summary>얼마나 탔나 (0..1): 소스의 탐, 그릇 속 재료 조각 중 가장 그을린 것.</summary>
    private float BurnLevel()
    {
        float burn = PanSauce.Of(bowl, false) is PanSauce s && s.Amount > 0.01f ? s.Burn : 0f;
        bowl.GetComponentsInChildren(groups); // 재사용 리스트에 담기 (매 프레임 새 배열 안 만듦)
        foreach (FoodPieceGroup g in groups) // 그릇 속 재료 묶음마다
            foreach (FoodPiece p in g.Pieces)
                if (p != null && p.Char > burn) burn = p.Char;
        float over = Overheat(); // 팬 자체가 너무 달궈짐
        if (over > 0f) burn = Mathf.Max(burn, Mathf.Lerp(greyFrom, 1f, over)); // 달궈질수록 회색 → 진한 검은 연기
        return burn;
    }

    [Tooltip("물기 없는 팬(빈 팬 · 기름뿐)이 이 온도(°C)를 넘으면 팬 자체에서 연기가 나기 시작.")]
    [SerializeField] private float overheatFrom = 250f;
    [Tooltip("이 온도(°C)면 팬에서 진한 검은 연기.")]
    [SerializeField] private float overheatBlackAt = 310f;

    /// <summary>팬 자체가 너무 달궈진 정도 (0..1) - 물기 없이 센불에 계속 두면 내용물이 없어도 검은 연기.
    /// 물 · 물기 소스가 있으면 100°C에 묶여 연기가 안 나고, 튀김 기름은 따로 다룬다.</summary>
    private float Overheat()
    {
        if (SpeedMode.On || bowl.HasFryingOil || bowl.WaterAmount > 0f) return 0f; // 빠른 모드 · 튀김 · 물 냄비는 제외
        if (!(VesselHeat.Of(bowl) is VesselHeat vh)) return 0f; // 팬 온도가 없는 그릇
        if (PanSauce.Of(bowl, false) is PanSauce s && s.Amount > 0.1f && s.IsWatery) return 0f; // 물기 소스가 끓는 중
        return Mathf.InverseLerp(overheatFrom, overheatBlackAt, vh.PanTemp); // 연기점 넘은 만큼
    }

    /// <summary>정상 조리의 흰 연기(김) 세기 (0..1): 끓는 소스 · 물, 뜨거운 팬의 재료, 튀김.</summary>
    private float CookingSmoke()
    {
        if (bowl.CurrentBurner == null || !bowl.CurrentBurner.IsOn) return 0f;
        float smoke = 0f;
        // 냄비 물이 끓음
        if (bowl.IsBoiling) smoke = Mathf.Max(smoke, 0.3f);
        // 물기 소스가 끓음
        if (PanSauce.Of(bowl, false) is PanSauce s && s.Amount > 0.5f && s.IsWatery && s.Temperature >= 95f)
            smoke = Mathf.Max(smoke, 0.35f);
        // 튀김: 뜨거운 기름에 재료
        if (bowl.HasFryingOil && bowl.IsOilHot && bowl.HasIngredients) smoke = Mathf.Max(smoke, 0.3f);
        // 볶기: 뜨거운 팬 바닥에 재료 (팬이 뜨거울수록)
        if (!bowl.HasFryingOil && bowl.HasIngredients && VesselHeat.Of(bowl) is VesselHeat vh)
            smoke = Mathf.Max(smoke, Mathf.InverseLerp(140f, 230f, vh.PanTemp) * 0.45f);
        return smoke;
    }

    private void Build()
    {
        var go = new GameObject("BurnSmoke");
        // (그릇을 따라다니지 않고 월드에 남는 연기 - 그릇 크기 배율도 안 받게)
        ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.32f, 0.3f, 0.29f, 0.7f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.05f;
        main.maxParticles = 120;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.rotation = new Vector3(-90f, 0f, 0f); // (위로)

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        // (색은 startColor(흰 김 ~ 검은 연기)가 정한다 - 여기선 퍼지며 조금 옅어지기만)
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.92f, 0.92f, 0.92f), 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.75f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.8f)));

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 0.6f;

        var r = go.GetComponent<ParticleSystemRenderer>();
        Material m = Resources.Load<Material>("VFX/BurnSmoke");
        if (m == null) { m = new Material(Shader.Find("Sprites/Default")); m.color = new Color(0.4f, 0.4f, 0.4f, 0.5f); }
        r.sharedMaterial = m;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var em = ps.emission;
        em.rateOverTime = 0f;
        ps.Play();
    }

    void OnDestroy()
    {
        if (ps != null) Destroy(ps.gameObject);
    }
}
