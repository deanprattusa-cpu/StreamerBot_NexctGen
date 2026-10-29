"""Voice-only protocol worker for the C# native app. Never starts Tkinter or a dashboard."""

from __future__ import annotations

import argparse
import contextlib
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sys
import types

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
PROTOCOL = 1
_model = None
_kokoro = None


class WorkerError(ValueError):
    """An intentionally public, credential-free worker diagnostic."""



def legacy_model():
    global _model
    if _model is None:
        import model
        _model = model
    return _model


def gpu_status():
    spec = importlib.util.spec_from_file_location("native_gpu_setup", ROOT / "windows/prepare_original.py")
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    return helper.probe_torch(True)


def ghost_engine(engine):
    if engine == "typecast":
        from typecast_bridge import TypecastGhostEngine as Bridge
    else:
        from elevenlabs_ghost_bridge import ElevenLabsGhostEngine as Bridge
    # The C# owner terminates only its worker process tree. Never kill other
    # Chrome processes using the original bridge's global taskkill scan.
    Bridge._kill_zombie_browsers = lambda self: None
    return Bridge()


@contextlib.contextmanager
def native_cache_capture(model, target, captured):
    """Keep legacy voice adapters inside the native user's selected cache root."""
    previous_cache = model._handle_tts_cache
    previous_play = model.play_wav_winsound
    model._handle_tts_cache = lambda *a, **k: (False, str(target), str(target.with_suffix(".txt")))
    model.play_wav_winsound = lambda path, *a, **k: captured.append(Path(path))
    try:
        yield
    finally:
        model._handle_tts_cache = previous_cache
        model.play_wav_winsound = previous_play


def synthesize(request, data):
    global _kokoro
    profile = request.get("profile", {})
    engine = profile.get("tts_mode", "edge")
    text = str(request.get("text", "")).strip()
    category = str(request.get("category", "manual"))
    if not text or len(text) > 10000:
        raise WorkerError("Voice text must contain 1–10000 characters.")
    cache = Path(profile.get("voice_cache_path") or data / "VoiceCache").resolve()
    cache.mkdir(parents=True, exist_ok=True)
    voice_settings = {k: v for k, v in profile.items() if k.startswith(engine + "_") or k in {"global_model_path"}}
    identity = json.dumps({"engine": engine, "text": text, "voice": voice_settings, "category": category}, sort_keys=True)
    target = cache / (hashlib.sha256(identity.encode()).hexdigest() + (".mp3" if engine == "edge" else ".wav"))
    if target.exists() and not request.get("fresh", False):
        return {"audio": str(target), "cached": True}
    if request.get("cache_only"):
        raise WorkerError("No matching voice cache entry. Disable cache-only mode to synthesize this phrase.")
    model_path = profile.get("global_model_path") or str(data / "Models")
    if model_path:
        os.environ["SB_MODEL_ROOT"] = str(model_path)
        os.environ["HF_HOME"] = str(Path(model_path) / "HuggingFaceCache")
        os.environ["TORCH_HOME"] = str(Path(model_path) / "TorchCache")
        os.environ["XDG_CACHE_HOME"] = str(Path(model_path) / "BarkCache")
    if profile.get("hf_token"):
        os.environ["HF_TOKEN"] = str(profile["hf_token"])
    if engine in {"bark", "kokoro"} and not profile.get("bark_use_cpu", False):
        report = gpu_status()
        if report["status"] != "passed":
            raise RuntimeError("GPU voice requested but CUDA validation failed. Use Audio → Install / repair voice engines and GPU diagnostics.")
    if engine == "bark":
        from bark_fast import engine_for, write_wav
        # Keep the original Bark flag spellings/model lookup behavior.
        audio = engine_for(types.SimpleNamespace(**profile)).synth(text)
        if audio is None:
            raise RuntimeError("Bark returned no audio.")
        write_wav(str(target), audio)
    elif engine == "edge":
        import asyncio
        import edge_tts
        asyncio.run(edge_tts.Communicate(text, str(profile.get("edge_voice", "en-US-SteffanNeural"))).save(str(target)))
    elif engine == "google":
        from google.cloud import texttospeech
        token_file = profile.get("native_google_token_file", "")
        if token_file:
            from google.oauth2.credentials import Credentials
            credentials = Credentials.from_authorized_user_file(str(token_file))
            client = texttospeech.TextToSpeechClient(credentials=credentials)
        else:
            client = texttospeech.TextToSpeechClient()
        name = str(profile.get("google_voice", "en-US-Wavenet-I"))
        response = client.synthesize_speech(input=texttospeech.SynthesisInput(text=text), voice=texttospeech.VoiceSelectionParams(language_code="-".join(name.split("-")[:2]), name=name), audio_config=texttospeech.AudioConfig(audio_encoding=texttospeech.AudioEncoding.LINEAR16))
        target.write_bytes(response.audio_content)
    elif engine in {"typecast", "elevenlabs_ghost"}:
        bridge = ghost_engine(engine)
        bridge.play_ghost_audio(text)
        return {"handled_by_engine": True, "queued": True}
    elif engine == "rvc":
        from rvc_voice import synthesize as rvc_synthesize
        rvc_synthesize(text, target, profile.get("rvc_model_path", ""), profile.get("rvc_index_path", ""), profile.get("rvc_pitch", 0))
    elif engine == "soprano":
        raise WorkerError(f"{engine}: the original checkout does not initialize a usable local engine. Its settings are retained; native integration remains incomplete.")
    else:
        model = legacy_model()
        ffmpeg = str(profile.get("ffmpeg_path", ""))
        if ffmpeg:
            os.environ["IMAGEIO_FFMPEG_EXE"] = ffmpeg
            if hasattr(model, "AudioSegment"):
                model.AudioSegment.converter = ffmpeg
        captured = []
        common = dict(volume=1.0, category=category, game_name=str(profile.get("game_name", "General")), channel_name=str(profile.get("channel_name", "DefaultChannel")), force_fresh=bool(request.get("fresh", False)))
        with native_cache_capture(model, target, captured):
            if engine == "fish":
                model.play_fish_speech_tts_audio(text, profile.get("fish_audio_path", ""), profile.get("fish_text_path", ""), **common)
            elif engine == "voicebox":
                model.play_voicebox_tts_audio(text, profile.get("voicebox_url", "http://127.0.0.1:17493"), profile.get("voicebox_voice", "default"), **common)
            elif engine == "tiktok":
                os.environ["TIKTOK_SESSION_ID"] = str(profile.get("tiktok_session_id", ""))
                model.play_tiktok_tts_audio(text, profile.get("tiktok_voice", "en_us_002"), None, **common)
            elif engine == "kokoro":
                if _kokoro is None:
                    from kokoro import KPipeline
                    _kokoro = KPipeline(lang_code=str(profile.get("kokoro_voice", "am_adam"))[0])
                model.play_kokoro_tts_audio(text, None, _kokoro, profile.get("kokoro_voice", "am_adam"), **common)
            elif engine == "elevenlabs":
                settings = dict(profile)
                settings["elevenlabs_api_keys"] = profile.get("elevenlabs_keys", [])
                model.StreamerBotModel.play_elevenlabs_tts_audio(types.SimpleNamespace(**settings), text, profile.get("elevenlabs_voice", "Default"), **common)
            else:
                raise WorkerError("Select a supported voice engine. Windows offline speech runs in the native app.")
            source = next((p for p in reversed(captured) if p.is_file()), None)
            if source is None:
                raise RuntimeError("The voice engine produced no audio. Check its account, model paths, presets and dependencies.")
            if source.resolve() != target:
                shutil.copyfile(source, target)
    if not target.is_file() or target.stat().st_size < 44:
        raise RuntimeError("Voice engine returned an empty audio file.")
    return {"audio": str(target), "cached": False}


def handle(request, data):
    if request.get("protocol") != PROTOCOL:
        raise WorkerError("Unsupported voice protocol.")
    operation = request.get("operation")
    if operation == "health":
        return {"worker": "voice-only", "python": sys.version.split()[0]}
    if operation == "gpu":
        return {"gpu": gpu_status()}
    if operation == "speak":
        return synthesize(request, data)
    if operation == "login":
        if request.get("engine") not in {"typecast", "elevenlabs_ghost"}:
            raise WorkerError("Select Typecast or ElevenLabs Ghost for browser login.")
        ghost_engine(request["engine"]).setup_login()
        return {"login_closed": True}
    if operation == "transcribe":
        audio = Path(str(request.get("audio", ""))).resolve()
        if not audio.is_relative_to(data) or not audio.is_file():
            raise WorkerError("Recording must be inside native app data.")
        if request.get("engine") == "whisper":
            import whisper
            recognizer = whisper.load_model("base")
            return {"text": recognizer.transcribe(str(audio))["text"]}
        import speech_recognition as sr
        recognizer = sr.Recognizer()
        with sr.AudioFile(str(audio)) as source:
            content = recognizer.record(source)
        return {"text": recognizer.recognize_google(content)}
    raise WorkerError("Unsupported voice operation.")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-root", required=True, type=Path)
    data = parser.parse_args().data_root.resolve()
    data.mkdir(parents=True, exist_ok=True)
    os.chdir(data)
    output = sys.stdout
    for raw in sys.stdin:
        response = {"protocol": PROTOCOL, "id": None, "ok": False}
        try:
            if len(raw) > 4 * 1024 * 1024:
                raise WorkerError("Voice request exceeds 4 MB.")
            request = json.loads(raw)
            response["id"] = request.get("id")
            with contextlib.redirect_stdout(sys.stderr):
                response.update(handle(request, data))
            response["ok"] = True
        except WorkerError as error:
            response["error"] = str(error)
        except Exception as error:
            # Provider exceptions can contain credentials or response bodies.
            response["error"] = f"Voice operation failed ({type(error).__name__}). Check engine dependencies, account and model configuration."
        output.write(json.dumps(response, ensure_ascii=True) + "\n")
        output.flush()


if __name__ == "__main__":
    main()
