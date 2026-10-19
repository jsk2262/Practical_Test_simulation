using UnityEngine;

/// <summary>
/// 뜨거운 기름에 물기가 들어갔을 때: "치지직 - 파바박" 하는 큰 소리와 함께 기름이
/// 사방으로 튄다 (파티클). 소리는 파일 없이 코드로 만든 잡음(지글거림 + 터지는
/// 소리). Play(그릇)를 부르면 한 번 터지고 몇 초 뒤 저절로 사라진다.
/// </summary>
public class OilSplatter : MonoBehaviour
{
    private static AudioClip clip;

    /// <summary>그 그릇의 기름 표면에서 한 번 크게 튄다.</summary>
    public static void Play(MovableBowl bowl)
    {
        if (bowl == null) return;
        MovableBowl.Interior inside = bowl.GetInterior();
        float h = bowl.OilSurfaceHeight > 0f ? bowl.OilSurfaceHeight : inside.rim * 0.5f;
        Vector3 at = inside.floorCenter + inside.up * h + Vector3.back * 0.15f;

        var go = new GameObject("OilSplatter");
        go.transform.position = at;
        go.AddComponent<OilSplatter>().Build(inside.radius);
        Debug.Log($"[기름 튐] {bowl.name}: 물기 있는 것이 뜨거운 기름에 들어가 기름이 튐");
    }

    private void Build(float radius)
    {
        // ---- 튀는 기름 방울 ----
        var ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 1.2f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.35f, 0.95f), new Color(1f, 0.95f, 0.7f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1.6f;
        main.maxParticles = 400;

        var em = ps.emission;
        em.rateOverTime = 0f;
        // (처음 크게 한 번, 이어서 몇 번 더 - 물기가 다 터질 때까지)
        em.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 140),
            new ParticleSystem.Burst(0.15f, 70),
            new ParticleSystem.Burst(0.35f, 60),
            new ParticleSystem.Burst(0.6f, 40),
            new ParticleSystem.Burst(0.9f, 25),
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 55f;
        shape.radius = Mathf.Max(0.05f, radius * 0.5f);
        shape.rotation = new Vector3(-90f, 0f, 0f); // (위로 퍼지게)

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.8f, 0.3f), 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var r = GetComponent<ParticleSystemRenderer>();
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var m = new Material(sh != null ? sh : Shader.Find("Sprites/Default"));
        m.color = new Color(1f, 0.9f, 0.5f, 1f);
        r.sharedMaterial = m;
        r.renderMode = ParticleSystemRenderMode.Stretch; // (날아가는 방향으로 길쭉한 방울)
        r.velocityScale = 0.015f;
        r.lengthScale = 1f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();

        // ---- 큰 소리 ----
        var audio = gameObject.AddComponent<AudioSource>();
        audio.clip = Clip();
        audio.volume = 1f;
        audio.spatialBlend = 0f; // (화면 어디서든 크게)
        audio.Play();

        Destroy(gameObject, 3f);
        Destroy(m, 3f);
    }

    /// <summary>지글거리는 잡음 위에 '팍팍' 터지는 소리들 - 1.6초.</summary>
    private static AudioClip Clip()
    {
        if (clip != null) return clip;
        const int rate = 44100;
        int n = (int)(rate * 1.6f);
        var data = new float[n];
        var rng = new System.Random(7);
        float lp = 0f;
        // 터지는 순간들 (앞쪽에 몰리게)
        var pops = new float[40];
        for (int i = 0; i < pops.Length; i++) pops[i] = (float)System.Math.Pow(rng.NextDouble(), 1.8) * 1.4f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            // 지글거림: 고음 위주 잡음, 처음 확 커졌다가 서서히 잦아듦
            lp += (white - lp) * 0.35f;
            float sizzle = (white - lp) * Mathf.Exp(-t * 1.6f) * Mathf.Clamp01(t * 60f);
            // 팍! 하는 짧은 터짐들
            float pop = 0f;
            foreach (float p in pops)
            {
                float d = t - p;
                if (d >= 0f && d < 0.03f) pop += white * Mathf.Exp(-d * 180f) * 1.4f;
            }
            data[i] = Mathf.Clamp((sizzle * 0.9f + pop) * 0.9f, -1f, 1f);
        }
        clip = AudioClip.Create("OilSplatter", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
