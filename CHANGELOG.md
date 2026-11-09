# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2026-11-09

### Added

- **Episode 1 and Episode 2 support, verified in game.** The same shim now binds
the Patreon (Episode 1) and Build 37 (Episode 2) builds, which the previous
release detected but never drove. Nothing about the injection route changed:
those builds run the same Unity 6000.2 Mono runtime.
- Episode 1 food fallback. Episode 1 ships no acorn asset; its food item is
`chicken_drumstick_GAT`, so food lookup now tries the original name list first
and falls back to `drumstick` only when nothing matches. Episode 2 and 3 keep
resolving `acorn_GAT` as before.

### Changed

- The creature-name filter now also excludes wolf and ghost templates, so the
`wolfgirl_GAT` and `ghost_dummy_GAT` groups can never be selected as food.
- Shim version 1.1.0.

### Verified in game

Every line below was produced by a command with its own id and confirmed by
re-reading the game's own state.

**Episode 1, `FamilyTime_Patreon`** - `episode=1 buildLabel=Patreon
renderer=Direct3D12 supported=1 bound=1`, `instances=184`, `hungry=3`,
`hungerTotal=15`. Episode 1 packs a 4-bit hunger level, which is why the level
total (15) exceeds the hungry count (3); the 16-byte element path reads it.

| Action | Response | Change |
| --- | --- | --- |
| Spawn food | `RES\|<id>\|SPAWN\|st=OK\|msg=Manual spawn: template=chicken_drumstick_GAT templateIndex=2 countBefore=184 countAfter=184 requested=x=7.60 y=0.00 z=29.97` | `instances 184 -> 185`, `chicken_drumstick_GAT` group `0 -> 1` |
| Feed | `RES\|<id>\|FEED\|st=OK\|msg=Manual feed: 3 need(s) satisfied.` | `hungerTotal 15 -> 12`, `feeds 0 -> 3` |
| Water | `RES\|<id>\|WATER\|st=ERR\|msg=Manual water: Episode 1 has no thirst mechanic, so water is not applicable.` | refused, as intended |

Simulation clock advanced `1794186419107 -> 1794186421164` (2.06 s over 2 s).

**Episode 2, `FamilyTime_Patreon-DX11-Build37`** - `episode=2 buildLabel=Build 37
(DX11) renderer=Direct3D11 supported=1 bound=1`, `instances=170`, `hungry=3`,
`hungerType=FOOD`.

| Action | Response | Change |
| --- | --- | --- |
| Spawn food | `RES\|<id>\|SPAWN\|st=OK\|msg=Manual spawn: template=chicken_drumstick_GAT templateIndex=1 countBefore=170 countAfter=170 requested=x=4.85 y=-82.40 z=32.68` | `instances 170 -> 171` |
| Feed | `RES\|<id>\|FEED\|st=OK\|msg=Manual feed: 3 need(s) satisfied.` | `hungry 3 -> 0`, `hungerTotal 3 -> 0`, `feeds 0 -> 3` |
| Water on a FOOD day | `RES\|<id>\|WATER\|st=ERR\|msg=Manual water: Today's need is FOOD; water applies on thirst days (every third day).` | refused, as intended |
| Set need | `RES\|<id>\|SETNEED\|st=OK\|msg=need type set to THIRST` | `hungerType FOOD -> THIRST` |

**Episode 2, `FamilyTime_Patreon-DX12-Build37`** - `episode=2 buildLabel=Build 37
(DX12) renderer=Direct3D12 supported=1 bound=1`, `instances=210`, `hungry=3`.
Spawn moved `instances 210 -> 211` with `template=chicken_drumstick_GAT
templateIndex=1`, and feed returned `3 need(s) satisfied` with `hungry 3 -> 0`.

Episode 2 uses the packed single hunger bit at bit 19, the same layout as
Episode 3 (`hungry=3`, `hungerTotal=3`).

**Episode 3 regression, `Family_Time_Ep_3_WINDOWS_DX11_b108`** - after the food
lookup change: `episode=3 supported=1 bound=1`, spawn still
`template=acorn_GAT templateIndex=6` with `instances 86 -> 87`, and feed on a
thirst day correctly refused. The Episode 1 change did not alter Episode 3.

## [0.2.0] - 2026-11-09

### Added

- **Automatic attach.** The Studio finds a running `FamilyTime.exe` and injects
  the shim itself, replacing the manual `ftinject.exe` step. It reports the
  injector's own exit code, skips work when `status.txt` already shows a bound
  session, and falls back to the manual path if the payload is missing.
- Version metadata embedded in the Studio executable.
- `docs/ANTIVIRUS.md`: why injection-based tools trip scanners, and the only
  legitimate remedies (code signing, false-positive submission). No evasion code.

### Notes

- The injection primitive is unchanged: `LoadLibrary` on a remote thread.
- Auto-attach makes the tool more visible to heuristics than running the injector
  manually. The manual path remains supported.

## [0.1.0] - 2026-11-08

First published release. **Episode 3 DX11 (b108) only.**

### Verified in game

Every item below was confirmed against the running game by re-reading the game's
own state, and each action is paired with the response line the shim returned for
that specific command id.

- **Detection** - `episode=3 buildLabel=b108 (DX11) renderer=Direct3D11
  supported=1 bound=1`, `screen=984x561 windowed`.
- **Manual spawn** - `RES|<id>|SPAWN|st=OK|msg=Manual spawn: template=acorn_GAT
  templateIndex=6 ...`, with the acorn group count rising per click.
- **Manual feed** (`FOOD` need active) - `RES|<id>|FEED|st=OK|msg=Manual feed:
  8 need(s) satisfied.` with `hungry` falling 8 and `feeds` rising 8 per click.
- **Manual water** (`THIRST` need active) - `RES|<id>|WATER|st=OK|msg=Manual
  water: 8 delivered` with `hungry` falling 8 and `waters` rising 8 per click.
- **Unfocused operation** - with the Studio window foreground for the whole block
  and the game never re-focused by a click, the simulation clock advanced
  6.08 s across 6 s wall time while all three actions executed.
- **Minimized operation** - with the game window minimized (`IsIconic=True`),
  the clock advanced 6.08 s across 6 s and all three actions executed.

### Added

- In-process shim (`FamilyTimeChef.InGame.dll`) loaded by external Mono
  injection, with reflection-only access to the game's `GAT` API.
- Native agent (`ftagent.dll`) plus `LoadLibrary` injector (`ftinject.exe`).
- WinForms control panel (`FamilyTimeChef.Studio.exe`) with live needs, manual
  actions, automation timers and an operation log.
- Message-level focus shield (WndProc subclass inside the player) that keeps the
  simulation running while the game is unfocused or minimized. This is required
  because `Application.runInBackground` has no effect in this build.
- File-based IPC channel with atomic status writes, line-wise queue consumption
  and per-command response ids.
- Portable ZIP and Inno Setup installer packaging with SHA-256 checksums.

### Known limitations

- Episode 3 DX12 is detected but unsupported: the tool refuses to bind.
- Episodes 1 and 2 are not implemented.
- `Application.runInBackground` remains inert in this build; the WndProc shield is
  the fix, and the `RELAX` verb's `runInBackground=1` is not claimed to work.
- Automation depends on the in-game simulation continuing; if the shield fails to
  install, timers pause with the game.

## Earlier work (unreleased)

Development history includes loader-based attempts (BepInEx 5/6, Doorstop 4.6.0,
MelonLoader 0.7.3) that all fail on this Unity 6000.2 Mono runtime, and a patched
BepInEx preloader route that reaches Unity type initialisation and stops there.
Those dead ends are documented in [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md);
`src/FamilyTimeChef.Boot` and `tools/patcher` are retained for reference and are
not part of the shipped tool.
