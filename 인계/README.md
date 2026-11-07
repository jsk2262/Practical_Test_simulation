# 인계 — 다른 컴퓨터에서 이어서 작업하기

이 폴더 하나만 있으면 다른 컴퓨터에서 같은 상태로 이어갈 수 있습니다.

| 파일 | 무엇 |
|---|---|
| `README.md` | 지금 이 파일 — 새 컴퓨터 설치 순서 |
| `대화록_2026-11-08.md` | 11월 6~8일 작업 기록 정리본 (무엇을 왜 고쳤는지) |
| `메모리/` | Claude 메모리 폴더 사본 — **git 으로 안 따라오므로 직접 복사해야 합니다** |
| `../작업_인계_2026-11-06.md` | 진행 상황과 남은 과제 전체 (426줄, 8개 절) |

---

## 1. 소스 받기

작업 브랜치는 `handoff-2026-11-06` 입니다.

```bash
git clone -b handoff-2026-11-06 https://github.com/jsk2262/Practical_Test_simulation.git
```

이미 clone 해 둔 게 있으면:

```bash
git fetch origin && git checkout handoff-2026-11-06
```

> `origin/main` 은 10월 19일 커밋 `9e706d0` 에 그대로 있습니다. 합치면 add/add 충돌이 많아 **일부러 안 합쳤습니다.** 그냥 `handoff-2026-11-06` 에서 계속 작업하세요.

## 2. Unity

**Unity 6000.5.1f1** 입니다. Unity Hub 에서 같은 버전을 설치하고 프로젝트를 엽니다. 처음 열 때 라이브러리 재생성에 몇 분 걸립니다.

## 3. Claude 메모리 복사 (이게 "연동"의 핵심입니다)

메모리는 저장소가 아니라 사용자 폴더에 있어서 git 으로 따라오지 않습니다. 새 컴퓨터에서 **한 번** 복사해 주세요.

가는 곳은 프로젝트 경로의 `\` 와 `:` 를 `-` 로 바꾼 이름의 폴더입니다. 프로젝트를 `C:\Users\내이름\Desktop\VisualStudio\Practical_Test_simulation` 에 두면:

```
C:\Users\내이름\.claude\projects\C--Users-내이름-Desktop-VisualStudio-Practical-Test-simulation\memory\
```

PowerShell 한 줄로 (프로젝트 폴더에서 실행):

```powershell
$dst = "$env:USERPROFILE\.claude\projects\$((Get-Location).Path -replace '[:\\]','-')\memory"; New-Item -ItemType Directory -Force $dst | Out-Null; Copy-Item "인계\메모리\*.md" $dst -Force; Get-ChildItem $dst | Select-Object Name
```

복사되면 새 대화를 열 때 `MEMORY.md` 가 자동으로 읽혀서, 한국어로 답하기·플레이 중 컴파일 금지·ChatGPT 쪽 파일 건드리지 않기 같은 규칙이 바로 적용됩니다.

## 4. unityMCP (Claude 가 Unity 를 직접 조작하는 연결)

버전을 **10.1.2 로 고정**해야 붙습니다. 고정하지 않으면 `Server disconnected` 가 납니다 (uv 캐시 잠김). `인계/메모리/unity-mcp-version-pin.md` 에 자세히 적혀 있습니다.

## 5. 새 대화에서 할 첫 마디

```
작업_인계_2026-11-06.md 읽고 이어서 작업하자
```

그 파일 8절에 프레임 문제의 원인·수정·측정값과 **다음에 할 후보 3개(추천 순서 포함)** 가 있습니다.

---

## 주의 — 커밋에서 빼야 하는 파일

ChatGPT 가 같은 프로젝트를 동시에 고치고 있어서, 아래 두 파일은 **계속 커밋에서 빼 두었습니다.** 섞이면 먼저 물어봐 주세요.

```
Assets/Fonts/NotoSansKR SDF.asset
Assets/Scripts/TowelSpread.cs
```

## 원본 대화 기록 (선택)

이 대화의 원본 전체는 70 MB zip 으로 내보냈습니다 — git 에 넣기엔 너무 커서 포함하지 않았습니다. 필요하면 USB·클라우드로 따로 옮기세요.

```
C:\Users\MBC-501-13\Downloads\session-export-1794068904438.zip
```

원본 jsonl 은 여기 있습니다.

```
C:\Users\MBC-501-13\.claude\projects\C--Users-MBC-501-13-Desktop-VisualStudio-Practical-Test-simulation\
```
