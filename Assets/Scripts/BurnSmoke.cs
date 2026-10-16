using UnityEngine;

/// <summary>
/// 탄 연기: 팬·웍 속 소스나 재료가 타고 있을 때 그릇 위로 피어오르는 회색 연기
/// (파티클). 타는 쪽(PanSauce, FoodPieceGroup)이 매 프레임 Emit(세기)를 부르고,
/// 그 프레임의 가장 큰 세기만큼 뿜는다 - 안 부르면 곧 멎는다. 처음 쓸 때 그릇에
/// 저절로 붙는다. 재질은 Resources/VFX/BurnSmoke (Vefects 연기 텍스처).
/// </summary>
[RequireComponent(typeof(MovableBowl))]
public class BurnSmoke : MonoBehaviour
{
    [Tooltip("세기 1일 때 1초에 뿜는 연기 수.")]
    [SerializeField] private float maxRate = 14f;

    private MovableBowl bowl;
    private ParticleSystem ps;
    private float wanted, shown;

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
        shown = Mathf.Lerp(shown, wanted, Mathf.Clamp01(Time.deltaTime * 4f));
        wanted = 0f;
        if (shown < 0.02f && ps == null) return;
        if (ps == null) Build();

        // 그릇 안 바닥 위, 가장자리 높이쯤에서 위로
        MovableBowl.Interior inside = bowl.GetInterior();
        ps.transform.position = inside.floorCenter + inside.up * inside.rim * 0.8f + Vector3.back * 0.2f;
        var shape = ps.shape;
        shape.radius = inside.radius * 0.6f;
        var em = ps.emission;
        em.rateOverTime = shown * maxRate;
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
        grad.SetKeys(new[] { new GradientColorKey(new Color(0.3f, 0.29f, 0.28f), 0f), new GradientColorKey(new Color(0.55f, 0.55f, 0.55f), 1f) },
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
