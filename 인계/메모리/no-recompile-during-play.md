---
name: no-recompile-during-play
description: "Unity가 플레이 중이면 스크립트 컴파일 전에 플레이를 멈추고, 테스트는 새 플레이로"
metadata:
  node_type: memory
  type: project
  originSessionId: c183f172-7115-4621-8d5b-cecc828761b7
  modified: 2026-10-21T12:24:57.738Z
---

플레이 모드 중에 스크립트를 다시 컴파일하면 Unity가 실행 중인 코드를 다시 불러온다(hot reload). 이때 `CookingPlanManager.Instance` 같은 static 값과 `Action` 필드(`ChineseExamSelectionManager.onContinue`)가 비어서, 순서 화면과 TTS가 안 뜨는 것처럼 보인다. 2026-10-21에 사용자가 "순서+tts가 사라졌다"고 했는데, 새로 플레이하니 정상이었다.

**Why:** 코드 버그가 아닌데 버그처럼 보여 사용자를 헷갈리게 한다.
**How to apply:** `refresh_unity`/컴파일 전에 에디터가 플레이 중인지 확인하고 멈춘다. 확인은 항상 새로 시작한 플레이로 한다. 사용자에게도 "고친 뒤에는 플레이를 다시 시작"하라고 안내한다. 관련: [[reply-in-korean]]
