---
name: frame-measure-in-unity
description: "프레임은 manage_profiler get_frame_timing 으로만 재고, 부하는 DemoFilm.RunFull 로 만든다"
metadata:
  node_type: memory
  type: reference
  originSessionId: b7151b61-5e54-4291-a7e0-c6b1f56e6721
  modified: 2026-11-07T16:29:00.987Z
---

이 프로젝트에서 프레임 저하를 조사할 때:

- **부하 생성기는 `DemoFilm.RunFull(false, "", false, false)`** — `Assets/Scripts/Editor/DemoFilmFull.cs`. 플레이 모드에서 호출하면 탕수육·마파두부를 처음부터 끝까지 혼자 만든다 (약 3분 30초). 끝나면 접시 하나에 조각 259개가 올라간 최악 상태가 된다. `DemoFilm.Stop()` 으로 중간에 멈출 수 있고, 멈춰도 씬 상태는 남는다.
- **프레임은 `manage_profiler get_frame_timing` 의 `cpu_main_thread_frame_time_ms` 로만 읽는다.** `Time.deltaTime` · `Time.smoothDeltaTime` 은 `Time.timeScale` 이 곱해진 값이고 시연은 timeScale 을 2~8배로 올리므로 그대로 보면 안 된다. `get_frame_timing` 첫 호출은 첫 프레임 스파이크(100 ms 쯤)가 나오므로 한 번 더 부른다.
- **범인 좁히기는 컴포넌트를 끄고 재는 이분법**이 가장 빠르다. `execute_code` 에서 의심 타입들을 `FindObjectsByType(Type)` 로 모아 `enabled = false` 하고 재고, 하나씩 되살리며 다시 잰다.
- `execute_code` 는 CodeDom(C# 6)이라 **private/internal 멤버에 직접 접근할 수 없다.** 내부 상태를 봐야 하면 `System.Reflection.BindingFlags.NonPublic` 리플렉션으로 읽는다. `using` 지시문은 못 쓰니 전부 정식 이름으로 적는다.
- 측정 코드를 넣으려고 컴파일하면 플레이가 끊긴다 ([[no-recompile-during-play]]) — 그래서 코드를 고치지 않고 `execute_code` 로만 재는 위 방법을 쓴다.

[[unity-mcp-version-pin]] · [[mesh-readable-verify-in-play]]
