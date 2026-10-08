using UnityEngine;

/// <summary>
/// BowlPlacementTest 씬 전용 배치 영역 마커. bowl/plate 오브젝트는 Collider가
/// 없으므로, Collider 대신 중심 위치 + 반경(areaHalfExtents)만으로 유효 배치
/// 영역을 정의한다 (요청서의 "Collider 또는 별도의 배치 영역" 중 후자).
/// </summary>
public class NainaiDropZoneTest : MonoBehaviour
{
    [Tooltip("드롭 유효 영역의 절반 크기(X,Y, 월드 단위) - (0,0)이면 자기 Renderer의 실제 크기로 자동 계산")]
    [SerializeField] private Vector2 areaHalfExtents = Vector2.zero;

    [Tooltip("재료가 이 영역에 정착할 때 자신의 표면보다 카메라 쪽으로 얼마나 띄울지")]
    [SerializeField] private float restZOffset = 0.05f;

    [SerializeField] private Color highlightColor = new Color(1f, 0.95f, 0.4f);

    private Renderer rend;
    private Color baseColor;
    private bool hasBaseColor;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        if (rend != null)
        {
            baseColor = rend.material.color;
            hasBaseColor = true;

            if (areaHalfExtents == Vector2.zero)
            {
                areaHalfExtents = new Vector2(rend.bounds.extents.x, rend.bounds.extents.y);
            }
        }
    }

    /// <summary>월드 XY 기준 이 영역 안에 들어오는지 - 카메라가 회전 없이 Z축을
    /// 바라보는 이 씬의 레이아웃에서는 X,Y만으로 화면상 footprint를 판단할 수
    /// 있다 (Z는 카메라와의 거리/레이어 순서일 뿐).</summary>
    public bool Contains(Vector3 worldPos)
    {
        Vector3 c = transform.position;
        return Mathf.Abs(worldPos.x - c.x) <= areaHalfExtents.x
            && Mathf.Abs(worldPos.y - c.y) <= areaHalfExtents.y;
    }

    /// <summary>영역을 벗어난 경우에만 가장 가까운 유효 위치로 Clamp하고, Z는
    /// 이 영역 표면보다 살짝 앞(카메라 쪽)으로 정착시킨다.</summary>
    public Vector3 ClampToArea(Vector3 worldPos)
    {
        Vector3 c = transform.position;
        float x = Mathf.Clamp(worldPos.x, c.x - areaHalfExtents.x, c.x + areaHalfExtents.x);
        float y = Mathf.Clamp(worldPos.y, c.y - areaHalfExtents.y, c.y + areaHalfExtents.y);
        return new Vector3(x, y, c.z - restZOffset);
    }

    public void SetHighlight(bool on)
    {
        if (!hasBaseColor) return;
        rend.material.color = on ? highlightColor : baseColor;
    }
}
