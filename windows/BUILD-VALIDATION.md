# Native Windows 0.3.2 — 2026-10-29

Source: `91fae6fb6d012a0de3bae5f28151b59137048609`.
[Successful Windows run](https://github.com/deanprattusa-cpu/StreamerBot_NexctGen/actions/runs/40872240495).
[Download verified package](https://github.com/deanprattusa-cpu/StreamerBot_NexctGen/actions/runs/40872240495/artifacts/12897655059).

Extract the ZIP. Close the installed app completely and run
`StreamerBot-Native-Update.exe`; no uninstall is needed. If the old hidden process
remains, end `StreamerBot.Native.exe` in Task Manager once. Use the Setup EXE for
a first installation on Windows 11 x64. Both packages bundle .NET/WindowsAppSDK.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| StreamerBot-Native-Setup.exe | 64,957,363 | `95c41699f559c29f8e0f2dd2e339f14fac78daf9afc93c0e4a5954e7a4729fc5` |
| StreamerBot-Native-Update.exe | 64,946,760 | `da2159110594a45cc5a9e53ed9859654a58035bb141296cd16d991fbf1310fac` |

## Changes and verification

Windows 2025, .NET SDK 10.0.403/runtime 10.0.14, Python 3.11 and NSIS 3.10:

- 64 C# core checks and 20 Python checks passed, covering profile/channel/credential
  isolation, worker/renderer boundaries, selected caches, update versions/origins
  and SHA-256 rejection. Provider/models use synthetic fixtures.
- Actual Windows SendInput: E typing while observed/suspended, observer disposal
  and rebinding. Shortcuts forward input instead of reserving bare letters.
- Actual mouse input: chat/shortcuts header dragging, hide/reopen saved position,
  and chat resizing beyond the original client boundary.
- Actual Paths primary Save/reopen with an unrelated unavailable imported path;
  general Settings persistence and worker reload. Legacy adapters use the selected cache.
- Self-contained publish, native main/detached/review windows, channel selection,
  native layout/shutdown, installed-app interaction smoke.
- Actual update EXE: reject fresh installations and a locked app; replace stale
  payload, record version 0.3.2, retain profile/cache bytes and user-added files.
- Actual silent `/S /RESTART /APP_PID` handoff: wait for the running installed app,
  close cleanly, apply, reopen; retain selected cache and unknown settings.
- Actual uninstall removes packaged files and retains user data.

Both EXE hashes and all smoke/installed-app markers were verified after downloading;
no crash/failure marker, published stderr empty. Native main/chat screenshots were
visually inspected. Local native compile passed with zero warnings/errors.
Earlier checks found and resolved focus setup, drag hit area/client coordinates and
resize capture outside XAML; final required gates passed without relaxing assertions.

## Publication status

**Release publication is blocked in this Coding task.** The runtime rejected
`gh release create` because CodeRabbit owns remote branch/PR delivery. No release
was published. Assets and release notes are prepared; see ../BUILD-NATIVE.md.
Help → Check for updates reads this repository's public latest stable release.
Its download/apply components passed synthetic core and actual Windows installer
checks; a live published-release download still needs verification after publication.

## Remaining limits

This remains a migration preview. Full original parity, advanced reaction/media
workflows and account policies remain incomplete; see NATIVE-PARITY.md. Real GPU
RVC/other voice/SadTalker inference, live providers/Google authorization, microphone
and audio devices have not been verified with real accounts/hardware. Soprano is
unavailable. Desktop mouse checks do not certify behavior over every game/display/DPI.
The main application is native C#/WinUI; voice/video helpers remain isolated Python.
