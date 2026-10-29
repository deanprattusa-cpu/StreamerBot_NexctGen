"""Run with the original app closed: python apply_voice_repair.py PATH_TO_STREAMERBOT."""
from pathlib import Path
import ast
import datetime
import shutil
import sys


def apply(folder):
    folder = Path(folder).resolve(); target = folder/'model.py'
    source = target.read_text(encoding='utf-8-sig')
    old = "        if 'global_rvc_client' in globals():\n            global_rvc_client.infer(text, cache_file, model_path, index_path, pitch)"
    new = "        from rvc_voice import synthesize\n        synthesize(text, cache_file, model_path, index_path, pitch)"
    if old not in source and new not in source:
        raise RuntimeError('Unknown model.py RVC implementation. No files changed; use the matching source folder.')
    updated = source.replace(old, new)
    updated = updated.replace('        if not pipeline: return', "        if pipeline is None:\n            from kokoro import KPipeline\n            pipeline = KPipeline(lang_code=voice_name[0] if voice_name else 'a')")
    old_soprano = 'if self.soprano_loaded and self.soprano_model: play_soprano_tts_audio(self.soprano_model, text, device_index, voice, vol)\n                        else: play_windows_tts_audio(text, vol)'
    updated = updated.replace(old_soprano, 'if self.soprano_loaded and self.soprano_model: play_soprano_tts_audio(self.soprano_model, text, device_index, voice, vol)\n                        else: print("[Soprano Error] No Soprano model loader is installed. Select a working engine; Windows speech is not Soprano.")')
    tree = ast.parse(updated)
    names = {n.name for n in tree.body if isinstance(n, (ast.FunctionDef, ast.AsyncFunctionDef))}
    imports = 'from original_voice_functions import play_edge_tts_audio_gui, play_google_tts_audio_gui'
    if not {'play_edge_tts_audio_gui', 'play_google_tts_audio_gui'} & names and imports not in updated:
        updated += '\n' + imports + '\n'
    ast.parse(updated)
    helper = Path(__file__).with_name('rvc_voice.py')
    if not helper.is_file() or not Path(__file__).with_name('original_voice_functions.py').is_file(): raise RuntimeError('Keep rvc_voice.py beside this repair script.')
    stamp=datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    backup=folder/('voice-repair-backup-'+stamp); backup.mkdir()
    shutil.copy2(target, backup/'model.py')
    if (folder/'rvc_voice.py').exists(): shutil.copy2(folder/'rvc_voice.py', backup/'rvc_voice.py')
    other = folder/'original_voice_functions.py'
    if other.exists(): shutil.copy2(other, backup/other.name)
    shutil.copy2(Path(__file__).with_name(other.name), other)
    shutil.copy2(helper, folder/'rvc_voice.py')
    temporary=folder/('model.py.'+stamp+'.tmp'); temporary.write_text(updated, encoding='utf-8'); temporary.replace(target)
    print('Voice repair applied. Backup:', backup)
    print('Use the original app Python environment. Select your RVC .pth and optional .index, then test speech.')

if __name__ == '__main__':
    if len(sys.argv)!=2: raise SystemExit('Usage: python apply_voice_repair.py PATH_TO_STREAMERBOT_FOLDER')
    apply(sys.argv[1])
