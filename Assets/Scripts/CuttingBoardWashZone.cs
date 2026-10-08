using UnityEngine;

/// <summary>
/// The white button at the cutting board's right corner - shows "씻기" while
/// the cursor is near; clicking carries the board (with the knife resting on
/// it) over to the sink, washes both clean, and brings it back
/// (CuttingBoardController.WashAtSink). Only an empty board can go - an
/// ingredient on it has to be cleared off first. Shown only while clean-up
/// mode (the 정리 tab) is on.
/// </summary>
public class CuttingBoardWashZone : CuttingBoardZoneBase
{
    [SerializeField] private HygieneTracker knifeHygiene;

    protected override string Label => "씻기";

    /// <summary>Only during clean-up (the 정리 tab), along with the dishes.</summary>
    protected override bool IsAvailable => CleanupMode.IsOn;
    protected override Color MarkerColor => new Color(0.97f, 0.97f, 0.97f, 0.7f);

    protected override void OnZoneClicked()
    {
        CuttingBoardController board = CuttingBoardController.Instance;
        if (board == null) return;

        // Picked up by hand (no animation) to be taken to the sink and washed
        // there (BoardSinkWash) - the old carried-over-and-back wash is the
        // fallback if that isn't on the board.
        if (board.GetComponent<BoardSinkWash>() is BoardSinkWash byHand)
        {
            if (!byHand.TryPickUp(out string reason)) ShowToast(reason);
            return;
        }
        WashBasin sink = FindFirstObjectByType<WashBasin>();
        if (sink != null && !board.WashAtSink(sink.transform, knifeHygiene))
            ShowToast(board.Current != null ? "재료를 먼저 치우세요" : "씻는 중");
    }
}
