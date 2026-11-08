# Compatibility and verification record

This document records what was measured, so no compatibility claim is made that
was not demonstrated. It is deliberately explicit about dead ends.

| Install | Episode | Renderer | Status |
| --- | --- | --- | --- |
| `Family_Time_Ep_3_WINDOWS_DX11_b108` | 3 | DX11 | **Supported — verified in game** |
| `Family_Time_Ep_3_WINDOWS_DX12_b108` | 3 | DX12 | Detected, **unsupported** |
| `FamilyTime_Patreon-DX11-Build37` | 2 | DX11 | **Not implemented** |
| `FamilyTime_Patreon-DX12-Build37` | 2 | DX12 | **Not implemented** |
| `FamilyTime_Patreon` | 1 | — | **Not implemented** |

Only one of the five installations has working code and in-game verification.
The others have no implementation in this project. Nothing here is a claim about
future support.

---

## 1. Why an external-only tool cannot work

Child need state does **not** live in ordinary CPU memory that another process
could read with `ReadProcessMemory`:

* `GAT.GetInstanceInfoBuffer()` returns a **`ComputeBuffer`**
  (`GAT.cs`, `instanceInfoBuffer` field). Hunger is a packed bit inside that GPU
  buffer.
* Hunger is advanced by compute kernels — `IncrementHunger`, `CheckHunger`,
  `CountHungry`, `DrinkWater` — and by the utility kernel behind
  `GATUtilities.ApplyHungerDelta`.
* Reading it back requires `AsyncGPUReadback` (`Tools.RequestBufferReadback`),
  which only works from inside the player.

Consequently spawning food, feeding and watering all require calling the game's
own managed API from inside the Unity process. An external process has no
console, dev command, TCP/HTTP/NamedPipe endpoint or debug port to do that with.

**Conclusion: in-process code execution is mandatory.** The tool is therefore a
two-part design: an in-process shim plus an external GUI that talks to it.

## 2. Verified game API facts (from decompilation)

| Fact | Evidence |
| --- | --- |
| `GAT.World.gat` is a static property | `public static GAT gat` |
| `instanceCount` is a property | `public int instanceCount => _instanceCount;` |
| `Handle` is a **struct** with `ushort stableIndex` | `public struct Handle : IEquatable<Handle>` |
| `GetInstanceInfoBuffer()` returns `ComputeBuffer` | `public ComputeBuffer GetInstanceInfoBuffer()` |
| `CreateInstance` signature | `public Handle CreateInstance(Matrix4x4 localToWorldMatrix, int templateIndex, Action<Handle> onCreated)` |
| `ApplyHungerDelta` signature | `public void ApplyHungerDelta(Handle targetHandle, short delta, Action<InfoDeltaResult> onResult)` |
| The game clears a need with `delta = -1` | 4 call sites, e.g. `World.gatUtilities?.ApplyHungerDelta(_event.SourceStableHandle, -1, null);` |
| `GATTemplateEntry` fields | `template`, `startIndex`, `count` |
| Hunger is 1 bit at bit 19 (Ep2/3) | `public byte hunger => (byte)((info >> 19) & 1);` |
| Need types | `enum HungerType { FOOD, THIRST }` |
| `ResolveHandle` returns `ushort`, 65535 = invalid | `public ushort ResolveHandle(Handle handle)` |
| Physical index lookup | `gat.GetHandle((ushort)physicalIndex)` |

The reflection bridge in `GameBridge.cs` matches all of the above, including the
`-1` delta the game itself uses.

## 3. Loader routes: all dead on this runtime

All five target installs are **Unity 6000.2.12f1, Mono** (the player loads
`MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll`; there is no
`GameAssembly.dll`).

| Loader | Version | Result |
| --- | --- | --- |
| Baseline (no loader) | — | Game launches normally. Proves the game itself is fine. |
| BepInEx | 5.4.23.6 (Doorstop 4.6.0) | Player **crashes** in `UnityPlayer!UnityMain`; no managed stack frame. |
| BepInEx | 6.0.0-pre.2 (Doorstop 4.3.0) | Preloader runs, then `MissingMethodException: void System.Reflection.Module.GetPEKind(...)`. |
| BepInEx | 6.0.0-be.793 (Doorstop 4.6.0) | Same `MissingMethodException` on `Module.GetPEKind`. |
| Doorstop | 4.6.0 + target built for `netstandard2.0` | Player **crashes** in `UnityPlayer!UnityMain`. |
| Doorstop | 4.6.0 + target built for `net472` | Player survives, but `Doorstop.Entrypoint:Start` is silently never invoked — regardless of path, identity, or visibility. |
| MelonLoader | 0.7.3 | Bootstrap fails: `CustomAttributeFormatException: Could not find a field with name CharSet`. |

Two conclusions:

1. **Off-the-shelf loaders do not run on this Mono runtime.** BepInEx cannot get
   past `Module.GetPEKind`, and MelonLoader cannot parse `DllImport` metadata.
2. **Doorstop 4.6.0 injects but only invokes BepInEx's own preloader assembly.**
   For any assembly built here it aborts the entrypoint lookup without logging.

These loaders are therefore **not** dependencies of this project.

## 3b. Direct attempt: patching BepInEx's preloader (deepest dead end)

`tools/patcher` rewrites the IL of `UnityPreloaderRunner.PreloaderPreMain` (Cecil)
so it calls `FamilyTimeChef.Boot.Loader.Run()` instead of BepInEx's platform probe,
skipping `Module.GetPEKind`. On a fresh copy of Episode 3 DX11 the player stayed
alive and our shim ran, then hit a chain of missing mscorlib members in Unity
6000.2's Mono: `AppDomain.BaseDirectory`, `Assembly.GetType(string, bool)`,
`File.AppendAllText(string, string)`. Compiling against the game's own
`Managed/mscorlib.dll` is impossible — it is a trimmed facade missing
`StructLayoutAttribute`, `AssemblyCompanyAttribute`, `TargetFrameworkAttribute`
and `TcpListener`. The deepest confirmed point is
`TypeInitializationException: The type initializer for 'UnityEngine.Object' threw
an exception`, i.e. Unity types cannot be initialised inside Doorstop's AppDomain.

`src/FamilyTimeChef.Boot` and `tools/patcher` are retained for reference and are
not built or shipped.

## 3c. Verified route: external Mono injection (Episode 3 DX11)

The working route joins the game's *already running* scripting domain instead of
starting a new one.

1. `ftagent.dll` is injected with `LoadLibrary` (`tools/injector/injector.c`). It
   finds `mono-2.0-bdwgc.dll` with `GetModuleHandleA` and resolves every entry
   point with `GetProcAddress` (exports read from the PE first, not assumed).
2. It calls `mono_thread_attach(mono_get_root_domain())`, then
   `mono_image_open_from_data_with_name` + `mono_assembly_load_from_full` to load
   `FamilyTimeChef.InGame.dll` into the live domain, then resolves and invokes
   `FamilyTimeChef.InGame.Shim.Entry()`.
3. Because the shim lives where Unity's own assemblies live, mscorlib, UnityEngine
   and Assembly-CSharp all resolve normally.

### Why the shim is shaped the way it is

The injected thread is not Unity's main thread; touching Unity from it terminates
the player. `Entry()` only installs a frame hook
(`beginCameraRendering`/`beginContextRendering` plus `Camera.onPreRender`/
`onPreCull`; `beginFrameRendering` does not exist in this CoreModule). All game
work runs on the first real frame.

Four members normal .NET code assumes are absent and were replaced:
`Assembly.GetType(string,bool)`, `Process.GetCurrentProcess().Id`,
`HashAlgorithm.ComputeHash(Stream)` (FNV-1a instead), and the non-generic
`Expression.Lambda`. `TcpListener` is unusable, so IPC is a plain-file channel in
`%LOCALAPPDATA%\FamilyTimeChef`.

### Measured, Episode 3 DX11 (b108), fresh copy of the install

Detection: `episode=3 buildLabel=b108 (DX11) renderer=Direct3D11 supported=1
bound=1`, `screen=984x561 windowed`.

Each action below is presented with the response line carrying **that command's
own id**, plus the game state re-read before and after.

**Unfocused block** — Studio foreground for the entire block, game never
re-focused by a click; simulation clock advanced `t=1794181591377 →
t=1794181597453` (6.08 s over 6 s wall).

| Action | Response | Observed change |
| --- | --- | --- |
| Spawn food | `RES\|23\|SPAWN\|st=OK\|msg=Manual spawn: template=acorn_GAT templateIndex=6 countBefore=205 countAfter=205 requested=x=6.26 y=-82.40 z=30.33\|n=0` | `acorn_GAT` absent → 1 |
| Feed (`FOOD`) | `RES\|36\|FEED\|st=OK\|msg=Manual feed: 8 need(s) satisfied.\|n=8` | `hungry 187 → 179`, `feeds 0 → 8` |
| Water (`THIRST`) | `RES\|49\|WATER\|st=OK\|msg=Manual water: 8 delivered.\|n=8` | `hungry 179 → 171`, `waters 0 → 8` |

`fg` at start and end: Studio. No focus-return fallback was used or needed.

**Minimized block** — `IsIconic=True`; clock advanced `t=1794181640831 →
t=1794181646912` (6.08 s over 6 s wall).

| Action | Response | Observed change |
| --- | --- | --- |
| Spawn food | `RES\|92\|SPAWN\|st=OK\|msg=Manual spawn: template=acorn_GAT templateIndex=6 countBefore=206 countAfter=206 requested=x=6.26 y=-82.40 z=30.33\|n=8` | `acorn_GAT 1 → 2` |
| Feed (`FOOD`) | `RES\|105\|FEED\|st=OK\|msg=Manual feed: 8 need(s) satisfied.\|n=16` | `hungry 171 → 163`, `feeds 8 → 16` |
| Water (`THIRST`) | `RES\|118\|WATER\|st=OK\|msg=Manual water: 8 delivered.\|n=16` | `hungry 163 → 155`, `waters 8 → 16` |

### The focus shield

The player stops simulating when it loses focus. `Application.runInBackground`
cannot be used: it is inert in this build and reads back `False` even when
re-asserted every frame, so the shim installs a WndProc subclass that swallows
`WM_ACTIVATEAPP(0)`, `WM_ACTIVATE(WA_INACTIVE)`, `WM_KILLFOCUS` and
`WM_NCACTIVATE(false)`. It must run in-process because the replacement window
procedure has to be a valid address in the target. The `RELAX` verb's
`runInBackground=1` is **not** claimed to have any effect.

## 4. Other installations

- **Episode 3 DX12** — same managed API, different renderer. Detected via
  `SystemInfo.graphicsDeviceType`; the tool reports it and refuses to bind.
  Never driven, so it is shipped as unsupported.
- **Episodes 1 and 2** — no implementation exists in this project. Episode 1 has
  no thirst mechanic (`HungerType` is FOOD only), so water delivery is not
  applicable; Episodes 2 and 3 reuse the packed bit at bit 19 and must read the
  active need type at runtime.

## 5. Build identification

`BuildDetector` identifies a running install by the Unity `build-guid` in
`boot.config`, cross-checked against `Assembly-CSharp.dll`'s SHA-256. The table in
`BuildDetector.cs` was populated by hashing the five target installations. Only
the Episode 3 DX11 entry has been exercised at runtime.

## 6. Known limitations

- Behaviour depends on a recognised build; unknown builds stay read-only.
- Automation requires the game's simulation to keep running, which depends on the
  focus shield having installed successfully.
- Actions are budget-limited per tick and cooldown-gated per child, so a large
  backlog clears over several ticks rather than instantly.
- The GUI's file channel is local only; there is no remote control and no
  telemetry.
