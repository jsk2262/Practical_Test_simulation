---
name: reply-in-korean
description: "All user-facing output in Korean; new code comments in Korean; identifiers/paths/raw errors kept as-is"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 16a8900e-2f1a-49d2-a050-aef039f27ced
  modified: 2026-10-21T11:01:56.710Z
---

사용자 규칙 (2026-10-14 명시):
- 모든 사용자 대상 응답은 한국어. 설명·구현 계획·진행 상황·오류 분석·작업 결과 요약 모두 한국어.
- 변수명·함수명, API 이름, 파일 경로, 원본 오류 메시지는 원문 유지.
- **새로 작성하는 코드 주석은 한국어로.** (기존 영어 주석은 건드리지 않음 - 새로 쓰는 부분만)
- 영문 자료·도구 결과가 영어여도 설명은 한국어.
- 사용자가 명시적으로 영어를 요청할 때만 영어.
- **도구 호출 사이 짧은 진행 메시지도 한국어.** (2026-10-21: 긴 세션에서 "Waiting a bit longer..." 같은 중간 문장이 영어로 새서 사용자가 채팅을 옮김)

**Why:** 처음엔 "영어말고 한글써"로 교정했고, 이후 위 규칙을 직접 적어 줌.
**How to apply:** 모든 메시지와 새 주석에 적용. 설명 방식은 [[explain-code-simply-korean]] 참고.
