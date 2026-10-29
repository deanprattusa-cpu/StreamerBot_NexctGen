# Original StreamerBot voice repair

Close StreamerBot before applying. This repairs the Python source folder containing
`model.py` (including the Portable-AutoSetup version). It cannot patch a bundled EXE.
Do not extract these files directly over your installation.

1. Extract this ZIP to a separate folder.
2. Use the same Python executable/environment that launches your original app:

   `"C:\path\to\original\python.exe" "C:\repair\apply_voice_repair.py" "D:\path\to\StreamerBot"`

3. Restart the original app. In RVC settings select an existing `.pth` model,
   optional matching `.index`, and pitch. Test one short sentence.
4. For prerequisite diagnostics, run with that same Python:

   `"C:\path\to\original\python.exe" "C:\repair\check_voices.py" --config "D:\path\to\StreamerBot\bot_config.json"`

   Omit `--config` if your profile is stored elsewhere or you do not know its name.
   Send the resulting report and the `[RVC]` / `[RVC Engine Error]` log lines.
   The report never prints credential values.

The repair saves the old model and any replaced helper files in a dated
`voice-repair-backup-*` folder. It preserves other source changes, profiles,
.env files, credentials and models. To undo, close the app and copy backed-up files
back; remove new helper files only if they were absent before the repair.

## What changed

- RVC previously called an uninitialized global and passed text to an audio converter.
  It now uses Windows SAPI to synthesize source speech, loads the selected RVC
  model/index, applies pitch, converts the WAV, validates it, and caches the result.
  Conversion is serialized and a failed conversion cannot replace existing audio.
  CUDA is used when PyTorch reports it available; otherwise the log explicitly
  identifies CPU. First use may download RVC's prerequisite model assets.
- Edge and Google entry points were missing from the original model. They now
  generate audio through their existing SDKs and shared playback/cache helpers.
- Kokoro now initializes its pipeline when missing instead of returning silently.
- Missing Soprano initialization reports an error instead of substituting Windows TTS.
  This does not implement Soprano itself.

## Engine audit — not a claim of successful speech on your PC

| Engine | Code/prerequisite result | Still needs a live test |
|---|---|---|
| RVC | Missing pipeline repaired; synthetic conversion regression checks pass | Windows SAPI, rvc-python dependencies, your model/index, actual CPU/CUDA inference |
| Edge | Missing original function restored; routing/conversion fixture passes | Network service, edge-tts, FFmpeg and playback |
| Google | Missing original function restored; selected voice/PCM fixture passes | Authorized Google TTS client, enabled API/quota and playback |
| Kokoro | Silent missing-pipeline path repaired | Kokoro weights, pronunciation dependencies, device and output |
| Bark / Bark CPU | Existing adapter retained | Models, PyTorch compatibility, CPU/CUDA inference |
| Bark cloud | Original code uses a fixed temporary Gradio URL | A running compatible endpoint; current URL is unverified |
| Fish cloud | Existing Gradio/fallback adapter retained | Endpoint/API compatibility and reference audio/transcript |
| Voicebox | Existing HTTP adapter retained | Running configured server and compatible response |
| TikTok | Existing adapter retained | Working service/session and chosen voice |
| ElevenLabs API | Existing preset-based adapter retained | Saved voice/API-key preset, quota and service |
| Typecast / ElevenLabs browser | Existing browser automation retained | Browser dependencies, account login and actual queue completion |
| Windows offline | Existing SAPI playback retained | Installed Windows voice and speaker device |
| Soprano | No working original model loader; explicitly blocked | Implementation/model initialization still required |

Validation: synthetic RVC/Edge/Google and worker checks plus repair against the
original archive pass. These do not validate GPU inference or all provider services.
No keys, accounts, models or dependency upgrades are included. The existing
`rvc-python`/fairseq dependency stack can still fail in your environment; use the
actual error log to select a compatible environment rather than upgrading Torch blindly.

RVC API reference: https://github.com/daswer123/rvc-python/blob/main/rvc_python/infer.py
