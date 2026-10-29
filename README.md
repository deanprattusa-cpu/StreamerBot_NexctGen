# StreamerBot Native

C# / .NET 10 / WinUI 3 migration preview for Windows 11 x64. The native source is
restored here from `StreamerBot-Native-Source.zip`; this is a working source
checkout, not just a collection of backups.

Start with [BUILD-NATIVE.md](BUILD-NATIVE.md) for Windows build requirements and
[windows/NATIVE-PARITY.md](windows/NATIVE-PARITY.md) for installation, resource
paths, SadTalker configuration and remaining original workflows. Keep your
working original application available while migration continues.

## Continuation

`Continue-StreamerBot.yaml` records the historical handoff. Its saved repository
and Windows run are currently inaccessible with this task's access (HTTP 404).
The backup's 46 source files were verified against `SOURCE-SHA256.json` before
restoring. That manifest deliberately describes the frozen backup revision
`85aea4e347dc58d2b4a43e1258e75a3f8f9fdd69`, not subsequent working-tree edits.
`BACKUP-SHA256.json` continues to describe the unchanged backup files.

The pending core assertion now constructs the expected relative Python path
with separate `Path.Combine` components, matching Windows separator
normalization. Profile-origin resolution and the application behavior are
unchanged. The Windows installer workflow targets this task's assigned branch,
`coderabbit/continue-from-yaml/43521e9c`, and also supports manual dispatch.

Local continuation checks passed: 28 core integration checks, 5 worker tests and
6 video/path tests. These use synthetic services and inference fixtures. A new
Windows workflow must pass full publish, native UI, installer, installed-app and
uninstall retention checks before an updated installer is verified. Read the
exact commit's workflow result and the `StreamerBot-Native-Windows-x64` artifact;
do not substitute the older installer linked in the historical YAML.

The continuation build at `c1348123148734ad8864aef788bd9985e2f6ffd6` has now passed
those Windows gates. See [windows/BUILD-VALIDATION.md](windows/BUILD-VALIDATION.md)
for the exact run, download and checksum. Real GPU inference remains unverified.

## Packages

- `StreamerBot-Native-Source.zip`: frozen source backup; not an installer.
- `StreamerBot-ControlPanel-Paths-Video-Repair.zip`: separate repair for the
  original Python control panel. Close that app, extract the repair separately
  and follow its README. It does not repair a packaged single-file EXE.
- `StreamerBot-Portable-AutoSetup.zip` and `StreamerBot-CUDA-Repair.zip`:
  historical recovery packages for the original application.

No credentials, private profiles or models are included. Actual GPU inference,
audio devices and live accounts still require testing on the user's Windows PC.
