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

The build runs the core/worker checks and a real native window startup test, then creates artifacts/native/StreamerBot-Native-Setup.exe.
The installed app bundles .NET and the Windows UI runtime. Python for original voice engines is prepared privately on demand. SadTalker uses the separately selected Python environment and existing models. No provider credentials, user profiles or models are included.

Do not treat this preview as a completed behavioral port of the original.
