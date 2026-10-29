import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import asyncio
import tempfile
import types
import unittest
from unittest.mock import patch
import original_voice_functions as voices

class SourceChecks(unittest.TestCase):
    def test_edge_converts_and_plays_and_cleans_temp(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d); audio=root/'out.wav'; transcript=root/'out.txt'; played=[]; temps=[]
            model=types.SimpleNamespace(_handle_tts_cache=lambda *a,**k:(False,str(audio),str(transcript)), play_wav_winsound=lambda *a:played.append(a))
            class Edge:
                def __init__(self,text,voice): self.text=text
                async def save(self,path): temps.append(Path(path));Path(path).write_bytes(b'mp3')
            class Segment:
                @staticmethod
                def from_file(path,format):
                    assert Path(path).read_bytes()==b'mp3';return Segment()
                def export(self,path,format): Path(path).write_bytes(b'converted wav')
            with patch.dict(sys.modules, {'model':model,'edge_tts':types.SimpleNamespace(Communicate=Edge),'pydub':types.SimpleNamespace(AudioSegment=Segment)}):
                asyncio.run(voices.play_edge_tts_audio_gui('hello','test',volume=.4))
            self.assertEqual(transcript.read_text(),'hello');self.assertEqual(played,[(str(audio),.4)]);self.assertFalse(temps[0].exists())

    def test_google_uses_selected_voice_and_linear16(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d); audio=root/'out.wav'; transcript=root/'out.txt'; calls=[]
            api=types.SimpleNamespace(SynthesisInput=lambda **kw:kw,VoiceSelectionParams=lambda **kw:kw,AudioConfig=lambda **kw:kw,AudioEncoding=types.SimpleNamespace(LINEAR16='PCM'))
            model=types.SimpleNamespace(_handle_tts_cache=lambda *a,**k:(False,str(audio),str(transcript)))
            def synth(**kw): calls.append(kw);return types.SimpleNamespace(audio_content=b'a'*80)
            with patch.dict(sys.modules, {'model':model,'google.cloud':types.SimpleNamespace(texttospeech=api)}):
                voices.play_google_tts_audio_gui(types.SimpleNamespace(synthesize_speech=synth),'hello','en-US-Wavenet-I',volume=0)
            self.assertEqual(calls[0]['voice'],{'language_code':'en-US','name':'en-US-Wavenet-I'});self.assertEqual(audio.stat().st_size,80)

if __name__=='__main__': unittest.main()
