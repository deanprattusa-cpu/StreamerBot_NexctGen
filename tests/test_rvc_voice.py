import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import tempfile
import types
import unittest
import wave
from unittest.mock import patch
import rvc_voice


def wav(path):
    with wave.open(str(path), 'wb') as f:
        f.setnchannels(1); f.setsampwidth(2); f.setframerate(16000); f.writeframes(b'\0\0' * 160)


class RvcChecks(unittest.TestCase):
    def setUp(self):
        rvc_voice._CLIENT = rvc_voice._IDENTITY = None

    def test_missing_model_fails_before_dependency_import(self):
        with self.assertRaisesRegex(ValueError, 'existing .pth'):
            rvc_voice.synthesize('hello', 'unused.wav', '')

    def test_speech_audio_conversion_model_switch_and_pitch(self):
        calls = []
        class Client:
            def __init__(self, **kw): calls.append(('init', kw))
            def load_model(self, model, **kw): calls.append(('load', model, kw))
            def set_params(self, **kw): calls.append(('params', kw))
            def infer_file(self, source, output):
                rvc_voice.validate_wav(source); calls.append(('convert',)); wav(output)
        modules = {'torch': types.SimpleNamespace(cuda=types.SimpleNamespace(is_available=lambda: False)), 'rvc_python.infer': types.SimpleNamespace(RVCInference=Client)}
        with tempfile.TemporaryDirectory() as d, patch.dict(sys.modules, modules), patch.object(rvc_voice, 'source_speech', side_effect=lambda text, path: wav(path)) as source:
            root = Path(d); model = root/'one.pth'; model.write_bytes(b'model'); index = root/'one.index'; index.write_bytes(b'index'); out = root/'result.wav'
            rvc_voice.synthesize('first', out, str(model), str(index), 3)
            rvc_voice.synthesize('second', out, str(model), '', -2)
            self.assertEqual(source.call_args_list[0].args[0], 'first')
            self.assertEqual(len([c for c in calls if c[0]=='load']), 2)
            self.assertIn(('params', {'f0up_key': 3, 'index_rate': 0.5}), calls)
            self.assertIn(('params', {'f0up_key': -2, 'index_rate': 0.0}), calls)
            rvc_voice.validate_wav(out)
            self.assertFalse(list(root.glob('rvc-*')))

    def test_failed_conversion_keeps_existing_output(self):
        class Client:
            def __init__(self, **kw): pass
            def load_model(self, *a, **kw): pass
            def set_params(self, **kw): pass
            def infer_file(self, source, output): Path(output).write_bytes(b'not audio')
        with tempfile.TemporaryDirectory() as d, patch.dict(sys.modules, {'torch': types.SimpleNamespace(cuda=types.SimpleNamespace(is_available=lambda: False)), 'rvc_python.infer': types.SimpleNamespace(RVCInference=Client)}), patch.object(rvc_voice, 'source_speech', side_effect=lambda text, path: wav(path)):
            model = Path(d)/'one.pth'; model.write_bytes(b'model'); out=Path(d)/'out.wav'; wav(out); before=out.read_bytes()
            with self.assertRaisesRegex(RuntimeError, 'nonempty PCM WAV'): rvc_voice.synthesize('hi', out, str(model))
            self.assertEqual(out.read_bytes(), before)
            self.assertFalse(list(Path(d).glob('rvc-*')))

if __name__ == '__main__': unittest.main()
