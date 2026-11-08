# Installation

Supported target: **Family Time, Episode 3, DX11, build b108**.

Nothing is written into the game folder and no game file is modified. All files
live in `%LOCALAPPDATA%\FamilyTimeChef`, and you remove the whole folder to
uninstall.

## Portable ZIP

1. Download `FamilyTimeChef-<version>-ep3-dx11-portable.zip` and its
   `SHA256SUMS.txt` from the release page.
2. Verify the download:
   ```powershell
   Get-FileHash .\FamilyTimeChef-<version>-ep3-dx11-portable.zip -Algorithm SHA256
   ```
   Compare with the matching line in `SHA256SUMS.txt`.
3. Extract anywhere writable, then:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```
   This copies the runtime files to `%LOCALAPPDATA%\FamilyTimeChef` and prints
   the injector path. It does not touch the game.
4. Start the game and get its process id:
   ```powershell
   (Get-Process FamilyTime).Id
   ```
5. Inject once per game session:
   ```powershell
   & "$env:LOCALAPPDATA\FamilyTimeChef\ftinject.exe" <pid> `
       "$env:LOCALAPPDATA\FamilyTimeChef\ftagent.dll"
   ```
   Wait a few seconds for the shim to bind. `status.txt` in the same folder
   appears once it has.
6. Launch the GUI:
   ```powershell
   & "$env:LOCALAPPDATA\FamilyTimeChef\FamilyTimeChef.Studio.exe"
   ```

The GUI shows `bound=1` and live instance/hungry counts when it is talking to
the game. If it shows "not bound", see [TROUBLESHOOTING.md](TROUBLESHOOTING.md).

## Installer

Run `FamilyTimeChef-<version>-ep3-dx11-setup.exe` and follow the prompts. It
installs the same files to `%LOCALAPPDATA%\FamilyTimeChef` and adds Start Menu
entries, then injects the same way as step 5 above.

The installer does **not** require administrator rights and does not install
anything into the game directory.

## Updating

Overwrite the contents of `%LOCALAPPDATA%\FamilyTimeChef` with the new release
(or run the newer installer). After updating, end the running game session and
inject again: the shim is loaded per session, so an old copy stays in memory
until the game restarts.

## Uninstalling

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

or delete `%LOCALAPPDATA%\FamilyTimeChef`, or use *Apps & features* for an
installer installation. Quit the game first: the shim lives in the game process
and the Studio window holds no files open, but the channel files are removed with
the folder.

No registry keys, services, scheduled tasks or start-up entries are created by
the portable package. The installer only adds Start Menu shortcuts.

## First-run checklist

- [ ] Game is Episode 3 DX11 (b108)
- [ ] `status.txt` shows `supported=1` and `bound=1`
- [ ] GUI shows a plausible instance count
- [ ] Manual Water/Spawn produce a new `RES|...|st=OK` line in `resp.txt`
