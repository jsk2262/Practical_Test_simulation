using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A yes/no window in the middle of the screen (제출하시겠습니까?). While it's
/// up, clicks don't reach the kitchen behind it (ClickDetector checks IsOpen).
/// </summary>
public class ConfirmDialog : MonoBehaviour
{
    public static bool IsOpen => current != null;
    private static ConfirmDialog current;

    private string title;
    private List<string> lines;
    private string yes, no;
    private Action onYes;
    private GUIStyle head, line, button;
    private Texture2D shade, panel;

    public static void Show(string title, List<string> lines, string yes, string no, Action onYes)
    {
        if (current != null) Destroy(current.gameObject);
        var d = new GameObject("ConfirmDialog").AddComponent<ConfirmDialog>();
        d.title = title;
        d.lines = lines ?? new List<string>();
        d.yes = yes;
        d.no = no;
        d.onYes = onYes;
        current = d;
    }

    void OnDestroy()
    {
        if (current == this) current = null;
        if (shade != null) Destroy(shade);
        if (panel != null) Destroy(panel);
    }

    private static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void OnGUI()
    {
        if (head == null)
        {
            head = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            line = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { fontSize = 20 };
            shade = Solid(new Color(0f, 0f, 0f, 0.45f));
            panel = Solid(new Color(0.12f, 0.12f, 0.14f, 0.97f));
        }
        GUI.depth = -100;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), shade);
        float w = Mathf.Min(560f, Screen.width - 40f), h = 190f + lines.Count * 30f;
        Rect r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        GUI.DrawTexture(r, panel);
        GUILayout.BeginArea(new Rect(r.x + 20f, r.y + 18f, r.width - 40f, r.height - 36f));
        GUILayout.Label(title, head);
        GUILayout.Space(10f);
        foreach (string l in lines) GUILayout.Label(l, line);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(no, button, GUILayout.Height(44f))) Destroy(gameObject);
        GUILayout.Space(16f);
        if (GUILayout.Button(yes, button, GUILayout.Height(44f)))
        {
            Action act = onYes;
            Destroy(gameObject);
            act?.Invoke();
        }
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }
}
