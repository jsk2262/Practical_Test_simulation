using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 체망 건지기 보조:
/// - 물속(데치기 · 찬물)의 조각이 많은 재료(데친 두부): 한 번에 절반 이상씩 - 두 번이면 다
///   건져진다. 망 안에 든 조각에 더해, 누른 자리에서 가까운 조각부터 채운다.
/// - 기름 속 앙금 조각(기름 온도 확인용): 망 안에 든 것은 건져서 버린다.
/// </summary>
public partial class Strainer
{
    /// <summary>그릇 · 재료 이름별로 처음 건지기 시작할 때의 조각 수.</summary>
    private static readonly Dictionary<string, int> firstCount = new Dictionary<string, int>();

    /// <summary>지금 든 것은 튀김 기름에서 건졌음 (물기 빼기 때 물이 아니라 기름이 떨어짐).</summary>
    private bool scoopedFromOil;

    private void AssistScoop(MovableBowl vessel)
    {
        if (!scooping || vessel == null || !PointerSpot(vessel, out Vector3 point)) return;
        BasketCenterWorld(out float radius);
        StartCoroutine(AssistRoutine(vessel, point, radius * 0.85f));
    }

    private IEnumerator AssistRoutine(MovableBowl vessel, Vector3 point, float radius)
    {
        // 망이 들어갔다 나오는 순간(CollectAt)까지 - 하나도 안 걸렸으면 다 들어 올린 뒤
        while (scooping && !HasContents) yield return null;
        if (vessel == null) yield break;

        if (vessel.HasFryingOil) ScoopBatterDrops(vessel, point, radius);
        // 물(두부 · 완두콩)이든 튀김 기름(고기)이든 두 번이면 다 건져지게
        if (vessel.WaterAmount > 0f || vessel.HasFryingOil) FillToHalf(vessel, point);
    }

    private void ScoopBatterDrops(MovableBowl vessel, Vector3 point, float radius)
    {
        Vector3 up = vessel.GetInterior().up;
        int n = 0;
        foreach (Transform t in vessel.GetComponentsInChildren<Transform>())
        {
            if (t.name != "BatterDrop") continue;
            if (Vector3.ProjectOnPlane(t.position - point, up).magnitude > radius * 1.2f) continue;
            Destroy(t.gameObject);
            n++;
        }
        if (n > 0) Debug.Log($"[체망] {vessel.name}: 앙금 조각 {n}개 건져 버림");
    }

    private void FillToHalf(MovableBowl vessel, Vector3 point)
    {
        Vector3 up = vessel.GetInterior().up;
        // 같은 재료(이름)의 조각을 그릇 전체에서 모은다 - 찬물에 흩어진 낱개 조각도 함께
        var byName = new Dictionary<string, List<KeyValuePair<IngredientItem, FoodPiece>>>();
        foreach (IngredientItem ing in vessel.GetComponentsInChildren<IngredientItem>())
        {
            if (ing == null || (object)ing.CurrentContainer != vessel) continue;
            FoodPieceGroup group = ing.GetComponent<FoodPieceGroup>();
            if (group == null) continue;
            if (!byName.TryGetValue(ing.IngredientName, out var list))
                byName[ing.IngredientName] = list = new List<KeyValuePair<IngredientItem, FoodPiece>>();
            foreach (FoodPiece p in group.Pieces) if (p != null) list.Add(new KeyValuePair<IngredientItem, FoodPiece>(ing, p));
        }

        bool added = false;
        foreach (var kv in byName)
        {
            List<KeyValuePair<IngredientItem, FoodPiece>> left = kv.Value;
            int taken = 0;
            foreach (IngredientItem c in contents) if (c != null && c.IngredientName == kv.Key) taken++;
            string key = vessel.GetInstanceID() + "/" + kv.Key;
            if (!firstCount.TryGetValue(key, out int first) || first < taken + left.Count) firstCount[key] = first = taken + left.Count;
            if (first < 2) continue; // (한 덩이짜리는 원래대로 망 안에 든 것만)
            int quota = Mathf.CeilToInt(first / 2f) - taken;

            // 누른 자리에서 가까운 조각부터
            left.Sort((a, b) => Vector3.ProjectOnPlane(a.Value.transform.position - point, up).sqrMagnitude
                .CompareTo(Vector3.ProjectOnPlane(b.Value.transform.position - point, up).sqrMagnitude));
            int got = 0;
            for (int i = 0; i < left.Count && quota > 0; i++)
            {
                IngredientItem ing = left[i].Key;
                FoodPieceGroup group = ing != null ? ing.GetComponent<FoodPieceGroup>() : null;
                if (group == null || !group.Pieces.Contains(left[i].Value)) continue;
                IngredientItem take = group.Count > 1 ? ing.SplitOffPiece(left[i].Value) : ing;
                if (take == null) continue;
                StoreScooped(take);
                quota--; got++;
                added = true;
            }
            if (got >= left.Count) firstCount.Remove(key); // (다 건짐)
        }
        if (added) LayoutContents();
    }
}
