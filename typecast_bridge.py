import os
import time
import threading
import queue
import subprocess
from playwright.sync_api import sync_playwright

class TypecastGhostEngine:
    _instance = None

    def __new__(cls):
        if cls._instance is None:
            cls._instance = super(TypecastGhostEngine, cls).__new__(cls)
            cls._instance.profile_dir = os.path.abspath("typecast_ghost_profile")
            cls._instance.human_user_agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            cls._instance.url_file = os.path.abspath("typecast_last_url.txt")
            cls._instance.task_queue = queue.Queue()
            
            # Start the browser ONE TIME in the background when the app launches
            cls._instance._start_background_worker()
        return cls._instance

    def _kill_zombie_browsers(self):
        try:
            if os.name == 'nt':
                subprocess.run("taskkill /F /IM chrome.exe /FI \"WINDOWTITLE eq about:blank*\"", shell=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        except: pass

    def _start_background_worker(self):
        def worker():
            while True: # 🔄 Infinite loop ensures it ALWAYS restarts if it crashes
                self._kill_zombie_browsers()
                print("[Typecast Ghost] 🚀 Booting persistent background engine...")
                try:
                    with sync_playwright() as p:
                        browser = p.chromium.launch_persistent_context(
                            user_data_dir=self.profile_dir,
                            headless=True, # 🛡️ True Headless: Never steals game focus!
                            ignore_default_args=["--mute-audio"], # 🛡️ Forces audio to play while hidden
                            user_agent=self.human_user_agent,
                            args=[
                                "--disable-blink-features=AutomationControlled",
                                "--autoplay-policy=no-user-gesture-required",
                                "--disable-features=AudioServiceOutOfProcess" # 🛡️ Helps prevent audio clipping in headless
                            ]
                        )
                        page = browser.new_page()
                        
                        target_url = "https://studio.typecast.ai/text-to-speech"
                        if os.path.exists(self.url_file):
                            with open(self.url_file, "r", encoding="utf-8") as f:
                                saved_url = f.read().strip()
                                if saved_url: target_url = saved_url
                        
                        page.goto(target_url, wait_until="domcontentloaded", timeout=60000)
                        
                        if page.url.rstrip('/').endswith("text-to-speech"):
                            try: page.get_by_text("StreamerBot", exact=True).first.click(timeout=5000)
                            except: pass
                            
                        editor = page.locator('.ProseMirror, [contenteditable="true"], [data-slate-editor="true"]').first
                        editor.wait_for(state="visible", timeout=30000)
                        
                        print("[Typecast Ghost] 🟢 Background engine is READY! Audio will now play instantly.")
                        
                        # ⚡ INFINITE LOOP: Waits for hotkey text to arrive, then plays instantly
                        while True:
                            text = self.task_queue.get()
                            if text is None: break
                            
                            try:
                                editor.click(force=True)
                                page.keyboard.press("Control+A")
                                page.wait_for_timeout(50)
                                page.keyboard.press("Backspace") # 🧹 Ensure the box is actually cleared
                                page.keyboard.insert_text(text)
                                page.wait_for_timeout(300) # ⏳ Increased buffer (300ms) so React UI state can catch up
                                
                                page.keyboard.press("Control+Enter")
                                print(f"[Typecast Ghost] ⚡ INSTANT PLAY: '{text[:40]}...'")
                            except Exception as e:
                                print(f"[Typecast Macro Error] {e}")
                            
                            self.task_queue.task_done()
                            
                except Exception as e:
                    print(f"\n[Typecast Background Error] {e}")
                    if "Timeout" in str(e):
                        print("⚠️ Tip: The editor took too long to load. Are you logged in? If not, please run setup_login() again!")
                    print("[Typecast Ghost] 🔄 Restarting engine in 5 seconds...\n")
                    time.sleep(5) # Give it a moment before trying to boot up again
                    
        threading.Thread(target=worker, daemon=True).start()

    def setup_login(self):
        print("\n[Typecast Ghost] 🌐 Opening visible browser for initial setup...")
        try:
            with sync_playwright() as p:
                browser = p.chromium.launch_persistent_context(
                    user_data_dir=self.profile_dir,
                    headless=False,
                    user_agent=self.human_user_agent,
                    args=["--disable-blink-features=AutomationControlled"]
                )
                page = browser.new_page()
                page.goto("https://studio.typecast.ai/text-to-speech")
                
                print("=========================================================")
                print("[Typecast Ghost] ⚠️ ACTION REQUIRED:")
                print("1. Log in to Typecast.")
                print("2. Open your StreamerBot project editor.")
                print("3. Once the editor is fully visible, CLOSE the browser window.")
                print("=========================================================")
                
                try:
                    while not page.is_closed():
                        current_url = page.url
                        if "/text-to-speech/" in current_url and not current_url.endswith("text-to-speech"):
                            with open(self.url_file, "w", encoding="utf-8") as f:
                                f.write(current_url)
                        page.wait_for_timeout(1000)
                except: pass
                try: browser.close()
                except: pass
                print("[Typecast Ghost] ✅ Setup complete. IMPORTANT: Restart the StreamerBot so the background worker can boot up!")
        except Exception as e:
            print(f"[Typecast Ghost Setup Error] {e} (If it says 'timeout', make sure no hidden chrome instances are running!)")

    def play_ghost_audio(self, text):
        # ⚡ Just toss the text into the queue. The background worker will pick it up instantly.
        self.task_queue.put(text)