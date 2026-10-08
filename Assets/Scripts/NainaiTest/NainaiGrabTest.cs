using UnityEngine;

/// <summary>
/// BowlPlacementTest 씬 전용 Grab 대상 마커. 기존 Carryable/HandCarry와는
/// 완전히 분리된 상태 컨테이너로, 실제 입력 처리는 NainaiGrabDragManager가
/// 담당하고 이 컴포넌트는 그 결과(Grab 오프셋/드래그 평면/정착 위치)만 들고
/// 있는다. 기존 IClickable/IDraggable 인터페이스는 구현하지 않는다 - 씬에 함께
/// 있는 ClickDetector가 이 오브젝트를 자기 시스템 대상으로 오인하지 않도록
/// 하기 위함이다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class NainaiGrabTest : MonoBehaviour
{
    [Tooltip("다른 재료와의 최소 유지 거리 계산에 쓰는 반경 - 0이면 Collider 크기로 자동 계산")]
    [SerializeField] private float footprintRadius = 0f;

    public float FootprintRadius => footprintRadius;
    public bool IsGrabbed { get; private set; }
    public NainaiDropZoneTest CurrentZone { get; private set; }

    private Vector3 grabOffset;
    private Plane dragPlane;
    private Vector3 restPosition;

    void Awake()
    {
        restPosition = transform.position;

        if (footprintRadius <= 0f)
        {
            Collider col = GetComponent<Collider>();
            footprintRadius = col != null ? Mathf.Max(col.bounds.extents.x, col.bounds.extents.y) : 0.15f;
        }
    }

    /// <summary>MouseDown 순간 호출 - "재료 중심 - 실제 클릭한 월드 위치"를
    /// grabOffset으로 저장하고, 카메라를 바라보는 드래그 평면을 현재 위치보다
    /// dragHeightOffset만큼 카메라 쪽으로 띄워서 만든다 (배치면을 뚫고 지나가지
    /// 않도록).</summary>
    public void BeginGrab(Vector3 hitWorldPoint, Vector3 cameraForward, float dragHeightOffset)
    {
        IsGrabbed = true;
        restPosition = transform.position;
        grabOffset = transform.position - hitWorldPoint;

        Vector3 liftedPoint = transform.position - cameraForward * dragHeightOffset;
        dragPlane = new Plane(cameraForward, liftedPoint);
    }

    /// <summary>매 프레임 마우스 위치로 만든 Ray를 드래그 평면과 교차시켜
    /// grabOffset을 더한 위치로 직접 이동시킨다 - Lerp 없이 1:1로 따라온다.</summary>
    public void UpdateDrag(Vector3 rayOrigin, Vector3 rayDirection)
    {
        Ray ray = new Ray(rayOrigin, rayDirection);
        if (dragPlane.Raycast(ray, out float enter))
        {
            transform.position = ray.GetPoint(enter) + grabOffset;
        }
    }

    /// <summary>유효한 DropZone 위에서 마우스를 놓았을 때 - 최종 보정된 위치에
    /// 정착시킨다.</summary>
    public void Settle(Vector3 finalPosition, NainaiDropZoneTest zone)
    {
        transform.position = finalPosition;
        CurrentZone = zone;
        restPosition = finalPosition;
        IsGrabbed = false;
    }

    /// <summary>DropZone 밖에서 마우스를 놓았을 때 - 이번 Grab 시작 전 위치로
    /// 되돌린다 (테스트 단계 한정 동작).</summary>
    public void CancelGrab()
    {
        transform.position = restPosition;
        IsGrabbed = false;
    }
}
