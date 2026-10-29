# StreamerBot Native 0.3.1 — migration preview

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
Open Audio → Voice presets / devices, select `offline`, and click Test voice first.

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
broadcast. Credentials → Import Google JSON / Authorize channel accepts the original Google Desktop app client-secret JSON and opens account consent in the system browser. It uses a local loopback callback and PKCE; real account authorization still needs user testing. Simulate
never posts messages. Provider availability, quotas and model access are external.

## Updates, keyboard and saved paths in 0.3.1

Existing installations: close StreamerBot completely and run
`StreamerBot-Native-Update.exe`. It updates the installed application and preserves
`%LOCALAPPDATA%\StreamerBotNative`, selected caches/models and user-added files.
The update requires an existing installation. The installer refuses to overwrite
a locked/running executable before changing files. No uninstall is needed.

Help → Check for updates reads this repository's latest published stable GitHub
release. Confirm Download and update to fetch the update EXE, verify its SHA-256,
close the app, apply the update silently and reopen it. The first update from
0.3.0 must be applied manually because that build has no update menu. Releases
must include `StreamerBot-Native-Update.exe` and `SHA256SUMS.txt`.

Shortcuts now observe and forward keyboard events. Bare-letter media bindings
such as E remain available to other apps and text fields; suspension does not
reserve the key. Closing/rebinding stops and releases the listener.

Paths saves edited selections without requiring unrelated old imported model
paths to exist. Invalid newly selected inputs still prevent saving. Cache
selections survive restart, and legacy voice adapters write directly into the
selected native cache rather than first writing into their own default cache.
Chat and shortcuts use captured pointer dragging and resizing; statistics drag
uses the same handler. Saved overlay positions survive hiding/reopening.

## Original UI and channel workflow restored in 0.3

The main screen uses the original menu/toolbar, live chat feed and green system
log arrangement. Channel is a dropdown again, with Add and Credentials controls.
Import the original `.env` through File → Import .env, then import your JSON
config through File → Load Config. Either order retains channel/environment data.
`CHANNEL_n_NAME`, `CHANNEL_n_ID` and `CHANNEL_n_TOKEN_PREFIX` are imported;
nonconsecutive channel numbers and the original zero-based `channel_index` work.
Unknown environment fields and multiple provider keys are retained in the private
native profile. Original input files are not rewritten.

Select a channel before Start. Authorized tokens named `<prefix>_token_N.json`
in `credentials` beside the imported `.env` are discovered for that channel.
Alternatively import its authorized token or authorize its Google Desktop client
through Credentials. An explicitly assigned token belongs to that channel only.
Live-broadcast lookup filters by the selected channel ID. Channel/game selection
is locked during a session; stop before switching. Original account quota-driven
credential rotation still needs migration.

Tools → Keybinds & Hotkeys opens a separate topmost native window, with General,
AI & Output, Chat Keys, Hype, Custom, Reactions, System, YouTube and Music groups.
Smart keys use compact two-line rows with inline override/prompt fields and a separate Sound dialog. System/YouTube/Music bindings use two columns. Save Changes & Re-Bind persists
bindings and closes the window. The original `hk_hotkeys_window` opens it too.

Tools → Chat List / Manual Reply opens a separate topmost chat selector. It has
message selection/editing, Hide Bots, Voice Only, Refresh, Reply, Improve, recording,
hold-to-talk, a draft editor and Send. Its borderless header drags the window; the corner grip resizes it. Enter sends; Shift+Enter inserts a newline. `hk_open_list`
toggles this window. Hotkeys suspend while editing text, as in the original.

Review mode opens a topmost AI Reply Review window with viewer context, editable
text, SEND (V+T), SEND (V), SEND (T), REVISE and REJECT. Closing rejects the reply.
The selected output mode and edited text reach the delivery pipeline; cancellation
closes the pending review. Review/chat geometry is saved in the original
`geo_review`/`geo_selector` fields. Other tool windows retain their geometry too.

The blue Keyboard Shortcuts overlay is a separate borderless, topmost native window.
`hk_guide` toggles it; `hk_guide_ghost` toggles click-through. Its keycaps reflect the
saved bindings each time it opens, and it retains `geo_guide`. Game context remains
a separate action. Live statistics opens native viewer/like tiles, refreshes every
30 seconds while visible, and retains `geo_stats`; unavailable subscriber totals
are displayed as `--`. These overlays support dragging, opacity and hide-all.

This is a restoration of the reported workflows, not certification of 100% parity.
Actual microphone capture and borderless window dragging still need interactive
Windows verification. Some keybind General/AI settings remain in the all-settings editor.
See the remaining behavior list below before replacing the working original app.

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
  while editing text in the app or its detached tools. Smart-key prompt/fixed phrase/audio paths.
- Native AI HTTP transport, key rotation, limited provider failover, cancellation.
- Native YouTube token refresh, active-broadcast lookup, polling, initial-history
  suppression, posting, manual replies, editable review, simulation, basic idle chat.
- Native Windows offline speech and audio playback; isolated original-engine
  voice adapters, cache files, microphone recording / transcription requests.
- Basic native music player, caption/image window, auxiliary YouTube window.

## Original behavior still requiring migration

- Advanced multi-tap and cache-shuffle hotkey behavior; unimplemented system
  actions report this explicitly when pressed. The editor retains their settings.
- Exact original failover policy, account/quota rotation and subscriber/statistics
  greeting triggers.
- Automated game-prompt/reaction rewriting, guide ghost modes, overlay ghost and
  opacity behavior, full music/YouTube controls, remote companion and generated
  full animated-avatar bridges (beyond the new SadTalker path).
- Full AI-generated cache batch workflows; this build caches fixed phrases only.
- Force-cache-only operation and visual gameplay capture/analysis are not ready.
- RVC now synthesizes a Windows SAPI source WAV and converts it with the selected
  RVC model/index. Real RVC/GPU playback still requires testing on the Windows PC.
  Soprano remains unavailable. Whisper requires an additional compatible whisper
  package; the original dependency installer does not supply it.
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
