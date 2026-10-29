using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using StreamerBot.Native.Core;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;

namespace StreamerBot.Native;

internal sealed class OverlayWindow : Window
{
    private readonly TextBlock _caption = new() { FontSize = 28, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20) };
    internal OverlayWindow(Profile profile)
    {
        Title = "StreamerBot captions"; AppWindow.Resize(new SizeInt32(700, 250));
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = true;
        var panel = new StackPanel { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 20, 28)), RequestedTheme = ElementTheme.Dark };
        panel.Children.Add(_caption); _caption.Text = "Stream captions appear here.";
        var avatar = profile.Text("avatar_path");
        if (avatar.Length > 0 && File.Exists(profile.Resolve(avatar))) panel.Children.Insert(0, new Image { Source = new BitmapImage(new Uri(profile.Resolve(avatar))), Height = 90 });
        Content = panel;
    }
    internal void SetCaption(string text) => _caption.Text = text;
}
internal sealed class MusicWindow : Window
{
    private readonly Profile _profile;
    private readonly MediaPlayer _player = new();
    private readonly List<string> _files = [];
    private readonly ListView _list = new();
    private int _index = -1;
    internal MusicWindow(Profile profile)
    {
        _profile = profile; Title = "StreamerBot music"; AppWindow.Resize(new SizeInt32(620, 500));
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(20), RequestedTheme = ElementTheme.Dark };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        void Add(string label, Action action) { var b = new Button { Content = label }; b.Click += (_, _) => action(); controls.Children.Add(b); }
        var open = new Button { Content = "Add audio files…" }; open.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker(); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            foreach (var extension in new[] { ".mp3", ".wav", ".m4a", ".flac" }) picker.FileTypeFilter.Add(extension);
            foreach (var file in await picker.PickMultipleFilesAsync()) { _files.Add(file.Path); _list.Items.Add(file.Name); }
            _profile.Set("native_music_files", new System.Text.Json.Nodes.JsonArray(_files.Select(s => System.Text.Json.Nodes.JsonValue.Create(s)).ToArray())); _profile.Save();
        };
        Add("Previous", () => Next(-1)); Add("Play / pause", Toggle); Add("Next", () => Next(1));
        var volume = new Slider { Header = "Music volume", Minimum = 0, Maximum = 1, Value = profile.Number("music_volume", 0.3) }; volume.ValueChanged += (_, _) => { _player.Volume = volume.Value; _profile.Set("music_volume", volume.Value); };
        _player.Volume = volume.Value; _player.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(() => Next(1));
        var musicFolder = ResourcePaths.Get(profile, "music_folder");
        var folderFiles = Directory.Exists(musicFolder) ? Directory.EnumerateFiles(musicFolder).Where(p => new[] { ".mp3", ".wav", ".flac", ".m4a" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)) : [];
        foreach (var file in profile.Keys("native_music_files").Concat(folderFiles).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase)) { _files.Add(file); _list.Items.Add(Path.GetFileName(file)); }
        _list.SelectionChanged += (_, _) => { if (_list.SelectedIndex >= 0 && _list.SelectedIndex != _index) Play(_list.SelectedIndex); };
        panel.Children.Add(open); panel.Children.Add(controls); panel.Children.Add(volume); panel.Children.Add(_list); Content = panel;
        Closed += (_, _) => _player.Dispose();
    }
    private void Play(int index) { if (_files.Count == 0) return; _index = (index + _files.Count) % _files.Count; _player.Source = MediaSource.CreateFromUri(new Uri(_files[_index])); _list.SelectedIndex = _index; _player.Play(); }
    internal void Next(int direction) => Play(_index + direction);
    internal void Toggle() { if (_index < 0) Play(0); else if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) _player.Pause(); else _player.Play(); }
}
internal sealed class YouTubeOverlay : Window
{
    internal YouTubeOverlay(Profile profile)
    {
        Title = "StreamerBot YouTube auxiliary player"; AppWindow.Resize(new SizeInt32(850, 600));
        var panel = new Grid(); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition());
        var entry = new TextBox { Text = profile.Text("native_youtube_player_url", "https://www.youtube.com/"), Margin = new Thickness(10) };
        var web = new WebView2(); Grid.SetRow(web, 1); panel.Children.Add(entry); panel.Children.Add(web); Content = panel;
        entry.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) await NavigateAsync(); };
        panel.Loaded += async (_, _) => await NavigateAsync();
        async Task NavigateAsync()
        {
            try
            {
                if (!Uri.TryCreate(entry.Text, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !(uri.Host == "youtube.com" || uri.Host.EndsWith(".youtube.com") || uri.Host == "youtu.be")) throw new InvalidOperationException("Enter an HTTPS YouTube address.");
                await web.EnsureCoreWebView2Async(); web.Source = uri; profile.Set("native_youtube_player_url", uri.ToString());
            }
            catch (Exception) { entry.Text = "YouTube player unavailable. Install Microsoft Edge WebView2 Runtime, then reopen this auxiliary window."; }
        }
        Closed += (_, _) => web.Close();
    }
}

internal sealed class AvatarVideoWindow : Window
{
    private readonly MediaPlayer _player = new();
    private TaskCompletionSource? _completion;
    internal AvatarVideoWindow()
    {
        Title = "StreamerBot avatar video"; AppWindow.Resize(new SizeInt32(720, 620));
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = true;
        var video = new MediaPlayerElement { AreTransportControlsEnabled = false, Stretch = Stretch.Uniform };
        video.SetMediaPlayer(_player); Content = video;
        _player.MediaEnded += (_, _) => _completion?.TrySetResult();
        _player.MediaFailed += (_, _) => _completion?.TrySetException(new IOException("Avatar video playback failed. Check the video format and output device."));
        Closed += (_, _) => { Stop(); _player.Dispose(); };
    }
    internal async Task PlayAsync(string path, Profile profile, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var device = profile.Text("native_speaker_id");
        if (device.Length > 0) _player.AudioDevice = await Windows.Devices.Enumeration.DeviceInformation.CreateFromIdAsync(device);
        _player.Volume = Math.Clamp(profile.Number("volume", 1), 0, 1);
        _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _player.Source = MediaSource.CreateFromUri(new Uri(Path.GetFullPath(path)));
        using var cancel = token.Register(Stop);
        _player.Play(); await _completion.Task;
    }
    internal void Stop() { _player.Pause(); _completion?.TrySetCanceled(); }
}
