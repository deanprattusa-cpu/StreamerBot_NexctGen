"""bark_fast: make Bark *feel* fast without changing the voice.

Bark is autoregressive, so a full line can never be truly instant. What this does:
  1. Loads the models ONCE and runs a warm-up line, so the first real line is not the slow one.
  2. Speaks sentence-by-sentence: audio starts when the FIRST (short) chunk is ready while the
     GPU is already generating the next chunk. Time-to-first-audio becomes ~1 short sentence.
  3. Uses KV-caching for the text->semantic stage and silent mode (less overhead).
  4. Hands you the full audio at the end so your existing cache code can save it.
Same voice presets (v2/en_speaker_N), same quality per chunk.

LOW-VRAM / GPU HANDLING (added):
  * engine_for(model, voice) reads ALL the UI toggles (Use CPU, Small Models, 1GB VRAM) from the model object,
    so the Bark menu really controls generation (1GB VRAM = small models + offload to CPU between stages).
  * gpu_report() explains exactly why Bark says "No GPU being used": that warning means PyTorch itself cannot
    see a CUDA GPU (usually a CPU-only PyTorch build), which no Bark setting can fix. It prints the fix.
  * Small cards are detected automatically and switched to small models / offload so they do not run out of memory.
"""
import os
import queue
import re
import threading
import time

import numpy as np

SAMPLE_RATE = 24000
_DONE = object()
_engines = {}
_engines_lock = threading.Lock()


def split_text(text, max_words=22, first_max_words=9):
    """Split into speakable chunks. The first chunk is kept short so audio starts sooner."""
    text = re.sub(r"\s+", " ", (text or "").strip())
    if not text:
        return []
    sentences = [s.strip() for s in re.split(r"(?<=[.!?])\s+", text) if s.strip()]
    # glue tiny fragments ("Oh." "No.") onto the next sentence so Bark gets enough context
    merged = []
    for s in sentences:
        if merged and len(merged[-1].split()) < 3:
            merged[-1] = merged[-1] + " " + s
        else:
            merged.append(s)
    # long sentences: break at commas/dashes near max_words
    out = []
    for s in merged:
        words = s.split()
        while len(words) > max_words:
            cut = max_words
            for i in range(max_words, max(4, max_words // 2), -1):
                if words[i - 1].endswith((",", ";", ":", "-", "...")):
                    cut = i
                    break
            out.append(" ".join(words[:cut]))
            words = words[cut:]
        if words:
            out.append(" ".join(words))
    # first chunk: split at the first comma if that makes it start sooner
    if out:
        w = out[0].split()
        if len(w) > first_max_words:
            for i in range(3, min(len(w), first_max_words + 3)):
                if w[i - 1].endswith((",", ";", "-", "...")):
                    out[0:1] = [" ".join(w[:i]), " ".join(w[i:])]
                    break
    return out


def gpu_report(print_it=False):
    """Why is (or isn't) Bark using the GPU? Returns {"cuda": bool, "message": str, ...}.
    Bark's "No GPU being used" warning is printed when torch.cuda.device_count() == 0, i.e. PyTorch cannot see CUDA."""
    info = {"cuda": False, "torch": None, "cuda_build": None, "device": None, "vram_gb": None, "message": ""}
    visible = os.environ.get("CUDA_VISIBLE_DEVICES")
    try:
        import torch
    except Exception as e:
        info["message"] = f"PyTorch is not installed or failed to import ({e}). Bark cannot run until it is."
        if print_it: print("[BarkFast] " + info["message"])
        return info
    info["torch"], info["cuda_build"] = torch.__version__, getattr(torch.version, "cuda", None)
    try:
        n = torch.cuda.device_count()
    except Exception:
        n = 0
    if n > 0 and torch.cuda.is_available():
        info["cuda"] = True
        try:
            p = torch.cuda.get_device_properties(0)
            info["device"], info["vram_gb"] = p.name, round(p.total_memory / 1024 ** 3, 1)
        except Exception:
            pass
        info["message"] = (f"GPU OK: {info['device']} ({info['vram_gb']} GB VRAM), torch {info['torch']} + CUDA {info['cuda_build']}.")
    else:
        fix = ("pip uninstall -y torch torchvision torchaudio  then  "
               "pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu126  "
               "(pick the CUDA version for your driver at pytorch.org/get-started/locally)")
        if visible is not None and visible.strip() in ("", "-1"):
            why = f"CUDA_VISIBLE_DEVICES is set to '{visible}', which hides the GPU from PyTorch. Remove that variable."
        elif not info["cuda_build"]:
            why = (f"your PyTorch ({info['torch']}) is a CPU-ONLY build, so it cannot use the GPU no matter what Bark "
                   f"options you pick. Fix: {fix}")
        else:
            why = (f"PyTorch {info['torch']} was built for CUDA {info['cuda_build']} but cannot reach a GPU. "
                   "Update the NVIDIA driver (run nvidia-smi to check it works), then restart.")
        info["message"] = "NO GPU visible to PyTorch: " + why
    if print_it:
        print("[BarkFast] " + info["message"])
    return info


def _flags(model):
    """(use_small, offload) from every spelling of the UI toggles."""
    low = bool(getattr(model, "bark_low_vram", False))
    small = bool(low or getattr(model, "bark_use_small_models", False) or getattr(model, "bark_small_models", False))
    offload = bool(low or getattr(model, "bark_use_cpu", False))     # menu "Use CPU (Slower, Saves VRAM)" == offload
    return small, offload


def engine_for(model, voice=None):
    """The resident FastBark for the model's current Bark menu settings."""
    voice = voice or getattr(model, "bark_voice", None) or getattr(model, "bark_speaker", None) or "v2/en_speaker_6"
    small, offload = _flags(model)
    return get_engine(voice, small, offload)


def _set_flag_env(name, on):
    """Set only when ON. Older Bark builds treat ANY non-empty string (even "False" / "0") as true."""
    if on: os.environ[name] = "True"
    else: os.environ.pop(name, None)


def _reset_bark_models():
    """Free every loaded Bark model (used when the Small / 1GB / CPU settings change so VRAM is not hoarded)."""
    try:
        import bark.generation as _bg
        try: _bg.clean_models()
        except Exception:
            try: _bg.models.clear()
            except Exception: pass
        if hasattr(_bg, "has_been_preloaded"): _bg.has_been_preloaded = False
    except Exception:
        pass
    try:
        import gc, torch
        gc.collect()
        if torch.cuda.is_available(): torch.cuda.empty_cache()
    except Exception:
        pass


# ---- where are the Bark model files? ------------------------------------------------------------
_BARK_FILES = ("text_2.pt", "text.pt", "coarse_2.pt", "coarse.pt", "fine_2.pt", "fine.pt")


def find_bark_models(extra_roots=(), max_depth=5):
    """Look for the folder that already holds the downloaded Bark .pt files (e.g. D:\\BARKAIMODELS\\BarkCache\\suno\\bark_v0).
    Searches SB_MODEL_ROOT, global_model_path from bot_config.json, D:\\BARKAIMODELS, XDG_CACHE_HOME and ~/.cache.
    Returns (folder or None, [roots searched])."""
    roots = []
    for r in list(extra_roots) + [os.environ.get("SB_MODEL_ROOT"), os.environ.get("XDG_CACHE_HOME"),
                                  r"D:\BARKAIMODELS", os.path.join(os.path.expanduser("~"), ".cache")]:
        if r and os.path.isdir(r) and r not in roots:
            roots.append(r)
    try:                                           # the path chosen inside the app
        import json
        with open("bot_config.json", "r") as f:
            p = json.load(f).get("global_model_path", "")
        if p and os.path.isdir(p) and p not in roots:
            roots.insert(0, p)
    except Exception:
        pass
    best, best_n = None, 0
    for root in roots:
        base = root.rstrip("\\/").count(os.sep)
        for dp, dn, fn in os.walk(root):
            if dp.count(os.sep) - base >= max_depth:
                dn[:] = []
            n = sum(1 for f in fn if f in _BARK_FILES)
            if n > best_n:
                best, best_n = dp, n
        if best:
            break
    return best, roots


def use_existing_models(bg=None):
    """Point Bark at the folder that already has the models so it never re-downloads or loads from the wrong place."""
    found, roots = find_bark_models()
    if found:
        try:
            if bg is None:
                import bark.generation as bg
            bg.CACHE_DIR = found
        except Exception as e:
            print(f"[BarkFast] could not set model folder: {e}")
        have = sorted(f for f in os.listdir(found) if f in _BARK_FILES)
        print(f"[BarkFast] using existing Bark models in: {found}  ({', '.join(have)})")
    else:
        print(f"[BarkFast] no Bark model files found in {roots or 'any known folder'}; Bark will download them "
              f"into its default cache (set SB_MODEL_ROOT to your models folder to change that).")
    return found


class FastBark:
    def __init__(self, voice="v2/en_speaker_6", use_small=False, offload=False, gap_seconds=0.06,
                 text_temp=0.7, waveform_temp=0.7):
        self.voice, self.use_small, self.offload = voice, bool(use_small), bool(offload)
        self.low_vram_forced = False
        self.gap = np.zeros(int(SAMPLE_RATE * gap_seconds), dtype=np.float32)
        self.text_temp, self.waveform_temp = text_temp, waveform_temp
        self.ready = False
        self._load_lock = threading.Lock()
        self._gen_lock = threading.Lock()       # Bark is not thread-safe: one generation at a time
        self.last_ttfa = None

    # ---- model loading ---------------------------------------------------
    def ensure_loaded(self):
        with self._load_lock:
            if self.ready:
                return
            rep = gpu_report(print_it=True)
            # Small cards: protect against out-of-memory automatically (an explicit 1GB / Small choice already did this).
            if rep["cuda"] and rep["vram_gb"]:
                if rep["vram_gb"] <= 6 and not self.use_small:
                    self.use_small = True
                    print(f"[BarkFast] {rep['vram_gb']} GB card: switching to SMALL models automatically.")
                if rep["vram_gb"] <= 3 and not self.offload:
                    self.offload = True
                    print(f"[BarkFast] {rep['vram_gb']} GB card: enabling CPU offload between stages automatically.")
            # Bark reads these flags when it is first imported, so set them before the import...
            _set_flag_env("SUNO_USE_SMALL_MODELS", self.use_small)
            _set_flag_env("SUNO_OFFLOAD_CPU", self.offload)
            from bark import preload_models
            # ...and also patch the module flags in case Bark was imported earlier (e.g. by another code path)
            try:
                import bark.generation as _bg
                use_existing_models(_bg)
                _bg.USE_SMALL_MODELS = self.use_small
                _bg.OFFLOAD_CPU = self.offload
            except Exception:
                pass
            t0 = time.time()
            preload_models(text_use_small=self.use_small, coarse_use_small=self.use_small, fine_use_small=self.use_small)
            try:                                  # warm-up: first generation compiles CUDA kernels
                self._gen_chunk("Okay, ready.")
            except Exception as e:
                print(f"[BarkFast] warm-up line failed: {e}")
            self.ready = True
            mode = "1GB mode" if (self.use_small and self.offload) else "small models" if self.use_small else "full models"
            print(f"[BarkFast] models loaded + warmed in {time.time() - t0:.1f}s ({mode}, "
                  f"{'GPU' if rep['cuda'] else 'CPU: slow'})")

    def warm_async(self):
        threading.Thread(target=self.ensure_loaded, daemon=True, name="bark-warm").start()

    # ---- generation ------------------------------------------------------
    def _gen_chunk(self, sentence):
        from bark.api import semantic_to_waveform
        from bark.generation import generate_text_semantic
        with self._gen_lock:
            kw = dict(history_prompt=self.voice, temp=self.text_temp, min_eos_p=0.05, silent=True)
            try:
                sem = generate_text_semantic(sentence, use_kv_caching=True, **kw)
            except TypeError:                     # older bark without the argument
                sem = generate_text_semantic(sentence, **kw)
            audio = semantic_to_waveform(sem, history_prompt=self.voice, temp=self.waveform_temp, silent=True)
        if self.offload:                          # low-VRAM mode: hand cached GPU memory back after every chunk
            try:
                import torch
                if torch.cuda.is_available(): torch.cuda.empty_cache()
            except Exception:
                pass
        return np.asarray(audio, dtype=np.float32)

    def synth(self, text):
        """Whole line -> one float32 array (no playback)."""
        return self.stream_play(text, volume=0.0)

    def stream_play(self, text, volume=1.0, play=None, on_first_audio=None):
        """Generate chunk by chunk, playing each as soon as it exists.
        volume <= 0 generates without playing. Returns the full audio (or None)."""
        self.ensure_loaded()
        chunks = split_text(text)
        if not chunks:
            return None
        q = queue.Queue(maxsize=2)                # keeps the GPU one chunk ahead of the speakers

        def producer():
            try:
                for c in chunks:
                    try:
                        q.put(self._gen_chunk(c))
                    except Exception as e:
                        print(f"[BarkFast] chunk failed ({c[:30]!r}): {e}")
                        q.put(None)
            finally:
                q.put(_DONE)

        started = time.time()
        threading.Thread(target=producer, daemon=True, name="bark-gen").start()
        play = play or self._default_play
        parts, first = [], True
        while True:
            a = q.get()
            if a is _DONE:
                break
            if a is None:
                continue
            parts.extend([a, self.gap])
            if volume > 0:
                if first:
                    self.last_ttfa = time.time() - started
                    if on_first_audio:
                        on_first_audio()
                    first = False
                play(np.clip(a * volume, -1.0, 1.0))
        return np.concatenate(parts) if parts else None

    @staticmethod
    def _default_play(arr):
        import sounddevice as sd
        sd.play(arr, SAMPLE_RATE)
        sd.wait()


_active_key = [None]


def get_engine(voice="v2/en_speaker_6", use_small=False, offload=False):
    """One resident engine per (voice, small, offload) so models stay warm between lines.
    When Small / offload change, the previous engine's models are freed first (no double VRAM use)."""
    key = (voice, bool(use_small), bool(offload))
    with _engines_lock:
        prev = _active_key[0]
        if prev is not None and prev[1:] != key[1:]:        # size/offload changed (a voice change reuses the loaded models)
            _engines.clear()
            _reset_bark_models()
        _active_key[0] = key
        if key not in _engines:
            _engines[key] = FastBark(voice, use_small, offload)
        return _engines[key]


def write_wav(path, audio, sr=SAMPLE_RATE):
    import wave
    pcm = (np.clip(audio, -1, 1) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr)
        w.writeframes(pcm.tobytes())
