using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 작업 영역: 조리도구·재료·접시 등(Carryable)이 놓일 수 있는 곳은 조리대 전체
/// (싱크~가스레인지 윗면, 아래 선반·냄비칸, 위 선반 - Station3D), 쓰레기통,
/// 제출대뿐. 그 밖(배경 벽, 화면 가장자리의 빈 곳)에 놓이면 가장 가까운 안쪽으로
/// 끌어들인다 - 중간에 화면 밖으로 사라지거나 손이 닿지 않는 데 남는 일 없게.
/// 놓는 순간(HandCarry.PlaceFreeAt: Clamp)과, 그 밖의 경로로 빠져나간 것도
/// 잠깐마다 살펴서(Update) 되돌린다. 손에 든 것, 다른 그릇 안에 담긴 것은 그
/// 그릇을 따라가므로 따로 보지 않는다. 플레이 시작 때 저절로 생긴다.
/// </summary>
public class PlayArea : MonoBehaviour
{
    public static PlayArea Instance { get; private set; }

    [Tooltip("영역 안쪽으로 끌어들일 때 가장자리에서 이만큼 안쪽에 둔다 (월드).")]
    [SerializeField] private float inset = 0.3f;
    [Tooltip("몇 초마다 살펴볼지.")]
    [SerializeField] private float checkSeconds = 0.25f;

    private readonly List<Rect> rects = new List<Rect>();
    private readonly List<Carryable> items = new List<Carryable>();
    private float nextCheck, nextRefresh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (Instance != null) return;
        new GameObject("PlayArea").AddComponent<PlayArea>();
    }

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start() => Measure();

    /// <summary>허용 영역 (화면 기준 x·y 사각형들): 조리대 본체, 쓰레기통, 제출대 -
    /// 카메라에 보이는 범위로 잘라서.</summary>
    private void Measure()
    {
        rects.Clear();
        Camera initialCamera = Camera.main; // 시작 구도를 고정 기준으로 사용한다.
        frameRotation = initialCamera != null ? initialCamera.transform.rotation : Quaternion.identity;
        inverseFrame = Quaternion.Inverse(frameRotation); // 회전 후에도 기존 화면 평면 규칙을 유지한다.
        GameObject counter = GameObject.Find("Counter_R");
        if (counter != null && counter.GetComponent<Renderer>() is Renderer cr)
            { Bounds cb = InFrame(cr.bounds); counterTop = Rect.MinMaxRect(cb.min.x, cb.min.y, cb.max.x, cb.max.y); counterSurface = cr.bounds.max.y; counterWorldBounds = cr.bounds; hasCounterSurface = true; }
        AddFrom(GameObject.Find("Station3D"));
        AddFrom(GameObject.Find("TrashCan"));
        if (SubmitShelf.Instance != null) AddFrom(SubmitShelf.Instance.gameObject);
        Camera cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            float h = cam.orthographicSize, w = h * cam.aspect;
            Vector3 cameraPoint = inverseFrame * cam.transform.position; // 회전된 기준 좌표
            Rect view = new Rect(cameraPoint.x - w, cameraPoint.y - h, w * 2f, h * 2f);
            for (int i = 0; i < rects.Count; i++)
            {
                Rect r = rects[i];
                float xMin = Mathf.Max(r.xMin, view.xMin), yMin = Mathf.Max(r.yMin, view.yMin);
                float xMax = Mathf.Min(r.xMax, view.xMax), yMax = Mathf.Min(r.yMax, view.yMax);
                rects[i] = Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin, xMax), Mathf.Max(yMin, yMax));
            }
        }
        if (rects.Count == 0) Debug.LogWarning("[작업 영역] 조리대(Station3D)를 찾지 못해 영역 제한을 하지 않아요");
    }

    private void AddFrom(GameObject go)
    {
        if (go == null) return;
        bool any = false;
        Bounds b = default;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer || r.GetComponent<TextMesh>() != null) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (any) { b = InFrame(b); rects.Add(Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y)); }
    }

    /// <summary>조리대 윗면 (Counter_R - 싱크 오른쪽~가스레인지): 밖에서 끌어들인 것,
    /// 빈 곳에 내려놓는 것이 가는 곳. 위쪽 벽면(선반·걸이)이나 바깥이 아니라.</summary>
    private Rect counterTop;
    private Quaternion frameRotation, inverseFrame; // 시작 카메라와 평행한 작업 영역 좌표
    private Bounds counterWorldBounds; // 실제 조리대 윗면의 범위
    private bool hasCounterSurface; // 조리대 렌더러를 찾았는지
    public static bool TryCounterPoint(Ray ray, out Vector3 point) // 회전된 카메라의 클릭을 실제 윗면에 투영한다.
    {
        point = Vector3.zero;
        PlayArea area = Instance;
        if (area == null || !area.hasCounterSurface || Vector3.Dot(Carryable.CounterUp, Vector3.up) < 0.999f) return false;
        Plane top = new Plane(Vector3.up, new Vector3(0f, area.counterSurface, 0f)); // 수평 조리대의 윗면
        if (!top.Raycast(ray, out float distance)) return false;
        point = ray.GetPoint(distance); // 화면 깊이를 추정하지 않고 교차점을 사용한다.
        return true;
    }
    public static bool TryPlaceOnCounter(Carryable item, Vector3 point) // 물체 바닥을 윗면에 맞춘다.
    {
        PlayArea area = Instance;
        if (area == null || !area.hasCounterSurface || Vector3.Dot(Carryable.CounterUp, Vector3.up) < 0.999f) return false;
        Bounds shape = new Bounds(item.transform.position, Vector3.zero); // 피벗이 치우친 모델도 처리한다.
        bool found = false;
        Renderer dish = item is MovableBowl bowl ? bowl.VisibleDishRenderer() : null; // 음식 대신 접시 자체를 기준으로 한다.
        if (dish != null && dish.enabled) { shape = dish.bounds; found = true; }
        if (!found)
            foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer || renderer.GetComponent<TextMesh>() != null) continue;
                if (!found) { shape = renderer.bounds; found = true; } else shape.Encapsulate(renderer.bounds);
            }
        Bounds top = area.counterWorldBounds; // 접시 전체가 조리대 안쪽에 들어오도록 제한한다.
        float ex = Mathf.Min(shape.extents.x + 0.02f, top.extents.x);
        float ez = Mathf.Min(shape.extents.z + 0.02f, top.extents.z);
        Vector3 center = new Vector3(Mathf.Clamp(point.x, top.min.x + ex, top.max.x - ex), area.counterSurface + shape.extents.y + 0.01f, Mathf.Clamp(point.z, top.min.z + ez, top.max.z - ez));
        item.transform.position += center - shape.center; // 눈에 보이는 접시 바닥을 기준으로 이동한다.
        return true;
    }
    private float counterSurface; // 수평 조리대 윗면 높이
    private Bounds InFrame(Bounds b) // 월드 경계의 여덟 꼭짓점을 작업 좌표로 변환한다.
    {
        Bounds result = new Bounds(inverseFrame * b.center, Vector3.zero);
        for (int i = 0; i < 8; i++)
            result.Encapsulate(inverseFrame * (b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        return result;
    }

    private bool Inside(Vector3 world)
    {
        Vector2 p = inverseFrame * world; // 화면 방향으로 투영한 점을 검사한다.
        foreach (Rect r in rects) if (r.Contains(p)) return true;
        return false;
    }

    /// <summary>조리대 윗면에서 이 점에 가장 가까운 안쪽 점.</summary>
    private Vector3 OntoCounter(Vector3 p)
    {
        Rect r = counterTop;
        if (r.width <= 0f) return p;
        p = inverseFrame * p; // 같은 기준에서 제한한다.
        float ix = Mathf.Min(inset, r.width * 0.5f), iy = Mathf.Min(inset, r.height * 0.5f);
        return frameRotation * new Vector3(Mathf.Clamp(p.x, r.xMin + ix, r.xMax - ix), Mathf.Clamp(p.y, r.yMin + iy, r.yMax - iy), p.z);
    }

    /// <summary>빈 곳에 내려놓을 자리 (HandCarry.PlaceFreeAt): 작업 영역 밖이거나,
    /// 안이라도 조리대 윗면보다 위(벽면 - 놓으면 벽 뒤로 숨는다)면 가장 가까운 윗면 자리.</summary>
    public static Vector3 ClampDrop(Vector3 p)
    {
        PlayArea a = Instance;
        if (a == null || a.rects.Count == 0) return p;
        Vector3 q = a.inverseFrame * p; // 벽면 판정도 같은 좌표계
        bool aboveCounter = a.counterTop.width > 0f && q.y > a.counterTop.yMax && q.x >= a.counterTop.xMin && q.x <= a.counterTop.xMax;
        return !a.Inside(p) || aboveCounter ? a.OntoCounter(p) : p;
    }

    void Update()
    {
        if (rects.Count == 0 || Time.time < nextCheck) return;
        nextCheck = Time.time + checkSeconds;
        if (Time.time >= nextRefresh)
        {
            nextRefresh = Time.time + 2f; // (새로 생긴 것 - 잘린 조각, 키친타월 등 - 도 보도록)
            items.Clear();
            items.AddRange(FindObjectsByType<Carryable>(FindObjectsSortMode.None));
        }
        HandCarry hand = HandCarry.Instance;
        foreach (Carryable c in items)
        {
            if (c == null || !c.isActiveAndEnabled || c.IsHeld || KnifeFall.IsFalling(c)) continue;
            if (hand != null && hand.IsDragging && (object)hand.Current == c) continue;
            // 그릇·도마 위에 담긴 것은 그 그릇을 따라간다 (그릇 쪽을 본다)
            Transform parent = c.transform.parent;
            if (parent != null && parent.GetComponentInParent<Carryable>() != null) continue;
            if (!VisibleCenter(c, out Vector3 center) || Inside(center)) continue;
            // 밖으로 나간 것: 가장 가까운 조리대 윗면으로, 윗면 높이(깊이)에 맞춰
            if (c.CurrentContainer != null) { c.CurrentContainer.Release(c); c.CurrentContainer = null; }
            Vector3 move = OntoCounter(center) - center;
            Vector3 pos = c.transform.position + move; // 회전된 화면의 이동량 전체를 적용한다.
            if (Vector3.Dot(Carryable.CounterUp, Vector3.up) > 0.999f) // 수평 조리대에서는 실제 높이에 맞춘다.
            {
                float bottom = float.MaxValue; // 보이는 물체의 바닥을 찾는다.
                foreach (Renderer r in c.GetComponentsInChildren<Renderer>())
                    if (r.enabled && !(r is ParticleSystemRenderer)) bottom = Mathf.Min(bottom, r.bounds.min.y);
                if (bottom < float.MaxValue) pos.y += counterSurface + 0.02f - (bottom + move.y);
            }
            else pos.z = HandCarry.FreeDropZ(c, pos.y); // 기존 기울어진 씬 호환
            c.transform.SetParent(null, true);
            c.transform.position = pos;
            Debug.Log($"[작업 영역] {c.name}: 조리대 밖에 있어 조리대 위로 옮김");
        }
    }

    private static bool VisibleCenter(Carryable c, out Vector3 center)
    {
        center = c.transform.position;
        bool any = false;
        Bounds b = default;
        foreach (Renderer r in c.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (any) center = b.center;
        return any;
    }
}
