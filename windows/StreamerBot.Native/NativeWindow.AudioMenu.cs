using Microsoft.UI.Xaml.Controls;
using StreamerBot.Native.Core;

namespace StreamerBot.Native;

internal sealed partial class NativeWindow
{
    private void BuildOriginalVoiceMenu(MenuBarItem audio)
    {
        foreach (var (title, key, capture) in new[] { ("Mic Input", "native_microphone_id", true), ("Speaker Output", "native_speaker_id", false) })
        {
            var menu = new MenuFlyoutSubItem { Text = title }; audio.Items.Add(menu);
            var refresh = new MenuFlyoutItem { Text = "Refresh devices" };
            refresh.Click += (_, _) => Run(async () =>
            {
                var selector = capture ? Windows.Media.Devices.MediaDevice.GetAudioCaptureSelector() : Windows.Media.Devices.MediaDevice.GetAudioRenderSelector();
                var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(selector);
                while (menu.Items.Count > 1) menu.Items.RemoveAt(1);
                foreach (var (id, name) in new[] { ("", "Windows default") }.Concat(devices.Select(d => (d.Id, d.Name))))
                {
                    var choice = new ToggleMenuFlyoutItem { Text = name, IsChecked = _profile.Text(key) == id };
                    choice.Click += (_, _) =>
                    {
                        _profile.Set(key, id); _profile.Save();
                        foreach (var item in menu.Items.OfType<ToggleMenuFlyoutItem>()) item.IsChecked = ReferenceEquals(item, choice);
                    };
                    menu.Items.Add(choice);
                }
            });
            menu.Items.Add(refresh);
        }
        void Setup(string title, params string[] keys)
        {
            var item = new MenuFlyoutItem { Text = title }; audio.Items.Add(item);
            item.Click += (_, _) => Run(async () =>
            {
                var panel = new StackPanel { Spacing = 8 };
                var fields = keys.Select(key => (Key: key, Input: new TextBox { Header = key.Replace('_', ' '), Text = _profile.Text(key) })).ToArray();
                foreach (var field in fields) panel.Children.Add(field.Input);
                if (await new ContentDialog { XamlRoot = _root.XamlRoot, Title = title, Content = panel, PrimaryButtonText = "Save", CloseButtonText = "Cancel" }.ShowAsync() != ContentDialogResult.Primary) return;
                foreach (var field in fields) _profile.Set(field.Key, field.Input.Text.Trim());
                _profile.Save(); await ReloadVoiceAsync();
            });
        }
        Setup("Configure Voicebox (Pinokio API)", "voicebox_url", "voicebox_voice");
        Setup("Fish Speech Setup (.wav cloning)", "fish_audio_path", "fish_text_path");
        Setup("RVC Setup (.pth voice cloning)", "rvc_model_path", "rvc_index_path");
        var voices = new MenuFlyoutSubItem { Text = "Voice Selection" }; audio.Items.Add(voices);
        MenuFlyoutSubItem Group(MenuFlyoutSubItem parent, string name)
        { var menu = new MenuFlyoutSubItem { Text = name }; parent.Items.Add(menu); return menu; }
        void Choices(MenuFlyoutSubItem menu, string engine, string key, (string Id, string Name)[] choices)
        {
            foreach (var (id, name) in choices)
            {
                var item = new ToggleMenuFlyoutItem { Text = name };
                void Refresh() => item.IsChecked = _profile.Text(key) == id;
                Refresh(); _menuRefresh.Add(Refresh);
                item.Click += (_, _) => Run(async () =>
                {
                    _profile.Set(key, id); _profile.Set("tts_mode", engine); _voices.SelectedItem = engine;
                    _profile.Save(); await ReloadVoiceAsync();
                    foreach (var refresh in _menuRefresh) refresh();
                    WriteLog("Voice selected: " + name + " (" + engine + ").");
                });
                menu.Items.Add(item);
            }
        }
        var tiktok = Group(voices, "TikTok Voices");
        Choices(Group(tiktok, "English (US/UK/AU)"), "tiktok", "tiktok_voice", [("en_us_001", "US Female 1"), ("en_us_002", "US Female 2 (Jessie)"), ("en_us_006", "US Male 1"), ("en_us_007", "US Male 2"), ("en_us_009", "US Male 3"), ("en_us_010", "US Male 4"), ("en_uk_001", "UK Male 1"), ("en_uk_003", "UK Male 2"), ("en_au_001", "AU Female"), ("en_au_002", "AU Male")]);
        Choices(Group(tiktok, "Characters (Disney/Movies)"), "tiktok", "tiktok_voice", [("en_us_ghostface", "Ghostface (Scream)"), ("en_us_chewbacca", "Chewbacca"), ("en_us_c3po", "C-3PO"), ("en_us_stitch", "Stitch"), ("en_us_stormtrooper", "Stormtrooper"), ("en_us_rocket", "Rocket Raccoon"), ("en_us_deadpool", "Deadpool"), ("blackmythwukong-male", "Wukong (Black Myth)")]);
        Choices(Group(tiktok, "Singing & Special"), "tiktok", "tiktok_voice", [("en_male_narration", "Narrator"), ("en_male_funny", "Funny / Wacky"), ("en_female_emotional", "Peaceful / Emotional"), ("en_female_f08_salut_damour", "Singing (Alto)"), ("en_male_m03_lobby", "Singing (Tenor)"), ("en_female_f08_warmy_breeze", "Singing (Warm Breeze)"), ("en_male_m03_sunshine_soon", "Singing (Sunshine Soon)")]);
        Choices(Group(tiktok, "International"), "tiktok", "tiktok_voice", [("fr_001", "French Male 1"), ("de_001", "German Female"), ("es_002", "Spanish Male"), ("es_mx_002", "Spanish MX Male"), ("br_005", "Portuguese BR Male"), ("id_001", "Indonesian / Pinoy Accent (Female)"), ("jp_001", "Japanese Female 1"), ("jp_006", "Japanese Male"), ("kr_003", "Korean Female"), ("kr_002", "Korean Male")]);
        Choices(Group(voices, "Kokoro Voices"), "kokoro", "kokoro_voice", [("am_adam", "Gamer Guy (Adam)"), ("am_michael", "Pro Streamer (Michael)"), ("am_onyx", "Realistic Deep (Onyx)"), ("am_puck", "Energetic (Puck)"), ("am_fenrir", "Deep Voice (Fenrir)"), ("af_bella", "Streamer Girl (Bella)"), ("af_sarah", "Calm Girl (Sarah)"), ("af_nicole", "Standard (Nicole)"), ("af_sky", "Soft (Sky)"), ("af_jessica", "Realistic (Jessica)")]);
        Choices(Group(voices, "Edge TTS Voices"), "edge", "edge_voice", [("en-US-SteffanNeural", "en-US-SteffanNeural"), ("en-US-GuyNeural", "en-US-GuyNeural"), ("en-US-ChristopherNeural", "en-US-ChristopherNeural"), ("en-US-AriaNeural", "en-US-AriaNeural"), ("en-US-JennyNeural", "en-US-JennyNeural"), ("en-US-MichelleNeural", "en-US-MichelleNeural"), ("en-PH-JamesNeural", "en-PH-JamesNeural"), ("en-PH-RosaNeural", "en-PH-RosaNeural"), ("en-GB-RyanNeural", "en-GB-RyanNeural"), ("en-GB-SoniaNeural", "en-GB-SoniaNeural"), ("en-US-AnaNeural", "en-US-AnaNeural"), ("tl-PH-AngeloNeural", "tl-PH-AngeloNeural"), ("tl-PH-BlessicaNeural", "tl-PH-BlessicaNeural")]);
        Choices(Group(voices, "Google TTS Voices"), "google", "google_voice", [("en-US-Wavenet-A", "en-US-Wavenet-A"), ("en-US-Wavenet-B", "en-US-Wavenet-B"), ("en-US-Wavenet-C", "en-US-Wavenet-C"), ("en-US-Wavenet-D", "en-US-Wavenet-D"), ("en-US-Wavenet-E", "en-US-Wavenet-E"), ("en-US-Wavenet-F", "en-US-Wavenet-F"), ("en-US-Wavenet-I", "en-US-Wavenet-I"), ("en-US-Wavenet-J", "en-US-Wavenet-J"), ("fil-PH-Wavenet-A", "fil-PH-Wavenet-A"), ("fil-PH-Wavenet-B", "fil-PH-Wavenet-B"), ("fil-PH-Wavenet-C", "fil-PH-Wavenet-C"), ("fil-PH-Wavenet-D", "fil-PH-Wavenet-D")]);
        var bark = Group(voices, "Bark AI Voices");
        foreach (var (key, name) in new[] { ("bark_use_cpu", "Use CPU (Slower, Saves VRAM)"), ("bark_small_models", "Use Small Models (Faster)"), ("bark_low_vram", "1GB VRAM (Aggressive Offload)") })
        {
            var toggle = new ToggleMenuFlyoutItem { Text = name, IsChecked = _profile.Flag(key) };
            _menuRefresh.Add(() => toggle.IsChecked = _profile.Flag(key));
            toggle.Click += (_, _) => Run(async () => { _profile.Set(key, toggle.IsChecked); _profile.Save(); await ReloadVoiceAsync(); });
            bark.Items.Add(toggle);
        }
        var gpu = new MenuFlyoutItem { Text = "Check GPU status" }; gpu.Click += (_, _) => Run(GpuAsync); bark.Items.Add(gpu);
        bark.Items.Add(new MenuFlyoutSeparator());
        Choices(bark, "bark", "bark_voice", Enumerable.Range(0, 10).Select(i => ($"v2/en_speaker_{i}", $"v2/en_speaker_{i}")).ToArray());
        foreach (var (title, engine, key) in new[] { ("ElevenLabs Voice ID", "elevenlabs", "elevenlabs_voice"), ("Voicebox Voice ID", "voicebox", "voicebox_voice"), ("Custom TikTok Voice ID", "tiktok", "tiktok_voice") })
        {
            var item = new MenuFlyoutItem { Text = title };
            item.Click += (_, _) => Run(async () =>
            {
                var input = new TextBox { Header = title, Text = _profile.Text(key) };
                if (await new ContentDialog { XamlRoot = _root.XamlRoot, Title = title, Content = input, PrimaryButtonText = "Use voice", CloseButtonText = "Cancel" }.ShowAsync() != ContentDialogResult.Primary) return;
                if (string.IsNullOrWhiteSpace(input.Text)) throw new InvalidDataException("Enter a voice ID.");
                _profile.Set(key, input.Text.Trim()); _profile.Set("tts_mode", engine); _voices.SelectedItem = engine; _profile.Save(); await ReloadVoiceAsync();
            });
            voices.Items.Add(item);
        }
    }
}
