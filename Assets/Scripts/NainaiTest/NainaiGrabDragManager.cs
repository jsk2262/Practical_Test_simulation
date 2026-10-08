using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// BowlPlacementTest 씬 전용 입력 코디네이터. Mouse.current를 직접 읽어
/// Raycast+DragPlane 방식으로 NainaiGrabTest 대상을 집고, 마우스를 놓는 순간
/// NainaiDropZoneTest 영역 안에서 최종 위치를 보정한다.
///
/// 기존 ClickDetector와 같은 씬에 공존하지만 서로 다른 컴포넌트(IClickable/
/// IDraggable을 구현한 Carryable류 vs 이 스크립트가 직접 조작하는
/// NainaiGrabTest)를 대상으로 하므로 입력 경로가 겹치지 않는다.
/// </summary>
public class NainaiGrabDragManager : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [Tooltip("드래그 중 배치면을 뚫지 않도록 카메라 쪽으로 띄우는 값 (월드 단위)")]
    [SerializeField] private float dragHeightOffset = 0.03f;
    [SerializeField] private float maxRayDistance = 100f;

    private NainaiGrabTest grabbed;
    private NainaiDropZoneTest hoveredZone;
    private int raycastMask;

    void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        // ClickDetector와 동일한 관례 - HandCursor 쪽 Collider가 클릭을 가로채지 않도록.
        raycastMask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || targetCamera == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            TryBeginGrab(mouse.position.ReadValue());
        }

        if (grabbed != null && mouse.leftButton.isPressed)
        {
            Ray ray = targetCamera.ScreenPointToRay(mouse.position.ReadValue());
            grabbed.UpdateDrag(ray.origin, ray.direction);
            UpdateHoveredZoneHighlight();
        }

        if (grabbed != null && mouse.leftButton.wasReleasedThisFrame)
        {
            ResolveDrop();
        }
    }

    private void TryBeginGrab(Vector2 screenPos)
    {
        Ray ray = targetCamera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, raycastMask)) return;

        NainaiGrabTest item = hit.collider.GetComponent<NainaiGrabTest>();
        if (item == null) return;

        grabbed = item;
        item.BeginGrab(hit.point, targetCamera.transform.forward, dragHeightOffset);
    }

    private void UpdateHoveredZoneHighlight()
    {
        NainaiDropZoneTest zone = FindZoneAt(grabbed.transform.position);
        if (zone == hoveredZone) return;

        hoveredZone?.SetHighlight(false);
        hoveredZone = zone;
        hoveredZone?.SetHighlight(true);
    }

    private static NainaiDropZoneTest FindZoneAt(Vector3 worldPos)
    {
        NainaiDropZoneTest[] zones = Object.FindObjectsOfType<NainaiDropZoneTest>();
        foreach (NainaiDropZoneTest zone in zones)
        {
            if (zone.Contains(worldPos)) return zone;
        }
        return null;
    }

    private void ResolveDrop()
    {
        hoveredZone?.SetHighlight(false);
        hoveredZone = null;

        NainaiDropZoneTest zone = FindZoneAt(grabbed.transform.position);
        if (zone == null)
        {
            // 테스트 단계에서는 유효 영역 밖 드롭은 그냥 원위치로 되돌린다.
            grabbed.CancelGrab();
            grabbed = null;
            return;
        }

        Vector3 target = zone.ClampToArea(grabbed.transform.position);
        target = ResolveOverlap(grabbed, target, zone);
        grabbed.Settle(target, zone);
        grabbed = null;
    }

    /// <summary>같은 영역에 이미 정착해 있는 다른 재료와 심하게 겹칠 때만
    /// 최소한으로 밀어낸다 - 정밀한 물리 해석이 아니라 "손맛" 테스트에 필요한
    /// 수준의 단순 보정.</summary>
    private static Vector3 ResolveOverlap(NainaiGrabTest self, Vector3 candidate, NainaiDropZoneTest zone)
    {
        NainaiGrabTest[] all = Object.FindObjectsOfType<NainaiGrabTest>();
        foreach (NainaiGrabTest other in all)
        {
            if (other == self || other.CurrentZone != zone) continue;

            Vector3 otherPos = other.transform.position;
            Vector2 delta = new Vector2(candidate.x - otherPos.x, candidate.y - otherPos.y);
            float minDist = self.FootprintRadius + other.FootprintRadius;
            float dist = delta.magnitude;

            if (dist <= 0.0001f)
            {
                candidate.x += minDist;
            }
            else if (dist < minDist)
            {
                Vector2 push = delta.normalized * (minDist - dist);
                candidate.x += push.x;
                candidate.y += push.y;
            }
        }

        return zone.ClampToArea(candidate);
    }
}
