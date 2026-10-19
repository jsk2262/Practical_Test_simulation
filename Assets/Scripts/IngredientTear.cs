using System.Collections;
using UnityEngine;

/// <summary>
/// 손으로 찢기 (불린 목이버섯): 손이 빈 채로 길게 누르면 양손으로 잡아 찢어
/// 3조각이 된다 (짧게 누르면 그냥 집는다). 칼은 쓰지 않고, 도마가 아니어도 된다.
/// 조각은 원래 모양을 조금 작게 해 세 갈래로 벌려 놓는다 - 팬에 넣으면 다른
/// 썬 채소처럼 조각마다 따로 익는다 (EnsurePieceGroup).
/// </summary>
public partial class IngredientItem
{
    [Header("손으로 찢기")]
    [Tooltip("길게 눌러 찢을 때 나오는 조각 수.")]
    [SerializeField, Range(2, 6)] private int tearPieces = 3;

    /// <summary>지금 손으로 찢을 수 있음: 불려 둔 목이버섯처럼 찢어 쓰는 재료, 아직 안 찢음.</summary>
    public bool CanTearByHand => isSoakable && CurrentSoakState == SoakState.Soaked && CurrentState != State.Cut && !isCutting && !IsHeld;

    private IClickable tearAction;
    /// <summary>길게 누르기가 다 차면 부르는 동작 (ClickDetector).</summary>
    public IClickable TearAction => tearAction ?? (tearAction = new TearByHand(this));

    private class TearByHand : IClickable
    {
        private readonly IngredientItem ing;
        public TearByHand(IngredientItem i) { ing = i; }
        public void OnClicked() => ing.StartTear();
    }

    private void StartTear()
    {
        if (!CanTearByHand) return;
        StartCoroutine(TearRoutine());
    }

    private IEnumerator TearRoutine()
    {
        isCutting = true;
        if (rend == null) rend = GetComponent<Renderer>();
        Vector3 center = rend != null ? rend.bounds.center : transform.position;
        // 양손이 버섯 양쪽을 잡고
        HandController.Instance?.GoTo(center + new Vector3(0.25f, -0.15f, -0.6f), null);
        SupportHandController.Instance?.ShowNear(center + new Vector3(-0.25f, -0.15f, -0.6f));

        Bounds mb = sharedMesh != null ? sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
        Vector3 ms = mb.size;
        int upAxis = 1;
        // (모델에서 조리대 위쪽을 향한 축 - 두께 방향은 그대로 둔다)
        float best = -1f;
        for (int a = 0; a < 3; a++)
        {
            Vector3 d = Vector3.zero; d[a] = 1f;
            float dot = Mathf.Abs(Vector3.Dot(transform.TransformDirection(d).normalized, Carryable.CounterUp));
            if (dot > best) { best = dot; upAxis = a; }
        }
        Vector3 pieceScale = Vector3.one * 0.62f;
        pieceScale[upAxis] = 1f;
        int n = Mathf.Max(2, tearPieces);
        CutSpec spec = BuildSpec(CutMethod.Tear, ReferenceThicknessCm(CutMethod.Tear));

        for (int i = 0; i < n; i++)
        {
            // 한 번 당길 때마다 한 조각씩 떨어져 나감
            HandController.Instance?.PlayChopGesture();
            yield return new WaitForSeconds(0.22f);

            GameObject piece = new GameObject($"Piece_{i}");
            piece.transform.SetParent(transform, false);
            piece.AddComponent<MeshFilter>().sharedMesh = sharedMesh;
            piece.AddComponent<MeshRenderer>().sharedMaterials = rend.sharedMaterials;
            piece.AddComponent<CutResultPiece>().Init(ingredientName, CutMethod.Tear, spec);

            // 세 갈래로 벌려 놓고, 찢긴 것처럼 제각각 조금씩 돌려 둔다
            float ang = (i / (float)n) * Mathf.PI * 2f + 0.4f;
            Vector3 offset = Vector3.zero;
            int a1 = (upAxis + 1) % 3, a2 = (upAxis + 2) % 3;
            offset[a1] = Mathf.Cos(ang) * ms[a1] * 0.55f;
            offset[a2] = Mathf.Sin(ang) * ms[a2] * 0.55f;
            Vector3 axis = Vector3.zero; axis[upAxis] = 1f;
            Quaternion turn = Quaternion.AngleAxis(Random.Range(-35f, 35f) + i * 120f, axis);
            piece.transform.localRotation = turn;
            piece.transform.localScale = pieceScale;
            piece.transform.localPosition = mb.center + offset - turn * Vector3.Scale(mb.center, pieceScale);
        }

        CurrentState = State.Cut;
        if (rend != null) rend.enabled = false;
        isCutting = false;
        SupportHandController.Instance?.Hide();
        ExpandColliderToCoverPieces();
        Debug.Log($"{ingredientName}: 손으로 찢기 완료 ({n}조각)");
    }
}
