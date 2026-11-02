using UnityEngine; // OnGUI 스킨

/// <summary>OnGUI의 공용 스킨 글꼴을 한글 글꼴로 한 번 바꾼다 (스킨은 OnGUI 안에서만 쓸 수 있음).
/// KoreanFontFallback이 시작할 때 만든다.</summary>
public sealed class KoreanGuiFont : MonoBehaviour
{
    private void OnGUI()
    {
        if (KoreanFontFallback.Font != null) GUI.skin.font = KoreanFontFallback.Font; // 모든 OnGUI 글자의 기본 글꼴
        Destroy(gameObject); // 한 번이면 충분
    }
}
