using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class CutMethodChooser : MonoBehaviour
{
    public static CutMethodChooser Instance { get; private set; }

    private const float Step = 0.1f;
    private const float MinCm = 0.1f;
    private const float MaxCm = 5f;

    private static readonly Color ValueBgIdle = new Color(0.95f, 0.95f, 0.9f);
    private static readonly Color ValueBgEditing = new Color(1f, 0.92f, 0.55f);

    private GameObject methodPanel;
    private GameObject specPanel;
    private TextMesh specTitle;
    private Action<IngredientItem.CutMethod, CutSpec> onChosen;
    private IngredientItem.CutMethod pendingMethod;
    private Vector3 shownPos;

    private float curWidth, curLength, curThickness;

    // Indexed 0=가로 1=세로 2=두께 - lets the click-to-type editor and the -/+
    // steppers share the same read/write/refresh plumbing per row.
    private readonly Func<float>[] rowGetters = new Func<float>[3];
    private readonly Action<float>[] rowSetters = new Action<float>[3];
    private readonly TextMesh[] rowValueTexts = new TextMesh[3];
    private readonly Renderer[] rowValueBgRenderers = new Renderer[3];
    private readonly GameObject[] rowRoots = new GameObject[3];
    private readonly TextMesh[] rowLabels = new TextMesh[3];
    private static readonly string[] RowNames = { "가로", "세로", "두께" };

    /// <summary>사각썰기: a cube - one size for 가로·세로·두께, so one row only.</summary>
    private bool SingleSize => pendingMethod == IngredientItem.CutMethod.Cube || pendingMethod == IngredientItem.CutMethod.Dice; // (다지기 too: one size)

    private int editingRow = -1;
    private string editBuffer = "";

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        if (editingRow < 0) return;
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb.backspaceKey.wasPressedThisFrame && editBuffer.Length > 0)
        {
            editBuffer = editBuffer.Substring(0, editBuffer.Length - 1);
            UpdateEditDisplay();
        }

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
        {
            // Enter: on to the next empty box; with all of them filled, cut.
            CommitEdit();
            int next = FirstEmptyRow();
            if (next >= 0) BeginEdit(next);
            else ConfirmSpec();
        }
        else if (kb.tabKey.wasPressedThisFrame)
        {
            // Tab: down to the next box (round to the first again).
            int rows = SingleSize ? 1 : 3;
            int next = (editingRow + 1) % rows;
            CommitEdit();
            BeginEdit(next);
        }
        else if (kb.escapeKey.wasPressedThisFrame)
        {
            CancelEdit();
        }
    }

    /// <summary>onTrim: "다듬기" (peel/stem/seeds off first) - offered only when
    /// given (the ingredient has something to trim and hasn't been trimmed).</summary>
    private readonly System.Collections.Generic.Dictionary<GameObject, Vector3> baseScales = new System.Collections.Generic.Dictionary<GameObject, Vector3>(); // 창마다 원래 크기

    /// <summary>창을 확대율만큼 줄여 화면에서 늘 같은 크기로 (전체 보기 때 크기 기준).</summary>
    private void FitToScreen(GameObject panel)
    {
        if (!baseScales.TryGetValue(panel, out Vector3 baseScale)) baseScales[panel] = baseScale = panel.transform.localScale; // 처음 크기 기억
        panel.transform.localScale = baseScale * KitchenCamera.ZoomScale; // 확대율만큼
    }

    private static void FaceCamera(GameObject panel) // 글자와 클릭 영역을 카메라와 평행하게 둔다.
    {
        Camera cam = Camera.main;
        if (cam != null) panel.transform.rotation = cam.transform.rotation;
    }

    public void Show(Vector3 worldPos, Action<IngredientItem.CutMethod, CutSpec> callback, Action onTrim = null)
    {
        CommitEdit();
        onChosen = callback;
        this.onTrim = onTrim;
        Camera camera = Camera.main; // 메뉴의 화면 위치는 유지하면서 깊이만 앞으로 옮긴다.
        if (camera != null)
        {
            Vector3 screen = camera.WorldToScreenPoint(worldPos);
            screen.z = Mathf.Max(camera.nearClipPlane + 0.5f, 2f); // 주방과 안내 표식보다 카메라에 가깝게 둔다.
            worldPos = camera.ScreenToWorldPoint(screen);
        }
        shownPos = worldPos;

        if (methodPanel == null) BuildMethodPanel();
        if (specPanel == null) BuildSpecPanel();
        // 주변 버튼과 안내 글자는 그대로 둔다. 메뉴는 카메라 가까이에 표시한다.

        methodPanel.transform.position = worldPos;
        FaceCamera(methodPanel); // 칼질 선택창을 회전된 화면 정면에 맞춘다.
        FitToScreen(methodPanel); // 확대 중이면 화면에 맞는 크기로
        methodPanel.SetActive(true);
        specPanel.SetActive(false);
        if (trimButton != null) trimButton.SetActive(onTrim != null);
    }

    private readonly System.Collections.Generic.List<Renderer> hiddenSceneLabels = new System.Collections.Generic.List<Renderer>(); // 켜져 있던 글자만 기억한다.
    private void HideSceneLabels()
    {
        if (hiddenSceneLabels.Count > 0) return; // 창 전환 중에는 다시 기록하지 않는다.
        foreach (TextMesh label in FindObjectsByType<TextMesh>(FindObjectsSortMode.None))
        {
            if (label.transform.IsChildOf(methodPanel.transform) || label.transform.IsChildOf(specPanel.transform)) continue;
            Renderer renderer = label.GetComponent<Renderer>();
            if (renderer != null && renderer.enabled) { hiddenSceneLabels.Add(renderer); renderer.enabled = false; }
        }
    }
    private void RestoreSceneLabels() // 창을 닫으면 원래 켜져 있던 글자만 되돌린다.
    {
        foreach (Renderer renderer in hiddenSceneLabels) if (renderer != null) renderer.enabled = true;
        hiddenSceneLabels.Clear();
    }
    void OnDestroy() { RestoreSceneLabels(); } // 씬 전환으로 종료되어도 남기지 않는다.

    private Action onTrim;
    private GameObject trimButton;

    /// <summary>The ring (or the 규격 screen) is up.</summary>
    public bool IsOpen => (methodPanel != null && methodPanel.activeSelf) || (specPanel != null && specPanel.activeSelf);

    /// <summary>Whether a click at this screen point lands on the menu itself.</summary>
    public bool IsOver(Ray ray)
    {
        // (the panels, not this component's object - that's the whole KitchenStation)
        foreach (RaycastHit h in Physics.RaycastAll(ray, 500f))
        {
            Transform t = h.collider.transform;
            if ((methodPanel != null && methodPanel.activeSelf && t.IsChildOf(methodPanel.transform))
                || (specPanel != null && specPanel.activeSelf && t.IsChildOf(specPanel.transform))) return true;
        }
        return false;
    }

    /// <summary>Clicked off to the side: the menu goes away, nothing is cut.</summary>
    public void Close()
    {
        CancelEdit();
        onChosen = null;
        onTrim = null;
        if (methodPanel != null) methodPanel.SetActive(false);
        if (specPanel != null) specPanel.SetActive(false);
        RestoreSceneLabels(); // 취소 시 안내 글자를 되돌린다.
    }

    private void Trim()
    {
        Action trim = onTrim;
        onTrim = null;
        onChosen = null;
        methodPanel.SetActive(false);
        RestoreSceneLabels(); // 다듬기를 시작하기 전에 안내를 되돌린다.
        trim?.Invoke();
    }

    private void BuildMethodPanel()
    {
        methodPanel = new GameObject("CutMethodPanel");
        methodPanel.transform.SetParent(transform, false);

        // A ring menu all the way round the knife (center left open so the
        // knife and ingredient stay visible), the cuts spaced evenly from 12
        // o'clock clockwise.
        // (its collider stays: a click on the ring itself does nothing)
        CreateMeshObject(methodPanel.transform, "Ring", new Vector3(0f, 0f, -0.02f),
            AnnulusMesh(RingInner, RingOuter, 64), RingColor);

        // The four named cuts open the 가로/세로/두께 screen pre-filled with
        // that cut's usual size; "이외" covers anything else (a custom or
        // triangular spec, etc.) and goes straight to the same screen without
        // presupposing a shape.
        IngredientItem.CutMethod[] methods =
        {
            IngredientItem.CutMethod.Slice, IngredientItem.CutMethod.Julienne, IngredientItem.CutMethod.DiagonalCut,
            IngredientItem.CutMethod.Cube, IngredientItem.CutMethod.Dice, IngredientItem.CutMethod.Other,
        };
        // Seven places round the ring: the six cuts, then 다듬기 (shown only when
        // the ingredient has something to trim).
        int slots = methods.Length + 1;
        for (int i = 0; i < slots; i++)
        {
            float a = (90f - i * 360f / slots) * Mathf.Deg2Rad;
            Vector3 at = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * ButtonOrbit + new Vector3(0f, 0f, -0.1f);
            if (i < methods.Length) CreateMethodButton(IngredientItem.CutMethodName(methods[i]), at, methods[i]);
            else
            {
                trimButton = CreateMeshObject(methodPanel.transform, "Btn_다듬기", at, DiscMesh(ButtonRadius, 40), new Color(0.88f, 0.95f, 0.85f, 0.95f));
                CreateLabel(trimButton.transform, "다듬기", new Vector3(0f, 0f, -0.05f), 24, Color.black, TextAnchor.MiddleCenter);
                trimButton.AddComponent<SimpleActionButton>().Init(Trim);
            }
        }

        methodPanel.SetActive(false);
    }

    private const float RingInner = 0.95f, RingOuter = 2.65f, ButtonOrbit = 1.8f, ButtonRadius = 0.68f; // (seven round the ring now)
    private static readonly Color RingColor = new Color(0.9f, 0.88f, 0.8f, 0.55f);
    private static readonly Color ButtonColor = new Color(0.97f, 0.96f, 0.92f, 0.95f);

    private static Material SpriteMaterial(Color color)
    {
        Shader s = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        Material m = new Material(s) { color = color };
        return m;
    }

    private GameObject CreateMeshObject(Transform parent, string name, Vector3 localPos, Mesh mesh, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = SpriteMaterial(color);
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    /// <summary>A flat ring in the XY plane, facing the camera (-Z).</summary>
    private static Mesh AnnulusMesh(float inner, float outer, int segments)
    {
        var v = new Vector3[segments * 2];
        var t = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            v[i * 2] = d * inner;
            v[i * 2 + 1] = d * outer;
            int n = (i + 1) % segments;
            int k = i * 6;
            t[k] = i * 2; t[k + 1] = n * 2; t[k + 2] = i * 2 + 1;
            t[k + 3] = i * 2 + 1; t[k + 4] = n * 2; t[k + 5] = n * 2 + 1;
        }
        var m = new Mesh { vertices = v, triangles = t };
        FixWinding(m);
        return m;
    }

    private static Mesh DiscMesh(float radius, int segments)
    {
        var v = new Vector3[segments + 1];
        var t = new int[segments * 3];
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            v[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
            t[i * 3] = 0; t[i * 3 + 1] = (i + 1) % segments + 1; t[i * 3 + 2] = i + 1;
        }
        var m = new Mesh { vertices = v, triangles = t };
        FixWinding(m);
        return m;
    }

    /// <summary>Faces the triangles toward -Z (the camera) whichever way they were wound.</summary>
    private static void FixWinding(Mesh m)
    {
        m.RecalculateNormals();
        if (m.normals.Length > 0 && m.normals[0].z > 0f)
        {
            int[] t = m.triangles;
            for (int i = 0; i < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
            m.triangles = t;
            m.RecalculateNormals();
        }
        m.RecalculateBounds();
    }

    private void BuildSpecPanel()
    {
        specPanel = new GameObject("SpecPanel");
        specPanel.transform.SetParent(transform, false);

        CreateQuad(specPanel.transform, "Background", new Vector3(0f, 0.25f, -0.02f), new Vector2(6.4f, 3.9f), new Color(0.9f, 0.88f, 0.8f, 0.95f));
        // (a collider on the panel itself, so a click on it doesn't count as "off to the side")
        specPanel.transform.Find("Background").gameObject.AddComponent<BoxCollider>();

        specTitle = CreateLabel(specPanel.transform, "", new Vector3(0f, 1.8f, -0.1f), 28, Color.black, TextAnchor.MiddleCenter);

        CreateSpecRow(0, 0.9f, "가로", () => curWidth, v => curWidth = v);
        CreateSpecRow(1, 0.1f, "세로", () => curLength, v => curLength = v);
        CreateSpecRow(2, -0.7f, "두께", () => curThickness, v => curThickness = v);

        CreateActionButton(specPanel.transform, "← 다시 선택", new Vector3(-1.3f, -1.5f, -0.1f), BackToMethods, new Vector2(2.3f, 0.5f), 24);
        CreateActionButton(specPanel.transform, "자르기", new Vector3(1.3f, -1.5f, -0.1f), () => { CommitEdit(); ConfirmSpec(); });

        CreateLabel(specPanel.transform, "숫자를 입력하고 Enter: 다음 칸으로, 다 채우면 자르기 (칸을 클릭해 고치기 / Esc 취소)",
            new Vector3(0f, -2.5f, -0.1f), 14, new Color(0.35f, 0.35f, 0.35f), TextAnchor.MiddleCenter);

        specPanel.SetActive(false);
    }

    /// <summary>One 가로/세로/두께 row: a label, a -/+ stepper pair, and a live cm
    /// readout that's itself a button - clicking it opens click-to-type entry
    /// for when the -/+ step (0.1cm) would take too many clicks to reach a very
    /// different value.</summary>
    private void CreateSpecRow(int index, float y, string label, Func<float> get, Action<float> set)
    {
        rowGetters[index] = get;
        rowSetters[index] = set;

        Transform row = new GameObject("Row_" + label).transform;
        row.SetParent(specPanel.transform, false);
        rowRoots[index] = row.gameObject;

        rowLabels[index] = CreateLabel(row, label, new Vector3(-3.0f, y, -0.1f), 26, Color.black, TextAnchor.MiddleLeft);

        GameObject valueBtn = CreateActionButton(row, "cm", new Vector3(-0.3f, y, -0.1f),
            () => BeginEdit(index), new Vector2(1.5f, 0.5f));
        rowValueTexts[index] = valueBtn.GetComponentInChildren<TextMesh>();
        rowValueBgRenderers[index] = valueBtn.GetComponent<Renderer>();

        CreateActionButton(row, "-", new Vector3(-1.6f, y, -0.1f), () =>
        {
            CommitEdit();
            set(Mathf.Clamp(get() - Step, MinCm, MaxCm));
            RefreshRow(index);
        }, new Vector2(0.4f, 0.4f));

        CreateActionButton(row, "+", new Vector3(1.1f, y, -0.1f), () =>
        {
            CommitEdit();
            set(Mathf.Clamp(get() + Step, MinCm, MaxCm));
            RefreshRow(index);
        }, new Vector2(0.4f, 0.4f));
    }

    /// <summary>A box shows its number, or stays blank until something's entered.</summary>
    private void RefreshRow(int index)
    {
        float v = rowGetters[index]();
        rowValueTexts[index].text = v > 0f ? v.ToString("0.0") + "cm" : "      cm";
    }

    /// <summary>The first (shown) box still left blank, or -1.</summary>
    private int FirstEmptyRow()
    {
        int rows = SingleSize ? 1 : 3;
        for (int i = 0; i < rows; i++) if (rowGetters[i]() <= 0f) return i;
        return -1;
    }

    private void BeginEdit(int row)
    {
        if (editingRow == row) return;
        CommitEdit();

        editingRow = row;
        float current = rowGetters[row]();
        editBuffer = current > 0f ? current.ToString("0.0") : "";
        rowValueBgRenderers[row].material.color = ValueBgEditing;
        if (Keyboard.current != null) Keyboard.current.onTextInput += OnTextInput;
        UpdateEditDisplay();
    }

    private void OnTextInput(char c)
    {
        if (editingRow < 0) return;

        if (char.IsDigit(c))
        {
            if (editBuffer.Length < 6) editBuffer += c;
        }
        else if (c == '.' && !editBuffer.Contains("."))
        {
            editBuffer += c;
        }
        else
        {
            return;
        }

        UpdateEditDisplay();
    }

    private void UpdateEditDisplay()
    {
        rowValueTexts[editingRow].text = editBuffer + "|cm";
    }

    private void CommitEdit()
    {
        if (editingRow < 0) return;
        int row = editingRow;

        if (float.TryParse(editBuffer, out float parsed))
        {
            rowSetters[row](Mathf.Clamp(parsed, MinCm, MaxCm));
        }

        StopEditingInput(row);
        RefreshRow(row);
    }

    private void CancelEdit()
    {
        if (editingRow < 0) return;
        int row = editingRow;

        StopEditingInput(row);
        RefreshRow(row);
    }

    private void StopEditingInput(int row)
    {
        if (Keyboard.current != null) Keyboard.current.onTextInput -= OnTextInput;
        rowValueBgRenderers[row].material.color = ValueBgIdle;
        editingRow = -1;
        editBuffer = "";
    }

    private GameObject CreateActionButton(Transform parent, string label, Vector3 localPos, Action onClick, Vector2 size = default, int fontSize = 28)
    {
        if (size == default) size = new Vector2(1.3f, 0.5f);

        GameObject btn = GameObject.CreatePrimitive(PrimitiveType.Quad);
        btn.name = "Btn_" + label.Replace("\n", "_");
        btn.transform.SetParent(parent, false);
        btn.transform.localPosition = localPos;
        btn.transform.localScale = new Vector3(size.x, size.y, 1f);

        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = new Color(0.95f, 0.95f, 0.9f, 1f);
        btn.GetComponent<Renderer>().material = mat;

        TextMesh tm = CreateLabel(btn.transform, label, new Vector3(0f, 0f, -0.05f), fontSize, Color.black, TextAnchor.MiddleCenter);
        // Undo the button's own non-uniform scale so the label renders at its
        // real size instead of being squashed by the quad's local scale.
        tm.transform.localScale = new Vector3(1f / size.x, 1f / size.y, 1f);

        btn.AddComponent<SimpleActionButton>().Init(onClick);
        return btn;
    }

    private void CreateMethodButton(string label, Vector3 localOffset, IngredientItem.CutMethod method)
    {
        GameObject btn = CreateMeshObject(methodPanel.transform, "Btn_" + label, localOffset, DiscMesh(ButtonRadius, 40), ButtonColor);
        CreateLabel(btn.transform, label, new Vector3(0f, 0f, -0.05f), 24, Color.black, TextAnchor.MiddleCenter);
        btn.AddComponent<SimpleActionButton>().Init(() => Choose(method));
    }

    private TextMesh CreateLabel(Transform parent, string text, Vector3 localPos, int fontSize, Color color, TextAnchor anchor)
    {
        GameObject go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;

        TextMesh tm = go.AddComponent<TextMesh>();
        KoreanFontFallback.Apply(tm); // 웹에서도 한글이 보이게 한글 글꼴
        tm.text = text;
        tm.fontSize = fontSize;
        tm.characterSize = 0.16f;
        tm.anchor = anchor;
        tm.color = color;
        return tm;
    }

    private void CreateQuad(Transform parent, string name, Vector3 localPos, Vector2 size, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);

        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = color;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        UnityEngine.Object.Destroy(go.GetComponent<Collider>());
    }

    public void Choose(IngredientItem.CutMethod method)
    {
        CommitEdit();
        pendingMethod = method;
        methodPanel.SetActive(false);

        // Blank boxes, the first one already taking typing - the player writes
        // the spec themselves (it used to come pre-filled with the usual size).
        curWidth = curLength = curThickness = 0f;
        bool single = SingleSize;
        for (int i = 0; i < 3; i++)
        {
            rowRoots[i].SetActive(i == 0 || !single);
            rowLabels[i].text = single ? "크기" : RowNames[i];
            RefreshRow(i);
        }

        specTitle.text = method == IngredientItem.CutMethod.Other ? "규격 입력 (가로·세로·두께)"
            : single ? IngredientItem.CutMethodName(method) + " 규격 (가로=세로=두께)"
            : IngredientItem.CutMethodName(method) + " 규격";
        specPanel.transform.position = shownPos;
        FaceCamera(specPanel); // 규격 입력창도 동일한 화면 방향을 쓴다.
        FitToScreen(specPanel); // 확대 중이면 화면에 맞는 크기로
        specPanel.SetActive(true);
        BeginEdit(0);
    }

    /// <summary>Back from the 규격 screen to pick a different cut.</summary>
    private void BackToMethods()
    {
        CommitEdit();
        specPanel.SetActive(false);
        methodPanel.transform.position = shownPos;
        FaceCamera(methodPanel); // 뒤로 돌아와도 화면 방향을 유지한다.
        FitToScreen(methodPanel); // 확대 중이면 화면에 맞는 크기로
        methodPanel.SetActive(true);
    }

    private void ConfirmSpec()
    {
        // Every box filled first - the next blank one takes the typing.
        int empty = FirstEmptyRow();
        if (empty >= 0) { BeginEdit(empty); return; }
        if (SingleSize) curLength = curThickness = curWidth;

        specPanel.SetActive(false);
        RestoreSceneLabels(); // 썰기를 시작하기 전에 안내를 되돌린다.
        Action<IngredientItem.CutMethod, CutSpec> callback = onChosen;
        onChosen = null;

        CutSpec spec = new CutSpec(curWidth, curLength, curThickness);
        callback?.Invoke(pendingMethod, spec);
    }
}
