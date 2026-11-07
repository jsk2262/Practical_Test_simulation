---
name: blender-mcp-socket-workaround
description: Blender MCP server (uvx mcp-for-blender) fails to install on this PC; talk to the Blender addon socket on 127.0.0.1:9876 directly instead
metadata:
  node_type: memory
  type: reference
  originSessionId: 16a8900e-2f1a-49d2-a050-aef039f27ced
  modified: 2026-10-01T22:13:56.685Z
---

On this PC, `uvx mcp-for-blender` (configured in .mcp.json) fails installing pywin32 with os error 32 (file lock, likely antivirus) — retries, other Python versions, cache clean did not help (2026-10-01).

Workaround that works: the Blender `blender_mcp` addon listens on 127.0.0.1:9876. Send JSON `{"type": "get_scene_info"|"execute_code", "params": {...}}` over TCP and read the JSON reply. Use the uv Python at `%APPDATA%\uv\python\cpython-3.14-windows-x86_64-none\python.exe` (plain `python` is the Windows Store stub).

Unity likewise: no unityMCP server in this session, but the MCP for Unity bridge in the open editor listens on 127.0.0.1:6400 (greeting then 8-byte big-endian length-framed JSON `{"type": "...", "params": {...}}`; e.g. ping, execute_menu_item, read_console, manage_scene, manage_editor, refresh_unity). Run editor code by adding a [MenuItem] and calling execute_menu_item. Blender can also run headless: `blender.exe -b <file> --python script.py`.

Blender 5.2 LTS, VARCO3D bridge addon (`varco3d_bridge_for_blender`) is installed. Main file: `Assets\요리재료.blend`.
