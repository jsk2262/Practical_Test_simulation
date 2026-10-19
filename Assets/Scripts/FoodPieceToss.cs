using UnityEngine;

/// <summary>
/// 팬 흔들기(WokDragHandler)에 팬 안 조각이 반응: 흔드는 방향으로 쏠리고 살짝 튀어
/// 오르며 뒤집힌다. 소스가 있으면 조금씩 묻는다. 주걱 젓기(Stir)와 달리 두부가
/// 부서지지 않는다 - 마파두부는 흔들어 섞는다.
/// </summary>
public partial class FoodPieceGroup
{
    [Header("팬 흔들기")]
    [Tooltip("마우스 1px 움직임마다 조각에 더해지는 속도.")]
    [SerializeField] private float tossPerPixel = 0.006f;
    [Tooltip("한 프레임에 더해지는 속도의 최대.")]
    [SerializeField] private float tossMaxPerFrame = 0.25f;
    [Tooltip("흔들 때 위로 튀어 오르는 정도 (옆 속도 대비).")]
    [SerializeField] private float tossLift = 0.6f;

    /// <summary>팬이 화면에서 mouseDelta(px)만큼 흔들림.</summary>
    public void Toss(Vector2 mouseDelta)
    {
        if (simBowl == null || external || Pieces.Count == 0) return;
        Quaternion inv = Quaternion.Inverse(Frame(out _, out _));
        // 화면 오른쪽 → 팬의 오른쪽, 화면 위 → 팬의 안쪽(뒤)
        Vector3 local = inv * new Vector3(mouseDelta.x, mouseDelta.y, 0f);
        local.y = 0f;
        Vector3 push = Vector3.ClampMagnitude(local * tossPerPixel, tossMaxPerFrame);
        float k = push.magnitude / Mathf.Max(tossMaxPerFrame, 1e-4f);
        if (k < 0.05f) return;

        PanSauce sauce = simBowl.GetComponent<PanSauce>();
        float avail = sauce != null ? Mathf.Clamp01(sauce.Amount / 3f) : 0f;
        foreach (FoodPiece p in Pieces)
        {
            if (p == null) continue;
            float r = Random.Range(0.7f, 1.3f);
            p.simVel += push * r + Vector3.up * push.magnitude * tossLift * Random.Range(0.3f, 1f);
            p.simSpin += Random.onUnitSphere * Random.Range(1f, 4f) * k;
            // 소스에 닿아 있는 조각은 흔들 때마다 조금씩 소스가 묻는다
            if (avail > 0f && p.simPos.y <= p.Radius * 1.6f)
                p.AddCoat(coatPerStir * 0.15f * avail * k, sauce.Color, Mathf.Max(sauce.Viscosity, sauce.Thickness));
        }
    }
}
