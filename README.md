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

The native app imports original environment channels and provides separate chat,
review, keybind, keyboard-shortcut and statistics windows. Version **0.3.2** adds one shared original credentials folder, quota-only token
rotation, reconnect handling and original Audio voice-selection menus. Choose
**Credentials → Choose shared credentials folder**, then select your channel and
Start. Existing keyboard, path, overlay and update fixes remain included.

[Download the verified 0.3.2 package](https://github.com/deanprattusa-cpu/StreamerBot_NexctGen/actions/runs/40872240495/artifacts/12897655059).
Extract it and run `StreamerBot-Native-Update.exe` to update an existing installation;
use `StreamerBot-Native-Setup.exe` for a first installation. Close the old app
completely before applying the one-time update. Settings, credentials, selected
caches and models are preserved.

Windows run `40872240495` verified source
`91fae6fb6d012a0de3bae5f28151b59137048609`: 64 core checks, 20 Python checks,
native keyboard/mouse/dialog interaction, install, upgrade, silent restart and
uninstall retention passed. See [windows/BUILD-VALIDATION.md](windows/BUILD-VALIDATION.md)
for checksums and limitations. Publication of the prepared `v0.3.2` GitHub Release
is still pending: this task's GitHub delivery policy rejected release publishing.
The in-app checker reads stable published releases, so it will offer updates once
the release EXE and checksum assets are published. See [BUILD-NATIVE.md](BUILD-NATIVE.md)
for the publication command. Do not substitute the older installer in the YAML.

## Packages

- `StreamerBot-Native-Source.zip`: frozen source backup; not an installer.
- `StreamerBot-ControlPanel-Paths-Video-Repair.zip`: separate repair for the
  original Python control panel. Close that app, extract the repair separately
  and follow its README. It does not repair a packaged single-file EXE.
- `StreamerBot-Portable-AutoSetup.zip` and `StreamerBot-CUDA-Repair.zip`:
  historical recovery packages for the original application.

No credentials, private profiles or models are included. Actual GPU inference,
audio devices and live accounts still require testing on the user's Windows PC.
