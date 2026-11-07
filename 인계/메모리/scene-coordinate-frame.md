---
name: scene-coordinate-frame
description: "PracticalStation3D 씬은 수평 3D 조리대로 이전 완료 — 보이는 바닥이 없고, 카메라 프레임 아래끝이 곧 바닥선이다"
metadata:
  node_type: memory
  type: project
  originSessionId: b7151b61-5e54-4291-a7e0-c6b1f56e6721
  modified: 2026-11-06T10:02:58.853Z
---

2026-11-02 조사. `Assets/Scenes/PracticalStation3D.unity` 의 좌표 구조:

- 실제 플레이 조리대는 `KitchenStation / Environment_Furniture / Station3D / CookingStation_Wide`. **회전 항등, 스케일 8.46.** `Counter_R/F/L/B`, `Counter_FrontLip`, `Sink_*`, `Burner1/2_*` 가 전부 그 자식.
- `CookingStationFrameManager.station` 이 여기에 연결돼 있어 **플레이 중 `Carryable.CounterUp == (0,1,0)`**. 축 이전은 끝나 있다. 코드의 `-41°` 는 station 이 비었을 때(=에디트 모드)만 쓰는 예비값이므로 **에디트 모드에서 읽은 값은 못 믿는다.**
- 실제 월드 좌표: 조리대 윗면 y=7.01 (z −2.82~3.27) / 앞 턱 `Counter_FrontLip` z 3.05~3.09, 윗면 y=7.28 / 싱크 바닥 y=5.14, x 4.48~6.51 / 수납장 문 앞면 z=3.05, 문틀 앞 z=3.13 / 수납장 발밑(=진짜 바닥) y=0.85.
- 카메라: orthographic size 6.8, pos (0,19,13.85), 41° 내려다봄, fwd (0,−0.656,−0.755), up (0,0.755,−0.656). **화면 오른쪽 = 월드 −X**, 조리대 앞쪽(열린 쪽) = **+Z**.
- 화면 투영 공식 (전체 보기 기준): `화면y = 0.755·y − 0.656·z − 5.259`, `뷰포트y = 0.5 + 화면y/13.6`. `KitchenCamera` 가 구역마다 확대·이동하지만 회전은 안 바뀌므로 이 공식은 전체 보기에서만 유효하다.

**보이는 바닥이 없다.** `Floor`·`CounterBody`·`Sink` 는 카메라를 향해 회전한 옛 2.5D 배경 판때기인데 **렌더러가 전부 꺼져 있다** (가리지도 않는다). 그리고 카메라 전체 보기의 아래끝이 정확히 수납장 발밑 y≈0.85 에 걸린다 → **진짜 바닥에 놓은 물체는 화면 밖으로 잘린다.** 그 앞(z>3.1)은 아래로 갈수록 더 잘린다(z 1 늘면 화면y 0.656 내려감).

**수납장 앞면은 콜라이더 있는 선반 벽이다.** `PlateShelf1/2`, `KitchenShelf1~3`, `PotLid`, `PotSmall`, `plateSize2*` 가 z 2.8~4.7, y 2.6~4.2 를 메우고 있다. 조리대 앞턱을 넘어 떨어진 물건은 바닥이 아니라 **이 선반 위에 얹힌다.** 그래서 "바닥에 닿았는지"로 판정하지 말고 "멈췄는지"로 판정해야 한다.

남은 함정 — 코드가 아니라 **옛 2.5D 시절 값**:

- `HandCarry.CounterZ(y) = 3.63 + 1.22y` 는 죽은 `CounterBody` 평면 공식. y≈6.9 를 넣으면 z≈12 (카메라 코앞)가 나온다. `BurnerGrate`/`BoardSinkWash`/`KnifeFall` 에서는 제거했으나 `Editor/` 셋업 스크립트 6곳과 `IngredientVanishWatch`(씬 미부착)에 남아 있다.
- `Vector3.ProjectOnPlane(Vector3.down, CounterUp)` 류의 "내리막" 계산은 수평 조리대에서 **영벡터**가 된다. 기울어진 조리대를 가정한 식은 전부 의심할 것.
- `BoardSinkWash` 인스펙터 `floorY`/`floorZ` 는 원래 −6 / 1.3 (옛 2.5D). **2026-11-02 에 floorY=2.0, floorZ=3.8 로 확정** — 선반을 다 지나쳐 떨어졌을 때의 받침 높이이자, 칼 두께(±0.57)까지 수납장 문틀(앞면 3.13) 바깥인 깊이. 더 낮추면 화면 밖, 더 앞으로 빼면 공중에 뜬다.
- `PlayArea`/`HandCarry`/`CuttingBoardController`/`ToolItem` 의 `Dot(CounterUp, up) > 0.999f` else 분기는 전부 죽은 코드.
- **월드 x 를 화면 좌우로 그대로 쓰는 코드.** 카메라가 180° 요(rot y=180, `right`=(-1,0,0))라 **월드 +x 가 화면 왼쪽**이다. 2026-11-06: 포 뜨기(`IngredientItem.KnifeFlatFromRight`/`KnifeFlatWithdraw`/`SteerFlatKnife`/`PalmPoint`)가 `b.max.x`=오른쪽으로 가정해 좌우가 거울처럼 뒤집혀 있었다 → `IngredientItem.ScreenRightX`(=-1) 부호를 도입해 고침. 다른 칼질·손 이동도 월드 x 를 쓰면 같은 식으로 뒤집힌다.
- **깊이(z)를 버리는 코드.** `err.z = 0f`, `(Vector2)` 캐스트 같은 2.5D 흔적. 2026-11-06: `ToolItem.TowardSpoonHead`/`TrySpoonBowl` 에서 제거(주걱·튀김젓가락이 팬에 못 닿던 원인). `ToolItem` 324·349·363·769 줄의 `Vector2` 는 화면 기준 자세라 의도된 것.

**유니티가 열려 있을 때 씬 파일을 밖에서 고치면 유니티가 덮어쓴다.** 인스펙터 값은 에디터 안에서 바꿔야 한다. [[no-recompile-during-play]] 도 같이 볼 것.
