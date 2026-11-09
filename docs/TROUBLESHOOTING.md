# Troubleshooting

## The game freezes or stops simulating when it is not focused

This is the bug the tool works around. `Application.runInBackground` is inert in
this build: writing `true` to it reads back `False`, even when re-asserted every
frame. The fix shipped here is a **message-level WndProc shield** installed inside
the player at start-up, which swallows the deactivation messages
(`WM_ACTIVATEAPP(0)`, `WM_ACTIVATE(WA_INACTIVE)`, `WM_KILLFOCUS`,
`WM_NCACTIVATE(false)`).

Check that it installed:

```powershell
Select-String "focus shield installed" "$env:LOCALAPPDATA\FamilyTimeChef\agent.log"
```

If the line is absent, the shim did not start - see *Nothing binds* below. The
`RELAX` verb's `runInBackground=1` is not a fix and is not claimed to work.

## The Studio could not attach automatically

The Studio looks for `FamilyTime.exe`, then runs `ftinject.exe` from its own folder
or from `%LOCALAPPDATA%\FamilyTimeChef`. The operation log states which of these
happened:

| Log message | Meaning |
| --- | --- |
| `Family Time is not running` | Start the game first, then press Connect. |
| `Could not find ftinject.exe and ftagent.dll` | The Studio is running outside its install folder. Run `install.ps1` or the installer. |
| `Already attached` | Nothing to do; the shim is live. |
| `Injector failed (exit N)` | See the exit code in `tools/injector/injector.c`; usually access denied or wrong architecture. |
| `timed out after 20 seconds` | The game may be hung or still loading a save. |

If automatic attach keeps failing, run the injector by hand as described in
[INSTALL.md](INSTALL.md); the manual path is unchanged.

## Antivirus flags the tool

Injection into another process is the same primitive loaders use, so scanners can
flag it. Read [ANTIVIRUS.md](ANTIVIRUS.md): verify the hash, report the false
positive, or build from source. Do not disable real-time protection or add broad
exclusions to work around it.

## Nothing binds (`bound=0`, no `status.txt`)

| Symptom | Cause | Fix |
| --- | --- | --- |
| `ftinject.exe` prints an error | wrong architecture or wrong path | use the x64 injector with an absolute path to `ftagent.dll` |
| Injection succeeds, no `status.txt` | shim not loaded, or not the supported build | check `%LOCALAPPDATA%\FamilyTimeChef\plugin.log` and `shim.log` |
| `supported=0` | different episode or renderer | only Episode 3 DX11 b108 is supported; DX12 is detected and refused on purpose |
| `bindError` is set | game world not created yet | inject after the save has loaded, then wait a few seconds |

Injection is **per game session**. Restarting the game requires injecting again.

## The GUI shows stale numbers

The channel is plain files. If a reader hits `Device or resource busy` during an
atomic replace, it retries; a persistent failure usually means a second copy of
the tool is writing the same folder. Close extra Studio windows and inject once.

## A click does nothing / the action never appears in `resp.txt`

- **Windows overlap.** If the game window covers the Studio window, a scripted
  click at the button's coordinates lands on the game instead. Move or resize the
  windows so they do not overlap, and check the hit target before clicking.
- **Wrong coordinates.** Button positions move with the Studio window; re-read
  them after moving the window.
- **Command not consumed.** Commands are appended one per line to `cmd.queue` and
  consumed line-wise; a command appended without a trailing newline is not read.

## Responses seem missing

Every command carries an id and the shim replies with the same id
(`RES|<id>|<VERB>|st=OK|...`). Studio's ids restart at 1 for each Studio process,
so a harness that filters responses by "greater than the highest id seen" will
discard valid replies. Match by the id of the command you just queued, or by
comparing the set of ids before and after the action.

## DX12 install

Detected and intentionally unsupported. The tool reports the renderer and refuses
to bind rather than performing untested actions.

## Collecting diagnostics

`%LOCALAPPDATA%\FamilyTimeChef` contains `status.txt`, `groups.txt`, `resp.txt`,
`events.log`, `agent.log`, `plugin.log` and `shim.log`. These contain no account
information, but they do contain your game's runtime values - review before
sharing them publicly.
