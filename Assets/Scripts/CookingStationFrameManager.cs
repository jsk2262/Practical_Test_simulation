using UnityEngine; // 조리대 기준 방향을 공유한다.

[DefaultExecutionOrder(-10000)] // 재료의 초기화보다 먼저 기준을 등록한다.
public sealed class CookingStationFrameManager : MonoBehaviour
{
    [SerializeField] private Transform station; // Inspector에서 실제 조리대 기준을 연결한다.
    public static CookingStationFrameManager Instance { get; private set; } // 씬의 기준 관리자를 보관한다.
    public static Vector3 Up => Instance != null && Instance.station != null ? Instance.station.up : Quaternion.Euler(-41f, 0f, 0f) * Vector3.up; // 기존 씬은 이전 방향을 유지한다.
    private void Awake() { Instance = this; } // 현재 씬의 기준을 등록한다.
    private void OnDestroy() { if (Instance == this) Instance = null; } // 씬 종료 시 오래된 참조를 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() { Instance = null; } // 도메인 재로드가 꺼져 있어도 초기화한다.
}
