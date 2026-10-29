# Native Windows UI repair — 2026-10-29

Source: `2c062a8dd5acd68588fcaca3c97bbf53fe3a4c7b`.

[Download installer artifact](https://github.com/deanprattusa-cpu/StreamerBot_NexctGen/actions/runs/40851228253/artifacts/12887218023). Extract the ZIP and run `StreamerBot-Native-Setup.exe` on Windows 11 x64.

Installer: 64,958,603 bytes. SHA-256: `63348dfcee4d3ea6d6fa498abcc186145d01b127eeea1fc14517beb4447ad368`.

## Restored in C# / .NET 10 / WinUI 3

- Original `.env` numbered channels, selected account, original token discovery, and Google Desktop client authorization flow.
- Light menu, black chat feed, green log, adjustable split and compact toolbar that wraps on narrow screens.
- Separate topmost chat, review, keybind, blue shortcuts and statistics windows.
- Compact reaction rows with override, prompt, output mode, Short, Mute AI and Sound settings.
- Edited review output choices, reject/cancel, chat edit/reply/refresh and hold-to-talk controls.

Re-import the original `.env` through File → Import .env; import the original JSON config through File → Load Config. Both imports preserve the original files. Select the channel and use Credentials if its original token is not found.

## Verification

[Windows run](https://github.com/deanprattusa-cpu/StreamerBot_NexctGen/actions/runs/40851228253) passed: 45 C# checks, 5 worker tests, 6 video/path tests, self-contained publish, native startup/layout/shutdown, channel selection, detached window toggle/reopen/topmost, review edit/mode/rejection, actual installation and installed-app smoke, uninstall with data retention. Local compile passed with zero warnings/errors. Artifact checksum verified. Native main, reaction editor, shortcut and chat screenshots visually inspected.

An earlier Windows run found a shutdown timeout caused by hidden tool windows; cleanup was fixed and subsequent runs passed.

## Still incomplete

This is not yet 100% original behavior. Automated game-specific reaction rewriting, advanced multi-tap/cache behavior, full music/YouTube controls, provider/account quota policy and remote companion work remain. Basic guide/statistics click-through is implemented; interactive click-through, dragging and opacity over games still need Windows testing. Real Google authorization, live counts, microphone/audio devices and GPU voices/SadTalker inference were not exercised with real accounts/hardware. Subscriber counts currently show `--`.

The Python voice worker and selected SadTalker runtime remain isolated helpers allowed by the YAML; the main application is native C#/WinUI.
