# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
