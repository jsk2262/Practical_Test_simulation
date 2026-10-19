using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 다지기(Dice): 고추 · 대파 · 마늘 · 생강 등 채소를 고른 크기(예: 0.5cm)의 작은
/// 정육면체로 - 단면이 정사각형인 조각들이 도마 위에 소복이 쌓인다. 예전에는 통째
/// 모양(고추는 씨 보이는 둥근 편)을 그대로 보여 줘서 다진 것처럼 안 보였다.
/// 조각은 Piece_* 이라 팬에 넣으면 다른 썬 채소처럼 하나하나 익고 움직인다.
/// </summary>
public partial class IngredientItem
{
    [Header("다지기 (작은 정육면체)")]
    [Tooltip("다진 조각 수의 최대 (너무 많으면 무거워진다).")]
    [SerializeField, Range(8, 120)] private int maxMinceCubes = 80; // (조각이 작아진 만큼 더 많이)
    [Tooltip("보이는 조각 한 변의 최소 크기(cm) - 너무 작으면 화면에서 안 보인다.")]
    [SerializeField] private float minMinceCubeCm = 0.35f;
    [Tooltip("다진 조각을 보이는 크기에 곱하는 값 (작을수록 잘게).")]
    [SerializeField] private float minceVisualScale = 0.5f;
    [Tooltip("켜면 편썰기 등을 네모 편(가로×세로, 두께는 한 겹)으로 - 양파 4×1 편.")]
    [SerializeField] private bool cutsToFlatSquares;
    [Tooltip("네모 편의 두께(cm) - 양파 한 겹.")]
    [SerializeField] private float flatLayerCm = 0.3f;
    [Tooltip("네모 편 조각 수의 최대.")]
    [SerializeField] private int maxFlatPieces = 10;

    private static Mesh cubeMesh;

    /// <summary>다지기를 정육면체 조각으로 하는 재료 (이미 다져 온 고기는 제외).</summary>
    private bool MincesToCubes => !alreadyMinced && Category != IngredientCategory.Meat;

    /// <summary>다지기: 고른 크기의 정육면체.</summary>
    private IEnumerator MinceCubesRoutine(CutSpec spec)
    {
        float c = Mathf.Max(spec.thicknessCm, minMinceCubeCm) * minceVisualScale; // 한 변(cm, 보이는 크기)
        return BlockPiecesRoutine(spec, new Vector3(c, c, c), CutMethod.Dice); // 정육면체 조각
    }

    /// <summary>양파 같은 편썰기: 가로 x 세로 네모 편 (두께는 양파 한 겹).</summary>
    private IEnumerator FlatSquaresRoutine(CutSpec spec, CutMethod method)
    {
        return BlockPiecesRoutine(spec, new Vector3(spec.widthCm, flatLayerCm, spec.thicknessCm), method); // 가로 · 두께(한 겹) · 세로
    }

    /// <summary>네모 조각들(크기 sizeCm: 가로 · 높이 · 깊이)을 도마 위에 소복이.</summary>
    private IEnumerator BlockPiecesRoutine(CutSpec spec, Vector3 sizeCm, CutMethod method)
    {
        if (cubeMesh == null)
        {
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }
        if (rend == null) rend = GetComponent<Renderer>();

        // 한 변(월드) - 고른 크기, 너무 작으면 보이는 만큼은 크게
        Vector3 size = sizeCm * worldPerCm; // 조각 크기 (월드)
        Bounds b = rend.bounds;
        // 고른 크기가 재료보다 크면(예: 0.5 대신 5cm) 재료의 가장 얇은 두께보다 크게는 못 자른다
        float thin = Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z)); // 재료의 가장 얇은 두께
        size = new Vector3(Mathf.Min(size.x, b.size.x), Mathf.Min(size.y, thin), Mathf.Min(size.z, thin)); // 원재료보다 큰 조각이 나오지 않게
        float side = Mathf.Max(size.x, size.z); // 늘어놓는 간격
        // 재료 부피의 절반쯤이 조각이 된다 (나머지는 빈틈)
        float volume = b.size.x * b.size.y * b.size.z * 0.5f;
        int count = Mathf.Clamp(Mathf.RoundToInt(volume / (size.x * size.y * size.z)), 1, method == CutMethod.Dice ? maxMinceCubes : maxFlatPieces); // 큰 조각이면 몇 개 안 나옴

        // 조각 색: 재료의 재질들 (꼭지는 다듬어 버렸으니 빼고), 겉색 위주
        // (단면 재질은 통째 모양용 텍스처(고추 씨 무늬 등)가 있어 작은 조각엔 무늬가
        // 이상하게 찍힌다 - 속은 겉색을 밝게 한 무늬 없는 색으로)
        Material skin = rend.sharedMaterial;
        foreach (Material m in rend.sharedMaterials)
            if (m != null && !m.name.Contains("Stem") && !m.name.Contains("Cut")) { skin = m; break; }
        Material inner = new Material(skin) { name = ingredientName + "_MinceInside", mainTexture = null };
        Color innerColor = Color.Lerp(skin.color, Color.white, 0.45f);
        inner.color = innerColor;
        if (inner.HasProperty("_BaseColor")) inner.SetColor("_BaseColor", innerColor);
        if (inner.HasProperty("_BaseMap")) inner.SetTexture("_BaseMap", null);
        var mats = new List<Material> { skin, inner };

        // 도마 위 자리: 원래 놓였던 길이만큼, 조금 좁게 소복이
        Vector3 up = Carryable.CounterUp;
        Vector3 right = Vector3.right;
        Vector3 depth = Vector3.Cross(right, up).normalized;
        bool dice = method == CutMethod.Dice; // 다지기(흩어 쌓기) / 편(가지런히 포개기)
        int perRow = dice ? Mathf.Max(2, Mathf.RoundToInt(b.size.x / (side * 1.15f))) : 1; // 편은 한 줄로
        int rowsFlat = dice ? 0 : 3; // 편은 3줄로 포갬
        int rows = dice ? Mathf.Max(2, Mathf.CeilToInt(Mathf.Sqrt(count / (float)perRow) * 1.6f)) : rowsFlat; // 줄 수
        Vector3 floor = b.center - up * (Vector3.Dot(b.extents, new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z))));
        Vector3 parentScale = transform.lossyScale;

        int chops = 8;
        int made = 0;
        for (int c = 0; c < chops; c++)
        {
            HandController.Instance?.PlayChopGesture();
            (HandCarry.Instance?.Current as ToolItem)?.PlayChopMotion();
            yield return StartCoroutine(ChopPulse());

            int upTo = Mathf.RoundToInt(count * (c + 1) / (float)chops);
            for (; made < upTo; made++)
            {
                int layer = made / (perRow * rows);
                int inLayer = made % (perRow * rows);
                int col = inLayer % perRow, row = inLayer / perRow;
                Vector3 at = floor
                    + right * ((col - (perRow - 1) * 0.5f) * size.x * 1.15f + Random.Range(-0.1f, 0.1f) * size.x) // 가로 자리
                    + depth * ((row - (rows - 1) * 0.5f) * size.z * 1.3f + Random.Range(-0.1f, 0.1f) * size.z) // 세로 자리
                    + up * (size.y * (0.5f + layer * 0.9f));

                GameObject piece = new GameObject($"Piece_{made}");
                piece.transform.SetParent(transform, true);
                piece.transform.position = at;
                // 납작하게 눕되 제각각 조금씩 돌아간 정육면체
                piece.transform.rotation = Quaternion.LookRotation(depth, up) * Quaternion.Euler(0f, dice ? Random.Range(0f, 90f) : Random.Range(-6f, 6f), 0f); // 다진 건 제각각, 편은 거의 나란히
                piece.transform.localScale = new Vector3(size.x / Mathf.Max(Mathf.Abs(parentScale.x), 1e-4f),
                    size.y / Mathf.Max(Mathf.Abs(parentScale.y), 1e-4f), size.z / Mathf.Max(Mathf.Abs(parentScale.z), 1e-4f)); // 조각 크기
                piece.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
                // 겉색 2 : 속(단면)색 1
                Material m = mats.Count > 1 && made % 3 == 2 ? mats[1] : mats[0];
                piece.AddComponent<MeshRenderer>().sharedMaterial = m;
                piece.AddComponent<CutResultPiece>().Init(ingredientName, method, spec); // 썬 방법 기록
            }
            yield return new WaitForSeconds(0.12f * diceSlowness);
        }
        Debug.Log($"{ingredientName}: {CutMethodName(method)} 완료 - {sizeCm.x:0.#}×{sizeCm.z:0.#}cm 네모 조각 {count}개"); // 결과 기록
    }
}
