---
name: read-claude-md-every-answer
description: 질문에 답할 때마다 프로젝트 루트 CLAUDE.md를 먼저 다시 읽고 그 규칙대로 답·작업
metadata:
  node_type: memory
  type: feedback
  originSessionId: 8b2da56b-edaf-4284-82db-00384bcdfc66
  modified: 2026-10-19T07:21:28.188Z
---

질문에 답하거나 작업하기 전에 매번 `C:\Users\MBC-501-13\Desktop\VisualStudio\Practical_Test_simulation\CLAUDE.md`를 Read로 다시 읽고 그 규칙에 맞춰 답한다.

**Why:** 사용자가 2026-10-19에 CLAUDE.md(코딩 규칙 + Unity 프로젝트 규칙)를 넣고 "질문한 거 답할때마다 md파일 읽고 답해줘"라고 요청함.

**How to apply:** 매 답변 전 Read. 주요 규칙(파일이 바뀌었을 수 있으니 매번 확인):
- 불확실하면 구현 전에 최대 3개 질문
- 요청한 것만 최소 변경
- GameObject.Find · 이름/태그 문자열 비교 금지
- Update 등 매 프레임 할당 금지
- 매니저 외 싱글턴 금지
- 새 코드는 줄마다 한국어 주석
- 씬 오브젝트는 최상위 그룹 아래에 둠

CLAUDE.md가 언급하는 Plan.md는 2026-10-19 기준 없음. 관련: [[reply-in-korean]], [[parallel-chatgpt-work]]
