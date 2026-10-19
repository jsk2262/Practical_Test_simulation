using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 소스 걸쭉함(PanSauce.Thickness)을 눈에 보이게: 묽은 소스는 팬 바닥색이 비치는
/// 옅은 색으로 얇게 퍼지며, 전분물이 익어 걸쭉해질수록 불투명하고 진한 윤기 있는
/// 소스가 도톰하게 고인다. 수치(점도 · 버무림)는 PanSauce · FoodPieceGroup이 계산하고
/// 여기서는 보이는 모습만 바꾼다. 씬에 따로 둘 필요 없이 시작할 때 저절로 생긴다.
/// </summary>
[DefaultExecutionOrder(100)] // (PanSauce가 웅덩이를 다시 만든 뒤에 덮어쓴다)
public class SauceThicknessLook : MonoBehaviour
{
    [Tooltip("이 걸쭉함부터 변하기 시작.")]
    [SerializeField] private float thinAt = 0.05f;
    [Tooltip("이 걸쭉함이면 다 걸쭉한 모습 (탕수육 · 마파두부 알맞은 농도 ≈ 0.4~0.6).")]
    [SerializeField] private float thickAt = 0.55f;
    [Tooltip("묽을 때 / 걸쭉할 때 소스 색이 드러나는 정도 (나머지는 밑바닥 색).")]
    [SerializeField] private float thinAlpha = 0.45f, thickAlpha = 0.95f;
    [Tooltip("걸쭉할 때 웅덩이 두께 배수.")]
    [SerializeField] private float thickHeight = 3f;
    [Tooltip("걸쭉할 때 색이 진해지는 정도 (채도 배수 추가분 · 어두워짐).")]
    [SerializeField] private float thickSaturate = 1.5f, thickDarken = 0.25f;

    [Tooltip("화면에 그릴 때 소스 색을 이만큼 곱해 진하게 (밝은 조명 · 후처리에 바래지 않게).")]
    [SerializeField] private float displayDarken = 0.7f;
    [Tooltip("묽은 소스로 비쳐 보이는 밑바닥(팬 · 접시) 색.")]
    [SerializeField] private Color underColor = new Color(0.8f, 0.79f, 0.78f);

    private PanSauce[] sauces = new PanSauce[0];
    private float nextScan;
    /// <summary>웅덩이 렌더러마다 바꿔 끼운 재질.</summary>
    private readonly Dictionary<Renderer, Material> own = new Dictionary<Renderer, Material>();
    private readonly List<Renderer> renderers = new List<Renderer>(); // 웅덩이 렌더러 찾기용 재사용 리스트

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindFirstObjectByType<SauceThicknessLook>() == null)
            new GameObject("SauceThicknessLook").AddComponent<SauceThicknessLook>();
    }

    void LateUpdate()
    {
        if (Time.time >= nextScan)
        {
            sauces = FindObjectsByType<PanSauce>(FindObjectsSortMode.None);
            nextScan = Time.time + 0.5f;
            CleanUpOwned();
        }
        foreach (PanSauce s in sauces) if (s != null) Apply(s);
    }

    private void Apply(PanSauce sauce)
    {
        if (sauce.Amount <= 0.01f) return;
        Transform pool = sauce.transform.Find("PanSauce");
        if (pool == null) return;

        // 접시 · 종지(얕은 그릇)에서는 웅덩이가 그릇 안쪽 면보다 아래에 그려져 묻혀 안
        // 보였다 (두반장 + 간장이 하얗게 보이던 것) - 그릇 안쪽 면 위로 올린다
        MovableBowl bowl = sauce.GetComponent<MovableBowl>();
        if (bowl != null && !bowl.IsCookingVessel)
        {
            MovableBowl.Interior inside = bowl.GetInterior();
            bool level = inside.rim > inside.radius * 0.35f; // (깊은 그릇: 수면 높이로 - PanSauce와 같은 기준)
            float minHeight = level ? inside.rim * 0.3f : inside.rim * 0.9f;
            float height = Vector3.Dot(pool.position - inside.floorCenter, inside.up);
            if (height < minHeight) pool.position += inside.up * (minHeight - height);
        }

        if (!sauce.IsWatery) return; // (고추기름 같은 기름 소스는 색 · 투명도 그대로)
        // 걸쭉함: 전분으로 걸쭉해진 정도와, 두반장 같은 장(페이스트) 자체의 되기 중 큰 쪽
        // (장이 주인 소스까지 묽은 물처럼 반투명하게 하면 색이 바래 보였다)
        float k = Mathf.InverseLerp(thinAt, thickAt, Mathf.Max(sauce.Thickness, sauce.Viscosity));
        // 걸쭉해질수록 도톰하게
        // (웅덩이는 월드 크기 그대로 붙어 있다 - 그릇 크기만큼 나눠 둔다)
        Vector3 ls = sauce.transform.lossyScale;
        float unit = 1f / Mathf.Max(Mathf.Abs(ls.x), 1e-4f);
        pool.localScale = new Vector3(unit, unit * Mathf.Lerp(1f, thickHeight, k), unit);

        Color baseColor = sauce.Color;
        Color.RGBToHSV(baseColor, out float h, out float sat, out float v);
        Color deep = Color.HSVToRGB(h, Mathf.Clamp01(sat * (1f + thickSaturate)), v * (1f - thickDarken));
        Color c = Color.Lerp(baseColor, deep, k);
        c.a = Mathf.Lerp(thinAlpha, thickAlpha, k);

        // (이 주방 조명은 아주 밝아서, 조명을 받는 재질은 진한 적갈색도 분홍 · 회색으로
        // 바랬다 - 소스 웅덩이는 조명 없이(Unlit) 소스 색 그대로, 조금 진하게 그린다)
        c = new Color(c.r * displayDarken, c.g * displayDarken, c.b * displayDarken, c.a);
        // (반투명 재질은 화면 합성에서 알파가 색을 바꿔(분홍 · 청록) 쓰지 않는다 - 묽을수록
        // 밑바닥 색에 가깝게 섞어 비쳐 보이는 것처럼)
        c = Color.Lerp(underColor, c, c.a);
        c.a = 1f;

        pool.GetComponentsInChildren(renderers); // 재사용 리스트에 담기 (매 프레임 새 배열 안 만듦)
        foreach (Renderer r in renderers) // 웅덩이의 렌더러마다
        {
            string n = r.transform.parent != null ? r.transform.parent.name : "";
            if (n != "Pool") continue; // (전분 덩어리 · 고춧가루 알갱이는 그대로)
            if (!own.TryGetValue(r, out Material m) || m == null)
            {
                m = MakeUnlitTransparent();
                if (m == null) continue;
                own[r] = m;
                r.sharedMaterial = m;
            }
            m.SetColor("_BaseColor", c);
            m.color = c;
        }
    }

    private Shader unlit;

    private Material MakeUnlitTransparent()
    {
        if (unlit == null) unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit == null) return null;
        return new Material(unlit) { name = "SauceLook" };
    }

    /// <summary>웅덩이가 다시 만들어져 사라진 렌더러의 재질은 치운다.</summary>
    private void CleanUpOwned()
    {
        var gone = new List<Renderer>();
        foreach (var kv in own) if (kv.Key == null) gone.Add(kv.Key);
        foreach (Renderer r in gone) { if (own[r] != null) Destroy(own[r]); own.Remove(r); }
    }
}
