# Antivirus and SmartScreen

## Why this tool can be flagged

FamilyTimeChef loads a DLL into another process. Internally it does this:

1. `OpenProcess` on `FamilyTime.exe`
2. `VirtualAllocEx` + `WriteProcessMemory`
3. `CreateRemoteThread` on `LoadLibraryA`

That is the same primitive used by a large share of malware loaders, so
heuristics and machine-learning scanners cannot tell this tool from one by shape
alone. Being unsigned makes it worse: SmartScreen shows "Windows protected your
PC" for any new, unsigned, low-reputation executable that behaves this way.

None of that implies the tool is malicious. It has no persistence, no network
access, no credential access, no encryption, no self-modification, and it only
touches the directory `%LOCALAPPDATA%\FamilyTimeChef` plus the game process you
started yourself. The full source is in this repository.

## What actually reduces false positives

These are the only legitimate levers, in order of effectiveness:

1. **Code signing.** Sign `FamilyTimeChef.Studio.exe`, `ftinject.exe`,
   `ftagent.dll` and both release EXEs with an Authenticode certificate. An OV
   certificate builds reputation over time; an EV certificate gets SmartScreen
   trust immediately. This is the only thing that reliably changes SmartScreen's
   mind. Certificates are purchased from a CA (DigiCert, Sectigo, SSL.com and
   others) and cannot be provided by this project.
2. **Report the false positive.** Submit the flagged file and the reason at
   <https://www.microsoft.com/en-us/wdsi/filesubmission>. Microsoft usually
   reclassifies honest tools within a few days. Other vendors have equivalent
   forms; a multi-engine scan on VirusTotal helps identify which products to
   contact.
3. **Build from source.** Users who compile the tool themselves, or run the
   repository's CI, are not relying on an unknown binary blob.
4. **Keep binaries stable and inspectable.** No packers, no obfuscation, no
   self-modifying code. Every artifact ships with a SHA-256 checksum so a user can
   confirm what they downloaded.

## What this project will not do

These are deliberately absent, because they are evasion techniques rather than
fixes:

- No obfuscation, packing, encryption or string hiding.
- No anti-analysis, anti-debug, anti-VM or sandbox detection.
- No unhooking, AMSI bypass, ETW patching or Defender tampering.
- No persistence, scheduled tasks or services.
- No instructions to disable real-time protection or add exclusions as a
  "solution". If your scanner objects, verify the hash, submit it, or build from
  source. Do not weaken your machine's protection to run a game tool.

Auto-attach (the Studio finding the game and injecting on its own) makes the
behaviour more visible to scanners than a manual `ftinject.exe` run, because the
GUI process itself launches the injector. That trade-off is documented rather than
hidden, and the manual path is still supported.

## Verified scanner results

Measured on 2026-11-09 with Windows Defender on this machine: real-time
protection on, `AMServiceEnabled=True`, signature `1.463.191.0`, no sandbox or
scan exclusions applied by this project.

| File | MpCmdRun verdict |
| --- | --- |
| `FamilyTimeChef-0.3.0-ep3-dx11-portable.exe` | `found no threats` |
| `FamilyTimeChef-0.3.0-ep3-dx11-setup.exe` | `found no threats` |
| `ftinject.exe` | `found no threats` |
| `ftagent.dll` | `found no threats` |
| `FamilyTimeChef.InGame.dll` | `found no threats` |
| `FamilyTimeChef.Studio.exe` | `found no threats` |

A custom scan of the whole `dist` directory raised no new detections: the count
before and after the scan was identical (3), and all three were pre-existing,
unrelated detections outside this project.

One honest caveat. On 2026-11-08 Defender did classify an earlier development
build, `build/native/ftinject.exe`, as `PUA:Win32/Vigua.A` (severity low). That is
exactly the heuristic behaviour described above: the same source, compiled a
different way, matched a potentially-unwanted-application rule. The file shipped
in `dist` today scans clean, but a single engine's clean result is not a promise
about every engine or every future build. If you want certainty, build from
source or wait for a signed release.

## If you are being blocked

1. Confirm the file matches the release `SHA256SUMS.txt`.
2. Check the source of the exact behaviour in `src/agent/agent.c`,
   `tools/injector/injector.c` and `src/FamilyTimeChef.Studio/Injector.cs`.
3. Report the false positive to your vendor with that context.
4. If you cannot get a clean verdict, do not run it. No game convenience is worth
   disabling your protection for.
