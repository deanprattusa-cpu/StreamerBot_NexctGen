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
        for engine in ("rvc", "soprano"):
            with tempfile.TemporaryDirectory() as directory:
                with self.assertRaisesRegex(worker.WorkerError, "incomplete"):
                    worker.synthesize({"text": "test", "profile": {"tts_mode": engine}}, Path(directory))

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
