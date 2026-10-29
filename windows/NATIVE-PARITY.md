# StreamerBot Native 0.2 — migration preview

This is a new C#/.NET 10 and WinUI 3 Windows desktop application. The main UI,
settings editor, global hotkeys, AI requests, session logic and YouTube REST
transport run in C#. It does not launch Tkinter or a browser dashboard.
Python voice/speech engines are isolated behind a versioned worker. SadTalker runs
as a separate renderer process in its selected Python environment. The optional auxiliary
YouTube player embeds WebView2; the main application does not use WebView2.

## Install and first test

Run StreamerBot-Native-Setup.exe on Windows 11 x64. Installation is per user and
requires no administrator account. .NET and Windows App SDK are bundled. No
Python installation is needed for the native interface or Windows offline voice.
Open Voice & devices, select `offline`, and click Test voice first.

Application files: `%LOCALAPPDATA%\Programs\StreamerBotNative`.
Default profiles, private voice runtime, models and caches: `%LOCALAPPDATA%\StreamerBotNative`.
Tools → Paths lets you select model, runtime, cache and media locations on other drives.
Uninstall removes application files and shortcuts; it preserves the data folder.
Keep the app closed while upgrading or uninstalling.

Use File → Load Config to import a COPY of your original JSON profile. All 521
original saver fields and unknown/nested fields are retained on export. Import
never rewrites the original file. Relative paths resolve against the imported
profile's folder; copy referenced models/audio to the other PC and update paths.
This is data compatibility, not a declaration of complete behavioral parity.
Imported numeric audio-device indices must be remapped on each PC.
Profiles contain API credentials: keep exports private.

For cloud AI, import your profile or enter your own keys and model names in Settings.
For YouTube, File → Import YouTube token accepts the original authorized-user JSON
with client_id, client_secret and refresh_token. The account needs an active live
broadcast. New-account OAuth authorization UI is not implemented yet. Simulate
never posts messages. Provider availability, quotas and model access are external.

## Voice and CUDA preparation

Audio → Install / repair voice engines downloads a private Python 3.11 runtime
and the original voice dependencies. A missing private Python runtime also starts
preparation on first non-offline voice use. Internet is required; downloads can be
several GB. Optional engines can still require compilers, models, services or
account logins. Preparation reports these failures rather than claiming success.
Audio → Repair CUDA reinstalls the pinned GPU-compatible PyTorch stack and runs
an actual CUDA tensor operation. A working NVIDIA driver must be installed on the
PC; no driver is bundled. The app does not silently downgrade a requested CUDA
voice to CPU. Use GPU diagnostics to inspect the actual result.
No API keys, YouTube tokens, real profiles, models or Python packages are bundled.
When a custom voice Python is selected, repair targets that selected virtual
environment. System Python is rejected; no unrelated Python install is modified.

## Resource paths and avatar video

Open **Tools → Paths — models, voices & video**. Browse to folders/files on any
local drive, or type their full paths. Blank means automatic defaults. Saved paths
remain in your profile; existing files are never moved. Missing input files are
reported; output folders are created when used. Voice workers restart after saving.

For SadTalker set:

1. **SadTalker project**: the folder containing `inference.py`.
2. **SadTalker Python**: its GPU-enabled `venv\Scripts\python.exe` or
   `.venv\Scripts\python.exe`. Blank detects a project venv, then uses voice Python.
3. **Avatar source image**, **checkpoints** (optional override), and **video output**.
4. **FFmpeg executable** if FFmpeg is not already available to that environment.

Enable rendering through Tools → Enable / disable avatar video rendering, or the
installed Toggle_Video_Rendering.bat. Rendering checks a real CUDA tensor in the
selected SadTalker environment before running; CPU mode is an explicit Paths
option. A voice environment passing CUDA does not establish SadTalker readiness.
SadTalker's code, checkpoints and Python dependencies must already be installed.
They are separate from the native installer and the original voice installer.

Detailed failures are saved in the selected video output folder (`renderer.log`),
with a link/path in the session log. Each render has a separate job folder; an old
MP4 is never returned as a new result. Failed renders fall back to voice audio.
The native app plays successful video through a native Windows player window.
Turning rendering off leaves audio playback available; original video-cache
shuffle/replay workflows are still incomplete in the native preview.

The original control panel also gains Tools → Paths through the separate repair
files. It saves `resource_paths.json` beside that app and includes paths in profile
exports. Its original voice runtime/cache controls are unchanged; native-only
runtime/cache fields are shown in the native app. Restart the original app after
changing an already-loaded model. The FasterLivePortrait bridge retains its
original driving-video behavior; choosing its location does not add audio-driven
lip sync.

## Implemented preview paths

- Native profile load/save/export, searchable settings and retained unknown fields.
- Original 49 smart/reaction keys and 32 system binding fields in the editor.
- Basic Windows modifier chords, duplicate/reserved-key diagnostics and suspension
  while the app is in the foreground. Smart-key prompt/fixed phrase/audio paths.
- Native AI HTTP transport, key rotation, limited provider failover, cancellation.
- Native YouTube token refresh, active-broadcast lookup, polling, initial-history
  suppression, posting, manual replies, editable review, simulation, basic idle chat.
- Native Windows offline speech and audio playback; isolated original-engine
  voice adapters, cache files, microphone recording / transcription requests.
- Basic native music player, caption/image window, auxiliary YouTube window.

## Original behavior still requiring migration

- Advanced multi-tap and cache-shuffle hotkey behavior; unimplemented system
  actions report this explicitly when pressed. The editor retains their settings.
- Exact original failover policy, account/quota rotation, subscriber/statistics
  greeting triggers, channel-account switching and new OAuth authorization UI.
- Automated game-prompt/reaction rewriting, guide ghost modes, overlay ghost and
  opacity behavior, full music/YouTube controls, remote companion and generated
  full animated-avatar bridges (beyond the new SadTalker path).
- Full AI-generated cache batch workflows; this build caches fixed phrases only.
- Force-cache-only operation and visual gameplay capture/analysis are not ready.
- RVC and Soprano did not have usable initialized loaders in the original checkout;
  their native integrations remain incomplete. Whisper requires an additional
  compatible whisper package; the original dependency installer does not supply it.
- Browser-automated Typecast/ElevenLabs Ghost queue completion is not confirmed by
  the worker. Some original voice adapters retain original limitations.

No game training or autonomous game input is included. Do not replace your working
original installation yet. A retained setting or engine menu entry does not mean
its implementation or account/hardware compatibility has been verified.

## Verification limits

See BUILD-VALIDATION.md next to this file for the actual build checks. Native UI,
audio hardware, GPU inference and live cloud accounts need real Windows testing.
This preview is unsigned; Windows may show an unknown-publisher notice. Verify
the supplied checksum and use only the installer you intended to download.
