---
name: explain-code-simply-korean
description: "After writing code, teach how it was implemented in easy, detailed Korean (child-level) so the user can learn and review — not just a summary"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 16a8900e-2f1a-49d2-a050-aef039f27ced
  modified: 2026-10-14T08:02:24.414Z
---

코드를 짜고 나면 한글로, 초등학생도 이해할 만큼 쉽게 **그리고 자세하게** 설명한다. 요약만 하지 말고 "내가 어떻게 구현했는지"를 사용자가 따라가며 배울 수 있게 풀어준다.

**Why:** 사용자가 결과를 직접 보고 공부하고 검토하고 싶어함. 요약만으로는 배울 수 없다고 명확히 말함 (2026-10-14).
**How to apply:**
- 짧은 요약 + 그 아래에 "어떻게 만들었는지" 단계별 풀이.
- 각 단계: 무엇을 하는 코드인지 → 왜 그렇게 했는지 → 실제로 어떤 순서로 돌아가는지(예: 클릭 → 이 함수 → 이 값 변경).
- 쉬운 비유를 쓰고, 처음 나오는 개념(변수, 함수, 매 프레임 실행, 컴포넌트 등)은 한 줄로 뜻을 설명.
- 중요한 코드 몇 줄은 직접 보여주고 한 줄씩 무슨 뜻인지 풀이.
- 영어 이름(클래스·함수)은 필요할 때 괄호로 덧붙이고, 파일 위치(파일:줄)도 알려줘서 직접 열어보게.
- 새로 쓰는 코드 주석은 한국어 (기존 영어 주석은 그대로). [[reply-in-korean]]
