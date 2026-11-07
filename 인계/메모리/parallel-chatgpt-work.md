---
name: parallel-chatgpt-work
description: "사용자가 ChatGPT로도 같은 Unity 프로젝트를 동시에 수정함 - 그쪽 파일·변경은 건드리지 말고, 커밋에 넣기 전 물어볼 것"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 8b2da56b-edaf-4284-82db-00384bcdfc66
  modified: 2026-10-16T05:11:12.234Z
---

사용자는 이 Unity 조리 시뮬레이터 프로젝트를 ChatGPT로도 동시에 작업한다 (예: ContextControlHint, StrainerLocalScoop, UpperBurnerGrateSetup, '화구확대' 버튼, KitchenCamera·ClickDetector 일부). ChatGPT가 만든 파일·코드는 수정하지 않는다.

**Why:** 2026-10-16 커밋 때 ChatGPT 변경까지 `git add -A`로 함께 커밋했더니 "그건 안 건드리는 게 나을 것 같다"고 함.

**How to apply:** 커밋할 때는 `git add -A` 대신 내가 이번에 바꾼 파일만 골라 올리고, 다른 변경이 섞여 있으면 먼저 물어본다. 파일이 디스크에서 바뀌었다는 알림이 오면 ChatGPT 작업일 수 있으니 되돌리지 않는다. 같은 파일을 양쪽이 고친 경우 내 부분만 최소로 수정한다. 관련: [[reply-in-korean]]
