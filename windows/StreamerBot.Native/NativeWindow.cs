using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StreamerBot.Native.Core;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace StreamerBot.Native;

internal sealed class NativeWindow : Window
{
    private readonly Profile _profile;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly AiClient _ai;
    private readonly StreamController _controller;
    private VoiceWorker _voice;
    private AvatarVideoWindow? _avatarVideo;
    private readonly NativeAudio _audio = new();
    private NativeHotkeys? _hotkeys;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Grid _root = new();
    private readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 130 };
    private readonly ListView _chat = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly List<ChatMessage> _messages = [];
    private readonly TextBox _manual = new() { PlaceholderText = "Reply, speak a line, or ask the AI…", AcceptsReturn = true, MinHeight = 65 };
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "V+T", "V", "T" }, SelectedIndex = 0, Width = 95 };
    private readonly ComboBox _voices = new() { ItemsSource = Catalog.Voices, Width = 170 };
    private readonly TextBox _game = new() { Width = 260, PlaceholderText = "Game" };
    private readonly ComboBox _persona = new() { ItemsSource = new[] { "Default (Casual)", "English", "Pinoy Gamer", "Custom" }, IsEditable = true, Width = 175 };
    private readonly TextBox _channel = new() { PlaceholderText = "Channel name", Width = 160 };
    private readonly TextBlock _status = new() { Text = "Ready", TextWrapping = TextWrapping.Wrap };
    private readonly TabView _tabs = new() { IsAddTabButtonVisible = false, CanReorderTabs = false, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly StackPanel _settingsRows = new() { Spacing = 14 };
    private readonly TextBox _search = new() { PlaceholderText = "Search every original setting…", Width = 300 };
    private readonly ComboBox _settingsGroup = new() { ItemsSource = new[] { "General", "AI", "Voice", "Hotkeys", "Music", "Overlays", "All" }, SelectedIndex = 0, Width = 150 };
    private int _settingsPage;
    private bool _closing;
    private bool _suspended;
    private MediaCapture? _recording;
    private StorageFile? _recordedFile;
    private OverlayWindow? _overlay;
    private MusicWindow? _music;
    private YouTubeOverlay? _youtubeOverlay;
    private CancellationTokenSource _playback = new();
    public NativeWindow()
    {
        Title = "StreamerBot Native — Preview";
        AppWindow.Resize(new SizeInt32(1400, 950));
        _profile = new Profile(Path.Combine(Program.DataRoot, "profile.json"));
        _ai = new AiClient(_http);
        _controller = new StreamController(_profile, _ai);
        _voice = CreateVoiceWorker();
        _controller.Log += WriteLog;
        _controller.Chat += message => DispatcherQueue.TryEnqueue(() => AddChat(message));
        _controller.Caption += text => DispatcherQueue.TryEnqueue(() => { _overlay?.SetCaption(text); });
        _controller.Speak += SpeakAsync;
        _controller.Review += ReviewAsync;
        _root.RequestedTheme = ElementTheme.Dark;
        _root.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 12, 15, 20));
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.Children.Add(BuildMenu());
        var toolbar = Row(new TextBlock { Text = "Channel", VerticalAlignment = VerticalAlignment.Center }, _channel, _persona, _game,
            Button("START", async () => { SaveToolbar(); var path = _profile.Text("native_youtube_token_path"); _controller.Start(path.Length > 0 ? new YouTubeClient(_http, path) : null, false); }),
            Button("SIMULATE", () => { SaveToolbar(); _controller.Start(null, true); return Task.CompletedTask; }), Button("STOP", StopAsync));
        Grid.SetRow(toolbar, 1); _root.Children.Add(toolbar);
        AddTab("Live chat", BuildChat()); AddTab("Settings", BuildSettings()); AddTab("Hotkeys", BuildHotkeys()); AddTab("Voice & devices", BuildVoice()); AddTab("Migration status", BuildStatus());
        Grid.SetRow(_tabs, 2); _root.Children.Add(_tabs);
        var footer = new Border { Padding = new Thickness(12, 6, 12, 6), Child = _status };
        Grid.SetRow(footer, 3); _root.Children.Add(footer);
        Content = _root;
        LoadToolbar();
        _root.Loaded += (_, _) => { if (!Program.SmokeTest) ApplyHotkeys(); WriteLog("Native interface ready. Import your original profile through File → Load Config. See Migration status for incomplete features.");
            if (Program.SmokeTest)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                timer.Tick += async (_, _) =>
                {
                    timer.Stop();
                    if (_root.ActualWidth > 0 && _root.ActualHeight > 0) File.WriteAllText(Path.Combine(Program.DataRoot, "smoke-ready.txt"), "Native WinUI window loaded and laid out.");
                    try
                    {
                        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(_root);
                        var buffer = await bitmap.GetPixelsAsync(); var bytes = new byte[buffer.Length]; using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer)) reader.ReadBytes(bytes);
                        var folder = await StorageFolder.GetFolderFromPathAsync(Program.DataRoot); var file = await folder.CreateFileAsync("native-preview.png", CreationCollisionOption.ReplaceExisting);
                        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
                        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
                        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes); await encoder.FlushAsync();
                    }
                    catch (Exception error) { File.WriteAllText(Path.Combine(Program.DataRoot, "screenshot-status.txt"), error.GetType().Name); }
                    await PathSettingsDialog.ShowAsync(this, _root.XamlRoot, _profile);
                    if (!File.Exists(Path.Combine(Program.DataRoot, "paths-ready.txt"))) throw new IOException("Native paths dialog did not lay out.");
                    var videoWindow = new AvatarVideoWindow(); videoWindow.Activate(); videoWindow.Close();
                    Close();
                };
                timer.Start();
            }
        };
        AppWindow.Closing += async (_, args) =>
        {
            if (_closing) return;
            args.Cancel = true; _closing = true;
            _lifetime.Cancel(); Interrupt(); _hotkeys?.Dispose();
            await _controller.DisposeAsync(); await _voice.DisposeAsync();
            _overlay?.Close(); _music?.Close(); _youtubeOverlay?.Close(); _avatarVideo?.Close();
            _recording?.Dispose(); _audio.Dispose(); _http.Dispose();
            try { SaveToolbar(); _profile.Save(); } catch { }
            Close();
        };
    }
    private static StackPanel Row(params UIElement[] children) { var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(10) }; foreach (var item in children) row.Children.Add(item); return row; }
    private static StackPanel Column(params UIElement[] children) { var col = new StackPanel { Spacing = 12, Margin = new Thickness(18) }; foreach (var item in children) col.Children.Add(item); return col; }
    private Button Button(string text, Func<Task> action) { var button = new Button { Content = text }; button.Click += (_, _) => Run(action); return button; }
    private void Run(Func<Task> action) => _ = ExecuteAsync(action);
    private async Task ExecuteAsync(Func<Task> action) { try { await action(); } catch (OperationCanceledException) { WriteLog("Operation stopped."); } catch (Exception e) { WriteLog(e.Message); await NoticeAsync(e.Message); } }
    private void AddTab(string title, UIElement content) => _tabs.TabItems.Add(new TabViewItem { Header = title, IsClosable = false, Content = content });
    private void SelectTab(int index) => _tabs.SelectedIndex = index;
    private async Task NoticeAsync(string message)
    {
        if (_closing || _root.XamlRoot is null) return;
        try { await new ContentDialog { XamlRoot = _root.XamlRoot, Title = "StreamerBot", Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, CloseButtonText = "OK" }.ShowAsync(); }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
    }
    private Task<string?> ReviewAsync(string text, CancellationToken token)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () => { try { completion.TrySetResult(await ReviewOnUiAsync(text, token)); } catch (Exception e) { completion.TrySetException(e); } })) completion.TrySetCanceled();
        return completion.Task;
    }
    private async Task<string?> ReviewOnUiAsync(string text, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var editor = new TextBox { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 150 };
        var dialog = new ContentDialog { XamlRoot = _root.XamlRoot, Title = "Review reply", Content = editor, PrimaryButtonText = "Send / speak", CloseButtonText = "Discard" };
        using var cancel = token.Register(() => DispatcherQueue.TryEnqueue(dialog.Hide));
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? editor.Text : null;
    }
    private MenuBar BuildMenu()
    {
        var bar = new MenuBar();
        MenuBarItem Menu(string text) { var menu = new MenuBarItem { Title = text }; bar.Items.Add(menu); return menu; }
        void Command(MenuBarItem menu, string text, Func<Task> action) { var item = new MenuFlyoutItem { Text = text }; item.Click += (_, _) => Run(action); menu.Items.Add(item); }
        void Toggle(MenuBarItem menu, string text, string key, bool fallback = false)
        { var item = new ToggleMenuFlyoutItem { Text = text, IsChecked = _profile.Flag(key, fallback) }; item.Click += (_, _) => { _profile.Set(key, item.IsChecked); _profile.Save(); }; menu.Items.Add(item); }
        var file = Menu("File");
        Command(file, "Load Config…", ImportProfileAsync); Command(file, "Save Config", () => { SaveToolbar(); _profile.Save(); WriteLog("Profile saved."); return Task.CompletedTask; });
        Command(file, "Save Config As…", ExportProfileAsync); Command(file, "Import .env…", ImportEnvironmentAsync); Command(file, "Import YouTube token…", ImportTokenAsync); Command(file, "Save session log…", ExportLogAsync);
        Command(file, "Open data folder", () => { Process.Start(new ProcessStartInfo(Program.DataRoot) { UseShellExecute = true }); return Task.CompletedTask; });
        Command(file, "Exit", () => { Close(); return Task.CompletedTask; });
        var ai = Menu("AI Backend");
        foreach (var backend in new[] { "local", "groq", "gemini", "openrouter", "sambanova" }) { var selected = backend; Command(ai, "Use " + backend, () => { _profile.Set("ai_mode", selected); _profile.Save(); WriteLog("AI backend: " + selected); return Task.CompletedTask; }); }
        Command(ai, "Configure keys / models", () => { _settingsGroup.SelectedItem = "AI"; SelectTab(1); return Task.CompletedTask; });
        Command(ai, "Test AI", async () => WriteLog(await _ai.CompleteAsync(_profile, "Say one short hello.", _lifetime.Token)));
        var bot = Menu("Bot Controls");
        Toggle(bot, "Review replies", "review_mode"); Toggle(bot, "Idle chat", "idle_chat", true); Toggle(bot, "Automatic replies", "automated_replies", true); Toggle(bot, "Greet viewers", "greet_viewers", true); Toggle(bot, "Hotkeys only", "hotkeys_only_mode"); Toggle(bot, "Global AI mute", "global_ai_mute"); Toggle(bot, "Cache only", "force_cache_only");
        var voiceMenu = new MenuFlyoutSubItem { Text = "Voice engine" }; bot.Items.Add(voiceMenu);
        foreach (var engine in Catalog.Voices) { var selected = engine; var item = new MenuFlyoutItem { Text = engine }; item.Click += (_, _) => { _profile.Set("tts_mode", selected); _voices.SelectedItem = selected; _profile.Save(); }; voiceMenu.Items.Add(item); }
        var tools = Menu("Tools");
        Command(tools, "Paths — models, voices & video…", PathsAsync);
        Command(tools, "Enable / disable avatar video rendering", ToggleVideoAsync);
        Command(tools, "Keybinds & Hotkeys", () => { SelectTab(2); return Task.CompletedTask; }); Command(tools, "Chat list / manual reply", () => { SelectTab(0); return Task.CompletedTask; });
        Command(tools, "Game guide / live context", GuideAsync); Command(tools, "Live statistics", StatsAsync); Command(tools, "Batch voice cache builder", BatchCacheAsync);
        Command(tools, "Standalone YouTube overlay", ShowYouTubeAsync); Command(tools, "Caption / avatar overlay", ShowOverlayAsync); Command(tools, "Music player", ShowMusicAsync);
        var audio = Menu("Audio");
        Command(audio, "Voice presets / devices", () => { SelectTab(3); return Task.CompletedTask; });
        Command(audio, "Test TTS voice", () => SpeakAsync("Hello! Your stream voice is ready to test.", "test", _lifetime.Token)); Command(audio, "Record / transcribe microphone", RecordAsync);
        Command(audio, "Install / repair voice engines", () => PrepareVoicesAsync(false)); Command(audio, "Repair CUDA voice runtime", () => PrepareVoicesAsync(true)); Command(audio, "GPU diagnostics", GpuAsync);
        Command(audio, "Typecast login", () => VoiceLoginAsync("typecast")); Command(audio, "ElevenLabs Ghost login", () => VoiceLoginAsync("elevenlabs_ghost"));
        return bar;
    }
    private UIElement BuildChat()
    {
        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(170) });
        grid.Children.Add(_chat);
        var composer = Column(_manual, Row(_mode, Button("Generate AI reply", () => _controller.GenerateAsync(_manual.Text, token: _lifetime.Token)), Button("Speak / send text", () => _controller.DeliverAsync(_manual.Text, "manual", _lifetime.Token, _mode.SelectedItem?.ToString())), Button("Reply to selected", async () => { if (_chat.SelectedIndex is >= 0 && _chat.SelectedIndex < _messages.Count) { var m = _messages[_chat.SelectedIndex]; await _controller.GenerateAsync($"Reply to {m.Author}: {m.Text}", token: _lifetime.Token); } }), Button("Stop audio", () => { Interrupt(); return Task.CompletedTask; }), Button("Clear chat", () => { _chat.Items.Clear(); _messages.Clear(); return Task.CompletedTask; })));
        Grid.SetRow(composer, 1); grid.Children.Add(composer); Grid.SetRow(_log, 2); grid.Children.Add(_log); return grid;
    }
    private UIElement BuildSettings()
    {
        var count = new TextBlock();
        var panel = Column(Row(_search, _settingsGroup, Button("Previous", () => { _settingsPage = Math.Max(0, _settingsPage - 1); RenderSettings(); return Task.CompletedTask; }), Button("Next", () => { _settingsPage++; RenderSettings(); return Task.CompletedTask; }), Button("Save", () => { _profile.Save(); ApplyHotkeys(); return Task.CompletedTask; })), count, _settingsRows);
        _search.TextChanged += (_, _) => { _settingsPage = 0; RenderSettings(); }; _settingsGroup.SelectionChanged += (_, _) => { _settingsPage = 0; RenderSettings(); };
        return new ScrollViewer { Content = panel };
    }
    private bool InGroup(string key)
    {
        var group = _settingsGroup.SelectedItem?.ToString();
        return group switch { "AI" => new[] { "ai_", "groq", "gemini", "sambanova", "openrouter", "jina", "global_directive", "chat_directive", "game_prompt", "persona", "lm_studio" }.Any(key.Contains), "Voice" => new[] { "voice", "bark", "kokoro", "fish", "rvc", "soprano", "tiktok", "elevenlabs", "tts", "stt", "volume", "mic", "spk", "model_path", "hf_token" }.Any(key.Contains), "Hotkeys" => key.StartsWith("hk_") || key.Contains("hotkey") || key.StartsWith("react_"), "Music" => key.StartsWith("music"), "Overlays" => key.Contains("overlay") || key.StartsWith("geo_") || key.StartsWith("yt_"), "All" => true, _ => !key.StartsWith("hk_") && !key.Contains("_keys") && !key.Contains("presets") };
    }
    private void RenderSettings()
    {
        _settingsRows.Children.Clear();
        var defaults = Catalog.Defaults();
        var keys = defaults.Select(x => x.Key).Concat(_profile.Data.Select(x => x.Key)).Distinct().Where(k => k.Contains(_search.Text, StringComparison.OrdinalIgnoreCase) && (_search.Text.Length > 0 || InGroup(k))).Order().ToArray();
        _settingsPage = Math.Clamp(_settingsPage, 0, Math.Max(0, (keys.Length - 1) / 40));
        _settingsRows.Children.Add(new TextBlock { Text = $"{keys.Length} fields · page {_settingsPage + 1} of {Math.Max(1, (keys.Length + 39) / 40)}. Imported fields are retained; settings for incomplete features are listed in Migration status.", TextWrapping = TextWrapping.Wrap });
        foreach (var key in keys.Skip(_settingsPage * 40).Take(40))
        {
            var current = _profile.Data[key] ?? defaults[key];
            var row = new StackPanel { Spacing = 4 }; row.Children.Add(new TextBlock { Text = key });
            if (current is JsonValue value && value.TryGetValue<bool>(out var flag))
            { var toggle = new ToggleSwitch { IsOn = flag }; toggle.Toggled += (_, _) => _profile.Set(key, toggle.IsOn); row.Children.Add(toggle); }
            else if (current is JsonValue number && number.TryGetValue<double>(out var numeric))
            { var box = new NumberBox { Value = numeric, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact }; box.ValueChanged += (_, _) => { if (double.IsFinite(box.Value)) _profile.Set(key, box.Value); }; row.Children.Add(box); }
            else if (key.Contains("_keys") || key.Contains("token") || key.EndsWith("api_key"))
            {
                var password = new PasswordBox { Password = current is JsonArray ? string.Join("\n", _profile.Keys(key)) : current?.GetValue<string>() ?? "", PlaceholderText = "Keys stay in your local profile" };
                password.PasswordChanged += (_, _) => { if (current is JsonArray) _profile.Set(key, new JsonArray(password.Password.Split(['\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => JsonValue.Create(s)).ToArray())); else _profile.Set(key, password.Password); }; row.Children.Add(password);
            }
            else
            {
                var structured = current is JsonArray or JsonObject;
                var box = new TextBox { Text = structured ? current!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) : current is JsonValue stringValue && stringValue.TryGetValue<string>(out var s) ? s : "", AcceptsReturn = structured || key.Contains("prompt") || key.Contains("directive") || key.EndsWith("_text"), TextWrapping = TextWrapping.Wrap, MaxHeight = 220 };
                box.LostFocus += (_, _) => { try { _profile.Set(key, structured ? JsonNode.Parse(box.Text) : JsonValue.Create(box.Text)); } catch (JsonException) { WriteLog(key + ": invalid JSON; previous value retained."); } }; row.Children.Add(box);
            }
            _settingsRows.Children.Add(row);
        }
    }
    private UIElement BuildHotkeys()
    {
        var select = new ComboBox { ItemsSource = Catalog.SmartKeys.Concat(Catalog.SystemKeys).Distinct().ToArray(), Width = 280 };
        var binding = new TextBox { Header = "Binding (example: ctrl+shift+1)" };
        var mode = new ComboBox { Header = "Output", ItemsSource = new[] { "V+T", "V", "T" }, Width = 150 };
        var custom = new TextBox { Header = "Override text / reaction phrases separated by |", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var prompt = new TextBox { Header = "AI prompt", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var audio = new TextBox { Header = "Soundboard audio path" };
        var shortText = new CheckBox { Content = "Short reply" }; var useAudio = new CheckBox { Content = "Play soundboard audio" }; var muteAi = new CheckBox { Content = "Mute AI for this key" };
        var enabled = new ToggleSwitch { Header = "Global hotkeys", IsOn = _profile.Flag("hotkeys_active", true) };
        enabled.Toggled += (_, _) => { _profile.Set("hotkeys_active", enabled.IsOn); ApplyHotkeys(); };
        select.SelectionChanged += (_, _) => { var key = select.SelectedItem?.ToString() ?? ""; binding.Text = _profile.Text(key); mode.SelectedItem = _profile.Text(key + "_mode", "V+T"); custom.Text = _profile.Text(key + "_custom"); prompt.Text = _profile.Text(key + "_prompt"); audio.Text = _profile.Text(key + "_audio"); shortText.IsChecked = _profile.Flag(key + "_short"); useAudio.IsChecked = _profile.Flag(key + "_audio_enabled"); muteAi.IsChecked = _profile.Flag(key + "_mute_ai"); };
        var save = Button("Apply binding and options", () => { var key = select.SelectedItem?.ToString() ?? throw new InvalidOperationException("Select a hotkey."); if (binding.Text.Length > 0 && !KeyChord.TryParse(binding.Text, out _)) throw new InvalidOperationException("This binding syntax is not supported yet. Your previous binding has been retained."); _profile.Set(key, binding.Text); _profile.Set(key + "_mode", mode.SelectedItem?.ToString() ?? "V+T"); _profile.Set(key + "_custom", custom.Text); _profile.Set(key + "_prompt", prompt.Text); _profile.Set(key + "_audio", audio.Text); _profile.Set(key + "_short", shortText.IsChecked == true); _profile.Set(key + "_audio_enabled", useAudio.IsChecked == true); _profile.Set(key + "_mute_ai", muteAi.IsChecked == true); _profile.Save(); ApplyHotkeys(); return Task.CompletedTask; });
        select.SelectedIndex = 0;
        return new ScrollViewer { Content = Column(enabled, select, binding, mode, shortText, custom, prompt, Row(audio, Button("Browse audio…", async () => { var file = await PickAsync(".wav", ".mp3"); if (file is not null) audio.Text = file.Path; })), useAudio, muteAi, save, new TextBlock { Text = "All original hotkey fields are retained. Basic Windows modifier chords are registered globally. Duplicate, reserved and unsupported combinations are reported rather than silently changed. Shortcuts suspend while typing in this app.", TextWrapping = TextWrapping.Wrap }) };
    }
    private UIElement BuildVoice()
    {
        _voices.SelectionChanged += (_, _) => { if (_voices.SelectedItem is string value) { _profile.Set("tts_mode", value); _profile.Save(); } };
        var volume = new Slider { Header = "Voice volume", Minimum = 0, Maximum = 1, Value = _profile.Number("volume", 1), Width = 340 };
        volume.ValueChanged += (_, _) => _profile.Set("volume", volume.Value);
        var cpu = new CheckBox { Content = "Bark CPU / offload setting", IsChecked = _profile.Flag("bark_use_cpu") }; cpu.Checked += (_, _) => _profile.Set("bark_use_cpu", true); cpu.Unchecked += (_, _) => _profile.Set("bark_use_cpu", false);
        var small = new CheckBox { Content = "Bark small models", IsChecked = _profile.Flag("bark_small_models") }; small.Checked += (_, _) => _profile.Set("bark_small_models", true); small.Unchecked += (_, _) => _profile.Set("bark_small_models", false);
        var low = new CheckBox { Content = "Bark low VRAM / stage offload", IsChecked = _profile.Flag("bark_low_vram") }; low.Checked += (_, _) => _profile.Set("bark_low_vram", true); low.Unchecked += (_, _) => _profile.Set("bark_low_vram", false);
        var windowsVoice = new ComboBox { Header = "Windows offline voice", ItemsSource = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.Select(v => v.DisplayName).ToArray(), Width = 360 };
        windowsVoice.SelectionChanged += (_, _) => { if (windowsVoice.SelectedIndex >= 0) _profile.Set("native_windows_voice", Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices[windowsVoice.SelectedIndex].Id); };
        var microphone = new ComboBox { Header = "Microphone", Width = 500 }; var speaker = new ComboBox { Header = "Output device", Width = 500 };
        Windows.Devices.Enumeration.DeviceInformationCollection? inputs = null, outputs = null;
        microphone.SelectionChanged += (_, _) => { if (inputs is not null && microphone.SelectedIndex >= 0) _profile.Set("native_microphone_id", inputs[microphone.SelectedIndex].Id); };
        speaker.SelectionChanged += (_, _) => { if (outputs is not null && speaker.SelectedIndex >= 0) _profile.Set("native_speaker_id", outputs[speaker.SelectedIndex].Id); };
        var refresh = Button("Refresh audio devices", async () => { microphone.SelectedIndex = -1; speaker.SelectedIndex = -1; inputs = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Media.Devices.MediaDevice.GetAudioCaptureSelector()); outputs = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Media.Devices.MediaDevice.GetAudioRenderSelector()); microphone.ItemsSource = inputs.Select(d => d.Name).ToArray(); speaker.ItemsSource = outputs.Select(d => d.Name).ToArray(); });
        var stt = new ComboBox { Header = "Speech recognition", ItemsSource = new[] { "google", "whisper" }, SelectedItem = _profile.Text("stt_mode", "google"), Width = 180 }; stt.SelectionChanged += (_, _) => _profile.Set("stt_mode", stt.SelectedItem?.ToString() ?? "google");
        return new ScrollViewer { Content = Column(new TextBlock { Text = "Voice engine", FontSize = 20 }, _voices, volume, windowsVoice, cpu, small, low, Row(Button("Test voice", () => SpeakAsync("Hello, this is your stream voice test.", "test", _lifetime.Token)), Button("GPU diagnostics", GpuAsync)), Row(Button("Install / repair voice engines", () => PrepareVoicesAsync(false)), Button("Repair CUDA", () => PrepareVoicesAsync(true))), microphone, speaker, refresh, stt, Button("Start / stop microphone recording", RecordAsync), Button("Model / voice presets and all voice settings", () => { _settingsGroup.SelectedItem = "Voice"; SelectTab(1); return Task.CompletedTask; }), new TextBlock { Text = "Windows offline speech is built into the native app. Other engines use an isolated voice worker. Missing private Python is downloaded on first voice use; internet is required. Model weights, API keys and logins are not bundled. Imported numeric device indices are preserved; select devices on each PC because device identities differ.", TextWrapping = TextWrapping.Wrap }) };
    }
    private UIElement BuildStatus() => new ScrollViewer { Content = Column(new TextBlock { Text = "Native migration preview", FontSize = 24 }, new TextBlock { Text = "This build is a native C# / WinUI interface with C# settings, hotkeys, AI transport, session logic and YouTube REST integration. Voice/speech models and the SadTalker renderer use separate Python engine environments. It does not launch the original Tk UI or a browser dashboard.", TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = "Implemented paths: full original profile field retention and export; key/prompt/voice editors; basic global key chords; provider requests and failover; chat/review/simulation; offline voice; worker-backed synthesis; CUDA checks; microphone recording; music/caption/YouTube auxiliary windows. Windows and external-service checks are still required.", TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = "Parity still incomplete: original advanced hotkey multi-tap/cache-shuffle behavior, automated game-prompt/reaction rewriting, all subscriber/statistics-triggered greetings, account quota rotation, guide ghost modes, original remote companion and full avatar rendering bridges and complete cache-batch workflow. Original RVC and Soprano loaders were already incomplete. A retained setting or visible menu choice does not prove its behavior is implemented. See NATIVE-PARITY.md in the installation folder.", TextWrapping = TextWrapping.Wrap }, Button("Open installation folder", () => { Process.Start(new ProcessStartInfo(AppContext.BaseDirectory) { UseShellExecute = true }); return Task.CompletedTask; })) };
    private void WriteLog(string text) => DispatcherQueue.TryEnqueue(() =>
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {text}";
        _log.Text = (_log.Text.Length > 40000 ? _log.Text[^30000..] : _log.Text) + line + Environment.NewLine;
        _status.Text = text.Length > 180 ? text[..180] : text;
    });
    private void AddChat(ChatMessage message)
    {
        _messages.Add(message); _chat.Items.Add(new TextBlock { Text = message.Author + ": " + message.Text, TextWrapping = TextWrapping.Wrap });
        if (_messages.Count > 1000) { _messages.RemoveAt(0); _chat.Items.RemoveAt(0); }
        _chat.ScrollIntoView(_chat.Items.Last());
    }
    private void SaveToolbar() { _profile.Set("game_name", _game.Text); _profile.Set("persona", _persona.Text); _profile.Set("channel_name", _channel.Text); }
    private void LoadToolbar() { _game.Text = _profile.Text("game_name"); _persona.SelectedItem = _profile.Text("persona", "Default (Casual)"); _persona.Text = _profile.Text("persona", "Default (Casual)"); _channel.Text = _profile.Text("channel_name"); _voices.SelectedItem = _profile.Text("tts_mode", "edge"); RenderSettings(); }
    private void ApplyHotkeys()
    {
        _hotkeys?.Dispose(); _hotkeys = null;
        if (!_profile.Flag("hotkeys_active", true)) return;
        _hotkeys = new NativeHotkeys(_profile, key => DispatcherQueue.TryEnqueue(() => Run(() => HandleHotkeyAsync(key))));
        WriteLog($"Registered {_hotkeys.Count} global shortcuts."); foreach (var problem in _hotkeys.Problems) WriteLog(problem);
    }
    private async Task HandleHotkeyAsync(string key)
    {
        if (key == "hk_suspend_hotkeys") { _suspended = !_suspended; WriteLog(_suspended ? "Shortcuts suspended." : "Shortcuts resumed."); return; }
        if (_suspended) return;
        SaveToolbar();
        if (Catalog.SmartKeys.Contains(key))
        {
            if (_profile.Flag(key + "_audio_enabled") && _profile.Text(key + "_audio").Length > 0) await _audio.PlayFileAsync(_profile.Resolve(_profile.Text(key + "_audio")), _profile, _lifetime.Token);
            if (!_profile.Flag(key + "_mute_ai")) await _controller.TriggerAsync(key, _lifetime.Token);
            return;
        }
        switch (key)
        {
            case "hk_stop": case "hk_stop_audio": Interrupt(); break;
            case "hk_start": _controller.Start(new YouTubeClient(_http, _profile.Text("native_youtube_token_path")), false); break;
            case "hk_mic": case "hk_voice_input": await RecordAsync(); break;
            case "hk_mp3_play_pause": _music?.Toggle(); break;
            case "hk_mp3_next": _music?.Next(1); break;
            case "hk_mp3_prev": _music?.Next(-1); break;
            case "hk_play_overlay": await ShowOverlayAsync(); break;
            case "hk_guide": case "hk_open_context_window": await GuideAsync(); break;
            case "hk_stats": await StatsAsync(); break;
            case "hk_open_list": SelectTab(0); Activate(); break;
            case "hk_hotkeys_window": SelectTab(2); Activate(); break;
            case "hk_music_window": await ShowMusicAsync(); break;
            case "hk_toggle_ai_mute": _profile.Set("global_ai_mute", !_profile.Flag("global_ai_mute")); WriteLog("AI mute: " + _profile.Flag("global_ai_mute")); break;
            case "hk_toggle_cache_mode": _profile.Set("force_cache_only", !_profile.Flag("force_cache_only")); WriteLog("Cache only: " + _profile.Flag("force_cache_only")); break;
            case "hk_reply_last": if (_messages.Count > 0) await _controller.GenerateAsync("Reply to " + _messages[^1].Author + ": " + _messages[^1].Text, token: _lifetime.Token); break;
            case "hk_hide_overlays": _overlay?.Close(); _music?.Close(); _youtubeOverlay?.Close(); break;
            case "hk_manual_reply": SelectTab(0); Activate(); _manual.Focus(FocusState.Programmatic); break;
            default: WriteLog(key + ": original system action has not been migrated yet. Its saved binding is retained."); break;
        }
    }
    private void Interrupt() { var previous = _playback; _playback = new CancellationTokenSource(); previous.Cancel(); previous.Dispose(); _controller.Interrupt(); _voice.Interrupt(); _audio.Stop(); _avatarVideo?.Stop(); }
    private async Task StopAsync() { Interrupt(); await _controller.StopAsync(); }
    private async Task<StorageFile?> PickAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker(); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
        return await picker.PickSingleFileAsync();
    }
    private async Task<StorageFile?> SaveFileAsync(string name, string extension)
    {
        var picker = new FileSavePicker { SuggestedFileName = name }; picker.FileTypeChoices.Add(extension, new[] { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this)); return await picker.PickSaveFileAsync();
    }
    private async Task ImportProfileAsync()
    {
        var file = await PickAsync(".json"); if (file is null) return;
        await StopAsync(); _profile.Import(file.Path); _profile.Save(); await ReloadVoiceAsync(); LoadToolbar(); ApplyHotkeys(); WriteLog("Imported profile copy. Original file was not changed.");
    }
    private async Task ExportProfileAsync() { SaveToolbar(); var file = await SaveFileAsync("StreamerBot-profile", ".json"); if (file is not null) _profile.Export(file.Path); }
    private async Task ImportEnvironmentAsync()
    {
        var file = await PickAsync("*"); if (file is null) return;
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["GROQ_API_KEY"] = "groq_keys", ["GEMINI_API_KEY"] = "gemini_keys", ["GOOGLE_API_KEY"] = "gemini_keys", ["OPENROUTER_API_KEY"] = "openrouter_keys", ["SAMBANOVA_API_KEY"] = "sambanova_keys", ["ELEVENLABS_API_KEY"] = "elevenlabs_keys" };
        var count = 0;
        foreach (var raw in await File.ReadAllLinesAsync(file.Path)) { var line = raw.Trim(); if (line.StartsWith('#')) continue; var at = line.IndexOf('='); if (at <= 0) continue; var name = line[..at].Trim(); if (mapping.TryGetValue(name, out var key)) { var value = line[(at + 1)..].Trim().Trim('"', '\''); _profile.Set(key, new JsonArray(JsonValue.Create(value))); count++; } }
        _profile.Save(); RenderSettings(); WriteLog($"Imported {count} supported credential entries. Other .env fields were not imported; use the profile settings for those.");
    }
    private async Task ImportTokenAsync()
    {
        var file = await PickAsync(".json"); if (file is null) return;
        var document = JsonNode.Parse(await File.ReadAllTextAsync(file.Path))?.AsObject() ?? throw new InvalidDataException("Invalid token JSON.");
        foreach (var key in new[] { "client_id", "client_secret", "refresh_token" }) if (string.IsNullOrWhiteSpace(document[key]?.GetValue<string>())) throw new InvalidDataException("Select the original authorized-user token file containing " + key + ".");
        var directory = Path.Combine(Program.DataRoot, "Credentials"); Directory.CreateDirectory(directory); var target = Path.Combine(directory, "youtube.json");
        if (!Path.GetFullPath(file.Path).Equals(target, StringComparison.OrdinalIgnoreCase)) File.Copy(file.Path, target, true);
        _profile.Set("native_youtube_token_path", target); _profile.Save(); WriteLog("YouTube token copied to your private application data.");
    }
    private async Task ExportLogAsync() { var file = await SaveFileAsync("StreamerBot-session", ".txt"); if (file is not null) await File.WriteAllTextAsync(file.Path, _log.Text); }
    private readonly SemaphoreSlim _speechGate = new(1);
    private async Task SpeakAsync(string text, string category, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _playback.Token);
        token = linked.Token;
        await _speechGate.WaitAsync(token);
        try
        {
            var cache = category.StartsWith("cache:");
            if (_profile.Text("tts_mode") == "offline")
            {
                if (cache || File.Exists(Path.Combine(Program.DataRoot, "enable_video.txt")))
                {
                    var offlineAudio = await _audio.SynthesizeOfflineFileAsync(text, _profile, ResourcePaths.VoiceCache(_profile, Program.DataRoot), token);
                    if (!cache) await PlayGeneratedAsync(offlineAudio, token);
                }
                else await _audio.SpeakOfflineAsync(text, _profile, token);
                return;
            }
            if (!File.Exists(ResourcePaths.VoicePython(_profile, Program.DataRoot))) await PrepareVoicesAsync(false);
            var workerProfile = _profile.Data.DeepClone().AsObject();
            foreach (var entry in ResourcePaths.Entries) if (ResourcePaths.Value(_profile, entry.Key).Length > 0) workerProfile[entry.Key] = ResourcePaths.Get(_profile, entry.Key);
            workerProfile["global_model_path"] = ResourcePaths.Models(_profile, Program.DataRoot);
            workerProfile["voice_cache_path"] = ResourcePaths.VoiceCache(_profile, Program.DataRoot);
            var result = await _voice.CallAsync("speak", new JsonObject { ["text"] = text, ["category"] = category.Replace("cache:", ""), ["profile"] = workerProfile, ["cache_only"] = _profile.Flag("force_cache_only") }, token);
            if (!cache && result["audio"] is JsonValue path)
            {
                await PlayGeneratedAsync(path.GetValue<string>(), token);
            }
            if (result["queued"]?.GetValue<bool>() == true) WriteLog("Browser voice request queued by original engine; completion is not confirmed.");
        }
        finally { _speechGate.Release(); }
    }
    private async Task PlayGeneratedAsync(string audioPath, CancellationToken token)
    {
                if (File.Exists(Path.Combine(Program.DataRoot, "enable_video.txt")))
                {
                    try
                    {
                        WriteLog("Rendering avatar video with the SadTalker environment selected in Tools → Paths…");
                        var video = await new VideoRenderer(Path.Combine(AppContext.BaseDirectory, "sadtalker_bridge.py"), Program.DataRoot).RenderAsync(_profile, audioPath, token);
                        await PlayAvatarAsync(video, token); return;
                    }
                    catch (Exception error) when (error is not OperationCanceledException) { WriteLog(error.Message + " Playing audio only."); }
                }
                await _audio.PlayFileAsync(audioPath, _profile, token);
    }
    private VoiceWorker CreateVoiceWorker() => new(ResourcePaths.VoicePython(_profile, Program.DataRoot), Path.Combine(AppContext.BaseDirectory, "windows", "voice", "worker.py"), Program.DataRoot, ResourcePaths.VoiceCache(_profile, Program.DataRoot), ResourcePaths.Runtime(_profile, Program.DataRoot));
    private async Task ReloadVoiceAsync() { await _voice.DisposeAsync(); _voice = CreateVoiceWorker(); }
    private async Task PathsAsync()
    {
        await StopAsync();
        if (await PathSettingsDialog.ShowAsync(this, _root.XamlRoot, _profile)) { await ReloadVoiceAsync(); _music?.Close(); _overlay?.Close(); RenderSettings(); WriteLog("Resource paths saved. Voice worker restarted; existing files stayed in their selected locations."); }
    }
    private Task ToggleVideoAsync()
    {
        var marker = Path.Combine(Program.DataRoot, "enable_video.txt");
        if (File.Exists(marker)) { File.Delete(marker); WriteLog("New avatar video rendering disabled. Voice playback remains available."); }
        else { File.WriteAllText(marker, "1"); WriteLog("Avatar video rendering enabled. Select SadTalker, its Python and an avatar in Tools → Paths."); }
        return Task.CompletedTask;
    }
    private Task PlayAvatarAsync(string path, CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (_avatarVideo is null) { _avatarVideo = new AvatarVideoWindow(); _avatarVideo.Closed += (_, _) => _avatarVideo = null; }
                _avatarVideo.Activate(); await _avatarVideo.PlayAsync(path, _profile, token); completion.TrySetResult();
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception error) { completion.TrySetException(error); }
        })) completion.TrySetCanceled();
        return completion.Task;
    }
    private async Task PrepareVoicesAsync(bool cuda)
    {
        _voice.Interrupt(); SaveToolbar(); _profile.Save();
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = true };
        // Data paths are quoted; no profile or credential values enter the command line.
        start.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(AppContext.BaseDirectory, "windows", "Prepare-Voices.ps1") + "\" -DataRoot \"" + Program.DataRoot + "\" -RuntimeRoot \"" + ResourcePaths.Runtime(_profile, Program.DataRoot) + "\"" + (ResourcePaths.Value(_profile, "voice_python_path").Length > 0 ? " -PythonPath \"" + ResourcePaths.VoicePython(_profile, Program.DataRoot) + "\"" : "") + " -GpuMode " + (cuda ? "cuda -CudaOnly" : "auto");
        using var process = Process.Start(start) ?? throw new IOException("Could not start voice preparation."); WriteLog("Voice setup running in a separate window. Large downloads may take several minutes.");
        await process.WaitForExitAsync(_lifetime.Token); if (process.ExitCode != 0) throw new InvalidOperationException("Voice preparation failed. Read the setup window and reports in the data folder."); WriteLog("Voice preparation completed. Run GPU diagnostics before selecting a GPU voice.");
    }
    private async Task GpuAsync() { var result = await _voice.CallAsync("gpu", new JsonObject(), _lifetime.Token); await NoticeAsync(result["gpu"]?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "No GPU report returned."); }
    private async Task VoiceLoginAsync(string engine) { await _voice.CallAsync("login", new JsonObject { ["engine"] = engine }, _lifetime.Token); WriteLog("Voice login browser closed. Test synthesis to verify login."); }
    private async Task RecordAsync()
    {
        if (_recording is null)
        {
            var capture = new MediaCapture();
            try
            {
                await capture.InitializeAsync(new MediaCaptureInitializationSettings { StreamingCaptureMode = StreamingCaptureMode.Audio, AudioDeviceId = _profile.Text("native_microphone_id") });
                var directory = Path.Combine(Program.DataRoot, "Recordings"); Directory.CreateDirectory(directory); var folder = await StorageFolder.GetFolderFromPathAsync(directory);
                _recordedFile = await folder.CreateFileAsync("microphone.wav", CreationCollisionOption.GenerateUniqueName);
                await capture.StartRecordToStorageFileAsync(MediaEncodingProfile.CreateWav(AudioEncodingQuality.Medium), _recordedFile); _recording = capture; WriteLog("Recording microphone. Press again to stop and transcribe.");
            }
            catch { capture.Dispose(); throw; }
        }
        else
        {
            var capture = _recording; _recording = null;
            try { await capture.StopRecordAsync(); } finally { capture.Dispose(); }
            var result = await _voice.CallAsync("transcribe", new JsonObject { ["audio"] = _recordedFile!.Path, ["engine"] = _profile.Text("stt_mode", "google") }, _lifetime.Token);
            _manual.Text = result["text"]?.GetValue<string>() ?? ""; SelectTab(0); WriteLog("Transcript ready for editing; it has not been sent.");
        }
    }
    private Task ShowOverlayAsync() { if (_overlay is null) { _overlay = new OverlayWindow(_profile); _overlay.Closed += (_, _) => _overlay = null; } _overlay.Activate(); return Task.CompletedTask; }
    private Task ShowMusicAsync() { if (_music is null) { _music = new MusicWindow(_profile); _music.Closed += (_, _) => _music = null; } _music.Activate(); return Task.CompletedTask; }
    private Task ShowYouTubeAsync() { if (_youtubeOverlay is null) { _youtubeOverlay = new YouTubeOverlay(_profile); _youtubeOverlay.Closed += (_, _) => _youtubeOverlay = null; } _youtubeOverlay.Activate(); return Task.CompletedTask; }
    private async Task GuideAsync()
    {
        var text = new TextBox { Text = _profile.Text("native_live_context"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 250 };
        if (await new ContentDialog { XamlRoot = _root.XamlRoot, Title = "Live game context", Content = text, PrimaryButtonText = "Apply", CloseButtonText = "Cancel" }.ShowAsync() == ContentDialogResult.Primary) { _profile.Set("native_live_context", text.Text); _profile.Save(); }
    }
    private async Task StatsAsync()
    {
        var path = _profile.Text("native_youtube_token_path"); if (path.Length == 0) throw new InvalidOperationException("Import your YouTube authorized-user token first.");
        var client = new YouTubeClient(_http, path); if (!await client.FindLiveAsync(_lifetime.Token)) throw new InvalidOperationException("No active live broadcast.");
        await NoticeAsync((await client.StatisticsAsync(_lifetime.Token)).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
    private async Task BatchCacheAsync()
    {
        var phrases = Catalog.SmartKeys.SelectMany(key => _profile.Text(key + "_custom").Split(['|', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().Select(text => (Key: key, Text: text))).ToArray();
        if (phrases.Length == 0) throw new InvalidOperationException("Add fixed custom / reaction phrases before building their voice cache.");
        foreach (var phrase in phrases) { _lifetime.Token.ThrowIfCancellationRequested(); await SpeakAsync(phrase.Text, "cache:" + phrase.Key, _lifetime.Token); }
        WriteLog($"Built {phrases.Length} fixed phrase cache entries. AI prompt batch generation is not included yet.");
    }
}
