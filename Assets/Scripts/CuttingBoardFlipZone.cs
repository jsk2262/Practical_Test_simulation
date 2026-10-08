using UnityEngine;

/// <summary>
/// The yellow button at the cutting board's left corner - shows "뒤집기" while
/// the cursor is near; clicking flips the board immediately
/// (CuttingBoardController.Flip()).
/// </summary>
public class CuttingBoardFlipZone : CuttingBoardZoneBase
{
    [SerializeField] private CuttingBoardController board;

    protected override string Label => "뒤집기";
    protected override Color MarkerColor => new Color(1f, 0.95f, 0.6f, 0.7f);

    protected override void OnZoneClicked() => board?.Flip();
}
