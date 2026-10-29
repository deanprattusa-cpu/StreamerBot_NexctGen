"""Synthetic worker boundary checks; no voice models, accounts or audio devices."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("native_voice_worker", Path(__file__).with_name("worker.py"))
worker = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(worker)


class WorkerChecks(unittest.TestCase):
    def test_cache_hit_survives_unrelated_settings(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            calls = []
            class Communicate:
                def __init__(self, text, voice):
                    calls.append((text, voice))
                async def save(self, path):
                    Path(path).write_bytes(b"synthetic audio fixture" * 8)
            profile = {"tts_mode": "edge", "edge_voice": "synthetic-voice", "volume": 1.0}
            request = {"profile": profile, "text": "Synthetic phrase", "category": "hk_test"}
            with patch.dict(sys.modules, {"edge_tts": types.SimpleNamespace(Communicate=Communicate)}):
                first = worker.synthesize(request, root)
                profile.update(volume=0.2, force_cache_only=True, game_name="another game")
                second = worker.synthesize(dict(request, cache_only=True), root)
            self.assertEqual(first["audio"], second["audio"])
            self.assertTrue(second["cached"])
            self.assertEqual(len(calls), 1)

    def test_cache_only_miss_never_imports_engine(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(worker.WorkerError, "No matching voice cache"):
                worker.synthesize({"text": "Missing phrase", "cache_only": True, "profile": {"tts_mode": "made-up"}}, Path(directory))

    def test_recording_cannot_escape_private_data(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(worker.WorkerError, "inside native app data"):
                worker.handle({"protocol": 1, "operation": "transcribe", "audio": __file__}, Path(directory))

    def test_incomplete_engines_are_explicit(self):
        for engine in ("soprano",):
            with tempfile.TemporaryDirectory() as directory:
                with self.assertRaisesRegex(worker.WorkerError, "incomplete"):
                    worker.synthesize({"text": "test", "profile": {"tts_mode": engine}}, Path(directory))

    def test_rvc_routes_selected_model_index_and_pitch(self):
        calls = []
        def synth(text, target, model, index, pitch):
            calls.append((text, model, index, pitch))
            Path(target).write_bytes(b"synthetic wav" * 8)
        with tempfile.TemporaryDirectory() as directory, patch.dict(sys.modules, {"rvc_voice": types.SimpleNamespace(synthesize=synth)}):
            profile = {"tts_mode": "rvc", "rvc_model_path": "selected.pth", "rvc_index_path": "selected.index", "rvc_pitch": -3}
            result = worker.synthesize({"text": "hello", "profile": profile}, Path(directory))
            self.assertTrue(Path(result["audio"]).is_file())
            self.assertEqual(calls, [("hello", "selected.pth", "selected.index", -3)])

    def test_legacy_adapters_write_inside_selected_cache_and_restore_helpers(self):
        for engine in ("fish", "voicebox", "tiktok", "elevenlabs"):
            with self.subTest(engine=engine), tempfile.TemporaryDirectory() as directory:
                root = Path(directory).resolve(); selected = root / "selected external cache"
                original_cache = lambda *a, **k: (_ for _ in ()).throw(AssertionError("unselected legacy cache used"))
                original_play = lambda *a: None
                model = types.SimpleNamespace(_handle_tts_cache=original_cache, play_wav_winsound=original_play)
                def speak(*args, **kwargs):
                    handled, audio, transcript = model._handle_tts_cache("unused")
                    self.assertFalse(handled)
                    self.assertEqual(Path(audio).parent, selected)
                    Path(audio).write_bytes(b"synthetic wav" * 8)
                    Path(transcript).write_text("synthetic phrase")
                    model.play_wav_winsound(audio)
                model.play_fish_speech_tts_audio = model.play_voicebox_tts_audio = model.play_tiktok_tts_audio = speak
                model.StreamerBotModel = types.SimpleNamespace(play_elevenlabs_tts_audio=speak)
                profile = {"tts_mode": engine, "voice_cache_path": str(selected)}
                with patch.object(worker, "legacy_model", return_value=model):
                    first = worker.synthesize({"text": "hello", "profile": profile}, root)
                    second = worker.synthesize({"text": "hello", "profile": profile, "cache_only": True}, root)
                self.assertTrue(second["cached"])
                self.assertEqual(Path(first["audio"]).suffix, ".wav")
                self.assertIs(model._handle_tts_cache, original_cache)
                self.assertIs(model.play_wav_winsound, original_play)
                self.assertEqual(list(root.iterdir()), [selected])

    def test_legacy_cache_helpers_restore_after_failure(self):
        cache, play = object(), object()
        model = types.SimpleNamespace(_handle_tts_cache=cache, play_wav_winsound=play)
        with self.assertRaises(ValueError):
            with worker.native_cache_capture(model, Path("unused.wav"), []):
                raise ValueError("synthetic engine failure")
        self.assertIs(model._handle_tts_cache, cache)
        self.assertIs(model.play_wav_winsound, play)

    def test_stdio_recovers_from_bad_json_without_echoing_input(self):
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run([sys.executable, str(Path(__file__).with_name("worker.py")), "--data-root", directory], input='["synthetic-private-value"]\n' + json.dumps({"protocol": 1, "operation": "health", "id": "test"}) + '\n', text=True, capture_output=True, check=True)
            lines = [json.loads(line) for line in result.stdout.splitlines()]
            self.assertFalse(lines[0]["ok"])
            self.assertNotIn("synthetic-private-value", result.stdout)
            self.assertTrue(lines[1]["ok"])
            self.assertEqual(lines[1]["id"], "test")


if __name__ == "__main__":
    unittest.main()
