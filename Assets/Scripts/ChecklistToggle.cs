using UnityEngine;

/// <summary>체크리스트 숨기기/보이기 버튼 (정리 탭 아래 3D 글자, 콜라이더로 클릭).</summary>
public class ChecklistToggle : MonoBehaviour, IClickable
{
    public void OnClicked() => ChecklistManager.Instance?.ToggleVisible(); // 체크리스트 숨기기 ↔ 보이기
}
