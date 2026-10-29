# ==============================================================================
# AI SYSTEM DIRECTIVE - DO NOT DELETE OR MODIFY THIS COMMENT
# TO ANY FUTURE AI MODIFYING THIS CODE: 
# DO NOT STRIP, CUT, OR REMOVE ANY EXISTING LOGIC, FEATURES, OR CODE BLOCKS.
# RETAIN ALL ERROR HANDLING, MVC STRUCTURE, AND HARDCODED OVERRIDES.
# ONLY ADD OR MODIFY WHAT THE USER EXPLICITLY PROMPTS YOU TO CHANGE.
# ==============================================================================

import os
import sys
import time
import traceback
import threading
import queue
import warnings 
import difflib 
import logging
import ctypes
import hashlib

# --- WINDOWS CPU/GPU PRIORITY ELEVATION (Fixes Alt-Tab Pause Bug) ---
try:
    ctypes.windll.kernel32.SetPriorityClass(ctypes.windll.kernel32.GetCurrentProcess(), 0x00000080)
    print("[System] Process priority elevated to HIGH to prevent game bottlenecking.")
except Exception as e:
    pass

# --- AGGRESSIVE WARNING & LOG SUPPRESSION (SPEED BOOST) ---
warnings.simplefilter("ignore")
os.environ["GRPC_VERBOSITY"] = "ERROR"
os.environ["GLOG_minloglevel"] = "2"
warnings.filterwarnings("ignore", category=FutureWarning)
warnings.filterwarnings("ignore", category=UserWarning)
warnings.filterwarnings("ignore", message=".*google.generativeai package has ended.*")
warnings.filterwarnings("ignore", message=".*torch.load.*")
warnings.filterwarnings("ignore", message=".*weight_norm.*")

logging.getLogger("httpx").setLevel(logging.WARNING)
logging.getLogger("httpcore").setLevel(logging.WARNING)
logging.getLogger("fairseq").setLevel(logging.WARNING)
logging.getLogger("rvc_python").setLevel(logging.WARNING)
logging.getLogger("urllib3").setLevel(logging.WARNING)

import glob
import asyncio 
from datetime import datetime, timezone
import json
import webbrowser 
import re 
import gc 
import numpy as np
import random
import requests
import base64

# --- Global Audio Imports (Moved out of try/except to ensure speaker menu works) ---
import sounddevice as sd

# --- Pre-compiled Regex ---
CLEAN_TEXT_REGEX = re.compile(r'[^\w\s.,?!:;\-\'\"\[\]]')
CLEAN_BRACKETS_REGEX = re.compile(r'[\(\*].*?[\)\*]')
MULTI_DOT_REGEX = re.compile(r'\.{2,}')
MULTI_SPACE_REGEX = re.compile(r'\s+')

# --- Standard Library Imports (NO GPU/CUDA LIBRARIES HERE TO SAVE VRAM) ---
try:
    import google.auth
    import google.auth.transport.requests
    from google_auth_oauthlib.flow import InstalledAppFlow
    from google.oauth2.credentials import Credentials
    from googleapiclient.discovery import build
    from googleapiclient.errors import HttpError
    from openai import OpenAI, APITimeoutError
    from dotenv import load_dotenv
    from google.cloud import texttospeech
    from pydub import AudioSegment
    import pyttsx3 
    import edge_tts 
    import speech_recognition as sr
    from scipy.io import wavfile
    import winsound 
except ImportError: pass

try: from groq import Groq, GroqError
except ImportError: Groq = None

try: import google.generativeai as genai
except ImportError: genai = None

try:
    import pygame
    pygame.mixer.init()
    HAS_PYGAME = True
except ImportError:
    HAS_PYGAME = False

# --- UNIVERSAL WINSOUND PLAYER (DJ Soundboard Setup - Instant & Non-Blocking) ---
def play_wav_winsound(wav_path, volume=1.0):
    try:
        from scipy.io import wavfile
        import numpy as np
        import pygame
        import time
        import os
        import random

        # 🛡️ FIX: Prevent WinError 2 if the cache file path doesn't exist yet
        if not wav_path or not os.path.exists(wav_path):
            return

        if not pygame.mixer.get_init():
            pygame.mixer.init()

        rate, data = wavfile.read(wav_path)
        if data.dtype == np.int16: data = data.astype(np.float32) / 32768.0

        data = np.clip(data * volume * 3.0, -1.0, 1.0)
        data_int16 = (data * 32767).astype(np.int16)

        temp_play = f"temp_winsound_play_{int(time.time()*1000)}_{random.randint(1000,9999)}.wav"
        wavfile.write(temp_play, rate, data_int16)

        sound = pygame.mixer.Sound(temp_play)
        sound.play()

        def _cleanup():
            time.sleep(sound.get_length() + 0.5)
            try: os.remove(temp_play)
            except: pass
        threading.Thread(target=_cleanup, daemon=True).start()

    except Exception as e:
        print(f"[Playback Error] {e}")

def play_windows_tts_audio(text, volume=1.0):
    try:
        import pyttsx3
        engine = pyttsx3.init()
        engine.setProperty('volume', volume)
        engine.say(text)
        engine.runAndWait()
    except: pass

# --- Shared TTS Cache Helper (Optimized for Hotkey Categories) ---
def _handle_tts_cache(engine_name, text, voice_name_or_pitch, volume, force_cache_file, category, game_name, channel_name, force_fresh=False):
    import re, hashlib, glob, random, os
    safe_channel = re.sub(r'[^a-zA-Z0-9_\- ]', '', str(channel_name)).strip().replace(" ", "_") or "DefaultChannel"
    safe_game = re.sub(r'[^a-zA-Z0-9_\- ]', '', str(game_name)).strip().replace(" ", "_") or "General"
    safe_category = re.sub(r'[^a-zA-Z0-9_\-]', '', str(category)).strip() or "generic"
    safe_voice = re.sub(r'[^a-zA-Z0-9_\-]', '', str(voice_name_or_pitch)).strip() or "default"
    
    cache_base_dir = f"{engine_name}_cache"
    # Structure: Engine / Channel / Game / Voice / HotkeyCategory
    cache_dir = os.path.join(cache_base_dir, safe_channel, safe_game, safe_voice, safe_category)
    if not os.path.exists(cache_dir): os.makedirs(cache_dir)
        
    if force_cache_file and isinstance(force_cache_file, str) and os.path.exists(force_cache_file):
        print(f"[{engine_name.capitalize()}] ⚡ CACHE PLAYBACK: Instantly playing cached audio! ({os.path.basename(force_cache_file)})")
        if volume > 0.0: play_wav_winsound(force_cache_file, volume)
        return True, None, None
        
    elif force_cache_file:
        print(f"[{engine_name.capitalize()}] ⚡ CACHE PLAYBACK: Randomizing and Instantly playing cached audio!")
        
        all_matching_wavs = glob.glob(os.path.join(cache_dir, "*.wav"))
        
        if not all_matching_wavs:
            # [FIX: Strict boundary to current game only. Recursively checks inside to find old cache structures.]
            game_dir = os.path.join(cache_base_dir, safe_channel, safe_game)
            all_matching_wavs.extend(glob.glob(os.path.join(game_dir, "**", safe_category, "*.wav"), recursive=True))
            
        if all_matching_wavs:
            random_wav = random.choice(all_matching_wavs)
            if volume > 0.0: play_wav_winsound(random_wav, volume)
            return True, None, None
        else:
            print(f"[{engine_name.capitalize()} Cache Warning] Double-tap requested, but zero files found for '{category}' using voice '{voice_name_or_pitch}'.")

    hash_str = hashlib.md5(f"{text}_{voice_name_or_pitch}".encode('utf-8')).hexdigest()
    cache_base = os.path.join(cache_dir, f"{safe_category}_{hash_str}")
    cache_file = cache_base + ".wav"
    cache_txt = cache_base + ".txt"

    if os.path.exists(cache_file) and not force_fresh:
        print(f"[{engine_name.capitalize()}] Playing from EXACT CACHE: {text[:30]}...")
        if volume > 0.0: play_wav_winsound(cache_file, volume)
        return True, cache_file, cache_txt

    print(f"[{engine_name.capitalize()}] Generating fresh audio (Caching to '{safe_channel}/{safe_game}/{safe_voice}/{safe_category}'): {text[:30]}...")
    return False, cache_file, cache_txt

# --- Constants ---
SCOPES = ['https://www.googleapis.com/auth/youtube.force-ssl', 'https://www.googleapis.com/auth/youtube.readonly', 'https://www.googleapis.com/auth/cloud-platform']
LM_STUDIO_BASE_URL = "http://localhost:1234/v1"
LM_STUDIO_MODEL_NAME = "dolphin-2.9-llama3-8b"
GROQ_DEFAULT_MODEL = "qwen/qwen3.6-27b" # 🛡️ FIX: Guaranteed working model
GEMINI_DEFAULT_MODEL = "gemini-3.6-flash" # 🛡️ FIX: Correct Gemini Model
AI_DEFAULT_GAME_NAME = "Just Chatting"
CHAT_POLLING_INTERVAL_SECONDS = 3 
REPLY_COOLDOWN_SECONDS = 0 
IDLE_CHAT_THRESHOLD_SECONDS = 180 
AUDIO_FILE_NAME = "bot_speech.mp3" 
CONFIG_FILE = "bot_config.json"
MAX_REPLIES_PER_POLL = 2 
STATS_POLL_SECONDS = 30      # videos.list (viewers/likes) - was hit every single loop
SUBS_POLL_SECONDS = 120      # subscriptions.list + channels.list - was hit every single loop
CHAT_SEND_MIN_GAP = 1.5      # seconds between outgoing chat messages (YouTube rate limits)

VIBES_LIST = [
    "Distracted by a difficult enemy/level.",
    "Hyper and screaming with excitement.",
    "Salty because you just missed a shot or died.",
    "Whispering because you're trying to focus intensely.",
    "Laughing uncontrollably at a previous joke.",
    "Arrogant and bragging about your skills (even if failing).",
    "Panic-asking viewers for help/backseating.",
    "Nonchalant and acting like the game is too easy.",
    "Hungry and thinking about food while playing.",
    "Sarcastic and roasting the game design.",
    "Confused and have no idea where to go.",
    "Feeling philosophical and deep about the game lore."
]

BARK_VOICES_LIST = [f"v2/en_speaker_{i}" for i in range(10)]
EDGE_VOICES_LIST = ["en-US-SteffanNeural", "en-US-GuyNeural", "en-US-ChristopherNeural", "en-US-AriaNeural", "en-US-JennyNeural", "en-US-MichelleNeural", "en-PH-JamesNeural", "en-PH-RosaNeural", "en-GB-RyanNeural", "en-GB-SoniaNeural", "en-US-AnaNeural", "tl-PH-AngeloNeural", "tl-PH-BlessicaNeural"]
GOOGLE_VOICES_LIST = ["en-US-Wavenet-A", "en-US-Wavenet-B", "en-US-Wavenet-C", "en-US-Wavenet-D", "en-US-Wavenet-E", "en-US-Wavenet-F", "en-US-Wavenet-I", "en-US-Wavenet-J", "fil-PH-Wavenet-A", "fil-PH-Wavenet-B", "fil-PH-Wavenet-C", "fil-PH-Wavenet-D"]
KOKORO_VOICES_LIST = {'am_adam': 'Gamer Guy (Adam)', 'am_michael': 'Pro Streamer (Michael)', 'am_onyx': 'Realistic Deep (Onyx)', 'am_puck': 'Energetic (Puck)', 'am_fenrir': 'Deep Voice (Fenrir)', 'af_bella': 'Streamer Girl (Bella)', 'af_sarah': 'Calm Girl (Sarah)', 'af_nicole': 'Standard (Nicole)', 'af_sky': 'Soft (Sky)', 'af_jessica': 'Realistic (Jessica)'}

def get_lm_studio_client(): return OpenAI(base_url=LM_STUDIO_BASE_URL, api_key="lm-studio", timeout=30.0)

def check_lm_studio_ready(client):
    try: client.chat.completions.create(model=LM_STUDIO_MODEL_NAME, messages=[{"role":"user","content":"hi"}], max_tokens=1); return True
    except: return False

def get_credential_sets(cred_dir, token_prefix):
    files = sorted(glob.glob(os.path.join(cred_dir, 'client_secret_*.json')))
    return [{'secret': f, 'token': os.path.join(cred_dir, f"{token_prefix}_token_{i+1}.json")} for i, f in enumerate(files)]

def get_authenticated_service(secret, token_path):
    creds = None
    if os.path.exists(token_path):
        try: creds = Credentials.from_authorized_user_file(token_path, SCOPES)
        except: pass
    if not creds or not creds.valid:
        if creds and creds.expired and creds.refresh_token:
            try: creds.refresh(google.auth.transport.requests.Request())
            except: creds = None
        if not creds:
            flow = InstalledAppFlow.from_client_secrets_file(secret, SCOPES)
            creds = flow.run_local_server(port=0)
        with open(token_path, 'w') as t: t.write(creds.to_json())
    return build('youtube', 'v3', credentials=creds), creds

def yt_error_info(e):
    """Returns (http_status, reason, message) for a googleapiclient HttpError (best-effort for anything else)."""
    status, reason, message = None, "", str(e)
    try:
        status = getattr(getattr(e, 'resp', None), 'status', None)
        content = getattr(e, 'content', b'')
        if isinstance(content, bytes): content = content.decode('utf-8', 'ignore')
        err = (json.loads(content) if content else {}).get('error', {})
        message = err.get('message', message)
        errs = err.get('errors') or []
        if errs: reason = errs[0].get('reason', '') or ''
    except Exception: pass
    return status, reason, message

def yt_is_daily_quota(e):
    """TRUE only when the DAILY quota is gone. (A plain 403 is NOT a quota error - the old code treated it as one.)"""
    status, reason, _ = yt_error_info(e)
    if reason in ('quotaExceeded', 'dailyLimitExceeded'): return True
    low = str(e).lower().replace(' ', '')
    return 'quotaexceeded' in low or 'dailylimitexceeded' in low

def yt_is_chat_gone(e):
    status, reason, _ = yt_error_info(e)
    return reason in ('liveChatEnded', 'liveChatNotFound', 'liveChatDisabled')

def get_live_stream_info(youtube, channel_id):
    try:
        b_resp = youtube.liveBroadcasts().list(part='id,snippet,status', mine=True).execute()
        for b in b_resp.get('items', []):
            if b['status']['lifeCycleStatus'] == 'live':
                vid = b['id']
                v_resp = youtube.videos().list(part='liveStreamingDetails', id=vid).execute()
                chat_id = v_resp['items'][0]['liveStreamingDetails'].get('activeLiveChatId')
                if chat_id: return chat_id, b['snippet']['title'], vid
    except Exception as e:
        if yt_is_daily_quota(e): raise e
        print(f"[YouTube] Live-broadcast lookup failed: {yt_error_info(e)[2]}")
    return None, None, None

def _optimize_torch_if_needed():
    try:
        import torch
        if torch.cuda.is_available() and not getattr(torch, '_system_optimized', False):
            torch.set_num_threads(4) 
            torch.set_grad_enabled(False) 
            torch.backends.cudnn.benchmark = True
            torch._system_optimized = True
            print(f"\n[System] Local AI Engine Triggered! CUDA Initialized on: {torch.cuda.get_device_name(0)}")
    except: pass


def play_fish_speech_tts_audio(text, audio_path, text_path, volume=1.0, force_cache_file=None, category="generic", game_name="General", channel_name="DefaultChannel", force_fresh=False):
    try:
        handled, cache_file, cache_txt = _handle_tts_cache("fish", text, os.path.basename(audio_path) if audio_path else "default", volume, force_cache_file, category, game_name, channel_name, force_fresh=force_fresh)
        if handled: return

        import shutil
        import requests
        import base64
        from gradio_client import Client, handle_file

        print(f"[Fish Speech Cloud] ☁️ Sending to Hugging Face GPU: '{text[:40]}...'")

        # --- ATTEMPT 1: THE FAST CLOUD GPU ---
        try:
            client = Client("damianpotts07/s1-mini")

            if audio_path and os.path.exists(audio_path) and text_path and os.path.exists(text_path):
                with open(text_path, "r", encoding="utf-8") as f:
                    ref_transcript = f.read().strip()
                
                result = client.predict(
                    text=text,
                    enable_reference_audio=True,
                    reference_audio=handle_file(audio_path),
                    reference_text=ref_transcript,
                    max_new_tokens=1024,
                    chunk_length=200,
                    top_p=0.7,
                    repetition_penalty=1.2,
                    temperature=0.7,
                    api_name="/infer"
                )
            else:
                result = client.predict(
                    text=text,
                    enable_reference_audio=False,
                    reference_audio=None,
                    reference_text="",
                    max_new_tokens=1024,
                    chunk_length=200,
                    top_p=0.7,
                    repetition_penalty=1.2,
                    temperature=0.7,
                    api_name="/infer"
                )

            source_wav = result[0] if isinstance(result, (tuple, list)) else result

            if source_wav and os.path.exists(source_wav):
                shutil.copy(source_wav, cache_file)
                with open(cache_txt, "w", encoding="utf-8") as f: f.write(text)
                print("[Fish Speech Cloud] ✅ Audio received from cloud!")
                if volume > 0.0: play_wav_winsound(cache_file, volume)
                return # Success! Exit the function.
            else:
                raise ValueError("Cloud returned empty audio.")

        # --- ATTEMPT 2: THE LOCAL FALLBACK (If Cloud Crashes) ---
        except Exception as cloud_err:
            print(f"[Fish Speech Cloud] ⚠️ Cloud failed or busy ({cloud_err}). Falling back to Local GPU...")
            
            url = "http://127.0.0.1:8080/v1/tts"
            payload = {"text": text, "format": "wav", "chunk_length": 200, "normalize": True}

            if audio_path and os.path.exists(audio_path):
                with open(audio_path, "rb") as f:
                    encoded_audio = base64.b64encode(f.read()).decode('utf-8')
                ref_transcript = "Sample text."
                if text_path and os.path.exists(text_path):
                    with open(text_path, "r", encoding="utf-8") as f:
                        ref_transcript = f.read().strip()
                payload["references"] = [{"audio": encoded_audio, "text": ref_transcript}]

            headers = {"Content-Type": "application/json"}
            resp = requests.post(url, json=payload, headers=headers, timeout=500)
            
            if resp.status_code == 200:
                with open(cache_file, "wb") as f: f.write(resp.content)
                with open(cache_txt, "w", encoding="utf-8") as f: f.write(text)
                print("[Fish Speech Local] ✅ Local fallback successful!")
                if volume > 0.0: play_wav_winsound(cache_file, volume)
            else:
                print(f"[Fish Speech Local Error] HTTP {resp.status_code}: {resp.text}")

    except ImportError:
        print("\n[Fish Speech Error] ❌ Missing Library! Open CMD and run: pip install gradio_client\n")
    except Exception as e:
        print(f"[Fish Speech Error] {e}")


def play_tiktok_tts_audio(text, voice, device_id, volume=1.0, force_cache_file=None, category="generic", game_name="General", channel_name="DefaultChannel", force_fresh=False):
    try:
        handled, cache_file, cache_txt = _handle_tts_cache("tiktok", text, voice, volume, force_cache_file, category, game_name, channel_name, force_fresh=force_fresh)
        if handled: return

        import requests
        import base64
        
        text = text[:300]
        
        session_id = os.environ.get("TIKTOK_SESSION_ID", "")
        if not session_id:
            print("[TikTok TTS Error] Missing TIKTOK_SESSION_ID in environment or config! Add 'tiktok_session_id' to bot_config.json.")
            return

        url = "https://api16-normal-v6.tiktokv.com/media/api/text/speech/invoke/"
        params = {
            "text_speaker": voice,
            "req_text": text,
            "speaker_map_type": 0,
            "aid": 1233
        }
        headers = {
            "User-Agent": "com.zhiliaoapp.musically/2022600030 (Linux; U; Android 7.1.2; en_US; SM-G988N; Build/NRD90M;tt-ok/3.12.13.1)",
            "Cookie": f"sessionid={session_id}"
        }
        
        response = requests.post(url, params=params, headers=headers)
        if response.status_code == 200:
            data = response.json()
            if data.get("message") == "success":
                v_str = data.get("data", {}).get("v_str")
                if v_str:
                    audio_data = base64.b64decode(v_str)
                    
                    # 🛡️ FIX: Ensure cache directory exists before writing
                    if cache_file:
                        os.makedirs(os.path.dirname(cache_file), exist_ok=True)
                        with open(cache_file, "wb") as f:
                            f.write(audio_data)
                    if cache_txt:
                        os.makedirs(os.path.dirname(cache_txt), exist_ok=True)
                        with open(cache_txt, "w", encoding="utf-8") as f: 
                            f.write(text)
                    
                    if volume > 0.0 and cache_file and os.path.exists(cache_file): 
                        play_wav_winsound(cache_file, volume)
                    return
            print(f"[TikTok TTS Error] API returned: {data}")
        else:
            print(f"[TikTok TTS Error] HTTP {response.status_code}: {response.text}")

    except Exception as e:
        print(f"[TikTok Engine Error] {e}")


def play_rvc_tts_audio(text, device_id, model_path, index_path, pitch, volume=1.0, force_cache_file=None, category="generic", game_name="General", channel_name="DefaultChannel", force_fresh=False):
    try:
        model_basename = os.path.basename(model_path) if model_path else "default"
        handled, cache_file, cache_txt = _handle_tts_cache("rvc", text, f"{model_basename}_{pitch}", volume, force_cache_file, category, game_name, channel_name, force_fresh=force_fresh)
        if handled: return

        if 'global_rvc_client' in globals():
            global_rvc_client.infer(text, cache_file, model_path, index_path, pitch)
        
        if os.path.exists(cache_file):
            with open(cache_txt, "w", encoding="utf-8") as f: f.write(text)
            if volume > 0.0: play_wav_winsound(cache_file, volume)

    except Exception as e: print(f"[RVC Engine Error] {e}")


def play_kokoro_tts_audio(text, device_id, pipeline, voice_name, volume=1.0, force_cache_file=None, category="generic", game_name="General", channel_name="DefaultChannel", force_fresh=False):
    _optimize_torch_if_needed()
    try:
        import torch
        import numpy as np
        
        handled, cache_file, cache_txt = _handle_tts_cache("kokoro", text, voice_name, volume, force_cache_file, category, game_name, channel_name, force_fresh=force_fresh)
        if handled: return

        if not pipeline: return
        if len(text) > 400: text = text[:400]
        if torch.cuda.is_available(): pipeline.model.to('cuda')
            
        full_audio_list = []
        with torch.inference_mode():
            try: generator = pipeline(text, voice=voice_name, speed=1, split_pattern=r'\n+')
            except Exception: generator = pipeline(text, voice='am_adam', speed=1, split_pattern=r'\n+')
            for i, (gs, ps, audio) in enumerate(generator):
                if audio is not None and len(audio) > 0:
                    full_audio_list.append(audio)
                    
        if full_audio_list:
            final_audio = np.concatenate(full_audio_list)
            from scipy.io import wavfile
            wavfile.write(cache_file, 24000, final_audio)
            with open(cache_txt, "w", encoding="utf-8") as f: f.write(text)
            
            if volume > 0.0: play_wav_winsound(cache_file, volume)

    except Exception as e: print(f"[Kokoro Error] {e}")
    finally:
        try:
            import torch
            if torch.cuda.is_available() and pipeline:
                pipeline.model.to('cpu')
                torch.cuda.empty_cache()
        except: pass


# --- FREE BARK CLOUD GPU ENDPOINT ---
BARK_COLAB_URL = "https://dca412b1b4e6645c66.gradio.live/"

def play_bark_cloud_tts_audio(text, device_id, force_cpu=False, voice_name="v2/en_speaker_6", volume=1.0, force_cache_file=None, category="generic", game_name="General", channel_name="DefaultChannel", use_small_models=False, force_fresh=False):
    try:
        handled, cache_file, cache_txt = _handle_tts_cache("bark", text, voice_name, volume, force_cache_file, category, game_name, channel_name, force_fresh=force_fresh)
        if handled: return

        print(f"[Bark Colab] ☁️ Sending to Free Cloud GPU: '{text[:40]}...'")
        
        def _fetch_and_play():
            try:
                from gradio_client import Client
                import shutil
                
                # Connect to your Colab tab
                client = Client(BARK_COLAB_URL)
                
                # Ping the temporary API
                result_wav = client.predict(text, api_name="/predict")
                
                # Save it to your bot's standard cache system
                shutil.copy(result_wav, cache_file)
                with open(cache_txt, "w", encoding="utf-8") as f: f.write(text)
                
                print("[Bark Colab] ✅ Audio received instantly!")
                if volume > 0.0: play_wav_winsound(cache_file, volume)
                
            except ImportError:
                print("[Bark Colab Error] You need to run: pip install gradio_client")
            except Exception as e:
                print(f"[Bark Colab Error] {e} (Did your Colab link expire?)")

        import threading
        threading.Thread(target=_fetch_and_play, daemon=True).start()

    except Exception as e:
        print(f"[Bark Wrapper Error] {e}")


def play_voicebox_tts_audio(text, url, voice="default", volume=1.0, force_cache_file=None, category="generic", game_name="General", channel_name="DefaultChannel", force_fresh=False):
    try:
        import requests
        handled, cache_file, cache_txt = _handle_tts_cache("voicebox", text, voice, volume, force_cache_file, category, game_name, channel_name, force_fresh=force_fresh)
        if handled: return
            
        base_url = url.rstrip('/')
        actual_profile_id = voice
        try:
            profiles_req = requests.get(f"{base_url}/profiles", timeout=5)
            if profiles_req.status_code == 200:
                p_data = profiles_req.json()
                if isinstance(p_data, list):
                    for p in p_data:
                        if str(p.get("name", "")).lower() == str(voice).lower():
                            actual_profile_id = p.get("id", voice)
                            break
                elif isinstance(p_data, dict) and "items" in p_data:
                    for p in p_data["items"]:
                        if str(p.get("name", "")).lower() == str(voice).lower():
                            actual_profile_id = p.get("id", voice)
                            break
        except Exception: pass

        endpoint = f"{base_url}/generate"
        payload = {"text": text, "profile_id": actual_profile_id}
        
        print(f"[Voicebox] Generating fresh audio using Profile ID '{actual_profile_id}' (This may take a while on CPU)...")
        response = requests.post(endpoint, json=payload, timeout=300)
        
        if response.status_code == 200:
            if 'application/json' in response.headers.get('Content-Type', ''):
                data = response.json()
                gen_id = data.get("id") or data.get("generation_id")
                if gen_id:
                    audio_res = requests.get(f"{base_url}/audio/{gen_id}", timeout=120)
                    if audio_res.status_code == 200:
                        with open(cache_file, "wb") as f: f.write(audio_res.content)
                        with open(cache_txt, "w", encoding="utf-8") as f: f.write(text)
                        if volume > 0.0: play_wav_winsound(cache_file, volume)
                        return
                    else: print(f"[Voicebox Audio Error] Failed to fetch audio. Code: {audio_res.status_code}")
                else: print(f"[Voicebox API Error] Generation successful, but no ID returned: {data}")
            
            elif response.headers.get('Content-Type', '').startswith('audio/'):
                with open(cache_file, "wb") as f: f.write(response.content)
                with open(cache_txt, "w", encoding="utf-8") as f: f.write(text)
                if volume > 0.0: play_wav_winsound(cache_file, volume)
        else: print(f"[Voicebox API Error] Status {response.status_code}: {response.text}")
            
    except Exception as e: print(f"[Voicebox Connection Error] Ensure Pinokio is running and URL is correct: {e}")

class EventEmitter:
    def __init__(self): self._listeners = {}
    def on(self, event, func):
        if event not in self._listeners: self._listeners[event] = []
        self._listeners[event].append(func)
    def emit(self, event, *args, **kwargs):
        for func in list(self._listeners.get(event, [])):
            try: func(*args, **kwargs)
            except Exception as e:
                print(f"[EventEmitter] Listener for '{event}' crashed (ignored): {e}")

class StreamerBotModel(EventEmitter):
    def __init__(self):
        super().__init__()
        try: load_dotenv()
        except: pass

        self.bot_running = False
        self.stop_event = threading.Event()
        self.current_persona = "English"
        self.review_mode = False
        self.idle_chat_enabled = True
        self.automated_replies_enabled = True 
        self.hotkeys_only_mode = False 
        self.force_cache_only = False
        self.chat_replies_text_only = False
        self.greet_new_viewers = True 
        self.voice_enabled = True
        self.tts_mode = "edge"
        self.stt_mode = "google" 
        self.bark_loaded = False
        self.bark_use_small_models = False 
        
        self.is_speaking = False 
        self.current_spoken_text = ""
        self.speak_start_time = 0
        
        self.soprano_loaded = False
        self.soprano_model = None
        self.soprano_model_path = ""
        
        self.rvc_model_path = ""
        self.rvc_index_path = ""
        self.rvc_pitch = 0
        self.rvc_presets = {} 
        
        self.fish_audio_path = ""
        self.fish_text_path = ""
        self.fish_presets = {}

        self.current_bot_mode = 3 
        self.volume_level = 1.0 
        
        self.bark_voice = "v2/en_speaker_6"
        self.edge_voice = "en-US-SteffanNeural"
        self.google_voice = "en-US-Wavenet-I"
        self.kokoro_voice = "am_adam" 
        self.soprano_voice = "Default (Female)"
        self.tiktok_voice = "en_us_002"
        self.voicebox_voice = "default"
        self.voicebox_url = "http://127.0.0.1:17493"
        self.bark_cache_only = False
        
        self.mic_device_index = None
        self.speaker_device_index = None
        self.recognizer = sr.Recognizer()

        self.ai_mode = "local" 
        self.groq_api_keys = []
        self.groq_key_index = 0
        self.groq_model = GROQ_DEFAULT_MODEL
        
        self.gemini_api_keys = []
        self.gemini_key_index = 0
        self.gemini_model = GEMINI_DEFAULT_MODEL
        
        # --- NEW OPENROUTER BACKEND (INDEPENDENT) ---
        self.openrouter_api_keys = []
        self.openrouter_key_index = 0
        self.openrouter_model = "meta-llama/llama-3.3-70b-instruct:free" 
        
        # --- SAMBANOVA BACKEND ---
        self.sambanova_api_keys = []
        self.sambanova_key_index = 0
        self.sambanova_model = "Meta-Llama-3.3-70B-Instruct" 
        # --- AI failover (ai_failover.py): keeps the stream talking when a provider is rate-limited/down/retired ---
        self.ai_failover_enabled = True
        self._ai_failover = None

        self.kokoro_pipeline = None
        self.whisper_model = None

        self.manual_reply_queue = queue.Queue()
        self.review_decision_queue = queue.Queue()
        self._review_gate = threading.Lock()   # only ONE review window / decision in flight at a time
        self.chat_send_queue = queue.Queue()
        self._chat_sender_lock = threading.Lock()
        self._chat_sender_started = False
        self.history_req_queue = queue.Queue()
        self.history_resp_queue = queue.Queue()
        self.target_reply_queue = queue.Queue()
        self.tts_queue = queue.Queue() 
        threading.Thread(target=self._tts_worker, daemon=True).start()
        
        self.music_playlist = []
        self.music_index = 0
        self.music_paused = True 
        self.music_vol = 0.5
        self.music_repeat_mode = "none" 
        self.music_folder_cache = "" 
        threading.Thread(target=self._music_worker_loop, daemon=True).start()

        self.last_chat_time = time.time()
        self.last_reply_time = 0
        self.last_msg_timestamp = None
        self.last_like_count = -1 
        self.stats_poll_counter = 0 
        self.last_sub_time = None 
        self.last_sub_count = -1 
        self.first_sub_run = True 
        self.last_viewer_count = -1
        self.current_viewer_count = 0
        self.current_like_count = 0
        self.chat_memory = []
        self.last_incoming_message = None
        self.channel_id = ""
        self.channel_name = ""
        self.token_prefix = ""
        self.game_name = AI_DEFAULT_GAME_NAME
        self.active_guide_context = ""
        self.strict_repetition_mode = False
        self.chat_list_voice_only = False 

        self.recent_cache_plays = []
        self.consecutive_errors = 0 
        
        self.os_is_typing = False
        threading.Thread(target=self._os_typing_monitor_loop, daemon=True).start()

    def _os_typing_monitor_loop(self):
        import ctypes
        import time
        from ctypes import wintypes
        user32 = ctypes.windll.user32
        
        class GUITHREADINFO(ctypes.Structure):
            _fields_ = [
                ("cbSize", wintypes.DWORD),
                ("flags", wintypes.DWORD),
                ("hwndActive", wintypes.HWND),
                ("hwndFocus", wintypes.HWND),
                ("hwndCapture", wintypes.HWND),
                ("hwndMenuOwner", wintypes.HWND),
                ("hwndMoveSize", wintypes.HWND),
                ("hwndCaret", wintypes.HWND),
                ("rcCaret", wintypes.RECT)
            ]

        while True:
            try:
                gui_info = GUITHREADINFO()
                gui_info.cbSize = ctypes.sizeof(GUITHREADINFO)
                hwnd_active = user32.GetForegroundWindow()
                thread_id = user32.GetWindowThreadProcessId(hwnd_active, None)
                
                is_typing = False
                if user32.GetGUIThreadInfo(thread_id, ctypes.byref(gui_info)):
                    # If a text caret is actively blinking on screen, user is typing
                    if gui_info.hwndCaret:
                        is_typing = True
                        
                if not is_typing and hwnd_active:
                    length = user32.GetWindowTextLengthW(hwnd_active)
                    buf = ctypes.create_unicode_buffer(length + 1)
                    user32.GetWindowTextW(hwnd_active, buf, length + 1)
                    title = buf.value.lower()
                    
                    # Fallback for apps that hide carets (Chrome, Discord, File Dialogs)
                    if any(x in title for x in ["save as", "open", "discord", "google chrome", "mozilla firefox", "notepad"]):
                        is_typing = True

                self.os_is_typing = is_typing
            except: pass
            time.sleep(0.05)

    def get_random_vibe(self):
        import random
        vibe = random.choice(VIBES_LIST)
        print(f"[Vibe Check] Active: {vibe}")
        return vibe

    # --- Cache Randomization Helper (Optimized bypass) ---
    def find_random_cached_audio(self, category):
        import re, glob, random, os
        safe_channel = re.sub(r'[^a-zA-Z0-9_\- ]', '', str(self.channel_name)).strip().replace(" ", "_") or "DefaultChannel"
        safe_game = re.sub(r'[^a-zA-Z0-9_\- ]', '', str(self.game_name)).strip().replace(" ", "_") or "General"
        safe_cat = re.sub(r'[^a-zA-Z0-9_\-]', '', str(category)).strip() or "generic"
        
        tts_engine = self.tts_mode.replace("_cpu", "")
        if tts_engine not in ["voicebox", "kokoro", "rvc", "bark", "fish", "soprano", "edge", "google"]: tts_engine = "bark"
        cache_base_dir = f"{tts_engine}_cache"
        
        if tts_engine == "voicebox": current_voice = getattr(self, 'voicebox_voice', 'default')
        elif tts_engine == "kokoro": current_voice = getattr(self, 'kokoro_voice', 'am_adam')
        elif tts_engine == "rvc": current_voice = f"{os.path.basename(self.rvc_model_path) if getattr(self, 'rvc_model_path', '') else 'default'}_{getattr(self, 'rvc_pitch', 0)}"
        elif tts_engine == "fish": current_voice = os.path.basename(self.fish_audio_path) if getattr(self, 'fish_audio_path', '') else "default"
        elif tts_engine == "soprano": current_voice = getattr(self, 'soprano_voice', 'Default (Female)')
        elif tts_engine == "edge": current_voice = getattr(self, 'edge_voice', 'en-US-SteffanNeural')
        elif tts_engine == "google": current_voice = getattr(self, 'google_voice', 'en-US-Wavenet-I')
        else: current_voice = getattr(self, 'bark_voice', 'v2/en_speaker_6')
        
        safe_voice = re.sub(r'[^a-zA-Z0-9_\-]', '', str(current_voice)).strip() or "default"
        
        # Priority 1: Exact Game + Voice + Category Subfolder
        target_dir = os.path.join(cache_base_dir, safe_channel, safe_game, safe_voice, safe_cat)
        wav_files = glob.glob(os.path.join(target_dir, "*.wav"))
        
        if not wav_files:
            # [FIX: Strict boundary limits search to the CURRENT GAME ONLY. Falls back to recursive to catch old cache formats]
            game_dir = os.path.join(cache_base_dir, safe_channel, safe_game)
            wav_files = glob.glob(os.path.join(game_dir, "**", safe_cat, "*.wav"), recursive=True)
            
        if wav_files:
            avail = [w for w in wav_files if w not in self.recent_cache_plays]
            if not avail:
                self.recent_cache_plays.clear()
                avail = wav_files
            chosen_wav = random.choice(avail)
            self.recent_cache_plays.append(chosen_wav)
            if len(self.recent_cache_plays) > max(1, len(wav_files) // 2): self.recent_cache_plays.pop(0)
            return chosen_wav
            
        print(f"\n[System Log] 🔇 Silent Drop: No '{safe_cat}' cached audio available for identity '{safe_channel}' & game '{safe_game}'.")
        return None

    def set_rvc_preset(self, model_path, index_path, pitch=0):
        self.rvc_model_path = model_path
        self.rvc_index_path = index_path
        self.rvc_pitch = int(pitch)
        print(f"[RVC] Preset configured -> Model: {os.path.basename(model_path) if model_path else 'None'} | Pitch: {pitch}")

    def offload_bark_to_cpu(self):
        try:
            from bark.generation import models
            import torch
            if isinstance(models, dict):
                for key, val in models.items():
                    if val is None: continue
                    if hasattr(val, 'to') and callable(getattr(val, 'to', None)):
                        try: val.to("cpu")
                        except: pass
                    elif isinstance(val, dict):
                        for k, v in val.items():
                            if hasattr(v, 'to') and callable(getattr(v, 'to', None)):
                                try: v.to("cpu")
                                except: pass
            if torch.cuda.is_available(): torch.cuda.empty_cache()
        except: pass

    def play_elevenlabs_tts_audio(self, text, preset_name, volume=1.0, force_cache_file=None, category="generic", game_name="General", channel_name="DefaultChannel", force_fresh=False):
        try:
            handled, cache_file, cache_txt = _handle_tts_cache("elevenlabs", text, preset_name, volume, force_cache_file, category, game_name, channel_name, force_fresh=force_fresh)
            if handled: return

            import requests
            
            presets = getattr(self, 'elevenlabs_presets', {})
            if not presets:
                print("[ElevenLabs Error] No Presets found! Please save a Voice ID and API Key in Settings.")
                return

            preset_keys = list(presets.keys())
            
            # Start at the currently selected preset, but default to the first one if not found
            current_preset_name = getattr(self, 'elevenlabs_voice', '')
            start_index = 0
            if current_preset_name in preset_keys:
                start_index = preset_keys.index(current_preset_name)

            payload = {
                "text": text,
                "model_id": "eleven_multilingual_v2",
                "voice_settings": {"stability": 0.5, "similarity_boost": 0.75}
            }

            success = False

            # --- THE NEW PRESET ROTATION LOOP ---
            for attempt in range(len(preset_keys)):
                current_idx = (start_index + attempt) % len(preset_keys)
                active_preset_name = preset_keys[current_idx]
                preset_data = presets[active_preset_name]
                
                # Extract the data
                if isinstance(preset_data, str):
                    active_voice_id = preset_data
                    current_key = getattr(self, 'elevenlabs_api_keys', [""])[0] if getattr(self, 'elevenlabs_api_keys', []) else ""
                else:
                    active_voice_id = preset_data.get("voice_id", "")
                    current_key = preset_data.get("api_key", "")
                
                if not current_key or not active_voice_id:
                    print(f"[ElevenLabs] ⚠️ Preset '{active_preset_name}' is missing a Key or Voice ID. Skipping...")
                    continue

                url = f"https://api.elevenlabs.io/v1/text-to-speech/{active_voice_id}?output_format=mp3_44100_128"
                print(f"[ElevenLabs] ☁️ Generating audio (Preset: {active_preset_name}, Voice: {active_voice_id})...")
                
                headers = {"Accept": "audio/mpeg", "Content-Type": "application/json", "xi-api-key": current_key}
                
                try:
                    response = requests.post(url, json=payload, headers=headers, timeout=15)
                except Exception as e:
                    print(f"[ElevenLabs Warning] ⚠️ Network error on '{active_preset_name}': {e}. Trying next preset...")
                    continue 
                
                if response.status_code == 200:
                    with open(cache_file, 'wb') as f: f.write(response.content)
                    with open(cache_txt, "w", encoding="utf-8") as f: f.write(text)
                    
                    if volume > 0.0: play_wav_winsound(cache_file, volume)
                    
                    self.elevenlabs_voice = active_preset_name # Save where we stopped so we start here next time!
                    success = True
                    break 
                    
                elif response.status_code in [401, 403, 404, 429]: 
                    print(f"[ElevenLabs] ⚠️ Preset '{active_preset_name}' rejected! EXACT ERROR: {response.text}")
                    print(f" -> Rotating to the next available preset...")
                    continue 
                else:
                    print(f"[ElevenLabs API Error] {response.status_code}: {response.text}")
                    print(f" -> Unhandled error, but rotating anyway to be safe...")
                    continue

            if not success:
                print("[ElevenLabs Error] All Presets/API keys exhausted or invalid.")

        except Exception as e:
            print(f"[ElevenLabs Execution Error] {e}")

    def _tts_worker(self):
        while True:
            try:
                task = self.tts_queue.get()
                if task is None: break 
                
                if len(task) == 10: 
                    text, mode, voice, vol, device_index, force_cache_file, category, game_name, channel_name, force_fresh = task
                elif len(task) == 9: 
                    text, mode, voice, vol, device_index, force_cache_file, category, game_name, channel_name = task
                    force_fresh = False
                elif len(task) == 8: 
                    text, mode, voice, vol, device_index, force_cache_file, category, game_name = task
                    channel_name = self.channel_name or "DefaultChannel"
                    force_fresh = False
                else: 
                    text, mode, voice, vol, device_index, force_cache_file, category = (*task[:7], "generic") if len(task) >= 7 else (*task, None, "generic")
                    game_name = self.game_name or "Just Chatting"
                    channel_name = self.channel_name or "DefaultChannel"
                    force_fresh = False

                # --- THE ROLEPLAY SCRUBBER ---
                # Deletes anything inside asterisks, brackets, or parentheses
                text = re.sub(r'\[.*?\]|\*.*?\*|\(.*?\)', '', text)
                # Cleans up any weird double spaces left behind
                text = " ".join(text.split()).strip()
                
                if not text: 
                    self.tts_queue.task_done()
                    continue
                # -----------------------------

                # === FORCE CACHE ONLY SILENT FALLBACK OVERRIDE ===
                is_force_cache_only = getattr(self, 'force_cache_only', False)
                if is_force_cache_only and not force_cache_file and not force_fresh:
                    fallback_cache = self.find_random_cached_audio(category)
                    if fallback_cache:
                        force_cache_file = fallback_cache
                    else:
                        print(f"\n[System Log] 🔇 FORCE CACHE ONLY is enabled, but no cache saved for '{category}'. Auto-reply made silent.\n")
                        self.is_speaking = False
                        self.tts_queue.task_done()
                        continue

                self.is_speaking = True 
                self.current_spoken_text = text
                self.speak_start_time = time.time()
                
                try:
                    if mode == "elevenlabs":
                        self.play_elevenlabs_tts_audio(text, voice, vol, force_cache_file=force_cache_file, category=category, game_name=game_name, channel_name=channel_name, force_fresh=force_fresh)
                    elif mode == "edge": 
                        loop = asyncio.new_event_loop()
                        asyncio.set_event_loop(loop)
                        loop.run_until_complete(play_edge_tts_audio_gui(text, voice, device_index, vol, force_fresh=force_fresh))
                        loop.close()
                    elif mode == "fish":
                        play_fish_speech_tts_audio(text, getattr(self, 'fish_audio_path', ''), getattr(self, 'fish_text_path', ''), vol, force_cache_file=force_cache_file, category=category, game_name=game_name, channel_name=channel_name, force_fresh=force_fresh)
                    elif mode == "rvc":
                        play_rvc_tts_audio(text, device_index, self.rvc_model_path, self.rvc_index_path, self.rvc_pitch, vol, force_cache_file=force_cache_file, category=category, game_name=game_name, channel_name=channel_name, force_fresh=force_fresh)
                    elif mode == "tiktok":
                        play_tiktok_tts_audio(text, voice, device_index, vol, force_cache_file=force_cache_file, category=category, game_name=game_name, channel_name=channel_name, force_fresh=force_fresh)
                    elif mode == "typecast":
                        print(f"[{mode.upper()}] Routing to Typecast Ghost Engine...")
                        try:
                            # Use a separate thread so it doesn't permanently block the TTS queue
                            import threading
                            def _run_tc():
                                from typecast_bridge import TypecastGhostEngine
                                tc_engine = TypecastGhostEngine()
                                tc_engine.play_ghost_audio(text)
                            threading.Thread(target=_run_tc, daemon=True).start()
                        except Exception as e:
                            print(f"[Typecast Integration Error] {e}")
                    elif mode == "elevenlabs_ghost":
                        print(f"[{mode.upper()}] Routing to ElevenLabs Stealth Engine...")
                        try:
                            import threading
                            def _run_el():
                                from elevenlabs_ghost_bridge import ElevenLabsGhostEngine
                                el_engine = ElevenLabsGhostEngine()
                                el_engine.play_ghost_audio(text)
                            threading.Thread(target=_run_el, daemon=True).start()
                        except Exception as e:
                            print(f"[ElevenLabs Ghost Integration Error] {e}")
                    elif mode == "kokoro": play_kokoro_tts_audio(text, device_index, self.kokoro_pipeline, voice, vol, force_cache_file=force_cache_file, category=category, game_name=game_name, channel_name=channel_name, force_fresh=force_fresh)
                    elif mode == "soprano":
                        if self.soprano_loaded and self.soprano_model: play_soprano_tts_audio(self.soprano_model, text, device_index, voice, vol)
                        else: play_windows_tts_audio(text, vol)
                    elif mode == "voicebox":
                        play_voicebox_tts_audio(text, getattr(self, 'voicebox_url', 'http://127.0.0.1:17493'), voice, vol, force_cache_file=force_cache_file, category=category, game_name=game_name, channel_name=channel_name, force_fresh=force_fresh)
                    elif mode == "bark_cloud":
                        play_bark_cloud_tts_audio(text, device_index, False, voice, vol, force_cache_file=force_cache_file, category=category, game_name=game_name, channel_name=channel_name, use_small_models=self.bark_use_small_models, force_fresh=force_fresh)
                    elif mode.startswith("bark"):
                        cache_only = getattr(self, 'bark_cache_only', False)
                        if self.bark_loaded or cache_only or force_cache_file or True: # Force true to allow lazy generation
                            self.play_bark_tts_audio(text, device_index, mode == "bark_cpu", voice, vol, force_cache_file=force_cache_file, category=category, game_name=game_name, channel_name=channel_name, use_small_models=self.bark_use_small_models, force_fresh=force_fresh)
                        else:
                            play_windows_tts_audio(text, vol)
                    elif mode == "google":
                        if getattr(self, 'google_tts_client', None):
                            play_google_tts_audio_gui(self.google_tts_client, text, voice, device_index, vol, force_fresh=force_fresh)
                        else:
                            print("[Google TTS Error] No active Google TTS client available. Falling back to Windows TTS.")
                            play_windows_tts_audio(text, vol)
                    else: play_windows_tts_audio(text, vol)
                except Exception as e: 
                    print(f"[TTS Error] System caught an audio error: {e}")
                finally: 
                    self.is_speaking = False 
                    self.tts_queue.task_done()
                    
                    try:
                        if 'torch' in sys.modules:
                            import torch
                            if torch.cuda.is_available(): torch.cuda.empty_cache()
                    except: pass
                    gc.collect()
            except Exception as outer_e:
                print(f"[TTS Worker Fatal Error] {outer_e}")
                self.is_speaking = False

    def _music_worker_loop(self):
        while True:
            try:
                if HAS_PYGAME and pygame.mixer.music.get_busy() == False and self.music_playlist and not self.music_paused:
                    if self.music_repeat_mode == "one": self.music_play(self.music_index)
                    else:
                         if self.music_index >= len(self.music_playlist) - 1:
                             if self.music_repeat_mode == "all": self.music_play(0)
                         else: self.music_next()
            except: pass
            time.sleep(1)
            
    def music_load_folder(self, d):
        if not HAS_PYGAME or not d: return
        self.music_folder_cache = d
        self.music_playlist = glob.glob(os.path.join(d, "*.mp3")) + glob.glob(os.path.join(d, "*.wav"))
        self.music_index = 0
        if self.music_playlist:
            print(f"[Music] Loaded {len(self.music_playlist)} songs from {d}")
            self.music_play(0)

    def music_play(self, index, start_pos=0.0):
        if not self.music_playlist: return
        if index >= len(self.music_playlist): index = 0
        if index < 0: index = len(self.music_playlist) - 1
        self.music_index = index
        self.music_current_pos = float(start_pos)
        try:
            pygame.mixer.music.load(self.music_playlist[index])
            pygame.mixer.music.play(start=self.music_current_pos)
            pygame.mixer.music.set_volume(self.music_vol)
            self.music_paused = False
            self.emit('music_song_changed', os.path.basename(self.music_playlist[index]))
        except: pass

    def get_music_time(self):
        if not HAS_PYGAME or not pygame.mixer.get_init() or not pygame.mixer.music.get_busy(): return getattr(self, 'music_current_pos', 0.0)
        t = pygame.mixer.music.get_pos() / 1000.0
        return getattr(self, 'music_current_pos', 0.0) + (t if t > 0 else 0.0)

    def music_toggle_pause(self):
        if not self.music_playlist: return
        if self.music_paused:
            pygame.mixer.music.unpause()
            self.music_paused = False
            if not pygame.mixer.music.get_busy(): self.music_play(self.music_index)
        else:
            pygame.mixer.music.pause()
            self.music_paused = True

    def music_next(self): self.music_play(self.music_index + 1)
    def music_prev(self): self.music_play(self.music_index - 1)
    
    def music_forward(self, seconds=10):
        if not self.music_playlist: return
        try:
            if HAS_PYGAME and pygame.mixer.get_init():
                current_time = pygame.mixer.music.get_pos() / 1000.0
                if current_time >= 0:
                    base = getattr(self, 'music_current_pos', 0.0)
                    new_pos = base + current_time + seconds
                    self.music_current_pos = new_pos
                    pygame.mixer.music.play(start=new_pos)
        except: pass

    def music_rewind(self, seconds=10):
        if not self.music_playlist: return
        try:
            if HAS_PYGAME and pygame.mixer.get_init():
                current_time = pygame.mixer.music.get_pos() / 1000.0
                if current_time >= 0:
                    base = getattr(self, 'music_current_pos', 0.0)
                    new_pos = max(0.0, base + current_time - seconds)
                    self.music_current_pos = new_pos
                    pygame.mixer.music.play(start=new_pos)
        except: pass
        
    def music_set_vol(self, val):
        self.music_vol = float(val)
        if HAS_PYGAME: pygame.mixer.music.set_volume(self.music_vol)

    def music_toggle_repeat(self):
        if self.music_repeat_mode == "none": self.music_repeat_mode = "one"
        elif self.music_repeat_mode == "one": self.music_repeat_mode = "all"
        else: self.music_repeat_mode = "none"
        print(f"[Music] Repeat mode: {self.music_repeat_mode}")
        return self.music_repeat_mode

    def get_failover(self):
        """Lazy-create the multi-provider failover engine (see ai_failover.py)."""
        if self._ai_failover is None:
            try:
                import ai_failover
                self._ai_failover = ai_failover.Failover(
                    self, base_dir=os.path.dirname(os.path.abspath(__file__)),
                    on_heal=lambda p, m: print(f"[AI Failover] {p} model auto-updated to '{m}' (saved on next config save)"))
                self._ai_failover.start_background()
            except Exception as e:
                print(f"[AI Failover] unavailable: {e}")
                self._ai_failover = False
        return self._ai_failover or None

    def _failover_try(self, messages, max_tokens, temperature):
        """Try every configured provider in turn. Returns text, or None if the failover has nothing to offer."""
        if not getattr(self, 'ai_failover_enabled', True): return None
        fo = self.get_failover()
        if not fo or not fo.enabled(): return None
        try:
            return fo.complete(messages, max_tokens=max_tokens, temperature=temperature, primary=self.ai_mode)
        except Exception as e:
            print(f"[AI Failover] error: {e}")
            return None

    def generate_ai_completion(self, messages, max_tokens=100, temperature=1.1):
        import re
        
        # 🛡️ THE ULTIMATE ANTI-ECHO GUARD: Injects the rule at the very end so the AI reads it last!
        if messages and len(messages) > 0 and messages[-1].get("role") == "user":
            if "(CRITICAL REMINDER:" not in messages[-1]["content"]:
                messages[-1]["content"] += "\n\n(CRITICAL REMINDER: You are the streamer. Output ONLY your spoken dialogue. Do NOT repeat this prompt. Do NOT narrate.)"

        if self.active_guide_context:
            for m in messages:
                if m["role"] == "system":
                    m["content"] += f"\n\n[GAME GUIDE / WIKI CONTEXT]: Use this info if helpful:\n{self.active_guide_context}"
                    break

        # 🛡️ FAILOVER FIRST for cloud modes: rotates keys, skips rate-limited ones instantly, falls through to
        # other providers and auto-heals retired models. The original per-provider code below stays as the
        # last resort for when the failover is disabled or has no keys at all.
        if self.ai_mode != "local" and getattr(self, 'ai_failover_enabled', True):
            _fo = self.get_failover()
            if _fo and _fo.enabled() and _fo.order(self.ai_mode):
                return self._failover_try(messages, max_tokens, temperature)

        if self.ai_mode == "local":
            try:
                client = get_lm_studio_client()
                completion = client.chat.completions.create(model=LM_STUDIO_MODEL_NAME, messages=messages, max_tokens=max_tokens, temperature=temperature, frequency_penalty=0.5, presence_penalty=0.5)
                raw_text = completion.choices[0].message.content.strip('"')
                import re
                return re.sub(r'<think>.*?(?:</think>|$)\s*', '', raw_text, flags=re.DOTALL).strip()
            except: return self._failover_try(messages, max_tokens, temperature)   # LM Studio down -> use the cloud chain

        elif self.ai_mode == "groq":
            if not self.groq_api_keys or Groq is None: return None
            attempts, max_attempts = 0, len(self.groq_api_keys) + 2
            while attempts < max_attempts:
                if self.groq_key_index >= len(self.groq_api_keys): self.groq_key_index = 0
                current_key = self.groq_api_keys[self.groq_key_index]
                try:
                    # 🛡️ Default to GPT-OSS because it does NOT use <think> tags!
                    active_model = getattr(self, 'groq_model', "openai/gpt-oss-20b")
                    
                    if "llama-3.1" in active_model or "llama-3.3" in active_model or "mixtral" in active_model or "gemma" in active_model:
                        active_model = "openai/gpt-oss-20b"
                        self.groq_model = "openai/gpt-oss-20b"

                    # If the user still uses Qwen, force it to put dialogue in quotes so we can extract it cleanly
                    if "qwen" in active_model or "deepseek" in active_model:
                        messages.append({"role": "user", "content": "IMPORTANT: Place your final spoken dialogue inside quotes like this: \"[dialogue here]\"."})
                        
                    # 🛡️ FIX: Added timeout=30.0 to prevent premature timeouts
                    client = Groq(api_key=current_key, timeout=30.0)
                    completion = client.chat.completions.create(
                        model=active_model,
                        messages=messages,
                        max_tokens=800,
                        temperature=temperature,
                        top_p=0.95,
                        frequency_penalty=0.5,
                        presence_penalty=0.5
                    )
                    
                    import re
                    raw_text = completion.choices[0].message.content
                    
                    # Strip thoughts
                    cleaned_text = re.sub(r'<think>.*?(?:</think>|$)', '', raw_text, flags=re.DOTALL | re.IGNORECASE).strip()
                    
                    if not cleaned_text:
                        # Extract from quotes if it got buried inside incomplete tags
                        quotes = re.findall(r'"([^"]*)"', raw_text)
                        if quotes:
                            cleaned_text = quotes[-1]
                        else:
                            cleaned_text = raw_text.replace('<think>', '').replace('</think>', '').strip()
                            
                    # 🛡️ THE ECHO KILLER: If the AI repeated the system directive, wipe it out and fallback cleanly.
                    if "What do I say on the microphone right now" in cleaned_text or "SCENARIO:" in cleaned_text:
                        import random
                        return random.choice(["Whoa!", "Let's go!", "Haha!", "Alright!", "Okay then."])
                        
                    return cleaned_text
                    
                except APITimeoutError as timeout_err:
                    print(f"[AI Error] ❌ Groq API Timed Out: {timeout_err}")
                    attempts += 1
                    time.sleep(1.0)
                    continue
                except Exception as e:
                    print(f"[AI Error] ❌ Backend (GROQ) failed to generate text! Error: {e}")
                    err_str = str(e).lower()
                    
                    if "model_decommissioned" in err_str or "not found" in err_str or "does not exist" in err_str: 
                        print("[System] Auto-healing: Switching to guaranteed 'openai/gpt-oss-20b' model...")
                        self.groq_model = "openai/gpt-oss-20b"
                        attempts += 1
                        time.sleep(0.5)
                        continue
                        
                    if "429" in err_str or "rate limit" in err_str or "401" in err_str: 
                        self.groq_key_index = (self.groq_key_index + 1) % len(self.groq_api_keys)
                        attempts += 1
                        time.sleep(0.5) 
                    else: 
                        self.groq_key_index = (self.groq_key_index + 1) % len(self.groq_api_keys)
                        attempts += 1
            return None

        elif self.ai_mode == "gemini":
            if not self.gemini_api_keys or genai is None: return None
            attempts, max_attempts = 0, len(self.gemini_api_keys)
            while attempts < max_attempts:
                if self.gemini_key_index >= len(self.gemini_api_keys): self.gemini_key_index = 0
                current_key = self.gemini_api_keys[self.gemini_key_index]
                try:
                    genai.configure(api_key=current_key)
                    
                    # 🛡️ INTERCEPTOR: Destroy old dead models loaded from bot_config.json
                    active_gem = getattr(self, 'gemini_model', "gemini-3.6-flash")
                    if "2.0-flash" in active_gem or "1.5" in active_gem or "pro" in active_gem:
                        active_gem = "gemini-3.6-flash"
                        
                    full_prompt = "".join([f"{m['role'].upper()}: {m['content']}\n" for m in messages]) + "ASSISTANT:" 
                    response = genai.GenerativeModel(active_gem).generate_content(full_prompt, generation_config=genai.types.GenerationConfig(max_output_tokens=max_tokens, temperature=temperature, top_p=0.95, frequency_penalty=0.5))
                    raw_text = response.text.strip()
                    return re.sub(r'<think>.*?(?:</think>|$)\s*', '', raw_text, flags=re.DOTALL).strip()
                except:
                    self.gemini_key_index = (self.gemini_key_index + 1) % len(self.gemini_api_keys)
                    attempts += 1; time.sleep(0.5)
            return None

        elif self.ai_mode == "openrouter":
            if not self.openrouter_api_keys: return None
            if self.openrouter_key_index >= len(self.openrouter_api_keys): self.openrouter_key_index = 0
            current_key = self.openrouter_api_keys[self.openrouter_key_index]
            try:
                import requests
                url = "https://openrouter.ai/api/v1/chat/completions"
                headers = {
                    "Authorization": f"Bearer {current_key}",
                    "HTTP-Referer": "http://localhost:1234", # Required for OpenRouter
                    "Content-Type": "application/json"
                }
                payload = {
                    "model": self.openrouter_model,
                    "messages": messages,
                    "max_tokens": max_tokens,
                    "temperature": temperature
                }
                resp = requests.post(url, headers=headers, json=payload, timeout=15)
                if resp.status_code == 200:
                    raw_text = resp.json()['choices'][0]['message']['content'].strip()
                    import re
                    return re.sub(r'<think>.*?(?:</think>|$)\s*', '', raw_text, flags=re.DOTALL).strip()
                elif resp.status_code == 429:
                    self.openrouter_key_index = (self.openrouter_key_index + 1) % len(self.openrouter_api_keys)
                return None
            except Exception as e:
                print(f"[OpenRouter API Error] {e}")
                return None

        elif self.ai_mode == "sambanova":
            if not self.sambanova_api_keys: return None
            if self.sambanova_key_index >= len(self.sambanova_api_keys): self.sambanova_key_index = 0
            current_key = self.sambanova_api_keys[self.sambanova_key_index]
            try:
                from openai import OpenAI
                client = OpenAI(base_url="https://api.sambanova.ai/v1", api_key=current_key)
                completion = client.chat.completions.create(
                    model=self.sambanova_model,
                    messages=messages,
                    max_tokens=max_tokens,
                    temperature=temperature
                )
                raw_text = completion.choices[0].message.content.strip()
                import re
                return re.sub(r'<think>.*?(?:</think>|$)\s*', '', raw_text, flags=re.DOTALL).strip()
            except Exception as e:
                print(f"[SambaNova Error] {e}")
                self.sambanova_key_index = (self.sambanova_key_index + 1) % len(self.sambanova_api_keys)
                return None

        return None

    def start_ptt_recording(self):
        if getattr(self, '_recording_active', False): return
        self._recording_active = True
        self.is_ptt_recording = True
        self.ptt_frames = []
        self.ptt_done_event = threading.Event()
        
        def _record():
            try:
                self.ptt_sample_rate = int(sd.query_devices(self.mic_device_index, 'input')['default_samplerate'])
                with sd.InputStream(samplerate=self.ptt_sample_rate, channels=1, dtype='int16', device=self.mic_device_index) as stream:
                    while self.is_ptt_recording: 
                        data, overflowed = stream.read(1024)
                        self.ptt_frames.append(data.copy())
            except Exception as e: print(f"PTT Record Error: {e}")
            finally:
                self._recording_active = False
                self.ptt_done_event.set()
        threading.Thread(target=_record, daemon=True).start()

    def stop_ptt_and_transcribe(self):
        self.is_ptt_recording = False
        if hasattr(self, 'ptt_done_event'): self.ptt_done_event.wait(timeout=2.0)
        if not getattr(self, 'ptt_frames', None) or len(self.ptt_frames) == 0: return ""
        try:
            audio_data_np = np.concatenate(self.ptt_frames, axis=0)
            audio_bytes = audio_data_np.tobytes()
            audio_data = sr.AudioData(audio_bytes, self.ptt_sample_rate, 2)
            
            _optimize_torch_if_needed()
            if self.stt_mode == "whisper" and self.whisper_model:
                import tempfile
                with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
                    f.write(audio_data.get_wav_data()); f.flush()
                    res = self.whisper_model.transcribe(f.name)
                os.remove(f.name)
                return res.get("text", "").strip()
            else: 
                return sr.Recognizer().recognize_google(audio_data).strip()
        except Exception as e:
            print(f"[PTT Error] {e}")
            return ""

    def transcribe_mic_audio(self, duration=None, phrase_time_limit=10):
        with sr.Microphone(device_index=self.mic_device_index) as source:
            self.recognizer.adjust_for_ambient_noise(source, duration=0.5)
            
            _optimize_torch_if_needed()
            if self.stt_mode == "whisper" and self.whisper_model:
                audio = self.recognizer.listen(source, timeout=5, phrase_time_limit=phrase_time_limit)
                try:
                    import torch
                    audio_np = np.frombuffer(audio.get_raw_data(convert_rate=16000, convert_width=2), dtype=np.int16).astype(np.float32) / 32768.0
                    with torch.inference_mode(): return self.whisper_model.transcribe(audio_np, fp16=torch.cuda.is_available())["text"].strip()
                except: return ""
            else:
                try: return self.recognizer.recognize_google(self.recognizer.listen(source, timeout=5, phrase_time_limit=phrase_time_limit))
                except: return ""

    def load_config(self, custom_path=None):
        path_to_load = custom_path if custom_path else CONFIG_FILE
        if not os.path.exists(path_to_load): return None
        try:
            with open(path_to_load, "r") as f:
                data = json.load(f)
                if data.get("hf_token"): os.environ["HF_TOKEN"] = data["hf_token"]
                if data.get("tiktok_session_id"): os.environ["TIKTOK_SESSION_ID"] = data["tiktok_session_id"]
                return data
        except: return None

    def save_config(self, data):
        try:
            with open(CONFIG_FILE, "w") as f: json.dump(data, f, indent=4)
            return True
        except: return False
            
    def save_new_channel_to_env(self, name, cid):
        idx = 1
        while os.environ.get(f"CHANNEL_{idx}_NAME") is not None and idx <= 50: idx += 1
        token_prefix = "".join(x for x in name if x.isalnum()).lower() or f"chan{idx}"
        try:
            mode = "a" if os.path.exists(".env") else "w"
            with open(".env", mode, encoding="utf-8") as f:
                if mode == "a": f.write("\n")
                f.write("\n".join([f"\n# --- Added via GUI (Channel {idx}) ---", f"CHANNEL_{idx}_NAME={name}", f"CHANNEL_{idx}_ID={cid}", f"CHANNEL_{idx}_TOKEN_PREFIX={token_prefix}\n"]))
            load_dotenv(override=True)
            return True, idx
        except Exception as e: return False, str(e)

    def run_bot_logic(self):
        print(f"\n[System] Starting bot for channel: {self.channel_name}...")
        if self.channel_id == "sim_id_123":
            self.run_simulation_loop()
            return
            
        self.last_sub_time = None 
        self.last_sub_count = -1
        self.first_sub_run = True 
        self.consecutive_errors = 0
        
        credential_sets = get_credential_sets("credentials", self.token_prefix)
        if not credential_sets:
            print(f"[Error] No API credentials found for prefix '{self.token_prefix}'!")
            print("[System] Please import your 'client_secret.json' in the Credential Manager 🔑.")
            self.bot_running = False
            self.emit("bot_stopped")
            return
            
        current_cred_index = 0
        while not self.stop_event.is_set():
            if current_cred_index >= len(credential_sets): 
                print("[Error] All credential sets are out of daily quota (it resets at midnight Pacific Time). Waiting 10 min, then retrying...")
                for _ in range(600):
                    if self.stop_event.is_set(): break
                    time.sleep(1)
                current_cred_index = 0
                continue
            current_creds = credential_sets[current_cred_index]
            try:
                print(f"[System] Authenticating YouTube API (Set #{current_cred_index+1}). Watch your browser if this is your first time!")
                youtube, creds = get_authenticated_service(current_creds['secret'], current_creds['token'])
                if not youtube: 
                    print("[Error] Authentication failed.")
                    break
                
                print("[System] Authentication successful!")
                self._yt_creds = creds
                tts_client = None
                try: 
                    tts_client = texttospeech.TextToSpeechClient(credentials=creds)
                    self.google_tts_client = tts_client
                    self.voice_enabled = True
                    print("[System] Google Cloud TTS Ready.")
                except: 
                    self.google_tts_client = None
                    self.voice_enabled = False
                    print("[System] Google Cloud TTS skipped (offline/local TTS used).")
                    
                self.run_stream_loop(youtube, tts_client)
            except Exception as e:
                status, reason, msg = yt_error_info(e)
                if yt_is_daily_quota(e):
                    print(f"[Warning] Daily API quota exhausted on credential set #{current_cred_index+1}. Switching...")
                    current_cred_index += 1
                else:
                    print(f"[Warning] Connection Error ({status} {reason}): {msg}")
                    if status == 403:
                        print("[YouTube] ➜ A 403 that is NOT a quota error usually means the saved token lacks chat permission. "
                              "Delete the matching *_token_N.json in /credentials and restart to re-authorise.")
                    print("[System] Retrying in 15s...")
                    for _ in range(15):
                        if self.stop_event.is_set(): break
                        time.sleep(1)
        
        print("[System] Bot disconnected.")
        self.bot_running = False
        self.emit("bot_stopped")

    def run_simulation_loop(self):
        last_idle_time = time.time()
        while not self.stop_event.is_set():
            if not self.history_req_queue.empty():
                self.history_req_queue.get()
                self.history_resp_queue.put([{"author": "SimViewer", "message": "Can I get a shoutout?", "publishedAt": "10:00"}])
            
            self.check_manual_queue(None, None, None)
            
            try:
                target_msg = self.target_reply_queue.get_nowait()
                threading.Thread(target=self.generate_reply_for_message, args=(target_msg['author'], target_msg['message'], None, None, None, target_msg.get('force_text_only', False), target_msg.get('force_voice_only', False), target_msg.get('force_fresh', True)), daemon=True).start()
                last_idle_time = time.time()
            except queue.Empty: pass
            
            if self.idle_chat_enabled and not self.hotkeys_only_mode and (time.time() - last_idle_time > 45) and not self.is_speaking and self.tts_queue.empty():
                self.handle_idle_chat(None, None, None)
                last_idle_time = time.time()
            time.sleep(1)

    def run_stream_loop(self, youtube, tts_client):
        while not self.stop_event.is_set():
            print(f"[System] Scanning for Live Stream on Channel ID: {self.channel_id}...")
            live_chat_id, stream_title, video_id = get_live_stream_info(youtube, self.channel_id)
            if not live_chat_id:
                self._live_chat_id = None
                print(f"[System] Stream offline or not found. Retrying in 60s...")
                for _ in range(60):
                    if self.stop_event.is_set(): return
                    # keep hotkeys / voice-only manual items alive while offline (they used to pile up silently)
                    try: self.check_manual_queue(youtube, tts_client, None)
                    except Exception as pump_err: print(f"[Offline Queue Error] {pump_err}")
                    time.sleep(1)
                continue
            
            print(f"[System] Connected to LIVE STREAM: {stream_title}")
            self.history_req_queue.put("FETCH")
            self.last_chat_time = time.time()
            self.last_reply_time = 0
            self.last_msg_timestamp = None
            self.last_like_count = -1 
            self.stats_poll_counter = 0 
            self.last_sub_time = None 
            self.last_sub_count = -1 
            self.first_sub_run = True
            self.last_viewer_count = -1
            self.current_viewer_count = 0
            self.current_like_count = 0
            self.consecutive_errors = 0
            self._chat_page_token = None
            self._seen_chat_ids, self._seen_chat_order = set(), None
            self._stats_last = 0
            self._subs_last = 0
            self._yt_service, self._live_chat_id = youtube, live_chat_id
            try:
                self.process_messages_loop(youtube, tts_client, live_chat_id, video_id)
            finally:
                self._live_chat_id = None

    def process_messages_loop(self, youtube, tts_client, live_chat_id, video_id):
        while not self.stop_event.is_set():
            try:
                try:
                    if not self.history_req_queue.empty() and self.history_req_queue.get_nowait() == "FETCH":
                        if self.chat_memory: self.history_resp_queue.put([{'author': 'System', 'message': 'Refreshing...', 'type': 'system'}])
                        try:
                            h_resp = youtube.liveChatMessages().list(liveChatId=live_chat_id, part="snippet,authorDetails", maxResults=200).execute()
                            parsed_msgs = []
                            for item in h_resp.get('items', []):
                                try:
                                    author = item['authorDetails']['displayName']
                                    msg_type = item['snippet'].get('type', 'textMessageEvent')
                                    message = item['snippet']['textMessageDetails']['messageText'] if msg_type == 'textMessageEvent' else \
                                              f"[SUPER CHAT {item['snippet']['superChatDetails']['amountDisplayString']}] {item['snippet']['superChatDetails'].get('userComment', '')}" if msg_type == 'superChatEvent' else \
                                              "[NEW MEMBER JOINED]" if msg_type == 'newSponsorEvent' else \
                                              "[SENT A SUPER STICKER]" if msg_type == 'superStickerEvent' else f"[{msg_type}]"
                                    parsed_msgs.append({'author': author, 'message': message, 'publishedAt': item['snippet']['publishedAt'], 'type': 'user'})
                                except: pass
                            self.history_resp_queue.put(parsed_msgs)
                        except Exception as e:
                            if yt_is_daily_quota(e) or yt_is_chat_gone(e): raise e
                            print(f"[Chat History Error] {yt_error_info(e)[2]}")
                except queue.Empty: pass

                try:
                    target_msg = self.target_reply_queue.get_nowait()
                    threading.Thread(target=self.generate_reply_for_message, args=(target_msg['author'], target_msg['message'], youtube, tts_client, live_chat_id, target_msg.get('force_text_only', False), target_msg.get('force_voice_only', False), target_msg.get('force_fresh', True)), daemon=True).start()
                except queue.Empty: pass

                self.check_manual_queue(youtube, tts_client, live_chat_id)
                if not self.hotkeys_only_mode: self.handle_idle_chat(youtube, tts_client, live_chat_id)
                
                _now = time.time()
                if _now - getattr(self, '_stats_last', 0) >= STATS_POLL_SECONDS:
                    self._stats_last = _now
                    self.check_stream_stats(youtube, tts_client, live_chat_id, video_id)
                if _now - getattr(self, '_subs_last', 0) >= SUBS_POLL_SECONDS:
                    self._subs_last = _now
                    self.check_new_subscribers(youtube, tts_client, live_chat_id) 
                    
                self.poll_and_reply(youtube, tts_client, live_chat_id)
                self.consecutive_errors = 0 
                
            except Exception as loop_err:
                if yt_is_daily_quota(loop_err): raise loop_err 
                if yt_is_chat_gone(loop_err):
                    print("[System] Live chat ended. Looking for a new stream...")
                    return
                import traceback as _tb
                print(f"[Chat Loop Error - Auto Recovering] {loop_err}\n{_tb.format_exc()}")
                
                self.consecutive_errors = getattr(self, 'consecutive_errors', 0) + 1
                if self.consecutive_errors >= 5:
                    print("[System] ⚠️ Too many consecutive chat errors. Stream may have dropped. Reconnecting...")
                    self.consecutive_errors = 0
                    return 
                    
                time.sleep(3)

    def handle_idle_chat(self, youtube, tts_client, live_chat_id):
        if not self.idle_chat_enabled: return
        if (time.time() - self.last_chat_time) > IDLE_CHAT_THRESHOLD_SECONDS:
            self.last_chat_time = time.time() 
            def _idle_gen():
                vibe = self.get_random_vibe()
                sys_p = (f"Act as {self.channel_name}, a live streamer playing {self.game_name}. The viewers have been quiet.\n"
                         f"CURRENT VIBE/MOOD: {vibe}\n"
                         f"CRITICAL RULES (MUST OBEY):\n"
                         f"- NO HALLUCINATIONS: You CANNOT see the screen. NEVER invent specific in-game events, weapons, characters, taking damage, or fighting bosses.\n"
                         f"- You are likely just standing still, in a lobby, or exploring. Keep commentary strictly to your feelings, the vibe, or general remarks.\n"
                         f"- NEVER say you are an AI or can't see the screen. Act human.\n"
                         f"- Speak in first-person ('I', 'me').\n"
                         f"- NEVER repeat the same phrase. Keep it short (1 sentence).\n"
                         f"- NEVER use the word 'chat', say 'viewers' or 'guys' instead.")
                         
                # 🛡️ THE MISSING LINK: Inject UI Directives into Idle Chat!
                if getattr(self, 'global_directive_enabled', False) and getattr(self, 'global_directive_text', "").strip():
                    sys_p += f"\n\n[CRITICAL GLOBAL DIRECTIVE]:\n{self.global_directive_text.strip()}"
                if getattr(self, 'chat_directive_enabled', False) and getattr(self, 'chat_directive_text', "").strip():
                    sys_p += f"\n\n[CRITICAL CHAT DIRECTIVE]:\n{self.chat_directive_text.strip()}"

                reply = self.generate_ai_completion([{"role": "system", "content": sys_p}, {"role": "user", "content": "Say something random and entertaining to break the silence. Do NOT invent game scenes."}], max_tokens=350, temperature=1.1)
                if reply: self.process_reply(youtube, tts_client, live_chat_id, reply, force_fresh=True)
            threading.Thread(target=_idle_gen, daemon=True).start()

    def check_stream_stats(self, youtube, tts_client, live_chat_id, video_id):
        try:
            resp = youtube.videos().list(part='statistics,liveStreamingDetails', id=video_id).execute()
            if not resp.get('items'): return
            item = resp['items'][0]
            current_viewers = int(item.get('liveStreamingDetails', {}).get('concurrentViewers', '0'))
            current_likes = int(item.get('statistics', {}).get('likeCount', 0))
            self.current_viewer_count, self.current_like_count = current_viewers, current_likes
            
            self.emit('viewer_count', current_viewers)
            self.emit('stats_display', current_viewers, current_likes)

            if not self.hotkeys_only_mode and self.automated_replies_enabled:
                if self.last_viewer_count != -1 and current_viewers - self.last_viewer_count > 0 and self.greet_new_viewers: 
                    self.trigger_new_viewer_welcome(current_viewers - self.last_viewer_count, youtube, tts_client, live_chat_id)
                self.last_viewer_count = current_viewers

                if self.last_like_count == -1: self.last_like_count = current_likes; return
                if current_likes - self.last_like_count > 0:
                    self.trigger_like_thanks(current_likes - self.last_like_count, youtube, tts_client, live_chat_id)
                self.last_like_count = current_likes
        except Exception as e: 
            if yt_is_daily_quota(e): raise e 
            print(f"[Stats Warning] {yt_error_info(e)[2]}")

    def check_new_subscribers(self, youtube, tts_client, live_chat_id):
        try:
            def parse_yt_date(t_str):
                try: return datetime.fromisoformat(t_str.replace('Z', '+00:00'))
                except: return datetime.fromisoformat(t_str.replace('Z', '').split('+')[0]).replace(tzinfo=timezone.utc)

            try: sub_resp = youtube.subscriptions().list(part="subscriberSnippet,snippet", mySubscribers=True, maxResults=15, order="relevance").execute()
            except Exception as e:
                if yt_is_daily_quota(e): raise e
                if hasattr(e, 'resp') and e.resp.status in [429, 500, 502, 503]: return 
                # Private/forbidden subscriber list (very common) -> fall back to the plain subscriber COUNT.
                # The old code raised here and the 403 was mistaken for a quota error, which rotated credentials until the bot shut down.
                if not getattr(self, '_subs_warned', False):
                    print(f"[YouTube] Subscriber list unavailable ({yt_error_info(e)[1] or 'error'}); using subscriber count only.")
                    self._subs_warned = True
                sub_resp = {'items': []}
            
            chan_resp = youtube.channels().list(part="statistics", mine=True).execute()
            total_subs = int(chan_resp['items'][0]['statistics']['subscriberCount']) if chan_resp.get('items') else 0
            self.emit('sub_count', total_subs)

            if self.first_sub_run:
                self.last_sub_time = parse_yt_date(sorted(sub_resp['items'], key=lambda x: x['snippet']['publishedAt'], reverse=True)[0]['snippet']['publishedAt']) if sub_resp.get('items') else datetime.now(timezone.utc)
                self.last_sub_count, self.first_sub_run = total_subs, False
                return

            found_new_names, newest_in_batch = [], self.last_sub_time 
            if sub_resp.get('items'):
                for item in sorted(sub_resp['items'], key=lambda x: x['snippet']['publishedAt'], reverse=True):
                    try:
                        pub_dt = parse_yt_date(item['snippet']['publishedAt'])
                        if pub_dt > self.last_sub_time:
                            found_new_names.append(item['subscriberSnippet']['title'])
                            if pub_dt > newest_in_batch: newest_in_batch = pub_dt
                        else: break 
                    except: continue
            
            self.last_sub_time = newest_in_batch
            for name in reversed(found_new_names): 
                self.trigger_sub_welcome(name, youtube, tts_client, live_chat_id, is_public=True); time.sleep(3) 

            if total_subs > self.last_sub_count and total_subs - self.last_sub_count - len(found_new_names) > 0:
                for _ in range(total_subs - self.last_sub_count - len(found_new_names)):
                    self.trigger_sub_welcome("Anonymous", youtube, tts_client, live_chat_id, is_public=False); time.sleep(2)
            self.last_sub_count = total_subs
        except Exception as e: 
            if yt_is_daily_quota(e): raise e 
            print(f"[Subscriber Check Warning] {yt_error_info(e)[2]}")

    def trigger_sub_welcome(self, sub_name, youtube, tts_client, live_chat_id, is_public=True):
        def _gen():
            vibe = self.get_random_vibe()
            reply = self.generate_ai_completion([{"role": "system", "content": f"You are {self.channel_name}. A new subscriber named '{sub_name}' just joined! \nCURRENT VIBE/MOOD: {vibe}\nCRITICAL RULES:\n- NEVER say 'Welcome in'.\n- Express your current vibe.\n- Talk to them like a best friend. Keep the reply poetic or rhythmic since you will sing it!"}, {"role": "user", "content": f"Give a quick welcome shoutout to {sub_name if is_public else 'mystery sub'}."}], max_tokens=250, temperature=1.1)
            if reply: 
                self.process_reply(youtube, tts_client, live_chat_id, reply, override_tts_mode="tiktok", override_voice="en_female_f08_warmy_breeze", force_fresh=True)
        threading.Thread(target=_gen, daemon=True).start()

    def trigger_new_viewer_welcome(self, count, youtube, tts_client, live_chat_id):
        def _gen():
            vibe = self.get_random_vibe()
            msgs=[{"role": "system", "content": f"You are {self.channel_name}. {count} new viewer(s) just joined.\nCURRENT VIBE/MOOD: {vibe}\nCRITICAL RULES:\n- NEVER say 'Welcome in'.\n- Express your vibe.\n- Be unpredictable."}] + (self.chat_memory[-4:] if self.chat_memory else []) + [{"role": "user", "content": "Acknowledge the new people joining. Keep it short."}]
            reply = self.generate_ai_completion(msgs, max_tokens=250, temperature=1.1)
            if reply: self.process_reply(youtube, tts_client, live_chat_id, reply, force_fresh=True)
        threading.Thread(target=_gen, daemon=True).start()

    def trigger_like_thanks(self, diff_count, youtube, tts_client, live_chat_id):
        def _gen():
            vibe = self.get_random_vibe()
            msgs = [{"role": "system", "content": f"You are {self.channel_name}. {diff_count} people just Liked the stream.\nCURRENT VIBE/MOOD: {vibe}\nCRITICAL RULES:\n- NEVER say 'Thanks for the likes'.\n- Show your vibe."}] + (self.chat_memory[-4:] if self.chat_memory else []) + [{"role": "user", "content": "React to the likes. Short."}]
            reply = self.generate_ai_completion(msgs, max_tokens=50, temperature=1.1)
            if reply: self.process_reply(youtube, tts_client, live_chat_id, reply, force_fresh=True)
        threading.Thread(target=_gen, daemon=True).start()

    def check_manual_queue(self, youtube, tts_client, live_chat_id):
        try:
            item_data = self.manual_reply_queue.get_nowait()
            if isinstance(item_data, dict):
                force_cache = item_data.get('force_cache', False)
                category = item_data.get('category', 'generic')
                is_silent = item_data.get('silent_cache', False)
                force_fresh = item_data.get('force_fresh', False)
                
                if force_cache:
                    chosen_wav = self.find_random_cached_audio(category)
                    if chosen_wav:
                        chosen_txt = chosen_wav.replace(".wav", ".txt")
                        if os.path.exists(chosen_txt):
                            with open(chosen_txt, "r", encoding="utf-8") as f:
                                text_to_send = f.read().strip()
                        else:
                            text_to_send = f"[Action: Uses {category.replace('_', ' ').title()} reaction]"

                        print(f"[Cache] Successfully loaded '{category}' from memory! ({os.path.basename(chosen_wav)})")
                        self.process_reply(youtube, tts_client, live_chat_id, text_to_send, force_text_only=item_data.get('force_text_only', False), force_voice_only=item_data.get('force_voice_only', False), is_manual=True, force_cache_file=chosen_wav, category=category, is_silent=is_silent)
                        self.last_reply_time = self.last_chat_time = time.time()
                        return
                    print(f"[Cache] No cached files found for '{category}'. Skipping double-tap.")
                    return
                
                text_to_send = item_data.get('message', '')
                force_text = item_data.get('force_text_only', False)
                force_voice = item_data.get('force_voice_only', False)
                is_manual_source = True
            else:
                text_to_send = str(item_data)
                force_text = False
                force_voice = False
                is_manual_source = False
                category = "generic"
                is_silent = False
                force_fresh = True
                
            if not is_manual_source and any(p.get('role') == 'assistant' and p.get('content') == text_to_send for p in self.chat_memory[-5:]): return
            try:
                self.process_reply(youtube, tts_client, live_chat_id, text_to_send, force_text_only=force_text, force_voice_only=force_voice, is_manual=is_manual_source, category=category, is_silent=is_silent, force_fresh=force_fresh)
                self.last_reply_time = self.last_chat_time = time.time()
            except Exception as e:
                if yt_is_daily_quota(e): raise e
                print(f"[Manual Queue Error] {e}")
        except queue.Empty: pass

    def generate_reply_for_message(self, author, content, youtube, tts_client, live_chat_id, force_text_only=False, force_voice_only=False, force_fresh=True):
        self.last_chat_time = time.time()
        clean_content = content.strip().lower()
        greeting_words = ['hi', 'hello', 'hey', 'sup', 'yo', 'greetings', 'wassup']
        is_simple_greeting = any(clean_content == w for w in greeting_words) or (len(clean_content.split()) <= 3 and any(clean_content.startswith(w) for w in greeting_words))
        vibe = self.get_random_vibe()

        custom_len = False
        target_words = 30
        try:
            import json
            with open("bot_config.json", "r") as f:
                c_data = json.load(f)
                custom_len = c_data.get("custom_length_enabled", False)
                target_words = c_data.get("custom_length_val", 30)
        except: pass

        if custom_len:
            length_rule = f"- You MUST reply with approximately {target_words} words. PERSONA & LANGUAGE: You MUST completely adopt the exact speaking style, slang, and language of your Persona ({self.current_persona}). Do not cut yourself short."
            max_toks = max(350, int(target_words * 4) + 200) # Added buffer for AI thinking
        else:
            length_rule = f"- Keep it under 15 words. PERSONA & LANGUAGE: You MUST completely adopt the exact speaking style, slang, and language of your Persona ({self.current_persona})."
            max_toks = 350 # Massive bump to allow AI to finish thinking and output dialogue

        if is_simple_greeting:
            sys_prompt = f"Act as {self.channel_name}, actively playing {self.game_name} right now. The user '{author}' is greeting you.\nCURRENT VIBE/MOOD: {vibe}\nCRITICAL RULES:\n- Speak in first-person ('I', 'me').\n- NEVER say 'Welcome in'.\n- EXPRESS YOUR VIBE HEAVILY.\n{length_rule}\n- NEVER say you are an AI or can't see the screen.\n- NEVER use the word 'chat', call them 'viewers' or 'guys'."
        else:
            sys_prompt = f"Act as {self.channel_name}, a real human live streamer actively playing {self.game_name} on PC right now.\nCURRENT VIBE/MOOD: {vibe}\nCRITICAL RULES:\n- YOU are the gamer holding the controller. Speak in first-person ('I', 'me').\n- NEVER say 'Welcome in' or 'How can I help you'.\n{length_rule}\n- Talk to the user like a best friend.\n- VARY YOUR PHRASING AND EMBRACE YOUR CURRENT VIBE.\n- NO HALLUCINATIONS: You CANNOT see the screen. NEVER invent specific in-game events, weapons (like shotguns), characters, or taking damage unless the viewer explicitly asks about it.\n- NEVER say you are an AI or cannot see the screen.\n- NEVER use the word 'chat', call them 'viewers' or 'guys'."

        if not is_simple_greeting and self.active_guide_context: sys_prompt += f"\n\n[GAME GUIDE DATA]:\n{self.active_guide_context}\n"
        if any(x in clean_content for x in ["liked", "naglike", "done like", "like done", "thumbs up", "tapus na like"]): sys_prompt += " The user Liked the stream."
        if any(x in clean_content for x in ["just subscribed", "new subscriber", "nagsubscribe"]): sys_prompt += f" {author} just SUBSCRIBED!"

        persona_map = {
            "pinoy": " PERSONA/LANGUAGE: Taglish/Slang. Use words like 'lods', 'pre', 'solid'.",
            "kid": " PERSONA/LANGUAGE: Gen Alpha / Zoomer Slang. Use terms like: Rizz, Sigma, Skibidi, Cap.",
            "roasting": " PERSONA: Playfully roast and trash-talk the chat. Be savage.",
            "joker": " PERSONA: Be a hilarious joker. Constantly make puns.",
            "serious": " PERSONA: You are an ultra-serious, try-hard pro gamer.",
            "genius": " PERSONA: Act like a know-it-all genius.",
            "friendly": " PERSONA: Extremely friendly, wholesome, and welcoming."
        }
        sys_prompt += next((v for k, v in persona_map.items() if k in self.current_persona.lower()), " LANGUAGE: Casual English. Gamer slang.")
        sys_prompt += "\nCRITICAL RULE: NEVER repeat the same phrase. Be highly unpredictable."
        if self.strict_repetition_mode: sys_prompt += "\n[STRICT MODE ACTIVE]: ABSOLUTELY FORBIDDEN to use any phrase used in the previous 10 messages."

        # 🛡️ THE MISSING LINK: Inject the UI Directives directly into Auto-Chat!
        if getattr(self, 'global_directive_enabled', False) and getattr(self, 'global_directive_text', "").strip():
            sys_prompt += f"\n\n[CRITICAL GLOBAL DIRECTIVE - YOU MUST OBEY THIS]:\n{self.global_directive_text.strip()}"
            
        if getattr(self, 'chat_directive_enabled', False) and getattr(self, 'chat_directive_text', "").strip():
            sys_prompt += f"\n\n[CRITICAL CHAT DIRECTIVE - STRICTLY ENFORCE FOR THIS REPLY]:\n{self.chat_directive_text.strip()}"

        msgs = [{"role": "system", "content": sys_prompt}] + (self.chat_memory[-10:] if self.chat_memory else []) + [{"role": "user", "content": f"{author}: {content}"}]

        for _ in range(3):
            try:
                reply = self.generate_ai_completion(msgs, max_tokens=max_toks, temperature=1.1)
                if not reply: break
                
                if self.strict_repetition_mode and self._check_repetition(reply):
                    msgs.extend([{"role": "assistant", "content": reply}, {"role": "user", "content": "You literally just said that. Say something completely different."}])
                    continue
                
                # 🛡️ THE FIX: Hard-lock the default variables before checking the queue
                final_force_text = force_text_only
                final_force_voice = force_voice_only
                
                if self.review_mode:
                    # Only one review at a time: concurrent threads used to steal each other's decisions and block forever.
                    if not self._review_gate.acquire(blocking=False):
                        print("[Review] A review is already open - skipping this auto-reply so decisions don't get mixed up.")
                        break
                    try:
                        while not self.review_decision_queue.empty():      # drop stale decisions
                            try: self.review_decision_queue.get_nowait()
                            except queue.Empty: break
                        self.emit('ask_review', reply, f"{author}: {content}")
                        try: action, mod_text = self.review_decision_queue.get(timeout=300)
                        except queue.Empty:
                            print("[Review] No decision within 5 minutes - discarding this reply.")
                            action, mod_text = "reject", ""
                    finally:
                        self._review_gate.release()
                    
                    if action == "reject":
                        reply = None
                    elif action == "regenerate":
                        continue
                    elif action.startswith("approve"):
                        reply = mod_text
                        
                        # 🛡️ NO LOCALS CHECK: Instantly override based on the button clicked!
                        if action == "approve_V":
                            final_force_voice = True
                            final_force_text = False
                        elif action == "approve_T":
                            final_force_voice = False
                            final_force_text = True
                        elif action == "approve_V+T":
                            final_force_voice = True
                            final_force_text = True
                
                if reply:
                    self.process_reply(youtube, tts_client, live_chat_id, reply, force_voice_only=final_force_voice, force_text_only=final_force_text, force_fresh=force_fresh)
                    self.last_reply_time = time.time()
                break
            except Exception as e: 
                print(f"[Generate Reply Error] {e}")
                break

    def _check_repetition(self, candidate_text):
        if not candidate_text: return False
        recent_bot_msgs = [m.get('content') or m.get('message') for m in reversed(self.chat_memory) if m.get('role') == 'assistant' or m.get('type') == 'bot'][:15]
        candidate_lower = candidate_text.lower().strip()
        candidate_words = candidate_lower.split()
        for past_msg in recent_bot_msgs:
            if not past_msg: continue
            past_lower = past_msg.lower().strip()
            if len(candidate_lower) > 10 and candidate_lower in past_lower: return True
            if len(past_lower) > 10 and past_lower in candidate_lower: return True
            past_words = past_lower.split()
            if len(candidate_words) >= 2 and len(past_words) >= 2 and candidate_words[:2] == past_words[:2]: return True 
            if difflib.SequenceMatcher(None, candidate_lower, past_lower).ratio() > 0.6: return True
        return False

    def poll_and_reply(self, youtube, tts_client, live_chat_id):
        # 1) Fetch ONLY new messages (nextPageToken) and honour YouTube's requested polling interval.
        #    The old code re-downloaded the last 200 messages every 1-3 s, which burned the daily quota in about an hour.
        try:
            req = dict(liveChatId=live_chat_id, part="snippet,authorDetails", maxResults=200)
            token = getattr(self, '_chat_page_token', None)
            if token: req['pageToken'] = token
            resp = youtube.liveChatMessages().list(**req).execute()
            self._chat_page_token = resp.get('nextPageToken') or token
            items = resp.get('items', [])
            poll_wait = max(2.0, min(resp.get('pollingIntervalMillis', 5000) / 1000.0, 10.0))
        except Exception as e:
            if yt_is_daily_quota(e) or yt_is_chat_gone(e): raise e
            status, reason, msg = yt_error_info(e)
            if status == 400: self._chat_page_token = None
            if status not in (429, 500, 502, 503) and reason not in ('rateLimitExceeded', 'userRateLimitExceeded'):
                print(f"[Chat Poll Error] {status} {reason}: {msg}")
            return time.sleep(5)

        if not items: return time.sleep(poll_wait)

        # 2) De-duplicate by message id (string timestamp compares were unreliable)
        from collections import deque
        if getattr(self, '_seen_chat_order', None) is None: self._seen_chat_order = deque()
        if not hasattr(self, '_seen_chat_ids'): self._seen_chat_ids = set()
        def _is_new(m):
            mid = m.get('id')
            if not mid: return True
            if mid in self._seen_chat_ids: return False
            self._seen_chat_ids.add(mid); self._seen_chat_order.append(mid)
            if len(self._seen_chat_order) > 3000: self._seen_chat_ids.discard(self._seen_chat_order.popleft())
            return True
        items = [m for m in items if _is_new(m)]
        if not items: return time.sleep(poll_wait)
        items.sort(key=lambda x: x.get('snippet', {}).get('publishedAt', ''))

        def _describe(m):
            try:
                t = m['snippet'].get('type', 'textMessageEvent')
                if t == 'textMessageEvent': return m['snippet']['textMessageDetails']['messageText']
                if t == 'superChatEvent': return f"[SUPER CHAT] {m['snippet']['superChatDetails'].get('userComment', '')}"
                if t == 'newSponsorEvent': return "[NEW MEMBER JOINED]"
                if t == 'superStickerEvent': return "[SENT A SUPER STICKER]"
            except Exception: pass
            return ""

        first_poll = self.last_msg_timestamp is None
        self.last_msg_timestamp = items[-1].get('snippet', {}).get('publishedAt', '') or (self.last_msg_timestamp or '')

        # 3) Decide who gets a reply (everyone still shows up in the preview/history - the old code hid the skipped ones)
        def _is_me(m): return m.get('authorDetails', {}).get('channelId') == self.channel_id
        if first_poll:
            last_bot_index = next((i for i in range(len(items)-1, -1, -1) if _is_me(items[i])), -1)
            pool = items[-5:] if last_bot_index == -1 else items[last_bot_index+1:][-10:]
        else:
            pool = items
        pool = [m for m in pool if not _is_me(m) and _describe(m)]
        if len(pool) > MAX_REPLIES_PER_POLL:
            pool = random.sample(pool, MAX_REPLIES_PER_POLL) if not first_poll else pool[-MAX_REPLIES_PER_POLL:]
        reply_ids = {id(m) for m in pool}

        for msg in items:
            if self.stop_event.is_set(): return
            try:
                author = msg['authorDetails'].get('displayName', 'Unknown')
                content = _describe(msg)
                if not content: continue
                is_me = _is_me(msg)
                published = msg['snippet'].get('publishedAt', '')
                try: self.history_resp_queue.put([{'author': author, 'message': content, 'publishedAt': published, 'type': 'bot' if is_me else 'user'}])
                except Exception: pass
                if is_me: continue
                self.last_incoming_message = {'author': author, 'message': content}
                self.emit('chat_preview', author, content)
                if self.hotkeys_only_mode or not self.automated_replies_enabled: continue
                if id(msg) in reply_ids and (time.time() - self.last_reply_time > REPLY_COOLDOWN_SECONDS):
                    threading.Thread(target=self.generate_reply_for_message, args=(author, content, youtube, tts_client, live_chat_id, self.chat_replies_text_only, False), daemon=True).start()
            except Exception as msg_err:
                print(f"[Chat Message Skipped] {msg_err}")
        time.sleep(poll_wait)


    def _ensure_chat_sender(self):
        with self._chat_sender_lock:
            if self._chat_sender_started: return
            self._chat_sender_started = True
        def _worker():
            last_sent = 0.0
            while True:
                service, chat_id, text = self.chat_send_queue.get()
                try:
                    wait = CHAT_SEND_MIN_GAP - (time.time() - last_sent)
                    if wait > 0: time.sleep(wait)
                    self._post_chat_message(service, chat_id, text)
                    last_sent = time.time()
                except Exception as e:
                    print(f"[YouTube Chat Sender] Unexpected error: {e}")
        threading.Thread(target=_worker, daemon=True, name="YTChatSender").start()

    def _get_sender_service(self, fallback):
        creds = getattr(self, '_yt_creds', None)
        if creds is None: return fallback
        if getattr(self, '_sender_creds_ref', None) is not creds or getattr(self, '_sender_service', None) is None:
            try:
                self._sender_service = build('youtube', 'v3', credentials=creds, cache_discovery=False)
                self._sender_creds_ref = creds
            except Exception as e:
                print(f"[YouTube Chat Sender] Could not build dedicated client ({e}); using shared one.")
                return fallback
        return self._sender_service

    def _post_chat_message(self, service, chat_id, text):
        service = service or getattr(self, '_yt_service', None)
        chat_id = chat_id or getattr(self, '_live_chat_id', None)
        if service is None:
            print(f"[Simulation Mode Chat Output]: {text}")
            return False
        if not chat_id:
            print(f"[YouTube Chat] ⚠️ Not connected to a LIVE chat right now - message was NOT posted: '{text[:60]}'")
            return False
        service = self._get_sender_service(service)
        body = {"snippet": {"type": "textMessageEvent", "liveChatId": chat_id, "textMessageDetails": {"messageText": text}}}
        for attempt in range(3):
            try:
                # part must be a plain string: a Python list gets stringified to "['snippet']" by googleapiclient
                service.liveChatMessages().insert(part="snippet", body=body).execute()
                print("[YouTube API] ✅ Text successfully posted to chat!")
                return True
            except Exception as e:
                status, reason, msg = yt_error_info(e)
                if yt_is_daily_quota(e):
                    print("❌ [YOUTUBE TEXT SEND ERROR] Daily API quota exhausted - chat messages cannot be posted until it resets.")
                    return False
                if status in (429, 500, 502, 503) or reason in ('rateLimitExceeded', 'userRateLimitExceeded'):
                    time.sleep(2 * (attempt + 1)); continue
                hints = {
                    'insufficientPermissions': "Token has no chat permission - delete the *_token_N.json in /credentials and re-authorise.",
                    'forbidden': "Chat may be disabled/slow-mode/subscribers-only for this account, or the token lacks permission.",
                    'liveChatEnded': "The live chat has ended.",
                    'liveChatNotFound': "Live chat not found (stream ended?).",
                    'liveChatDisabled': "Chat is disabled on this stream.",
                }
                print(f"\n❌ [YOUTUBE TEXT SEND ERROR] {status} {reason}: {msg}")
                if reason in hints: print(f"   ➜ {hints[reason]}")
                return False
        print("❌ [YOUTUBE TEXT SEND ERROR] Gave up after 3 attempts (rate limited).")
        return False

    def process_reply(self, youtube_service, tts_client, live_chat_id, text, force_voice_only=False, force_text_only=False, is_manual=False, override_tts_mode=None, override_voice=None, force_cache_file=None, category="generic", is_silent=False, force_fresh=False):
        text = CLEAN_BRACKETS_REGEX.sub('', text).strip()
        text = MULTI_SPACE_REGEX.sub(' ', text)
        if not text: return 
        
        chat_post_text = text[:197] + "..." if len(text) > 200 else text
        
        if not is_silent:
            self.emit('chat_preview', self.channel_name or "Me", chat_post_text)
            print(f"[{self.channel_name}]: {text}")
            try:
                self.history_resp_queue.put([{'author': self.channel_name or "Me", 'message': chat_post_text, 'publishedAt': datetime.now().isoformat(), 'type': 'bot'}])
                self.chat_memory.append({'role': 'assistant', 'content': text})
                if len(self.chat_memory) > 20: self.chat_memory.pop(0) 
            except: pass
        
        # --- THE ULTIMATE OUTPUT ROUTER ---
        should_speak = False
        should_text = False

        if force_voice_only and force_text_only:
            should_speak, should_text = True, True
            print("[Router] Mode: Voice + Text (Forced)")
        elif force_voice_only:
            should_speak, should_text = True, False
            print("[Router] Mode: Voice Only (Forced)")
        elif force_text_only:
            should_speak, should_text = False, True
            print("[Router] Mode: Text Only (Forced)")
        else:
            should_speak = self.current_bot_mode in [2, 3]
            should_text = self.current_bot_mode in [1, 3]
            print(f"[Router] Mode: Global Fallback (Bot Mode: {self.current_bot_mode}) -> Speak:{should_speak}, Text:{should_text}")

        # 1. Handle Text  (queued to ONE sender thread that owns its own API client - sharing the poller's
        #    httplib2 client across threads is not thread-safe and caused random crashes / lost messages)
        if should_text and not is_silent:
            self._ensure_chat_sender()
            self.chat_send_queue.put((youtube_service, live_chat_id, chat_post_text))

        # 2. Handle Audio
        if not should_speak:
            print("[Audio Engine] 🔇 Skipped audio (Text-Only Mode active).")
        else:
            try:
                tts_text = ' '.join([w.lower() if w.isupper() else w for w in MULTI_DOT_REGEX.sub('.', text).split()])
                target_tts_mode = override_tts_mode if override_tts_mode else self.tts_mode
                
                if override_voice: 
                    voice = override_voice
                else: 
                    voice = getattr(self, 'elevenlabs_voice', '21m00Tcm4TlvDq8ikWAM') if self.tts_mode == "elevenlabs" else getattr(self, 'voicebox_voice', 'default') if self.tts_mode == "voicebox" else getattr(self, 'tiktok_voice', 'en_us_002') if self.tts_mode == "tiktok" else getattr(self, 'bark_voice', 'v2/en_speaker_6') if "bark" in self.tts_mode else getattr(self, 'kokoro_voice', 'am_adam') if self.tts_mode == "kokoro" else getattr(self, 'soprano_voice', 'Default (Female)') if self.tts_mode == "soprano" else getattr(self, 'edge_voice', 'en-US-SteffanNeural') if self.tts_mode == "edge" else getattr(self, 'google_voice', 'en-US-Wavenet-I')
                
                vol = 0.0 if is_silent else self.volume_level
                
                # Push safely to the background audio queue
                self.tts_queue.put((tts_text, target_tts_mode, voice, vol, self.speaker_device_index, force_cache_file, category, self.game_name, self.channel_name, force_fresh))
            except Exception as e: 
                print(f"[TTS Router Error] {e}")

    def play_bark_tts_audio(self, text, device_id=None, force_cpu=False, voice_preset="v2/en_speaker_6", volume=1.0, force_cache_file=None, category="generic", *args, **kwargs):
        """Generates, CACHES, and plays TTS using Bark AI."""
        import os
        import sounddevice as sd
        import hashlib
        import re
        import numpy as np
        from scipy.io.wavfile import write as write_wav

        # Read the toggles that your controller set
        use_small = getattr(self, 'bark_use_small_models', False)
        
        try:
            from bark import SAMPLE_RATE, generate_audio, preload_models
            print(f"[TTS] Preloading Bark models (Small: {use_small})...")
            preload_models()
            
            print(f"[TTS] Generating Bark audio for: '{text[:30]}...'")
            audio_array = generate_audio(text, history_prompt=voice_preset)
            
            # --- 💾 NEW CACHING LOGIC ---
            chan_safe = re.sub(r'[^a-zA-Z0-9_\- ]', '', str(getattr(self, 'channel_name', 'Default'))).strip().replace(" ", "_")
            game_safe = re.sub(r'[^a-zA-Z0-9_\- ]', '', str(getattr(self, 'game_name', 'General'))).strip().replace(" ", "_")
            voice_safe = re.sub(r'[^a-zA-Z0-9_\-]', '', str(voice_preset).replace('/', '').replace('\\', ''))
            cat_safe = re.sub(r'[^a-zA-Z0-9_\-]', '', str(category))

            # Build the exact folder structure from your screenshot
            save_dir = os.path.join("bark_cache", chan_safe, game_safe, voice_safe, cat_safe)
            os.makedirs(save_dir, exist_ok=True)

            # Create a unique filename so it never overwrites old audio
            text_hash = hashlib.md5(text.encode('utf-8')).hexdigest()
            base_filename = f"{cat_safe}_{text_hash}"
            wav_path = os.path.join(save_dir, f"{base_filename}.wav")
            txt_path = os.path.join(save_dir, f"{base_filename}.txt")

            # Save the WAV and TXT files directly to your PC
            write_wav(wav_path, SAMPLE_RATE, np.int16(audio_array * 32767))
            with open(txt_path, "w", encoding="utf-8") as f:
                f.write(text)
            print(f"[Cache] ✅ Successfully saved new Bark audio to: {wav_path}")

            # Play the audio live
            sd.play(audio_array, SAMPLE_RATE)
            sd.wait()
            
        except Exception as e:
            print(f"[Bark TTS Error] Failed to generate or cache audio: {e}")