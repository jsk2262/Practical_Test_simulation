using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The results after submission (ExamEvaluation): by dish, each item with its
/// status (적정 / 감점 / 평가 보류 / 실격), its points, what was seen, the cause
/// the records show (if they do) and what to try next time. Official points
/// (verified official rules only) and practice results are kept apart - a
/// practice result is never shown as an exam score. 미설정 / 미검증 listed as
/// such. Shows the same results every time it's opened.
/// </summary>
public class ExamResultView : MonoBehaviour
{
    private Vector2 scroll;
    private GUIStyle head, sub, line, small, statusStyle, verdict;
    private Texture2D backing;

    /// <summary>The results are up (clicks don't reach the kitchen behind them).</summary>
    public static bool IsOpen => open != null;
    private static ExamResultView open;

    public static void ShowResults()
    {
        if (open != null) return;
        open = new GameObject("ExamResultView").AddComponent<ExamResultView>();
    }

    void OnDestroy()
    {
        if (open == this) open = null;
        if (backing != null) Destroy(backing);
    }

    private static Color StatusColor(ExamEvaluation.Status s) =>
        s == ExamEvaluation.Status.적정 ? new Color(0.55f, 0.9f, 0.55f)
        : s == ExamEvaluation.Status.감점 ? new Color(1f, 0.6f, 0.4f)
        : s == ExamEvaluation.Status.실격 ? new Color(1f, 0.3f, 0.3f)
        : new Color(0.75f, 0.75f, 0.8f);

    private static string StatusText(ExamEvaluation.Status s) => s == ExamEvaluation.Status.평가보류 ? "평가 보류" : s.ToString();

    void OnGUI()
    {
        if (head == null)
        {
            head = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            sub = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            line = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            backing = new Texture2D(1, 1);
            backing.SetPixel(0, 0, new Color(0.1f, 0.1f, 0.12f, 0.96f));
            backing.Apply();
        }
        float w = Mathf.Min(860f, Screen.width - 40f), h = Mathf.Min(700f, Screen.height - 40f);
        Rect r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        GUI.DrawTexture(r, backing);
        GUILayout.BeginArea(new Rect(r.x + 18f, r.y + 14f, r.width - 36f, r.height - 28f));
        GUILayout.Label("최종 평가 결과", head);
        if (PassFail.Decided)
        {
            if (verdict == null) verdict = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
            verdict.normal.textColor = PassFail.Passed ? new Color(0.45f, 0.9f, 0.5f) : new Color(1f, 0.4f, 0.35f);
            GUILayout.Label(PassFail.Passed ? "합격" : "불합격", verdict);
            if (SpeedMode.EverOn)
                GUILayout.Label("※ 빠른 모드로 진행한 연습 결과 - 시간·탐 관련 평가는 생략되어 실제 판정과 다를 수 있어요", line);
            GUILayout.Label($"점수 {PassFail.Score:0.#} / {ExamScoring.Current.totalScore:0} (합격 {ExamScoring.Current.passScore:0}점 이상 · 세부 배점 적용 전 임시 판정)", small);
            foreach (string reason in PassFail.Reasons) GUILayout.Label("· " + reason, line);
            GUILayout.Space(6f);
        }

        // official vs practice, never mixed
        float official = 0f;
        int officialItems = 0, practice = 0, held = 0, dq = 0;
        foreach (ExamEvaluation.Item it in ExamEvaluation.Items)
        {
            if (it.status == ExamEvaluation.Status.실격) dq++;
            else if (it.status == ExamEvaluation.Status.평가보류) held++;
            else if (it.status == ExamEvaluation.Status.감점 && !it.duplicate) // (a duplicate is shown but not counted)
            {
                if (it.official && it.points > 0f) { official += it.points; officialItems++; }
                else practice++;
            }
        }
        string officialText = officialItems > 0 ? $"공식 감점 -{official:0.#}점 ({officialItems}건)" : "공식 점수: 산정 불가 (검증된 공식 배점 없음)";
        GUILayout.Label(officialText + (dq > 0 ? $" · 실격 {dq}건" : ""), sub);
        GUILayout.Label($"연습 피드백 감점 {practice}건 (공식 점수 아님) · 평가 보류 {held}건", small);

        scroll = GUILayout.BeginScrollView(scroll);
        var dishes = new List<string>();
        foreach (ExamEvaluation.Item it in ExamEvaluation.Items) if (!dishes.Contains(it.dish)) dishes.Add(it.dish);
        foreach (string dish in dishes)
        {
            GUILayout.Space(10f);
            GUILayout.Label(dish, sub);
            foreach (ExamEvaluation.Item it in ExamEvaluation.Items)
            {
                if (it.dish != dish) continue;
                GUILayout.Space(4f);
                GUILayout.BeginHorizontal();
                statusStyle.normal.textColor = StatusColor(it.status);
                GUILayout.Label($"{it.title} — {StatusText(it.status)}", statusStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label(it.pointsText, small, GUILayout.Width(220f));
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(it.observed)) GUILayout.Label(it.observed, line);
                if (!string.IsNullOrEmpty(it.cause) && it.status != ExamEvaluation.Status.적정) GUILayout.Label("원인(기록): " + it.cause, small);
                if (!string.IsNullOrEmpty(it.tip) && it.status == ExamEvaluation.Status.감점) GUILayout.Label("다음 연습: " + it.tip, small);
            }
        }

        var sections = new List<string>();
        foreach (ExamEvaluation.Result res in ExamEvaluation.Results)
            if (!sections.Contains(res.section)) sections.Add(res.section);
        foreach (string s in sections)
        {
            GUILayout.Space(10f);
            GUILayout.Label(s, sub);
            foreach (ExamEvaluation.Result res in ExamEvaluation.Results)
                if (res.section == s) GUILayout.Label("· " + res.text, small);
        }
        GUILayout.EndScrollView();
        if (GUILayout.Button("닫기", GUILayout.Height(34f))) Destroy(gameObject);
        GUILayout.EndArea();
    }
}
