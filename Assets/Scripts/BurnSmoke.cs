using UnityEngine;

/// <summary>
/// 조리 연기: 팬 · 웍 · 냄비 위로 피어오르는 연기 (파티클).
/// - 흰 연기(김): 소스 · 물이 끓을 때, 뜨거운 팬에서 재료를 볶을 때, 튀길 때 -
///   정상적인 조리 과정에서 나는 것.
/// - 회색 → 검은 연기: 재료가 실제로 그을리거나(FoodPiece.Char) 소스가 타기
///   시작하면(PanSauce.Burn) 탄 정도만큼 짙어진다 - 검은 연기 = 타고 있다는 신호.
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
    [Tooltip("흰 연기(김) / 검은 연기 색.")]
    [SerializeField] private Color whiteSmoke = new Color(0.97f, 0.97f, 0.97f, 0.55f),
                                   blackSmoke = new Color(0.16f, 0.15f, 0.14f, 0.8f);

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
        shown = Mathf.Lerp(shown, Mathf.Max(wanted, cooking), Mathf.Clamp01(Time.deltaTime * 4f));
        wanted = 0f;
        // (색은 천천히 - 흰 연기가 한 번에 검게 바뀌지 않게)
        shownBurn = Mathf.Lerp(shownBurn, burn, Mathf.Clamp01(Time.deltaTime * 1.5f));
        if (shown < 0.02f && ps == null) return;
        if (ps == null) Build();

        var main = ps.main;
        main.startColor = Color.Lerp(whiteSmoke, blackSmoke, Mathf.InverseLerp(greyFrom, blackAt, shownBurn));

        // 그릇 안 바닥 위, 가장자리 높이쯤에서 위로
        MovableBowl.Interior inside = bowl.GetInterior();
        ps.transform.position = inside.floorCenter + inside.up * inside.rim * 0.8f + Vector3.back * 0.2f;
        var shape = ps.shape;
        shape.radius = inside.radius * 0.6f;
        var em = ps.emission;
        em.rateOverTime = shown * maxRate;
    }

    /// <summary>얼마나 탔나 (0..1): 소스의 탐, 그릇 속 재료 조각 중 가장 그을린 것.</summary>
    private float BurnLevel()
    {
        float burn = PanSauce.Of(bowl, false) is PanSauce s && s.Amount > 0.01f ? s.Burn : 0f;
        bowl.GetComponentsInChildren(groups); // 재사용 리스트에 담기 (매 프레임 새 배열 안 만듦)
        foreach (FoodPieceGroup g in groups) // 그릇 속 재료 묶음마다
            foreach (FoodPiece p in g.Pieces)
                if (p != null && p.Char > burn) burn = p.Char;
        return burn;
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
