# Installation

Supported target: **Family Time, Episode 3, DX11, build b108**.

Nothing is written into the game folder and no game file is modified. All files
live in `%LOCALAPPDATA%\FamilyTimeChef`, and you remove that folder to uninstall.

Two single-file downloads are published. Pick one:

| Download | What it is |
| --- | --- |
| `FamilyTimeChef-<version>-ep3-dx11-setup.exe` | Installer. Creates Start Menu shortcuts and writes the runtime files. |
| `FamilyTimeChef-<version>-ep3-dx11-portable.exe` | Self-extracting portable. Choose a folder, extract, then run `install.ps1`. |

Both contain the same five runtime files. Neither needs administrator rights.

## Verify the download

Download `SHA256SUMS.txt` from the same release and compare:

```powershell
Get-FileHash .\FamilyTimeChef-<version>-ep3-dx11-setup.exe -Algorithm SHA256
```

The printed hash must match the corresponding line in `SHA256SUMS.txt`.

## Installer

1. Run `FamilyTimeChef-<version>-ep3-dx11-setup.exe`.
2. Accept the licence/notice pages and finish.
3. Continue with *Inject and launch* below.

## Portable EXE

1. Run `FamilyTimeChef-<version>-ep3-dx11-portable.exe`.
2. Choose an extraction folder when prompted.
3. In that folder, run:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```
   This copies the runtime files to `%LOCALAPPDATA%\FamilyTimeChef`.

## Inject and launch

The shim is loaded once per game session.

1. Start Family Time and get its process id:
   ```powershell
   (Get-Process FamilyTime).Id
   ```
2. Inject:
   ```powershell
   & "$env:LOCALAPPDATA\FamilyTimeChef\ftinject.exe" <pid> `
       "$env:LOCALAPPDATA\FamilyTimeChef\ftagent.dll"
   ```
   Wait a few seconds for the shim to bind; `status.txt` appears in the same
   folder once it has.
3. Launch the GUI:
   ```powershell
   & "$env:LOCALAPPDATA\FamilyTimeChef\FamilyTimeChef.Studio.exe"
   ```

The GUI shows `bound=1` plus live instance and hungry counts when it is talking
to the game. If it shows "not bound", see [TROUBLESHOOTING.md](TROUBLESHOOTING.md).

## Updating

Run the newer setup EXE (or replace the contents of
`%LOCALAPPDATA%\FamilyTimeChef`). After updating, end the game session and inject
again: the previously injected shim stays in memory until the game restarts.

## Uninstalling

- Installer: use *Apps & features*, or the Start Menu uninstall shortcut.
- Portable: run `uninstall.ps1`, or delete `%LOCALAPPDATA%\FamilyTimeChef`.

Quit the game first. Neither flow creates registry keys, services, scheduled
tasks or start-up entries beyond the installer's Start Menu shortcuts.

## First-run checklist

- [ ] Game is Episode 3 DX11 (b108)
- [ ] `status.txt` shows `supported=1` and `bound=1`
- [ ] GUI shows a plausible instance count
- [ ] Manual Water or Spawn produces a new `RES|...|st=OK` line in `resp.txt`
