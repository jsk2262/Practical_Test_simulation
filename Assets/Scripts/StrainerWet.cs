using UnityEngine;

/// <summary>
/// 체망의 물기: 물(데치는 냄비·찬물·개수대)에 들어갔다 나오면 한동안 젖어 있다.
/// 젖은 체망을 뜨거운 튀김 기름에 넣으면 물이 순간 끓어 기름이 크게 튄다 -
/// 큰 소리와 함께 기름이 튀고(OilSplatter), 안전사고로 실격 사유가 기록된다.
/// 물기는 그냥 두면 dryAfterSeconds 뒤 마른다 (빠른 모드에서는 그만큼 빨리).
/// </summary>
public partial class Strainer
{
    [Header("물기 (젖은 체망 → 뜨거운 기름 = 기름 튐 · 실격)")]
    [Tooltip("물에 닿은 뒤 체망이 저절로 마르기까지 걸리는 시간(초).")]
    [SerializeField] private float dryAfterSeconds = 90f;
    [Tooltip("이 온도(℃) 이상인 기름에 젖은 체망이 들어가면 기름이 튄다.")]
    [SerializeField] private float splatterOilTempC = 110f;

    private float wetAt = -1000f;

    /// <summary>물에 닿은 뒤 아직 마르지 않음.</summary>
    public bool IsWet => Time.time - wetAt < dryAfterSeconds / (SpeedMode.On ? SpeedMode.WaitFactor : 1f);

    /// <summary>물에 닿음 - 지금부터 다시 젖은 상태.</summary>
    public void MarkWet() => wetAt = Time.time;

    /// <summary>젖은 채로 뜨거운 기름에 넣으려 함: 기름이 튀고 실격 사유가 기록된다.
    /// 그랬으면 true (그 동작은 하지 않음 - 놀라서 체망을 뺀 것).</summary>
    private bool SplashIfWetInOil(MovableBowl vessel)
    {
        if (vessel == null || !vessel.HasFryingOil || !IsWet) return false;
        if (vessel.OilTemperatureC < splatterOilTempC) return false;
        OilSplatter.Play(vessel);
        ExamDeductions.Disqualify("물기 있는 체망을 뜨거운 기름에 넣어 기름이 튐 (안전사고 - 화상 위험)");
        wetAt = -1000f; // (물기는 그 순간 다 튀어 날아감)
        return true;
    }
}
