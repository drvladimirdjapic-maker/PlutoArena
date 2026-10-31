# Pluto Arena changes to the Pluto Remastered bridge

The bridge is lpflhh's project:
<https://github.com/lpflhh/Pluto-AI-Starcraft-Remaster>
(patch made against commit `44736dd754c9a07fb8efc6412f138fb6607bcd77`).
Only Pluto Arena's changes are here, not the bridge itself.

## pluto-arena-bridge.patch

| File | Change |
|---|---|
| `src/legacy_view.cpp`, `src/legacy_view.h` | Difficulty: reaction delay (`PLUTO_REACTION_MS`) and APM limit (`PLUTO_APM`, token bucket; selections are free; commands that would arrive more than ~1 s late are skipped). The values can also come from `bridge.ini` (`reaction_ms=`, `apm=`), read at the start of every game (rematch). The binding table is chosen by the loaded `pluto.dll` (1.0 or 2.0). In multiplayer, commands are sent in the frame Pluto issued them, so Pluto acts after the 4 frames it was trained for (it measured 5 in LAN games). |
| `src/pluto_bindings.h` | `pluto_bindings_v2`: the 250 address operands of the Pluto v2.0 `pluto.dll` (same BW 1.16.1 addresses as `cog2026-2578600`, generated with the same rules as `scripts/generate_pluto_bindings.py`; the method reproduces the original table for `cog2026-2578600` exactly). |
| `src/file_hash.h`, `src/bridge.cpp` | Accept the SHA-256 of the Pluto v2.0 `pluto.dll` as well. |
| `scripts/build-arena.ps1` | Builds the bridge and copies it as `pluto-scr-tuned.dll` to Pluto Arena's difficulty folders. |

```powershell
git clone https://github.com/lpflhh/Pluto-AI-Starcraft-Remaster
cd Pluto-AI-Starcraft-Remaster
git checkout 44736dd754c9a07fb8efc6412f138fb6607bcd77
git apply ..\PlutoArena\bridge-patch\pluto-arena-bridge.patch
powershell -ExecutionPolicy Bypass -File .\scripts\build-arena.ps1
```

## start-pluto.patch

For `native\scripts\start-pluto.ps1` of the bridge package: accepts the files
of both Pluto releases (`cog2026-2578600` and `v2.0`), checked by SHA-256 as
before - the three files of one release must be used together.
