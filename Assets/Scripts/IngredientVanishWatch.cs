using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Catches food that "vanishes": an ingredient (or scrap) that ends up
/// behind the counter top, off the screen, or with nothing of it showing.
/// Each case is logged once (a warning starting [사라짐 감지]) with where it
/// is, what holds it and the call stack that last set it down - to find
/// what put it there. The first two are also put right: brought back in
/// front of the counter top / to where it was last seen.
/// </summary>
public class IngredientVanishWatch : MonoBehaviour
{
    private readonly Dictionary<Carryable, Vector3> lastSeen = new Dictionary<Carryable, Vector3>();
    private readonly HashSet<string> reported = new HashSet<string>();
    private Rect counter;
    private float next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindAnyObjectByType<IngredientVanishWatch>() == null)
            new GameObject("IngredientVanishWatch").AddComponent<IngredientVanishWatch>();
    }

    void Start()
    {
        GameObject c = GameObject.Find("Counter_R");
        Renderer r = c != null ? c.GetComponent<Renderer>() : null;
        if (r != null) counter = new Rect(r.bounds.min.x, r.bounds.min.y, r.bounds.size.x, r.bounds.size.y);
    }

    void Update()
    {
        if (Time.time < next) return;
        next = Time.time + 0.5f;
        Camera cam = Camera.main;
        if (cam == null) return;

        foreach (Carryable c in FindObjectsByType<Carryable>(FindObjectsSortMode.None))
        {
            if (!(c is IngredientItem || c is FoodScrap) || c.IsHeld) continue;
            if (HandCarry.Instance != null && (object)HandCarry.Instance.Current == c) continue;
            bool any = false;
            Bounds b = default;
            foreach (Renderer r in c.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) { Report(c, "보이는 부분 없음 (렌더러 전부 꺼짐)", c.transform.position); continue; }

            Vector3 vp = cam.WorldToViewportPoint(b.center);
            if (vp.x < -0.02f || vp.x > 1.02f || vp.y < -0.02f || vp.y > 1.02f)
            {
                Report(c, "화면 밖", b.center);
                if (lastSeen.TryGetValue(c, out Vector3 back)) c.transform.position += back - b.center;
                continue;
            }

            // Loose on the counter top but behind it (in its depth): hidden under it.
            bool loose = c.CurrentContainer == null && c.GetComponentInParent<MovableBowl>() == null;
            if (loose && counter.width > 0f && counter.Contains(new Vector2(b.center.x, b.center.y))
                && b.center.y > counter.yMin + 0.4f && b.min.z > HandCarry.CounterZ(b.center.y) + 0.05f)
            {
                Report(c, "작업대 뒤에 묻힘", b.center);
                c.transform.position += new Vector3(0f, 0f, HandCarry.CounterZ(b.center.y) - b.max.z - 0.02f);
                continue;
            }
            lastSeen[c] = b.center;
        }
    }

    private void Report(Carryable c, string why, Vector3 at)
    {
#if UNITY_6000_5_OR_NEWER
        if (!reported.Add(unchecked((int)UnityEngine.EntityId.ToULong(c.GetEntityId())) + why)) return;
#else
        if (!reported.Add(c.GetInstanceID() + why)) return;
#endif
        string holder = c.CurrentContainer is Component h ? h.name : "-";
        string parent = c.transform.parent != null ? c.transform.parent.name : "-";
        Debug.LogWarning($"[사라짐 감지] {c.name}: {why} - 위치 {at:F2}, 담긴 곳 {holder}, 부모 {parent}\n마지막으로 놓은 경로:\n{c.LastPlacedBy}");
    }
}
