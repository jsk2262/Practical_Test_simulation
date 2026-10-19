using UnityEngine;

public partial class FoodPieceGroup
{
    private float nextMincePress;

    // 처음에는 큰 덩이 몇 개. 이미 나눈 조각이 있으면 다시 만들지 않는다.
    public void EnsureMincePieces()
    {
        if (ingredient == null || !ingredient.IsMincedMeat || Pieces.Count > 0) return;
        Bounds bounds = ingredient.VisibleBounds;
        Renderer body = ingredient.GetComponent<Renderer>();
        Material material = body != null ? body.sharedMaterial : null;
        const int count = 10;
        float size = Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.y) * 0.22f, 0.06f, 0.10f);
        for (int n = 0; n < count; n++)
        {
            float angle = n * Mathf.PI * 2f / count;
            Vector3 at = bounds.center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * size * 0.65f;
            FoodPiece p = MakeMince(at, Vector3.one * size, material);
            p.Init(ingredient, ingredient.AmountGrams / count, 0f, Vector3.one * size);
            Add(p);
        }
        if (body != null) body.enabled = false;
    }

    private FoodPiece MakeMince(Vector3 world, Vector3 size, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "MincePiece";
        go.transform.position = world;
        go.transform.localScale = size;
        go.transform.SetParent(transform, true);
        Destroy(go.GetComponent<Collider>()); // 기존 FoodPieceGroup이 충돌과 이동을 계산한다.
        if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
        return go.AddComponent<FoodPiece>();
    }

    public int PressMinceAt(Vector3 world, float radius)
    {
        if (ingredient == null || !ingredient.IsMincedMeat || simBowl == null
            || !simBowl.IsCookingVessel || Time.time < nextMincePress || Pieces.Count >= 80) return 0;
        FoodPiece nearest = null;
        float distance = radius;
        foreach (FoodPiece p in Pieces)
        {
            if (p == null || p.Grams <= Mathf.Max(ingredient.AmountGrams / 40f, 0.05f) + 0.0001f) continue;
            float d = Vector3.ProjectOnPlane(p.transform.position - world, inside.up).magnitude;
            if (d < distance) { nearest = p; distance = d; }
        }
        if (nearest == null) return 0;
        nextMincePress = Time.time + 0.10f;
        float grams = nearest.Grams * 0.5f;
        Vector3 size = nearest.HalfSize * 2f * 0.7937005f; // 반으로 나눠도 총부피 유지
        Quaternion frame = Frame(out _, out _);
        Vector3 spread = new Vector3(Mathf.Cos(Time.time * 13f), 0f, Mathf.Sin(Time.time * 13f)) * size.x * 0.4f;
        for (int n = 0; n < 2; n++)
        {
            float sign = n == 0 ? -1f : 1f;
            FoodPiece child = MakeMince(nearest.transform.position + frame * spread * sign, size,
                nearest.GetComponent<Renderer>().sharedMaterial);
            child.CopyStateFrom(nearest);
            child.Init(ingredient, grams, nearest.Cook, size);
            child.simPos = nearest.simPos + spread * sign;
            child.simPos.y = Mathf.Max(child.Radius, child.simPos.y);
            child.simVel = nearest.simVel + spread.normalized * sign * 0.3f;
            child.simRot = nearest.simRot;
            child.simSpin = Random.onUnitSphere * 3f;
            Add(child);
        }
        Pieces.Remove(nearest);
        nearest.gameObject.SetActive(false);
        Destroy(nearest.gameObject);
        workedUntil = Time.time + 0.4f;
        return 1;
    }
}
