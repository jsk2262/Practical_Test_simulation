---
name: unity-mcp-version-pin
description: unityMCP 가 "Server disconnected" 로 죽으면 uv 캐시 잠김 — 서버 버전을 고정하면 설치를 건너뛰어 붙는다
metadata:
  type: reference
---

증상: Claude Desktop 설정 → 로컬 MCP 서버에서 `unityMCP` 가 **실패 / Server disconnected**.

진짜 원인은 uv 캐시 잠김이다. 명령을 직접 돌리면 보인다:

```
error: Failed to install: pywin32-312-...whl
  Caused by: failed to remove directory ...\uv\cache\builds-v0\.tmp...
  (os error 32 — 다른 프로세스가 파일을 사용 중)
```

ChatGPT(codex.exe)가 띄워둔 `mcp-for-unity` 프로세스가 캐시를 물고 있어서, Claude 쪽 uvx 가 새 패키지를 설치하지 못한다. [[parallel-chatgpt-work]] 규칙상 그 프로세스는 건드리지 않는다.

해법 — `%APPDATA%\Claude\claude_desktop_config.json` 의 args 를 **이미 캐시에 있는 버전으로 고정**해서 설치 자체를 건너뛴다:

```json
"args": ["--from", "mcpforunityserver==10.1.2", "mcp-for-unity", "--transport", "stdio"]
```

(`--offline --prerelease explicit --from mcpforunityserver>=0.0.0a0` 였던 것을 교체. 버전은 Unity 쪽 MCP for Unity 패키지 버전에 맞춘다.)

주의할 점:
- **설정 창은 앱이 켜질 때 읽은 값을 보여준다.** 파일을 고쳐도 완전 종료 후 재시작 전에는 옛 인수가 그대로 보인다 — 되돌아간 걸로 오해하지 말 것.
- 이 설정은 Claude Desktop 용이지만 Code 탭 세션에도 적용된다. 프로젝트 `.mcp.json` 과는 별개다.
- uv 캐시가 16GB / 임시폴더 233개까지 불어 있었다. 언젠가 codex 닫고 `uv cache clean` 필요.

## 2026-11-04 — 핀을 해 뒀는데도 또 같은 오류

버전 핀은 이미 적용돼 있었는데 `pywin32-312` / `os error 32` 가 또 났다. **핀만으로는 부족하다.**
이번 범인은 codex 가 아니라 **부모가 죽은 좀비 mcp-for-unity 프로세스**(10/31 에 뜬 채 4일간 살아 있던 uvx/python)였다.

진단 → 조치:

```powershell
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -match 'mcp-for-unity|mcpforunity' } |
  Select-Object ProcessId, Name, CreationDate, ParentProcessId
```

`CreationDate` 가 오늘이 아니거나 부모(`ParentProcessId`)가 이미 죽은 것들을 `Stop-Process -Force` 로 정리한다.
부모가 `claude.exe` 인 사슬은 지금 쓰는 서버이니 남긴다. 좀비 uvx 를 죽이면 그 밑 uv/python 은 같이 내려간다.

정리한 뒤 이 명령이 그냥 뜨면 해결된 것 (FastMCP 배너 + `tool visibility synced from Unity`):

```
uvx --from mcpforunityserver==10.1.2 mcp-for-unity --transport stdio
```

단, **세션 중에 끊긴 unityMCP 는 그 세션으로 안 돌아온다** — 설정에서 빠져 버리므로 새 세션을 열어야 도구가 보인다.

### 캐시 청소: `uv cache prune` 은 서버가 켜진 동안 절대 못 돈다

`uv tool uvx` 는 **서버가 사는 내내 `cache\.lock` 을 쥐고 있다.** 그래서 prune 은 5분 기다리다 `Timeout (300s) when waiting for lock` 으로 끝난다 — 이게 찌꺼기가 8월부터 안 치워지던 진짜 이유다. `--force` 는 돌아가는 서버 파일까지 지우니 쓰지 말 것.

서버를 안 끄고 치우는 법 — `builds-v0\.tmp*` 는 전부 일회용 가상환경이라 폴더째 지우면 된다 (2026-11-04 기준 260개 안에 사용자 파일 0건, 전부 `Lib`/`pyvenv.cfg`/`Scripts` 구조의 파이썬 venv 75만개 `.py`):

```bash
CUT=$(( $(date +%s) - 7*86400 ))
for d in "$CACHE/builds-v0"/.tmp*; do
  [ -f "$d/pyvenv.cfg" ] || continue          # 가상환경이라고 증명된 것만
  [ "$(stat -c %Y "$d")" -lt "$CUT" ] || continue   # 7일 넘은 것만 (진행 중 빌드 제외)
  rm -rf "$d"
done
```

실제로 193개 지워 **18.08GB → 7.71GB** (10.4GB 회수), 실패 0건, 서버는 돌아가는 채로 멀쩡했다.
`pyvenv.cfg` 가 없는 `.tmpLaVjii`(Scripts/.gitignore/CACHEDIR.TAG 만 있는 만들다 만 것) 는 일부러 남겨 뒀다.
