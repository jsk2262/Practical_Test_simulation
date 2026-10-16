using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 튀김 거품: 달군 튀김 기름(MovableBowl.IsOilHot)에 재료 조각이 들어 있으면, 조각
/// 둘레에서 작은 거품이 보글보글 올라와 기름 표면에서 퍼졌다 터진다 - 넣은 직후
/// (수분이 많을 때) 많고, 익을수록 줄어든다. 조각에서 먼 곳에도 드문드문.
/// 보이는 것만 (계산엔 영향 없음). 튀김 기름이 생길 때 그 그릇에 붙는다.
/// </summary>
[RequireComponent(typeof(MovableBowl))]
public class FryBubbles : MonoBehaviour
{
    [Tooltip("조각 하나당 1초에 생기는 거품 수 (날것일 때 - 익을수록 줄어듦).")]
    [SerializeField] private float bubblesPerPiece = 30f;
    [Tooltip("다 익은 조각은 이 비율만큼만 (남은 수분이 적어서).")]
    [SerializeField, Range(0f, 1f)] private float doneShare = 0.3f;
    [Tooltip("조각에서 먼 기름 표면에 드문드문 생기는 거품 (조각 수당 1초에).")]
    [SerializeField] private float ambientPerPiece = 6f;
    [SerializeField] private float lifeMin = 0.3f, lifeMax = 0.8f;
    [Tooltip("거품 크기 (조각 반지름 대비).")]
    [SerializeField] private float sizeMin = 0.35f, sizeMax = 0.85f;
    [SerializeField] private int maxBubbles = 300;

    private MovableBowl bowl;
    private static Material bubbleMat;

    private sealed class Bubble { public Transform t; public float age, life, size; public Vector3 at; }
    private readonly List<Bubble> live = new List<Bubble>();
    private readonly Stack<Transform> pool = new Stack<Transform>();
    private float carry;

    void Awake() => bowl = GetComponent<MovableBowl>();

    void OnDestroy()
    {
        foreach (Bubble b in live) if (b.t != null) Destroy(b.t.gameObject);
        foreach (Transform t in pool) if (t != null) Destroy(t.gameObject);
    }

    void Update()
    {
        Advance(Time.deltaTime);
        if (bowl == null || !bowl.HasFryingOil || !bowl.IsOilHot || bowl.IsHeld) return;

        MovableBowl.Interior inside = bowl.GetInterior();
        float oilH = bowl.OilSurfaceHeight;
        if (oilH <= 0f) return;
        Vector3 surface = inside.floorCenter + inside.up * oilH;

        // 기름 속 조각들
        var pieces = new List<(FoodPiece p, float wet)>();
        foreach (FoodPieceGroup g in GetComponentsInChildren<FoodPieceGroup>())
        {
            CookableItem ck = g.GetComponent<CookableItem>();
            float ttc = ck != null ? Mathf.Max(ck.TimeToCooked, 0.01f) : 6f;
            foreach (FoodPiece p in g.Pieces)
                if (p != null) pieces.Add((p, Mathf.Lerp(1f, doneShare, Mathf.Clamp01(p.Cook / ttc))));
        }
        if (pieces.Count == 0) return;

        float heat = bowl.OilHeat01;
        float rate = 0f;
        foreach (var x in pieces) rate += bubblesPerPiece * x.wet;
        rate += ambientPerPiece * pieces.Count;
        carry += rate * heat * Time.deltaTime;
        Vector3 right = Vector3.ProjectOnPlane(Vector3.right, inside.up).normalized;
        Vector3 back = Vector3.Cross(right, inside.up).normalized;
        while (carry >= 1f && live.Count < maxBubbles)
        {
            carry -= 1f;
            float pick = Random.value * rate;
            Vector3 at;
            float r;
            if (pick < rate - ambientPerPiece * pieces.Count)
            {
                // 조각 둘레 (조각이 표면 가까이 떠 있든 바닥에 있든, 거품은 표면에서 보인다)
                var x = pieces[Random.Range(0, pieces.Count)];
                r = x.p.Radius;
                Vector2 o = Random.insideUnitCircle.normalized * r * Random.Range(0.8f, 1.6f);
                Vector3 flat = Vector3.ProjectOnPlane(x.p.transform.position - inside.floorCenter, inside.up);
                at = surface + flat + right * o.x + back * o.y;
            }
            else
            {
                // 기름 표면 아무 데나 (팬 안쪽 0.85 반지름 안)
                Vector2 o = Random.insideUnitCircle * inside.radius * 0.85f;
                r = pieces[0].p.Radius;
                at = surface + right * o.x + back * o.y;
            }
            Spawn(at, inside.up, r * Random.Range(sizeMin, sizeMax), Random.Range(lifeMin, lifeMax));
        }
        if (live.Count >= maxBubbles) carry = 0f;
    }

    private void Spawn(Vector3 at, Vector3 up, float size, float life)
    {
        Transform t = pool.Count > 0 ? pool.Pop() : Make();
        t.gameObject.SetActive(true);
        t.rotation = Quaternion.FromToRotation(Vector3.up, up);
        live.Add(new Bubble { t = t, age = 0f, life = life, size = size, at = at });
    }

    /// <summary>부풀었다가 (표면에서 살짝 떠오르며) 톡 터진다.</summary>
    private void Advance(float dt)
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            Bubble b = live[i];
            b.age += dt;
            float k = b.age / b.life;
            if (k >= 1f || b.t == null)
            {
                if (b.t != null) { b.t.gameObject.SetActive(false); pool.Push(b.t); }
                live.RemoveAt(i);
                continue;
            }
            float s = b.size * Mathf.Sin(Mathf.Min(k * 1.15f, 1f) * Mathf.PI * 0.5f + 0.2f);
            b.t.localScale = new Vector3(s, s * 0.45f, s);
            b.t.position = b.at + b.t.up * (s * 0.15f);
        }
    }

    private Transform Make()
    {
        if (bubbleMat == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            bubbleMat = new Material(sh != null ? sh : Shader.Find("Unlit/Color")) { color = new Color(1f, 0.97f, 0.85f, 0.55f) };
        }
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = "FryBubble";
        Destroy(g.GetComponent<Collider>());
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = bubbleMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // (그릇 아래에 두지 않는다 - 그릇의 크기 배율이 거품 크기에 곱해지지 않게. 짧게 살다 사라짐)
        return g.transform;
    }
}
