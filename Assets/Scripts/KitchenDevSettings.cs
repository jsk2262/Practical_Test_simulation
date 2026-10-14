using UnityEngine;

/// <summary>
/// Development assumptions that stand in for data not measured yet - each one
/// named, so it isn't mistaken for a verified value. Resources/KitchenDevSettings.
/// </summary>
[CreateAssetMenu(menuName = "Kitchen/Dev Settings", fileName = "KitchenDevSettings")]
public class KitchenDevSettings : ScriptableObject
{
    [Header("앙금녹말 (가정)")]
    [Tooltip("ASSUMPTION: mL of drained 앙금 per mL of dry 감자전분 put in. The water left in the 앙금 after pouring off is taken as always the same (not modelled).")]
    public float settledMlPerDryStarchMl = 1f;
    [TextArea]
    public string settledStarchNote = "가정: 윗물을 버린 뒤 앙금의 수분 상태는 항상 같다고 본다 (잔류 수분은 계산하지 않음). 실측 후 조정할 것.";

    [Tooltip("ASSUMPTION: how much of a spoonful of 물기 많은 앙금 (spooned before pouring the water off) counts as starch - the rest as water.")]
    [Range(0f, 1f)] public float wetSettledStarchShare = 0.5f;

    [Header("밑간 (미지원)")]
    [TextArea]
    public string marinadeNote = "밑간 액체(간장·청주 등)는 고기에 넣은 mL만 기록하고 질감 계산의 액체량에 더한다. 고기에 흡수되는 양은 계산하지 않음.";

    private static KitchenDevSettings cached;
    public static KitchenDevSettings Current
    {
        get
        {
            if (cached == null) cached = Resources.Load<KitchenDevSettings>("KitchenDevSettings");
            if (cached == null) cached = CreateInstance<KitchenDevSettings>(); // (defaults, if the asset is missing)
            return cached;
        }
    }
}
