"""Local prerequisite audit. Does not contact providers or print credentials."""
import argparse
import importlib.util
import json
import platform
from pathlib import Path

ENGINES = {
    'Windows offline': ['pyttsx3'], 'Edge': ['edge_tts', 'pydub'],
    'Google': ['google.cloud.texttospeech'], 'RVC': ['torch', 'rvc_python', 'pyttsx3', 'pythoncom', 'soundfile'],
    'Kokoro': ['kokoro', 'torch', 'soundfile'], 'Bark / Bark CPU': ['torch', 'bark', 'numpy'],
    'Fish cloud': ['gradio_client', 'requests', 'pydub'], 'Bark cloud': ['gradio_client'],
    'Voicebox': ['requests', 'pydub'], 'TikTok': ['requests', 'pydub'],
    'ElevenLabs API': ['requests', 'pydub'], 'Typecast / ElevenLabs browser': ['selenium', 'playwright'],
}


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--config', type=Path);args=parser.parse_args()
    config=json.loads(args.config.read_text(encoding='utf-8-sig')) if args.config else {}
    print('Python:', platform.python_version(), 'OS:', platform.system())
    print('Prerequisites only: NOT an inference, login, endpoint or audio-device test.')
    for engine, packages in ENGINES.items():
        missing=[]
        for name in packages:
            try: present=importlib.util.find_spec(name) is not None
            except (ImportError, ModuleNotFoundError, ValueError): present=False
            if not present: missing.append(name)
        print(engine+': '+('MISSING '+', '.join(missing) if missing else 'imports discoverable; speech test required'))
    print('Soprano: BLOCKED — original source has no initialized model loader.')
    if config:
        for name in ['rvc_model_path','rvc_index_path','fish_audio_path','fish_text_path']:
            value=config.get(name, '')
            print(name+': '+('file exists' if value and Path(value).is_file() else 'unset or missing'))
        print('RVC pitch:', config.get('rvc_pitch', 0))
    print('RVC uses Windows SAPI source speech; select .pth and optional .index. First load may download RVC assets.')
    print('Bark cloud uses a hard-coded temporary Gradio URL; endpoint availability is unverified.')
    print('Fish cloud, Voicebox, TikTok, Google, ElevenLabs and browser voices need their live services/accounts.')

if __name__=='__main__': main()
