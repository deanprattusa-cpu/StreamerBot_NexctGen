using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using StreamerBot.Native.Core;
using Windows.Storage;
using Windows.System;

namespace StreamerBot.Native;

internal sealed partial class NativeWindow
{
    private bool _loadingToolbar;
    private readonly Dictionary<string, FloatingWindow> _toolWindows = [];
    private ReviewWindow? _reviewWindow;
    private readonly Slider _mainVolume = new() { Minimum = 0, Maximum = 1, Width = 80 };
    private readonly Slider _mainOpacity = new() { Minimum = 0.1, Maximum = 1, Width = 80 };
    private readonly ListView _chatList = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly CheckBox _hideBots = new() { Content = "Hide Bots" };
    private readonly CheckBox _voiceOnly = new() { Content = "Voice Only" };
    private readonly Dictionary<string, TextBox> _bindingEditors = [];
    private readonly List<Action> _hotkeyRefresh = [];
    private TabView? _hotkeyTabs;
    private static SolidColorBrush Color(byte r, byte g, byte b) => new(Windows.UI.Color.FromArgb(255, r, g, b));
    private static TextBlock Label(string text) => new() { Text = text, Foreground = Color(0, 255, 0), VerticalAlignment = VerticalAlignment.Center };
    private FrameworkElement BuildOriginalToolbar()
    {
        _channel.Width = 180; _game.Width = 140; _persona.Width = 165;
        _mainVolume.ValueChanged += (_, _) => { if (!_loadingToolbar) _profile.Set("volume", _mainVolume.Value); };
        _mainOpacity.ValueChanged += (_, _) =>
        {
            if (_loadingToolbar) return;
            _profile.Set("overlay_opacity", _mainOpacity.Value);
            if (_toolWindows.TryGetValue("chat", out var window)) window.SetOpacity(_mainOpacity.Value);
        };
        var toolbar = Row(Label("Channel:"), _channel, Button("+", AddChannelAsync), Button("Credentials", CredentialsAsync), Label("Persona:"), _persona, Label("Game:"), _game);
        var controls = Row(Label("Vol:"), _mainVolume, Label("Opacity:"), _mainOpacity,
            Button("▶ START", () => StartSessionAsync(false)), Button("SIMULATE", () => StartSessionAsync(true)), Button("■ STOP", StopAsync));
        return new StackPanel { Children = { new ScrollViewer { Content = toolbar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, controls } };
    }
    private FrameworkElement BuildOriginalMain()
    {
        var grid = new Grid { Margin = new Thickness(10), RowSpacing = 6 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 100 });
        grid.Children.Add(Row(new TextBlock { Text = "LIVE CHAT FEED", Foreground = Color(0, 230, 255), FontWeight = Microsoft.UI.Text.FontWeights.Bold }, Button("Clear Chat", () => { _chat.Items.Clear(); _messages.Clear(); RefreshChatList(); return Task.CompletedTask; })));
        _chat.Background = Color(0, 0, 0); grid.Children.Add(_chat.AtRow(1));
        grid.Children.Add(new TextBlock { Text = "System Log", Foreground = Color(0, 230, 255) }.AtRow(2));
        _log.Background = Color(17, 17, 17); _log.Foreground = Color(0, 255, 0); _log.FontFamily = new FontFamily("Consolas"); _log.FontSize = 12;
        grid.Children.Add(_log.AtRow(3)); return grid;
    }
    private FloatingWindow ShowTool(string key, string title, Func<UIElement> build, int width, int height)
    {
        if (!_toolWindows.TryGetValue(key, out var window))
        {
            window = new FloatingWindow(_profile, title, key == "chat" ? "geo_selector" : "geo_native_" + key, build(), width, height, borderless: key == "chat");
            _toolWindows.Add(key, window);
        }
        window.Show(); return window;
    }
    private Task StartSessionAsync(bool simulation)
    {
        SaveToolbar();
        if (_controller.Running) return Task.CompletedTask;
        var channel = OriginalEnvironment.Selected(_profile);
        if (!simulation && channel is null && _profile.Text("native_youtube_token_path").Length == 0) throw new InvalidOperationException("Import your original .env or add a channel with +, then select it.");
        var path = OriginalEnvironment.TokenFile(_profile);
        if (!simulation && !File.Exists(path)) throw new InvalidOperationException($"No authorized YouTube token found for {channel?.Name ?? "this account"}. Use Credentials → Import YouTube token, or keep the original credentials folder beside the imported .env.");
        _profile.Save();
        _controller.Start(simulation ? null : new YouTubeClient(_http, path, channel?.ChannelId), simulation);
        _channel.IsEnabled = false; _game.IsEnabled = false;
        return Task.CompletedTask;
    }
    private async Task AddChannelAsync()
    {
        var name = new TextBox { Header = "Channel name" }; var id = new TextBox { Header = "YouTube channel ID (UC…)" };
        if (await new ContentDialog { XamlRoot = _root.XamlRoot, Title = "Add Channel", Content = Column(name, id), PrimaryButtonText = "Add", CloseButtonText = "Cancel" }.ShowAsync() != ContentDialogResult.Primary) return;
        await StopAsync(); OriginalEnvironment.Add(_profile, name.Text, id.Text); LoadToolbar(); _profile.Save();
    }
    private Task CredentialsAsync()
    {
        ShowTool("credentials", "Credential & Paths Manager", () => Column(
            Label("Original environment & channel accounts"), Button("Import original .env", ImportEnvironmentAsync),
            new TextBlock { Text = "Select your channel in the main window, then import its authorized *_token_N.json. Existing tokens in the credentials folder beside your .env are detected by that channel's token prefix.", TextWrapping = TextWrapping.Wrap },
            Button("Import Google JSON / Authorize channel", AuthorizeChannelAsync),
            Button("Import YouTube token for selected channel", ImportTokenAsync),
            Button("API keys and voice credentials", () => { _settingsGroup.SelectedItem = "AI"; SelectTab(1); return Task.CompletedTask; }),
            Button("Models, Python and cache paths", PathsAsync)), 560, 500);
        return Task.CompletedTask;
    }
    private bool _authorizing;
    private async Task AuthorizeChannelAsync()
    {
        if (_authorizing) { WriteLog("Channel authorization is already open in your browser."); return; }
        var channel = OriginalEnvironment.Selected(_profile) ?? throw new InvalidOperationException("Select or add a channel before connecting its Google account.");
        var file = await PickAsync(".json"); if (file is null) return;
        _authorizing = true;
        try
        {
            await StopAsync();
            WriteLog("Complete Google authorization in your browser for " + channel.Name + ".");
            var credentials = await YouTubeAuthorization.AuthorizeAsync(_http, file.Path, uri => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }), _lifetime.Token);
            var directory = Path.Combine(Program.DataRoot, "Credentials"); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"channel-{channel.Number}.json");
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllTextAsync(temporary, credentials.ToJsonString(), _lifetime.Token); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            OriginalEnvironment.SetToken(_profile, channel, path); _profile.Save();
            WriteLog("YouTube authorization saved for " + channel.Name + ". Select it and press Start.");
        }
        finally { _authorizing = false; }
    }
    private void ShowChatWindow(bool toggle = false)
    {
        if (toggle && _toolWindows.TryGetValue("chat", out var existing) && existing.IsShown) { existing.Hide(); return; }
        var window = ShowTool("chat", "StreamerBot Chat List", BuildChatList, 650, 700);
        window.SetOpacity(_profile.Number("overlay_opacity", 0.85));
        RefreshChatList(); _manual.Focus(FocusState.Programmatic);
    }
    private UIElement BuildChatList()
    {
        var grid = new Grid { Margin = new Thickness(10), RowSpacing = 8 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock { Text = "≡ LIVE CHAT — drag to move", Foreground = Color(255, 170, 0), VerticalAlignment = VerticalAlignment.Center };
        title.PointerPressed += (_, e) => { if (e.GetCurrentPoint(title).Properties.IsLeftButtonPressed && _toolWindows.TryGetValue("chat", out var window)) { e.Handled = true; window.Drag(); } };
        grid.Children.Add(Row(title, Button("×", () => { _toolWindows["chat"].Hide(); return Task.CompletedTask; })));
        grid.Children.Add(_chatList.AtRow(1));
        _hideBots.IsChecked = _profile.Flag("hide_bot_chat", true); _voiceOnly.IsChecked = _profile.Flag("chat_list_voice_only");
        _hideBots.Checked += (_, _) => { _profile.Set("hide_bot_chat", true); RefreshChatList(); }; _hideBots.Unchecked += (_, _) => { _profile.Set("hide_bot_chat", false); RefreshChatList(); };
        _voiceOnly.Checked += (_, _) => _profile.Set("chat_list_voice_only", true); _voiceOnly.Unchecked += (_, _) => _profile.Set("chat_list_voice_only", false);
        var mic = new Button { Content = "🎤 Hold" };
        async Task StartHoldAsync() { mic.Content = "Recording…"; try { await SetRecordingAsync(true); } catch { mic.Content = "🎤 Hold"; throw; } }
        async Task StopHoldAsync() { try { await SetRecordingAsync(false); } finally { mic.Content = "🎤 Hold"; } }
        mic.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) => { if (e.GetCurrentPoint(mic).Properties.IsLeftButtonPressed) { mic.CapturePointer(e.Pointer); e.Handled = true; Run(StartHoldAsync); } }), true);
        mic.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) => { mic.ReleasePointerCapture(e.Pointer); e.Handled = true; Run(StopHoldAsync); }), true);
        mic.PointerCaptureLost += (_, _) => Run(StopHoldAsync);
        var tools = Column(Row(mic, Button("Improve", ImproveChatDraftAsync), _voiceOnly, _hideBots), Row(Button("Refresh", RefreshChatHistoryAsync), Button("Reply to selected", ReplyToSelectedAsync), Button("Stop audio", () => { Interrupt(); return Task.CompletedTask; })));
        grid.Children.Add(tools.AtRow(2));
        var composer = new Grid { ColumnSpacing = 8 }; composer.ColumnDefinitions.Add(new ColumnDefinition()); composer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        composer.Children.Add(_manual); var send = Button("Send\n(Enter)", SendChatDraftAsync); Grid.SetColumn(send, 1); composer.Children.Add(send); grid.Children.Add(composer.AtRow(3));
        _manual.KeyDown += (_, e) =>
        {
            var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            if (e.Key == VirtualKey.Enter && !shift) { e.Handled = true; Run(SendChatDraftAsync); }
        };
        _chatList.DoubleTapped += (_, _) => Run(EditSelectedChatAsync);
        var grip = new TextBlock { Text = "◢", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, FontSize = 20 };
        grip.PointerPressed += (_, e) => { if (e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) { e.Handled = true; _toolWindows["chat"].Drag(resize: true); } };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.Children.Add(grip.AtRow(4));
        return grid;
    }
    private ChatMessage? SelectedChat => (_chatList.SelectedItem as ListViewItem)?.Tag as ChatMessage;
    private static ListViewItem ChatItem(ChatMessage message)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var time = DateTimeOffset.TryParse(message.PublishedAt, out var parsed) ? parsed.ToLocalTime().ToString("HH:mm:ss") : message.PublishedAt;
        grid.Children.Add(new TextBlock { Text = time, FontSize = 11 });
        var user = new TextBlock { Text = message.Author, TextWrapping = TextWrapping.Wrap, Foreground = Color(0, 191, 255) }; Grid.SetColumn(user, 1); grid.Children.Add(user);
        var text = new TextBlock { Text = message.Text, TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(text, 2); grid.Children.Add(text);
        return new ListViewItem { Content = grid, Tag = message, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }
    private void RefreshChatList()
    {
        var selected = SelectedChat?.Id;
        var names = new[] { "nightbot", "streamlabs", "streamelements", "system", "me", "streamerbot", _profile.Text("channel_name") };
        var items = _messages.Where(m => _hideBots.IsChecked != true || !names.Contains(m.Author, StringComparer.OrdinalIgnoreCase)).Select(ChatItem).ToArray();
        _chatList.ItemsSource = items; _chatList.SelectedItem = items.FirstOrDefault(i => (i.Tag as ChatMessage)?.Id == selected);
    }
    private async Task ReplyToSelectedAsync()
    {
        if (SelectedChat is not ChatMessage message) throw new InvalidOperationException("Select a chat message first.");
        await _controller.GenerateAsync($"Reply to {message.Author}: {message.Text}", token: _lifetime.Token);
    }
    private async Task SendChatDraftAsync()
    {
        var text = _manual.Text; if (string.IsNullOrWhiteSpace(text)) return;
        await _controller.DeliverAsync(text, "manual", _lifetime.Token, _voiceOnly.IsChecked == true ? "V" : "V+T");
        if (_manual.Text == text) _manual.Text = "";
    }
    private async Task ImproveChatDraftAsync()
    {
        var text = _manual.Text; if (string.IsNullOrWhiteSpace(text)) return;
        var improved = await _ai.CompleteAsync(_profile, "Improve this chat reply. Return only the revised reply: " + text, _lifetime.Token);
        if (_manual.Text == text) _manual.Text = improved;
    }
    private async Task RefreshChatHistoryAsync()
    {
        var messages = await _controller.ChatHistoryAsync(_lifetime.Token);
        foreach (var message in messages) if (!_messages.Any(m => m.Id == message.Id)) AddChat(message);
        RefreshChatList();
    }
    private async Task EditSelectedChatAsync()
    {
        if (SelectedChat is not ChatMessage selected || !_toolWindows.TryGetValue("chat", out var window)) return;
        var author = new TextBox { Header = "Viewer name", Text = selected.Author };
        var message = new TextBox { Header = "Message", Text = selected.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        if (await new ContentDialog { XamlRoot = window.Root.XamlRoot, Title = "Edit chat before reply", Content = Column(author, message), PrimaryButtonText = "Apply", CloseButtonText = "Cancel" }.ShowAsync() != ContentDialogResult.Primary) return;
        var index = _messages.FindIndex(m => m.Id == selected.Id);
        if (index >= 0) { _messages[index] = selected with { Author = author.Text, Text = message.Text }; RefreshChatList(); }
    }
    private UIElement BuildOriginalHotkeys()
    {
        var grid = new Grid { RowSpacing = 8 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(Column(Label("Keybinds & Soundboard"), new TextBlock { Text = "Blank Override / Prompt = AI decides. Bindings, output modes and soundboard options are retained from your original config.", TextWrapping = TextWrapping.Wrap }));
        var tabs = _hotkeyTabs = new TabView { IsAddTabButtonVisible = false, CanReorderTabs = false }; grid.Children.Add(tabs.AtRow(1));
        void Tab(string name, UIElement content) => tabs.TabItems.Add(new TabViewItem { Header = name, IsClosable = false, Content = new ScrollViewer { Content = content } });
        var general = Column();
        foreach (var (key, label) in new[] { ("hotkeys_active", "Global hotkeys"), ("hotkeys_only_mode", "Hotkeys only"), ("force_cache_only", "Cache playback only"), ("global_ai_mute", "Global AI mute"), ("review_mode", "Review replies"), ("idle_chat", "Idle chat"), ("automated_replies", "Automatic replies"), ("greet_viewers", "Greet new viewers") })
        {
            var flag = new CheckBox { Content = label, IsChecked = _profile.Flag(key) };
            flag.Checked += (_, _) => _profile.Set(key, true); flag.Unchecked += (_, _) => _profile.Set(key, false);
            _hotkeyRefresh.Add(() => flag.IsChecked = _profile.Flag(key)); general.Children.Add(flag);
        }
        Tab("General", general);
        var allMode = new ComboBox { Header = "Set all smart-key output modes", ItemsSource = new[] { "V+T", "V", "T" }, Width = 250 };
        allMode.SelectionChanged += (_, _) => { if (allMode.SelectedItem is string value) { foreach (var key in Catalog.SmartKeys) _profile.Set(key + "_mode", value); RefreshHotkeyEditors(); } };
        Tab("AI & Output", Column(allMode, Button("AI prompts, directives and provider settings", () => { _settingsGroup.SelectedItem = "AI"; SelectTab(1); return Task.CompletedTask; })));
        var hype = new[] { "hk_taunt", "hk_intro", "hk_frustration", "hk_attack", "hk_talk_viewers", "hk_shoutout_users", "hk_puzzle", "hk_boss_win", "hk_feeling_good", "hk_feeling_bad", "hk_clip_that", "hk_panic" };
        string Group(string key) => key.StartsWith("hk_custom_") ? "Custom 1–15" : key.StartsWith("hk_react_") ? "Reactions" : key.StartsWith("hk_yt_") ? "YouTube" : key.StartsWith("hk_mp3_") || key == "hk_music_window" ? "Music" : Catalog.SystemKeys.Contains(key) || key == "hk_live_context" ? "System" : hype.Contains(key) ? "Hype" : "Chat Keys";
        foreach (var group in new[] { "Chat Keys", "Hype", "Custom 1–15", "Reactions", "System", "YouTube", "Music" })
        {
            var keys = Catalog.SmartKeys.Concat(Catalog.SystemKeys).Distinct().Where(k => Group(k) == group).ToArray();
            if (group is "System" or "YouTube" or "Music")
            {
                var rows = new Grid { Margin = new Thickness(10), ColumnSpacing = 12, RowSpacing = 8 };
                rows.ColumnDefinitions.Add(new ColumnDefinition()); rows.ColumnDefinitions.Add(new ColumnDefinition());
                for (var i = 0; i < keys.Length; i++)
                {
                    if (i % 2 == 0) rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var item = (FrameworkElement)BuildHotkeyRow(keys[i]); Grid.SetRow(item, i / 2); Grid.SetColumn(item, i % 2); rows.Children.Add(item);
                }
                Tab(group, rows);
            }
            else { var rows = Column(); foreach (var key in keys) rows.Children.Add(BuildHotkeyRow(key)); Tab(group, rows); }
        }
        grid.Children.Add(Row(Button("Save Changes & Re-Bind", () =>
        {
            foreach (var (key, editor) in _bindingEditors)
                if (editor.Text.Length > 0 && !KeyChord.TryParse(editor.Text, out _)) throw new InvalidOperationException($"Unsupported binding for {key}. Previous binding is retained until this is corrected.");
            foreach (var (key, editor) in _bindingEditors) _profile.Set(key, editor.Text.Trim());
            _profile.Save(); ApplyHotkeys(); _toolWindows["hotkeys"].Hide(); return Task.CompletedTask;
        })).AtRow(2));
        return grid;
    }
    private UIElement BuildHotkeyRow(string key)
    {
        var binding = new TextBox { Header = key.Replace("hk_", "").Replace('_', ' '), Text = _profile.Text(key), Width = 230 };
        _bindingEditors[key] = binding; _hotkeyRefresh.Add(() => binding.Text = _profile.Text(key));
        var row = Column(binding);
        if (Catalog.SmartKeys.Contains(key) || key is "hk_reply_last" or "hk_vision")
        {
            var mode = new ComboBox { Header = "Output", ItemsSource = new[] { "V+T", "V", "T" }, SelectedItem = _profile.Text(key + "_mode", "V+T"), Width = 100 };
            mode.SelectionChanged += (_, _) => { if (mode.SelectedItem is string value) _profile.Set(key + "_mode", value); }; _hotkeyRefresh.Add(() => mode.SelectedItem = _profile.Text(key + "_mode", "V+T"));
            var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; options.Children.Add(mode);
            foreach (var (suffix, label) in new[] { ("_short", "Short"), ("_audio_enabled", "Soundboard"), ("_mute_ai", "Mute AI") })
            {
                var box = new CheckBox { Content = label, IsChecked = _profile.Flag(key + suffix) }; box.Checked += (_, _) => _profile.Set(key + suffix, true); box.Unchecked += (_, _) => _profile.Set(key + suffix, false); _hotkeyRefresh.Add(() => box.IsChecked = _profile.Flag(key + suffix)); options.Children.Add(box);
            }
            row.Children.Add(options);
            foreach (var (suffix, label) in new[] { ("_custom", "Override text / phrases separated by |"), ("_prompt", "AI Prompt"), ("_audio", "Soundboard audio file") })
            {
                var text = new TextBox { Header = label, Text = _profile.Text(key + suffix), TextWrapping = TextWrapping.Wrap, AcceptsReturn = suffix != "_audio" };
                text.TextChanged += (_, _) => _profile.Set(key + suffix, text.Text); _hotkeyRefresh.Add(() => text.Text = _profile.Text(key + suffix)); row.Children.Add(text);
                if (suffix == "_audio") row.Children.Add(Button("Browse audio", async () => { var file = await PickAsync(".wav", ".mp3"); if (file is not null) text.Text = file.Path; }));
            }
        }
        return new Border { BorderBrush = Color(55, 55, 55), BorderThickness = new Thickness(1), Child = row };
    }
    private void RefreshHotkeyEditors() { foreach (var update in _hotkeyRefresh) update(); }
    private void ShowSystemStatus() => ShowTool("system-status", "System Status", () => Column(Label("StreamerBot Native"), new TextBlock { Text = $"Runtime: .NET {Environment.Version}\nProcess memory: {Environment.WorkingSet / 1024 / 1024} MB\nVoice engine: {_profile.Text("tts_mode")}\nChannel: {_profile.Text("channel_name")}" }, Button("GPU diagnostics", GpuAsync)), 420, 300);

    private async Task SmokeOriginalWorkflowsAsync()
    {
        var env = Path.Combine(Program.DataRoot, "synthetic.env");
        await File.WriteAllTextAsync(env, "CHANNEL_1_NAME=Preview Channel\nCHANNEL_1_ID=synthetic-one\nCHANNEL_1_TOKEN_PREFIX=one\nCHANNEL_3_NAME=Second Channel\nCHANNEL_3_ID=synthetic-three\nCHANNEL_3_TOKEN_PREFIX=three\n");
        OriginalEnvironment.Import(_profile, env); LoadToolbar();
        if (_channel.Items.Count != 2) throw new IOException("Imported .env channels did not populate the native dropdown.");
        _channel.SelectedIndex = 1;
        if (_profile.Text("channel_id") != "synthetic-three" || _profile.Number("channel_index") != 1) throw new IOException("Channel dropdown selection was not applied.");
        _channel.SelectedIndex = 0;
        _profile.Set("review_mode", true); LoadToolbar();
        if (!_menuToggles["review_mode"].IsChecked) throw new IOException("Imported menu toggle state did not refresh.");
        _profile.Set("review_mode", false); LoadToolbar();
        AddChat(new ChatMessage("synthetic-message", "Preview Viewer", "Hello! This is the restored native chat window.", "12:00:00"));
        _chat.Focus(FocusState.Programmatic);
        await HandleHotkeyAsync("hk_hotkeys_window"); await Task.Delay(500);
        var hotkeys = _toolWindows["hotkeys"];
        if (hotkeys.AppWindow.Presenter is not OverlappedPresenter { IsAlwaysOnTop: true } || _bindingEditors.Count != Catalog.SmartKeys.Concat(Catalog.SystemKeys).Distinct().Count()) throw new IOException("Detached keybind window did not load all bindings as topmost.");
        _hotkeyTabs!.SelectedItem = _hotkeyTabs.TabItems.OfType<TabViewItem>().Single(t => t.Header?.ToString() == "System"); await Task.Delay(200);
        await CaptureWindowAsync(hotkeys.Root, "hotkeys-preview.png"); hotkeys.Hide();
        ShowChatWindow(); await Task.Delay(300);
        var chat = _toolWindows["chat"];
        if (chat.AppWindow.Presenter is not OverlappedPresenter { IsAlwaysOnTop: true } || _chatList.Items.Count == 0) throw new IOException("Detached chat window did not load.");
        await CaptureWindowAsync(chat.Root, "chat-preview.png"); ShowChatWindow(toggle: true);
        if (chat.IsShown) throw new IOException("Chat hotkey toggle did not hide the window.");
        ShowChatWindow(toggle: true); if (!chat.IsShown) throw new IOException("Chat hotkey toggle did not reopen the window."); chat.Hide();
        var result = ReviewOnUiAsync(new ReviewRequest("Synthetic draft", "Preview Viewer: hello", "V+T"), _lifetime.Token);
        await Task.Delay(400);
        if (_reviewWindow is null || _reviewWindow.Window.AppWindow.Presenter is not OverlappedPresenter { IsAlwaysOnTop: true }) throw new IOException("Review did not open in a topmost native window.");
        await CaptureWindowAsync(_reviewWindow.Window.Root, "review-preview.png");
        _reviewWindow.Editor.Text = "Edited synthetic reply"; _reviewWindow.Submit("T");
        if (await result != new ReviewDecision("Edited synthetic reply", "T")) throw new IOException("Review did not retain edited text and chosen output mode.");
        var reject = ReviewOnUiAsync(new ReviewRequest("Reject me", "synthetic", "V"), _lifetime.Token); await Task.Delay(100); _reviewWindow!.Reject();
        if (await reject is not null) throw new IOException("Rejected review was delivered.");
        await File.WriteAllTextAsync(Path.Combine(Program.DataRoot, "original-workflows-ready.txt"), "Imported channels and selection; topmost keybind/chat/review windows; chat toggle/reopen; edited review output mode; rejection passed.");
    }
    private async Task CaptureWindowAsync(FrameworkElement root, string name)
    {
        if (root.ActualWidth <= 0 || root.ActualHeight <= 0) throw new IOException("Detached window did not lay out: " + name);
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(root);
        var buffer = await bitmap.GetPixelsAsync(); var bytes = new byte[buffer.Length]; using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer)) reader.ReadBytes(bytes);
        var folder = await StorageFolder.GetFolderFromPathAsync(Program.DataRoot); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes); await encoder.FlushAsync();
    }
}
