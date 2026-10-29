"""Missing original Edge/Google entry points; retain shared cache and playback."""
import os
import tempfile
from pathlib import Path


async def play_edge_tts_audio_gui(text, voice, device_id=None, volume=1.0, force_fresh=False):
    import model
    import edge_tts
    from pydub import AudioSegment
    handled, audio, transcript = model._handle_tts_cache('edge', text, voice, volume, None, 'generic', 'General', 'DefaultChannel', force_fresh=force_fresh)
    if handled:
        return
    fd, path = tempfile.mkstemp(suffix='.mp3')
    os.close(fd)
    try:
        await edge_tts.Communicate(text, voice).save(path)
        AudioSegment.from_file(path, format='mp3').export(audio, format='wav')
        Path(transcript).write_text(text, encoding='utf-8')
        if volume > 0:
            model.play_wav_winsound(audio, volume)
    finally:
        Path(path).unlink(missing_ok=True)


def play_google_tts_audio_gui(client, text, voice, device_id=None, volume=1.0, force_fresh=False):
    import model
    from google.cloud import texttospeech
    handled, audio, transcript = model._handle_tts_cache('google', text, voice, volume, None, 'generic', 'General', 'DefaultChannel', force_fresh=force_fresh)
    if handled:
        return
    response = client.synthesize_speech(
        input=texttospeech.SynthesisInput(text=text),
        voice=texttospeech.VoiceSelectionParams(language_code='-'.join(voice.split('-')[:2]), name=voice),
        audio_config=texttospeech.AudioConfig(audio_encoding=texttospeech.AudioEncoding.LINEAR16))
    if len(response.audio_content) < 44:
        raise RuntimeError('Google TTS returned empty audio.')
    Path(audio).write_bytes(response.audio_content)
    Path(transcript).write_text(text, encoding='utf-8')
    if volume > 0:
        model.play_wav_winsound(audio, volume)
