using UnityEngine;

public interface IDraggable
{
    /// <summary>Returns false if this object refused to start a drag right now
    /// (e.g. a Carryable while the hand already holds something else) - the
    /// press then redirects to the held item, or still resolves as a click.</summary>
    bool OnDragStart();
    void OnDragMove(Vector2 mouseDelta, float deltaTime);
    void OnDragEnd();
}
