# Native Windows 0.3.1 — 2026-10-29

Source: `76c963d873c84bae626b1ad5ec16e1da0ac6fc43`.
[Successful Windows run](https://github.com/deanprattusa-cpu/StreamerBot_NexctGen/actions/runs/40864098782).
[Download verified package](https://github.com/deanprattusa-cpu/StreamerBot_NexctGen/actions/runs/40864098782/artifacts/12893316533).

Extract the ZIP. Close the installed app completely and run
`StreamerBot-Native-Update.exe`; no uninstall is needed. If the old hidden process
remains, end `StreamerBot.Native.exe` in Task Manager once. Use the Setup EXE for
a first installation on Windows 11 x64. Both packages bundle .NET/WindowsAppSDK.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| StreamerBot-Native-Setup.exe | 64,964,293 | `aa1796cc04e86245e9a97272bb3a13f3ca127b264f42ae86176cadcf58ff5c8f` |
| StreamerBot-Native-Update.exe | 64,959,550 | `1a86723877d280a321fb291c25b5b610190aee3166767fd8258e2d04de5a324b` |

## Changes and verification

Windows 2025, .NET SDK 10.0.403/runtime 10.0.14, Python 3.11 and NSIS 3.10:

- 56 C# core checks and 19 Python checks passed, covering profile/channel/credential
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
  payload, record version 0.3.1, retain profile/cache bytes and user-added files.
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
