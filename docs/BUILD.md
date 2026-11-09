# Building

## Requirements

| Tool | Why | Notes |
| --- | --- | --- |
| Windows 10/11 x64 | target platform | |
| .NET SDK | Studio GUI and shim | the Studio project targets `net10.0-windows`, so use a matching-or-newer SDK |
| C compiler | `ftagent.dll`, `ftinject.exe`, `ftinput.exe` | MSVC (`cl`) or MinGW-w64 (`gcc`) |
| Inno Setup 6 (optional) | installer | only needed to build `setup.exe` |
| A Family Time install | Unity reference assemblies | Episode 3 DX11 b108 is the supported one |

## 1. Unity reference assemblies

The shim compiles against `UnityEngine.dll`, `UnityEngine.CoreModule.dll` and
`UnityEngine.IMGUIModule.dll`. These are Unity build artefacts and are **not**
distributed with this project - point the build at your own copy:

```powershell
powershell -ExecutionPolicy Bypass -File tools\fetch-refs.ps1 `
    -GameDir "D:\Games\Family_Time_Ep_3_WINDOWS_DX11_b108"
```

That copies them into `lib\unity\` (git-ignored). If you prefer, pass the
directory directly to MSBuild instead of copying:

```powershell
dotnet build src\FamilyTimeChef.Plugin\FamilyTimeChef.Plugin.csproj -c Release `
    -p:FamilyTimeManagedDir="D:\Games\Family_Time_Ep_3_WINDOWS_DX11_b108\FamilyTime_Data\Managed"
```

The plugin targets **net472 on purpose**: the game's Mono cannot resolve the
`netstandard` facade during bootstrap, and a `netstandard2.0` build crashes the
player on load.

## 2. Managed components

```powershell
dotnet build src\FamilyTimeChef.Plugin\FamilyTimeChef.Plugin.csproj -c Release
dotnet build src\FamilyTimeChef.Studio\FamilyTimeChef.Studio.csproj -c Release
```

## 3. Native components

MSVC:

```bat
cl /O2 /LD src\agent\agent.c /Fe:build\native\ftagent.dll /link user32.lib
cl /O2 tools\injector\injector.c /Fe:build\native\ftinject.exe /link user32.lib
cl /O2 tools\input\input.c /Fe:build\native\ftinput.exe /link user32.lib
```

MinGW-w64:

```bash
gcc -O2 -shared -o build/native/ftagent.dll src/agent/agent.c
gcc -O2 -o build/native/ftinject.exe tools/injector/injector.c
gcc -O2 -o build/native/ftinput.exe tools/input/input.c
```

`ftagent.dll` must be built for x64 to match `FamilyTime.exe`.

## 4. Everything at once

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Outputs land in `dist\`.

## 5. Packages

```powershell
powershell -ExecutionPolicy Bypass -File packaging\build-portable.ps1
```

Produces the portable ZIP and `SHA256SUMS.txt` in `dist\`. To also build the
installer (requires Inno Setup 6, `iscc.exe` on `PATH`):

```powershell
powershell -ExecutionPolicy Bypass -File packaging\build-portable.ps1 -Installer
```

## Legacy sources

`src/FamilyTimeChef.Boot` and `tools/patcher` are the abandoned loader-based
bootstrap (patching BepInEx's preloader). They are kept for reference, are not
built by `build.ps1`, and are not part of the shipped tool. See
[COMPATIBILITY.md](COMPATIBILITY.md) §3–3b.
