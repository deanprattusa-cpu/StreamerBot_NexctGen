# StreamerBot Native source

This archive contains the native C# / .NET 10 / WinUI 3 migration preview and its isolated voice and avatar-rendering workers.
See windows/NATIVE-PARITY.md for incomplete original workflows and installation instructions.

## Build on Windows 11 x64
1. Install the .NET 10 x64 SDK, Python 3.11 x64 (for build checks), and NSIS 3.10.
2. Extract this complete archive into a writable folder.
3. Open PowerShell in that folder and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\windows\Build-NativeInstaller.ps1 -MakeNsis 'C:\Program Files (x86)\NSIS\makensis.exe'
```

The build runs the core/worker checks and a real native window startup test, then creates `artifacts/native/StreamerBot-Native-Setup.exe`,
`StreamerBot-Native-Update.exe` and `SHA256SUMS.txt`. On disposable Windows CI, it
also tests actual update replacement, running-app PID handoff/restart, data
retention and uninstall.
The installed app bundles .NET and the Windows UI runtime. Python for original voice engines is prepared privately on demand. SadTalker uses the separately selected Python environment and existing models. No provider credentials, user profiles or models are included.

Do not treat this preview as a completed behavioral port of the original.

## Publish app updates

The in-app updater reads stable GitHub Releases for
`deanprattusa-cpu/StreamerBot_NexctGen`. Each release must contain exactly named
`StreamerBot-Native-Update.exe` and `SHA256SUMS.txt`; publish the Setup EXE as well
for first installations. Use a `vMAJOR.MINOR.PATCH` tag matching the app assembly,
installer and registry versions. Always wait for the exact source Windows run
to pass and verify both asset hashes before publishing.

The verified 0.3.1 assets are in
[Windows artifact 12893316533](https://github.com/deanprattusa-cpu/StreamerBot_NexctGen/actions/runs/40864098782/artifacts/12893316533).
Publication remains pending because this Coding task's GitHub delivery policy
rejected release publishing. From a separately authenticated GitHub CLI, in the
extracted artifact folder with the prepared release notes saved as `RELEASE-NOTES.md`:

```powershell
gh release create v0.3.1 StreamerBot-Native-Setup.exe StreamerBot-Native-Update.exe SHA256SUMS.txt NATIVE-PARITY.md --repo deanprattusa-cpu/StreamerBot_NexctGen --target 76c963d873c84bae626b1ad5ec16e1da0ac6fc43 --title "StreamerBot Native 0.3.1" --notes-file RELEASE-NOTES.md
```

This command has not published a release from this task. After publication,
verify the actual public feed and checksum download using the app's UpdateClient:
an older version must receive the release, and the matching version must report
that no newer update is available. Version 0.3.0 needs the one-time update EXE to
add the update menu; subsequent published versions use the in-app flow.
