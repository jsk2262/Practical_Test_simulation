using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class Strainer
{
    private bool scooping;

    private bool PointerSpot(MovableBowl vessel, out Vector3 point)
    {
        point = Vector3.zero;
        if (vessel == null || Camera.main == null || Mouse.current == null) return false;
        var inside = vessel.GetInterior();
        Vector3 surface = inside.floorCenter + inside.up * inside.surface;
        if (vessel.HasFryingOil) surface = inside.floorCenter + inside.up * vessel.OilSurfaceHeight;
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!new Plane(inside.up, surface).Raycast(ray, out float distance)) return false;
        point = ray.GetPoint(distance);
        // 손잡이/테두리를 누르면 가운데로 보정해서 건지지 않는다.
        return Vector3.ProjectOnPlane(point - surface, inside.up).magnitude <= inside.radius;
    }

    private void BeginLocalScoop(MovableBowl vessel)
    {
        var hand = HandCarry.Instance;
        if (scooping || HasContents || hand == null || hand.Current != this || hand.IsLocked) return;
        if (PointerSpot(vessel, out Vector3 point)) StartCoroutine(ScoopAt(vessel, point));
    }

    private IEnumerator ScoopAt(MovableBowl vessel, Vector3 point)
    {
        var hand = HandCarry.Instance;
        var controller = HandController.Instance;
        if (hand == null || controller == null) yield break;
        scooping = true;
        hand.IsLocked = true;
        try
        {
            transform.rotation = HandRotation;
            Vector3 basket = BasketCenterWorld(out float radius);
            Vector3 centerFromHand = basket - transform.position + HandHoldOffset;
            Vector3 target = point - centerFromHand;
            controller.GoTo(target, null);
            yield return new WaitForSeconds(0.35f);
            if (vessel == null) yield break;
            // 눌렀을 때 전부 가져오지 않고, 망을 넣었다 들어 올리는 시점에 검사한다.
            Vector3 dip = -vessel.GetInterior().up * Mathf.Min(radius * 0.35f, 0.12f);
            for (float t = 0; t < 0.2f; t += Time.deltaTime)
            {
                controller.SetGestureOffset(dip * Mathf.Clamp01(t / 0.2f));
                yield return null;
            }
            CollectAt(vessel, point, radius * 0.85f);
            for (float t = 0; t < 0.25f; t += Time.deltaTime)
            {
                controller.SetGestureOffset(dip * (1f - Mathf.Clamp01(t / 0.25f)));
                yield return null;
            }
            controller.SetGestureOffset(Vector3.zero);
            controller.SlideTo(target + vessel.GetInterior().up * radius * 0.8f, 0.2f);
            yield return new WaitForSeconds(0.2f);
        }
        finally
        {
            if (controller != null) controller.SetGestureOffset(Vector3.zero);
            if (hand != null) hand.IsLocked = false;
            scooping = false;
        }
    }

    // 조각의 중심이 망 안에 들어온 것만 옮긴다. 무게/튀김옷/익힘은 기존 분리 함수가 보존.
    public int CollectAt(MovableBowl vessel, Vector3 point, float radius)
    {
        if (vessel == null || HasContents || radius <= 0f) return 0;
        Vector3 up = vessel.GetInterior().up;
        int count = 0;
        foreach (var ingredient in vessel.GetComponentsInChildren<IngredientItem>())
        {
            if (ingredient == null || ingredient.CurrentContainer != vessel) continue;
            var group = ingredient.GetComponent<FoodPieceGroup>();
            if (group != null && group.Count > 0)
            {
                foreach (FoodPiece piece in new List<FoodPiece>(group.Pieces))
                {
                    if (piece == null || Vector3.ProjectOnPlane(piece.transform.position - point, up).sqrMagnitude > radius * radius) continue;
                    IngredientItem take = group.Count > 1 ? ingredient.SplitOffPiece(piece) : ingredient;
                    if (take != null) { StoreScooped(take); count++; }
                }
            }
            else if (Vector3.ProjectOnPlane(ingredient.VisibleBounds.center - point, up).sqrMagnitude <= radius * radius)
            {
                StoreScooped(ingredient);
                count++;
            }
        }
        LayoutContents();
        return count;
    }

    private void StoreScooped(IngredientItem item)
    {
        if (item.CurrentContainer != null) item.CurrentContainer.Release(item);
        item.CurrentContainer = null;
        foreach (var collider in item.GetComponentsInChildren<Collider>())
            if (collider.enabled) { collider.enabled = false; contentColliders.Add(collider); }
        item.transform.SetParent(transform, true);
        contents.Add(item);
    }

    void OnGUI()
    {
        if (!IsHeld || HasContents || scooping || Camera.main == null || Mouse.current == null) return;
        if (HandCarry.Instance == null || HandCarry.Instance.IsLocked) return;
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        var hits = Physics.RaycastAll(ray);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        MovableBowl vessel = null;
        foreach (var hit in hits)
        {
            if (hit.transform.IsChildOf(transform)) continue;
            vessel = hit.transform.GetComponentInParent<MovableBowl>();
            if (vessel != null) break;
        }
        if (!PointerSpot(vessel, out Vector3 point)) return;
        BasketCenterWorld(out float radius);
        Vector3 up = vessel.GetInterior().up;
        Vector3 right = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
        Vector3 forward = Vector3.Cross(up, right);
        Color oldColor = GUI.color;
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.color = new Color(0.3f, 1f, 0.55f, 0.9f);
        for (int i = 0; i < 40; i++)
        {
            float a = i * Mathf.PI * 2f / 40f, b = (i + 1) * Mathf.PI * 2f / 40f;
            Vector3 p = Camera.main.WorldToScreenPoint(point + (right * Mathf.Cos(a) + forward * Mathf.Sin(a)) * radius * 0.85f);
            Vector3 q = Camera.main.WorldToScreenPoint(point + (right * Mathf.Cos(b) + forward * Mathf.Sin(b)) * radius * 0.85f);
            Vector2 from = new Vector2(p.x, Screen.height - p.y), to = new Vector2(q.x, Screen.height - q.y);
            GUIUtility.RotateAroundPivot(Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg, from);
            GUI.DrawTexture(new Rect(from.x, from.y, Vector2.Distance(from, to), 2f), Texture2D.whiteTexture);
            GUI.matrix = oldMatrix;
        }
        GUI.color = oldColor;
    }
}
