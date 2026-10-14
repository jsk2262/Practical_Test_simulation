using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Under the 정리 tab: 제출 → (마무리 정리) → 정리 완료 → 결과 보기 (ExamFlow).
/// Each step is final, so a window asks first (두 접시 모두 제출하시겠습니까?).
/// In the scene (Tools > Kitchen > Setup Exam Buttons); put up by itself at play if it isn't.
/// </summary>
public class SubmitButton : MonoBehaviour, IClickable
{
    [SerializeField] private TextMesh label;
    [SerializeField] private Renderer back;
    private float confirmUntil;

    void Awake()
    {
        if (label == null) label = GetComponentInChildren<TextMesh>();
        if (back == null) back = GetComponent<Renderer>();
        if (back != null) back.material = new Material(back.sharedMaterial != null ? back.sharedMaterial : new Material(Shader.Find("Sprites/Default")));
    }

    void Start() => Refresh();

    private static readonly Color Idle = new Color(0.32f, 0.36f, 0.4f), Confirm = new Color(0.75f, 0.45f, 0.2f), Done = new Color(0.25f, 0.45f, 0.3f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindFirstObjectByType<SubmitButton>() != null) return;
        CleanupTabButton tab = FindFirstObjectByType<CleanupTabButton>();
        if (tab == null) return;
        Renderer tr = tab.GetComponent<Renderer>();
        Bounds tb = tr != null ? tr.bounds : new Bounds(tab.transform.position, new Vector3(2f, 0.6f, 0.1f));

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "SubmitButton";
        go.transform.SetParent(tab.transform.parent, true);
        go.transform.rotation = Quaternion.identity;
        go.transform.position = new Vector3(tb.center.x, tb.min.y - tb.size.y * 0.75f, tb.min.z - 0.01f);
        go.transform.localScale = Vector3.one;
        Vector3 ls = go.transform.lossyScale;
        go.transform.localScale = new Vector3(tb.size.x / ls.x, tb.size.y / ls.y, 1f);
        var b = go.AddComponent<SubmitButton>();
        b.back = go.GetComponent<Renderer>();
        b.back.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = Idle };
        Object.Destroy(go.GetComponent<Collider>());
        go.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 0.2f);

        var t = new GameObject("Label");
        t.transform.SetParent(go.transform, false);
        t.transform.localPosition = new Vector3(0f, 0f, -0.05f);
        ls = go.transform.lossyScale;
        t.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f);
        b.label = t.AddComponent<TextMesh>();
        b.label.fontSize = 60;
        b.label.characterSize = tb.size.y * 0.09f;
        b.label.anchor = TextAnchor.MiddleCenter;
        b.label.alignment = TextAlignment.Center;
        b.label.color = Color.white;
        b.Refresh();
    }

    public void OnClicked()
    {
        if (ExamFlow.Current == ExamFlow.Phase.Finished) { ExamResultView.ShowResults(); return; }
        if (ConfirmDialog.IsOpen) return;
        if (ExamFlow.Current == ExamFlow.Phase.Cooking)
        {
            // asked once more, with what's on each serving plate
            var lines = new List<string>();
            var plates = new List<PlatingDestination>(PlatingDestination.All);
            plates.RemoveAll(d => d == null || d.Plate == null);
            plates.Sort((a, b) => string.CompareOrdinal(a.Plate.name, b.Plate.name)); // (PlatingPlate1, 2)
            foreach (PlatingDestination d in plates) lines.Add($"{d.Plate.name}: {PlateText(d.Plate)}");
            lines.Add("제출하면 요리를 더 고칠 수 없어요.");
            ConfirmDialog.Show("두 접시 모두 제출하시겠습니까?", lines, "제출", "취소", () => { ExamFlow.SubmitDishes(); Refresh(); });
        }
        else
        {
            ConfirmDialog.Show("정리를 마치고 시험을 종료하시겠습니까?", new List<string> { "종료하면 정리·위생까지 채점하고 결과가 나와요." },
                "종료", "취소", () => { ExamFlow.FinishCleanup(); Refresh(); });
        }
    }

    /// <summary>What's on a serving plate: its food (by name), or 비어 있음.</summary>
    private static string PlateText(MovableBowl plate)
    {
        var names = new List<string>();
        foreach (Carryable c in plate.Contents)
            if (c is IngredientItem ing && !names.Contains(ing.IngredientName)) names.Add(ing.IngredientName);
        bool sauce = PanSauce.Of(plate, false) is PanSauce s && s.Amount > 0.01f;
        if (names.Count == 0) return sauce ? "소스만 있음" : "비어 있음";
        return string.Join(", ", names) + (sauce ? " + 소스" : "");
    }

    void Update()
    {
        if (confirmUntil > 0f && Time.unscaledTime > confirmUntil) { confirmUntil = 0f; Refresh(); }
        if (label != null && ExamFlow.Current == ExamFlow.Phase.Finished && label.text != "결과 보기") Refresh();
    }

    private void Refresh()
    {
        if (label == null) return;
        bool confirming = confirmUntil > 0f;
        switch (ExamFlow.Current)
        {
            case ExamFlow.Phase.Cooking: label.text = "제출"; break;
            case ExamFlow.Phase.Cleaning: label.text = "정리 완료"; break;
            default: label.text = "결과 보기"; break;
        }
        if (back != null) back.material.color = confirming ? Confirm : ExamFlow.Current == ExamFlow.Phase.Cooking ? Idle : Done;
    }
}
