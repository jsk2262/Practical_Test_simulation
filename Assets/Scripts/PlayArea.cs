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
        GameObject counter = GameObject.Find("Counter_R");
        if (counter != null && counter.GetComponent<Renderer>() is Renderer cr)
            counterTop = Rect.MinMaxRect(cr.bounds.min.x, cr.bounds.min.y, cr.bounds.max.x, cr.bounds.max.y);
        AddFrom(GameObject.Find("Station3D"));
        AddFrom(GameObject.Find("TrashCan"));
        if (SubmitShelf.Instance != null) AddFrom(SubmitShelf.Instance.gameObject);
        Camera cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            float h = cam.orthographicSize, w = h * cam.aspect;
            Rect view = new Rect(cam.transform.position.x - w, cam.transform.position.y - h, w * 2f, h * 2f);
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
        if (any) rects.Add(Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y));
    }

    /// <summary>조리대 윗면 (Counter_R - 싱크 오른쪽~가스레인지): 밖에서 끌어들인 것,
    /// 빈 곳에 내려놓는 것이 가는 곳. 위쪽 벽면(선반·걸이)이나 바깥이 아니라.</summary>
    private Rect counterTop;

    private bool Inside(Vector2 p)
    {
        foreach (Rect r in rects) if (r.Contains(p)) return true;
        return false;
    }

    /// <summary>조리대 윗면에서 이 점에 가장 가까운 안쪽 점.</summary>
    private Vector3 OntoCounter(Vector3 p)
    {
        Rect r = counterTop;
        if (r.width <= 0f) return p;
        float ix = Mathf.Min(inset, r.width * 0.5f), iy = Mathf.Min(inset, r.height * 0.5f);
        return new Vector3(Mathf.Clamp(p.x, r.xMin + ix, r.xMax - ix), Mathf.Clamp(p.y, r.yMin + iy, r.yMax - iy), p.z);
    }

    /// <summary>빈 곳에 내려놓을 자리 (HandCarry.PlaceFreeAt): 작업 영역 밖이거나,
    /// 안이라도 조리대 윗면보다 위(벽면 - 놓으면 벽 뒤로 숨는다)면 가장 가까운 윗면 자리.</summary>
    public static Vector3 ClampDrop(Vector3 p)
    {
        PlayArea a = Instance;
        if (a == null || a.rects.Count == 0) return p;
        bool aboveCounter = a.counterTop.width > 0f && p.y > a.counterTop.yMax && p.x >= a.counterTop.xMin && p.x <= a.counterTop.xMax;
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
            Vector3 pos = c.transform.position + new Vector3(move.x, move.y, 0f);
            pos.z = HandCarry.FreeDropZ(c, pos.y);
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
