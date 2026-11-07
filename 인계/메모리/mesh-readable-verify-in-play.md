---
name: mesh-readable-verify-in-play
description: 메시 정점을 읽는 코드는 에디터에서만 통과할 수 있다 - 검증은 반드시 플레이 모드에서
metadata:
  node_type: memory
  type: project
  originSessionId: b7151b61-5e54-4291-a7e0-c6b1f56e6721
  modified: 2026-11-06T03:08:31.791Z
---

유니티 에디터 모드에서는 `isReadable = false` 인 메시도 `mesh.vertices` 가 읽힌다. 런타임에서는 막혀서 빈 배열이 오고, 측정 코드는 에러 없이 조용히 기본값으로 떨어진다.

`ToolItem.EdgeSign` 이 이것 때문에 칼날 방향을 기본값 `1` 로 잡아 칼을 거꾸로 쥐었다. 에디터에서 한 검증은 두 번 다 "날 왼쪽 1.00" 으로 통과했는데, 실제 플레이에서는 계속 거꾸로였다. 2026-11-06 `Assets/NEW/ChineseCleaver.fbx` 의 Read/Write 를 켜서 해결.

**Why:** 검증 환경과 실행 환경이 달라서, 고친 쪽과 검사하는 쪽이 같은 틀린 값을 쓰면 자기 일관적이지만 틀린 "통과"가 나온다.

**How to apply:** 메시 정점·부피를 재는 코드를 고쳤으면 `manage_editor play` 로 들어가 런타임에서 값을 다시 확인한다. 콘솔의 `Not allowed to access vertices on mesh ... (isReadable is false)` 가 그 신호이고, 임포트 설정의 Read/Write 를 켜면 사라진다. 플레이 진입 전에 컴파일을 끝내둘 것 ([[no-recompile-during-play]]). 관련: [[scene-coordinate-frame]]
