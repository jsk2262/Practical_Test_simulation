using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Kitchen > Place Cutting Board Buttons - puts the flip (left) and
/// wash (right) buttons back into the board's bottom corners, wherever the
/// board has been moved to: the same insets they had in the original layout
/// (SampleScene: board bounds x -2.04..1.37, y -2.90.., zones at
/// x -1.77 / 1.12, y -2.60). Depth (z) is left as it is. Undoable; save after.
/// </summary>
public static class CuttingBoardButtonsPlacer
{
    private const float FlipInsetX = 0.27f;
    private const float WashInsetX = 0.25f;
    private const float InsetY = 0.30f;

    [MenuItem("Tools/Kitchen/Place Cutting Board Buttons")]
    private static void Place()
    {
        CuttingBoardController board = Object.FindFirstObjectByType<CuttingBoardController>(FindObjectsInactive.Include);
        Collider boardCollider = board != null ? board.GetComponent<Collider>() : null;
        if (boardCollider == null) { Debug.LogError("[BoardButtons] No cutting board (with a collider) in the open scene."); return; }

        Bounds b = boardCollider.bounds;
        int moved = 0;
        foreach (CuttingBoardFlipZone z in Object.FindObjectsByType<CuttingBoardFlipZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            moved += Move(z.transform, b.min.x + FlipInsetX, b.min.y + InsetY);
        foreach (CuttingBoardWashZone z in Object.FindObjectsByType<CuttingBoardWashZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            moved += Move(z.transform, b.max.x - WashInsetX, b.min.y + InsetY);

        EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        Debug.Log($"[BoardButtons] {moved} button(s) moved to {board.name}'s corners (board {b.center:F2}/{b.size:F2}) - save the scene.");
    }

    private static int Move(Transform t, float x, float y)
    {
        Undo.RecordObject(t, "Place Cutting Board Buttons");
        t.position = new Vector3(x, y, t.position.z);
        Debug.Log($"[BoardButtons] {t.name} -> {t.position:F2}");
        return 1;
    }
}
