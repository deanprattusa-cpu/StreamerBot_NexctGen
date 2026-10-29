"""Text-to-speech source followed by local RVC conversion (no UI or playback)."""
from pathlib import Path
import os
import tempfile
import threading
import wave

_LOCK = threading.Lock()
_CLIENT = None
_IDENTITY = None


def validate_wav(path):
    try:
        with wave.open(str(path), 'rb') as audio:
            if audio.getnframes() == 0:
                raise ValueError('empty audio')
    except (OSError, EOFError, wave.Error, ValueError) as exc:
        raise RuntimeError('RVC did not produce a readable nonempty PCM WAV.') from exc


def source_speech(text, path):
    if os.name != 'nt':
        raise RuntimeError('RVC source speech requires Windows SAPI on this installation.')
    import pythoncom
    import pyttsx3
    pythoncom.CoInitialize()
    engine = None
    try:
        engine = pyttsx3.init('sapi5')
        engine.setProperty('volume', 1.0)
        engine.save_to_file(text, str(path))
        engine.runAndWait()
    finally:
        if engine is not None:
            engine.stop()
        pythoncom.CoUninitialize()
    validate_wav(path)


def synthesize(text, output, model_path, index_path='', pitch=0):
    """Preserve the selected RVC model/index/pitch; never substitute another voice."""
    global _CLIENT, _IDENTITY
    model = Path(model_path).expanduser()
    if not model_path or not model.is_file() or model.suffix.lower() != '.pth':
        raise ValueError('RVC: select an existing .pth voice model in the RVC settings.')
    index = Path(index_path).expanduser() if index_path else None
    if index is not None and not index.is_file():
        raise ValueError('RVC: the selected .index file does not exist. Select it again or clear the optional index.')
    pitch = int(pitch)
    if not -24 <= pitch <= 24:
        raise ValueError('RVC pitch must be between -24 and 24 semitones.')
    if not str(text).strip():
        raise ValueError('RVC needs nonempty speech text.')
    output = Path(output).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    with _LOCK:
        import torch
        from rvc_python.infer import RVCInference
        device = 'cuda:0' if torch.cuda.is_available() else 'cpu:0'
        identity = (str(model.resolve()), model.stat().st_mtime_ns, str(index.resolve()) if index else '', index.stat().st_mtime_ns if index else 0, device)
        if _CLIENT is None or _IDENTITY != identity:
            # Publish only a fully loaded client; failed model changes never use the old voice.
            client = RVCInference(device=device)
            client.load_model(str(model.resolve()), index_path=str(index.resolve()) if index else '')
            _CLIENT, _IDENTITY = client, identity
            print('[RVC] Loaded selected voice on ' + device)
        _CLIENT.set_params(f0up_key=pitch, index_rate=0.5 if index else 0.0)
        with tempfile.TemporaryDirectory(prefix='rvc-', dir=output.parent) as scratch:
            source = Path(scratch) / 'source.wav'
            converted = Path(scratch) / 'converted.wav'
            source_speech(str(text), source)
            _CLIENT.infer_file(str(source), str(converted))
            validate_wav(converted)
            os.replace(converted, output)
    return str(output)
