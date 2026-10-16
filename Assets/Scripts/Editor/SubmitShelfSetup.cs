using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Kitchen > Setup Submit Shelf: 제출대(SubmitShelf)를 씬에 미리 놓는다
/// (제출 버튼 위 ~ 타이머 아래). 놓인 뒤엔 위치·크기를 손으로 고쳐도 그대로
/// 쓴다 - 플레이 때 다시 만들지 않는다. 재질은 Assets/Generated/SubmitShelf에 저장.
/// </summary>
public static class SubmitShelfSetup
{
    private const string Folder = "Assets/Generated/SubmitShelf";

    [MenuItem("Tools/Kitchen/Setup Submit Shelf")]
    public static void Setup()
    {
        if (Application.isPlaying) { Debug.LogWarning("[제출대] 플레이 중에는 만들 수 없어요"); return; }
        SubmitShelf existing = Object.FindFirstObjectByType<SubmitShelf>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("[제출대] 이미 씬에 있어요 - 그대로 둡니다 (다시 만들려면 지우고 실행)");
            return;
        }
        if (!AssetDatabase.IsValidFolder(Folder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
            AssetDatabase.CreateFolder("Assets/Generated", "SubmitShelf");
        }
        SubmitShelf shelf = SubmitShelf.Build(Mat("Board", SubmitShelf.BoardColor), Mat("Frame", SubmitShelf.FrameColor), Mat("Ledge", SubmitShelf.LedgeColor));
        if (shelf == null) { Debug.LogWarning("[제출대] 제출 버튼(SubmitButton)이 씬에 없어 만들 수 없어요"); return; }
        Undo.RegisterCreatedObjectUndo(shelf.gameObject, "Setup Submit Shelf");
        Selection.activeGameObject = shelf.gameObject;
        EditorSceneManager.MarkSceneDirty(shelf.gameObject.scene);
        Debug.Log($"[제출대] 씬에 놓음: {shelf.transform.position}");
    }

    private static Material Mat(string name, Color c)
    {
        string path = $"{Folder}/SubmitShelf_{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        m = new Material(lit != null ? lit : Shader.Find("Standard")) { color = c };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
