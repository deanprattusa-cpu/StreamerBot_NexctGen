import os
import time
import threading
import queue
import subprocess
from playwright.sync_api import sync_playwright

try:
    from playwright_stealth import Stealth
    HAS_STEALTH = True
except ImportError:
    print("CRITICAL: You must run 'pip install playwright-stealth' for ElevenLabs Ghost to work!")
    HAS_STEALTH = False

class ElevenLabsGhostEngine:
    _instance = None

    def __new__(cls):
        if cls._instance is None:
            cls._instance = super(ElevenLabsGhostEngine, cls).__new__(cls)
            cls._instance.profile_dir = os.path.abspath("elevenlabs_ghost_profile")
            cls._instance.human_user_agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            cls._instance.task_queue = queue.Queue()
            cls._instance._start_background_worker()
        return cls._instance

    def _kill_zombie_browsers(self):
        try:
            if os.name == 'nt':
                subprocess.run("taskkill /F /IM chrome.exe /FI \"WINDOWTITLE eq about:blank*\"", shell=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        except: pass

    def _start_background_worker(self):
        def worker():
            while True:
                self._kill_zombie_browsers()
                print("[ElevenLabs Ghost] 🚀 Booting stealth background engine...")
                try:
                    # 🛡️ THE NEW V2.0 STEALTH API
                    stealth_context = Stealth().use_sync(sync_playwright()) if HAS_STEALTH else sync_playwright()
                    
                    with stealth_context as p:
                        browser = p.chromium.launch_persistent_context(
                            user_data_dir=self.profile_dir,
                            headless=True,
                            ignore_default_args=["--mute-audio"],
                            user_agent=self.human_user_agent,
                            args=[
                                "--disable-blink-features=AutomationControlled",
                                "--autoplay-policy=no-user-gesture-required",
                                "--disable-features=AudioServiceOutOfProcess"
                            ]
                        )
                        page = browser.new_page()
                        
                        page.goto("https://elevenlabs.io", wait_until="domcontentloaded", timeout=60000)
                        
                        # Wait for the main free text area to load
                        page.wait_for_timeout(3000)
                        editor = page.locator('textarea').first
                        editor.wait_for(state="visible", timeout=30000)
                        
                        print("[ElevenLabs Ghost] 🟢 Stealth engine is READY! Audio will play instantly.")
                        
                        while True:
                            text = self.task_queue.get()
                            if text is None: break
                            
                            try:
                                editor.click(force=True)
                                page.keyboard.press("Control+A")
                                page.wait_for_timeout(50)
                                page.keyboard.press("Backspace")
                                page.wait_for_timeout(50)
                                
                                # ElevenLabs free homepage limit is 333 chars
                                page.keyboard.insert_text(text[:333]) 
                                page.wait_for_timeout(300)
                                
                                # Try clicking the Generate/Play button
                                play_button = page.locator('button', has_text="Generate").first
                                if not play_button.is_visible():
                                    play_button = page.locator('button[aria-label="Play"]').first
                                    
                                if play_button.is_visible():
                                    play_button.click(force=True)
                                else:
                                    page.keyboard.press("Control+Enter")
                                
                                print(f"[ElevenLabs Ghost] ⚡ INSTANT PLAY: '{text[:40]}...'")
                                
                                page.wait_for_timeout(2000)
                                
                                if page.locator('text="unusual activity"').is_visible() or page.locator('text="quota"').is_visible():
                                    print("[ElevenLabs Ghost] 🛑 IP Rate Limit Hit! You've used up the free tries for your WiFi network.")
                                    
                            except Exception as e:
                                print(f"[ElevenLabs Ghost Error] {e}")
                            
                            self.task_queue.task_done()
                            
                except Exception as e:
                    print(f"\n[ElevenLabs Background Error] {e}")
                    print("[ElevenLabs Ghost] 🔄 Restarting engine in 5 seconds...\n")
                    time.sleep(5)
        threading.Thread(target=worker, daemon=True).start()

    def setup_login(self):
        print("\n[ElevenLabs Ghost] 🌐 Opening visible browser for initial setup/captcha bypass...")
        try:
            stealth_context = Stealth().use_sync(sync_playwright()) if HAS_STEALTH else sync_playwright()
            
            with stealth_context as p:
                browser = p.chromium.launch_persistent_context(
                    user_data_dir=self.profile_dir,
                    headless=False,
                    user_agent=self.human_user_agent,
                    args=["--disable-blink-features=AutomationControlled"]
                )
                page = browser.new_page()
                page.goto("https://elevenlabs.io")
                
                print("=========================================================")
                print("[ElevenLabs Ghost] ⚠️ ACTION REQUIRED:")
                print("1. Clear any Cookie popups or Captchas.")
                print("2. Set up the Voice you want to use in the Homepage Demo.")
                print("3. Once the editor is fully visible and working, CLOSE the browser window.")
                print("=========================================================")
                
                try:
                    while not page.is_closed():
                        page.wait_for_timeout(1000)
                except: pass
                try: browser.close()
                except: pass
                print("[ElevenLabs Ghost] ✅ Setup complete. Restart StreamerBot to boot the worker!")
        except Exception as e:
            print(f"[ElevenLabs Ghost Setup Error] {e}")

    def play_ghost_audio(self, text):
        self.task_queue.put(text)