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
        var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); layout.ColumnDefinitions.Add(new ColumnDefinition());
        toolbar.Margin = controls.Margin = new Thickness(6); toolbar.Spacing = controls.Spacing = 6;
        var scroll = new ScrollViewer { Content = toolbar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        layout.Children.Add(scroll); layout.Children.Add(controls);
        layout.SizeChanged += (_, e) => { var compact = e.NewSize.Width < 1400; Grid.SetRow(controls, compact ? 1 : 0); Grid.SetColumn(controls, compact ? 0 : 1); Grid.SetColumnSpan(scroll, compact ? 2 : 1); Grid.SetColumnSpan(controls, compact ? 2 : 1); };
        return layout;
    }
    private FrameworkElement BuildOriginalMain()
    {
        var grid = new Grid { Margin = new Thickness(10), RowSpacing = 6 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 100 });
        grid.Children.Add(Row(new TextBlock { Text = "LIVE CHAT FEED", Foreground = Color(0, 230, 255), FontWeight = Microsoft.UI.Text.FontWeights.Bold }, Button("Clear Chat", () => { _chat.Items.Clear(); _messages.Clear(); RefreshChatList(); return Task.CompletedTask; })));
        _chat.Background = Color(0, 0, 0); grid.Children.Add(_chat.AtRow(1));
        var divider = new Microsoft.UI.Xaml.Controls.Primitives.Thumb { Height = 7, Background = Color(80, 80, 80), HorizontalAlignment = HorizontalAlignment.Stretch };
        divider.DragDelta += (_, e) =>
        {
            var available = grid.RowDefinitions[1].ActualHeight + grid.RowDefinitions[3].ActualHeight;
            if (available <= 200) return;
            var chatHeight = Math.Clamp(grid.RowDefinitions[1].ActualHeight + e.VerticalChange, 100, available - 100);
            grid.RowDefinitions[1].Height = new GridLength(chatHeight, GridUnitType.Star);
            grid.RowDefinitions[3].Height = new GridLength(available - chatHeight, GridUnitType.Star);
        };
        grid.Children.Add(new StackPanel { Spacing = 6, Children = { divider, new TextBlock { Text = "System Log", Foreground = Color(0, 230, 255) } } }.AtRow(2));
        _log.Background = Color(17, 17, 17); _log.Foreground = Color(0, 255, 0); _log.FontFamily = new FontFamily("Consolas"); _log.FontSize = 12;
        grid.Children.Add(_log.AtRow(3)); return grid;
    }
    private FloatingWindow ShowTool(string key, string title, Func<UIElement> build, int width, int height)
    {
        if (!_toolWindows.TryGetValue(key, out var window))
        {
            window = new FloatingWindow(_profile, title, key == "chat" ? "geo_selector" : key == "shortcuts" ? "geo_guide" : key == "stats" ? "geo_stats" : "geo_native_" + key, build(), width, height, borderless: key is "chat" or "shortcuts" or "stats");
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
        if (!simulation && !File.Exists(path)) throw new InvalidOperationException($"No authorized YouTube token found for {channel?.Name ?? "this account"}. Keep the original credential folder beside your imported .env/config. Existing tokens need no separate import.");
        _profile.Save();
        _controller.Start(simulation ? null : new YouTubeClient(_http, OriginalEnvironment.TokenFiles(_profile), channel?.ChannelId), simulation);
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
            new TextBlock { Text = "The credential (or credentials) folder beside your original .env/config is detected automatically. Existing channel tokens are ready to use and rotate on daily quota exhaustion.\nDetected folder: " + OriginalEnvironment.CredentialsDirectory(_profile) + "\nChannel tokens: " + OriginalEnvironment.TokenFiles(_profile).Length, TextWrapping = TextWrapping.Wrap },
            new Expander { Header = "Repair or override account setup (optional)", Content = Column(
                Button("Use a different credentials folder", ChooseCredentialsFolderAsync),
                Button("Reconnect an expired/revoked account", AuthorizeChannelAsync)) },
            Button("API keys and voice credentials", () => { _settingsGroup.SelectedItem = "AI"; SelectTab(1); return Task.CompletedTask; }),
            Button("Models, Python and cache paths", PathsAsync)), 560, 500);
        return Task.CompletedTask;
    }
    private async Task ChooseCredentialsFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker(); picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync(); if (folder is null) return;
        await StopAsync();
        _profile.Set("native_credentials_directory", folder.Path); _profile.Save();
        WriteLog("Shared credentials folder saved: " + folder.Path + ". Found " + OriginalEnvironment.TokenFiles(_profile).Length + " token(s) for the selected channel.");
    }
    private bool _authorizing;
    private async Task AuthorizeChannelAsync()
    {
        if (_authorizing) { WriteLog("Channel authorization is already open in your browser."); return; }
        var channel = OriginalEnvironment.Selected(_profile) ?? throw new InvalidOperationException("Select or add a channel before connecting its Google account.");
        var file = await PickAsync(".json"); if (file is null) return;
        if (OriginalEnvironment.CredentialsDirectory(_profile).Length == 0)
            _profile.Set("native_credentials_directory", Path.GetDirectoryName(file.Path)!);
        var path = OriginalEnvironment.TokenForSecret(_profile, file.Path);
        _authorizing = true;
        try
        {
            await StopAsync();
            WriteLog("Complete Google authorization in your browser for " + channel.Name + ".");
            var credentials = await YouTubeAuthorization.AuthorizeAsync(_http, file.Path, uri => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }), _lifetime.Token);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllTextAsync(temporary, credentials.ToJsonString(), _lifetime.Token); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            _profile.Save();
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
        var header = new Grid { ColumnSpacing = 8 }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dragArea = new Border { Name = "OverlayDragHandle", Background = Color(30, 30, 30), Padding = new Thickness(8), Child = title };
        FloatingWindow.AttachDrag(dragArea, () => _toolWindows["chat"]);
        header.Children.Add(dragArea);
        var close = Button("×", () => { _toolWindows["chat"].Hide(); return Task.CompletedTask; }); Grid.SetColumn(close, 1); header.Children.Add(close); grid.Children.Add(header);
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
        var grip = new TextBlock { Name = "OverlayResizeGrip", Text = "◢", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, FontSize = 20 };
        FloatingWindow.AttachDrag(grip, () => _toolWindows["chat"], resize: true);
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
            else { var rows = new StackPanel { Spacing = 8, Margin = new Thickness(10) }; foreach (var key in keys) rows.Children.Add(BuildHotkeyRow(key)); Tab(group, rows); }
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
        var binding = new TextBox { Text = _profile.Text(key), Width = 150, MinHeight = 28, Padding = new Thickness(5, 2, 5, 2), Foreground = Color(255, 255, 255), Background = Color(0, 110, 0) };
        _bindingEditors[key] = binding; _hotkeyRefresh.Add(() => binding.Text = _profile.Text(key));
        var heading = new TextBlock { Text = HotkeyLabel(key), Width = 170, VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        top.Children.Add(heading); top.Children.Add(binding);
        var row = new StackPanel { Spacing = 4, Margin = new Thickness(8) }; row.Children.Add(top);
        if (Catalog.SmartKeys.Contains(key) || key is "hk_reply_last" or "hk_vision")
        {
            var mode = new ComboBox { ItemsSource = new[] { "V+T", "V", "T" }, SelectedItem = _profile.Text(key + "_mode", "V+T"), Width = 75, MinHeight = 28, Padding = new Thickness(5, 2, 5, 2) };
            mode.SelectionChanged += (_, _) => { if (mode.SelectedItem is string value) _profile.Set(key + "_mode", value); }; _hotkeyRefresh.Add(() => mode.SelectedItem = _profile.Text(key + "_mode", "V+T")); top.Children.Add(mode);
            CheckBox Flag(string suffix, string label)
            {
                var box = new CheckBox { Content = label, IsChecked = _profile.Flag(key + suffix), MinHeight = 28 };
                box.Checked += (_, _) => _profile.Set(key + suffix, true); box.Unchecked += (_, _) => _profile.Set(key + suffix, false); _hotkeyRefresh.Add(() => box.IsChecked = _profile.Flag(key + suffix)); return box;
            }
            top.Children.Add(Flag("_short", "Short"));
            var fields = new Grid { ColumnSpacing = 8 };
            fields.ColumnDefinitions.Add(new ColumnDefinition()); fields.ColumnDefinitions.Add(new ColumnDefinition()); fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foreach (var (suffix, label, column) in new[] { ("_custom", "Override:", 0), ("_prompt", "AI Prompt:", 1) })
            {
                var text = new TextBox { Text = _profile.Text(key + suffix), MinHeight = 28, Padding = new Thickness(4, 2, 4, 2), Foreground = column == 0 ? Color(0, 230, 255) : Color(255, 220, 0) };
                text.TextChanged += (_, _) => _profile.Set(key + suffix, text.Text); _hotkeyRefresh.Add(() => text.Text = _profile.Text(key + suffix));
                var field = new Grid { ColumnSpacing = 5 }; field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); field.ColumnDefinitions.Add(new ColumnDefinition());
                field.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(text, 1); field.Children.Add(text); Grid.SetColumn(field, column); fields.Children.Add(field);
            }
            var sound = Button("♫ Sound", async () =>
            {
                var path = new TextBox { Header = "Soundboard audio file", Text = _profile.Text(key + "_audio") };
                var enabled = new CheckBox { Content = "Play sound with this key", IsChecked = _profile.Flag(key + "_audio_enabled") };
                var content = Column(enabled, path, Button("Browse audio", async () => { var file = await PickAsync(".wav", ".mp3"); if (file is not null) path.Text = file.Path; }));
                if (await new ContentDialog { XamlRoot = _toolWindows["hotkeys"].Root.XamlRoot, Title = HotkeyLabel(key) + " sound", Content = content, PrimaryButtonText = "Apply", CloseButtonText = "Cancel" }.ShowAsync() == ContentDialogResult.Primary)
                { _profile.Set(key + "_audio", path.Text); _profile.Set(key + "_audio_enabled", enabled.IsChecked == true); }
            });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; var mute = Flag("_mute_ai", "Mute AI"); mute.Foreground = Color(255, 70, 70); actions.Children.Add(mute); actions.Children.Add(sound); Grid.SetColumn(actions, 2); fields.Children.Add(actions);
            row.Children.Add(fields);
        }
        return new Border { BorderBrush = Color(110, 110, 110), BorderThickness = new Thickness(1), Background = Color(35, 35, 35), Child = row };
    }
    private static string HotkeyLabel(string key) => key switch
    {
        "hk_react_1" => "1. Uh-oh!", "hk_react_2" => "2. Surprise / Gasp", "hk_react_3" => "3. Grunt / Effort", "hk_react_4" => "4. Attack / Thrust", "hk_react_5" => "5. Hit / Pain", "hk_react_6" => "6. Laugh", "hk_react_7" => "7. Sigh / Annoyed", "hk_react_8" => "8. Scared", "hk_react_9" => "9. Hype / Yes!", "hk_react_10" => "10. Confused", "hk_react_11" => "11. Nice / Impressed", "hk_react_12" => "12. Oof / Cringe",
        "hk_idle" => "Idle Chat", "hk_thanks" => "Thanks / Greet", "hk_question" => "Game Question", "hk_help" => "Ask for Help", "hk_death" => "Death / Rage", "hk_likes_thanks" => "Thanks for Likes", "hk_brb" => "BRB / AFK", "hk_tech_issue" => "Tech Issue", "hk_talk_viewers" => "Talk Trends", "hk_attack" => "Attack Callout", "hk_puzzle" => "Puzzle Solved", "hk_boss_win" => "Boss Defeated", "hk_clip_that" => "Clip That!", "hk_panic" => "Panic/Scream", "hk_shoutout_users" => "Shoutout Users", "hk_guide" => "Keyboard Shortcuts", "hk_open_context_window" => "Game Context", "hk_guide_ghost" => "Toggle Guide Ghost", "hk_stats_ghost" => "Toggle Stats Ghost",
        _ => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key.Replace("hk_", "").Replace('_', ' '))
    };
    private void RefreshHotkeyEditors() { foreach (var update in _hotkeyRefresh) update(); }
    private void ShowSystemStatus() => ShowTool("system-status", "System Status", () => Column(Label("StreamerBot Native"), new TextBlock { Text = $"Runtime: .NET {Environment.Version}\nProcess memory: {Environment.WorkingSet / 1024 / 1024} MB\nVoice engine: {_profile.Text("tts_mode")}\nChannel: {_profile.Text("channel_name")}" }, Button("GPU diagnostics", GpuAsync)), 420, 300);

    private async Task SmokeOriginalWorkflowsAsync()
    {
        Program.Trace("Smoke: importing channels");
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
        // Keyboard smoke leaves a text editor focused. Focus a laid-out message before
        // invoking shortcuts, whose production contract deliberately ignores text editing.
        await Task.Delay(100); _chat.UpdateLayout();
        if (_chat.ContainerFromIndex(0) is Control messageItem) messageItem.Focus(FocusState.Programmatic);
        else _chat.Focus(FocusState.Programmatic);
        if (FloatingWindow.IsEditing(this, _root)) throw new IOException("Chat message focus did not leave the game text editor.");
        Program.Trace("Smoke: original game session and direct cache dispatch");
        var sharedCache = Path.Combine(Program.DataRoot, "original cache fixture");
        _profile.Set("voice_cache_path", sharedCache);
        var gameData = _profile.Data.DeepClone().AsObject(); gameData.Remove("voice_cache_path");
        gameData["game_name"] = "Shadow Of The  Colossus"; gameData["tts_mode"] = "typecast";
        gameData["ai_mode"] = "groq"; gameData["hk_death_prompt"] = "Synthetic loaded game prompt";
        gameData["hk_death_mode"] = "V"; gameData["hk_death_mute_ai"] = false;
        gameData["force_cache_only"] = true; gameData["global_ai_mute"] = false;
        var gameFile = Path.Combine(Program.DataRoot, "synthetic-game-session.json");
        await File.WriteAllTextAsync(gameFile, gameData.ToJsonString());
        OriginalEnvironment.ImportProfile(_profile, gameFile); LoadToolbar(); RefreshHotkeyEditors(); _profile.Save();
        if (_game.Text != "Shadow Of The  Colossus" || _voices.SelectedItem?.ToString() != "typecast" || _profile.Text("voice_cache_path") != sharedCache || _profile.Text("hk_death_prompt") != "Synthetic loaded game prompt") throw new IOException("Game-session UI/cache settings did not restore together.");
        var credentialFolder = Path.Combine(Program.DataRoot, "credential"); Directory.CreateDirectory(credentialFolder);
        var tokenFile = Path.Combine(credentialFolder, "one_token_1.json"); await File.WriteAllTextAsync(tokenFile, "{}");
        if (OriginalEnvironment.TokenFile(_profile) != tokenFile) throw new IOException("Original singular credential folder was not discovered automatically.");
        var cacheFolder = Path.Combine(sharedCache, "typecast_cache", "Preview_Channel", "Shadow_Of_The__Colossus", "voice", "death"); Directory.CreateDirectory(cacheFolder);
        var cacheFile = Path.Combine(cacheFolder, "death_original.wav"); await File.WriteAllBytesAsync(cacheFile, new byte[] { 1, 2, 3 });
        await File.WriteAllTextAsync(Path.ChangeExtension(cacheFile, ".txt"), "Synthetic cached phrase");
        string? dispatched = null;
        Task ObserveCache(string path, CancellationToken token) { dispatched = path; return Task.CompletedTask; }
        _controller.PlayCachedAudio -= PlayExistingClipAsync; _controller.PlayCachedAudio += ObserveCache;
        try
        {
            await HandleHotkeyAsync("hk_death");
            if (dispatched != cacheFile) throw new IOException("Force-cache hotkey did not dispatch the original clip directly.");
            _profile.Set("force_cache_only", false); dispatched = null;
            var firstTap = HandleHotkeyAsync("hk_death"); await Task.Delay(100);
            var secondTap = HandleHotkeyAsync("hk_death"); await Task.WhenAll(firstTap, secondTap);
            if (dispatched != cacheFile) throw new IOException("Double-tap did not select the original cached clip.");
            dispatched = null; var pendingTap = HandleHotkeyAsync("hk_death"); await Task.Delay(100); Interrupt(); await pendingTap;
            if (dispatched is not null) throw new IOException("Stop Audio did not cancel a pending hotkey.");
            _profile.Set("force_cache_only", true);
            await HandleHotkeyAsync("hk_toggle_cache_mode");
            if (!_profile.Flag("force_cache_only") || _profile.Text("cache_playback_mode") != "repeat_all") throw new IOException("Cache-mode shortcut changed Force Cache instead of playback mode.");
        }
        finally { _controller.PlayCachedAudio -= ObserveCache; _controller.PlayCachedAudio += PlayExistingClipAsync; }
        await File.WriteAllTextAsync(Path.Combine(Program.DataRoot, "game-cache-ready.txt"), "Game session UI restore, shared cache retention, singular credential discovery and original force-cache/double-tap dispatch and pending-tap cancellation passed. Media output mocked; no audio device/provider used.");
        Program.Trace("Smoke: opening keybind window");
        await HandleHotkeyAsync("hk_hotkeys_window"); await Task.Delay(500);
        var hotkeys = _toolWindows["hotkeys"];
        if (hotkeys.AppWindow.Presenter is not OverlappedPresenter { IsAlwaysOnTop: true } || _bindingEditors.Count != Catalog.SmartKeys.Concat(Catalog.SystemKeys).Distinct().Count()) throw new IOException("Detached keybind window did not load all bindings as topmost.");
        _hotkeyTabs!.SelectedItem = _hotkeyTabs.TabItems.OfType<TabViewItem>().Single(t => t.Header?.ToString() == "System"); await Task.Delay(200);
        await CaptureWindowAsync(hotkeys.Root, "hotkeys-preview.png"); hotkeys.Hide();
        _hotkeyTabs.SelectedItem = _hotkeyTabs.TabItems.OfType<TabViewItem>().Single(t => t.Header?.ToString() == "Reactions"); hotkeys.Show(); await Task.Delay(200);
        await CaptureWindowAsync(hotkeys.Root, "reactions-preview.png"); hotkeys.Hide();
        Program.Trace("Smoke: opening shortcuts");
        _chat.Focus(FocusState.Programmatic); await HandleHotkeyAsync("hk_guide"); await Task.Delay(200);
        var guide = _toolWindows["shortcuts"];
        if (!guide.IsShown || guide.AppWindow.Presenter is not OverlappedPresenter { IsAlwaysOnTop: true }) throw new IOException("Keyboard Shortcuts did not open as a topmost window.");
        await SmokeOverlayDragAsync("shortcuts");
        await CaptureWindowAsync(guide.Root, "shortcuts-preview.png");
        guide.ToggleGhost(); guide.ToggleGhost(); ShowShortcutGuide(); if (guide.IsShown) throw new IOException("Keyboard Shortcuts did not toggle closed.");
        ShowShortcutGuide(); if (!guide.IsShown) throw new IOException("Keyboard Shortcuts did not reopen."); guide.Hide();
        Program.Trace("Smoke: opening statistics");
        await StatsAsync(); await Task.Delay(150);
        if (!_toolWindows["stats"].IsShown) throw new IOException("Statistics overlay did not open.");
        await CaptureWindowAsync(_toolWindows["stats"].Root, "stats-preview.png"); await StatsAsync();
        Program.Trace("Smoke: opening chat");
        ShowChatWindow(); await Task.Delay(300);
        var chat = _toolWindows["chat"];
        if (chat.AppWindow.Presenter is not OverlappedPresenter { IsAlwaysOnTop: true } || _chatList.Items.Count == 0) throw new IOException("Detached chat window did not load.");
        await SmokeOverlayDragAsync("chat");
        await File.WriteAllTextAsync(Path.Combine(Program.DataRoot, "overlay-drag-ready.txt"), "Chat/shortcuts drag, hide/reopen position, chat resize passed using Windows mouse input.");
        await CaptureWindowAsync(chat.Root, "chat-preview.png"); ShowChatWindow(toggle: true);
        if (chat.IsShown) throw new IOException("Chat hotkey toggle did not hide the window.");
        ShowChatWindow(toggle: true); if (!chat.IsShown) throw new IOException("Chat hotkey toggle did not reopen the window."); chat.Hide();
        Program.Trace("Smoke: opening review");
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
