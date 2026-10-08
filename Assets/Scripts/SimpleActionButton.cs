using System;
using UnityEngine;

/// <summary>A plain clickable button that just invokes a callback - for UI panels
/// (reset/close, etc.) that don't fit any existing button type.</summary>
public class SimpleActionButton : MonoBehaviour, IClickable
{
    private Action onClick;

    public void Init(Action onClick)
    {
        this.onClick = onClick;
    }

    public void OnClicked()
    {
        onClick?.Invoke();
    }
}
