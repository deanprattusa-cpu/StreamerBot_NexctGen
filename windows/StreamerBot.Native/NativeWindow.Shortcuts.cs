using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace StreamerBot.Native;

internal sealed partial class NativeWindow
{
    private void ShowShortcutGuide()
    {
        if (_toolWindows.TryGetValue("shortcuts", out var existing))
        {
            if (existing.IsShown) { existing.Hide(); return; }
            existing.Root.Children.Clear(); existing.Root.Children.Add(BuildShortcutGuide());
        }
        var window = ShowTool("shortcuts", "Keyboard Shortcuts", BuildShortcutGuide, 380, 750);
        window.Root.Background = Color(12, 75, 142);
        window.SetOpacity(_profile.Number("overlay_opacity", 0.85));
    }
    private UIElement BuildShortcutGuide()
    {
        var grid = new Grid { Margin = new Thickness(12), RowSpacing = 12 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = "Keyboard Shortcuts", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        title.PointerPressed += (_, e) => { if (e.GetCurrentPoint(title).Properties.IsLeftButtonPressed) { e.Handled = true; _toolWindows["shortcuts"].Drag(); } };
        header.Children.Add(title); var close = Button("×", () => { _toolWindows["shortcuts"].Hide(); return Task.CompletedTask; }); Grid.SetColumn(close, 1); header.Children.Add(close); grid.Children.Add(header);
        var sections = new StackPanel { Spacing = 22 };
        var groups = new (string Title, string[] Keys)[]
        {
            ("Chat Reactions", ["hk_idle", "hk_thanks", "hk_question", "hk_help", "hk_death", "hk_likes_thanks", "hk_brb", "hk_tech_issue", "hk_talk_viewers", "hk_attack"]),
            ("Hype & Moments", ["hk_puzzle", "hk_boss_win", "hk_feeling_good", "hk_feeling_bad", "hk_clip_that", "hk_panic", "hk_shoutout_users"]),
            ("Media & YouTube", ["hk_yt_play_pause", "hk_yt_next", "hk_yt_forward", "hk_yt_ghost", "hk_mp3_play_pause", "hk_mp3_next"]),
            ("System Overrides", ["hk_reply_last", "hk_live_context", "hk_suspend_hotkeys", "hk_stop_audio", "hk_toggle_ai_mute", "hk_stats_ghost", "hk_guide_ghost", "hk_strict_mode", "hk_hide_overlays"]),
            ("Custom Hotkeys", Enumerable.Range(1, 15).Select(i => "hk_custom_" + i).ToArray())
        };
        foreach (var group in groups)
        {
            var section = new StackPanel { Spacing = 4 };
            section.Children.Add(new TextBlock { Text = group.Title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
            foreach (var key in group.Keys)
            {
                var chord = _profile.Text(key); if (chord.Length == 0) continue;
                var label = HotkeyLabel(key);
                if (key.StartsWith("hk_custom_")) { var custom = _profile.Text(key + "_custom", _profile.Text(key + "_prompt")); if (custom.Length > 0) label = custom; }
                var row = new Grid { Padding = new Thickness(2, 2, 2, 4), ColumnSpacing = 8, BorderBrush = Color(42, 112, 182), BorderThickness = new Thickness(0, 0, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
                var cap = new Border { Background = Color(255, 255, 255), Padding = new Thickness(6, 3, 6, 3), Child = new TextBlock { Text = chord.ToUpperInvariant().Replace("+", " + "), FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = Color(12, 75, 142) } };
                Grid.SetColumn(cap, 1); row.Children.Add(cap); section.Children.Add(row);
            }
            if (section.Children.Count > 1) sections.Children.Add(section);
        }
        grid.Children.Add(new ScrollViewer { Content = sections, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }.AtRow(1));
        var grip = new TextBlock { Text = "◢", HorizontalAlignment = HorizontalAlignment.Right };
        grip.PointerPressed += (_, e) => { if (e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) { e.Handled = true; _toolWindows["shortcuts"].Drag(resize: true); } };
        grid.Children.Add(grip.AtRow(2)); return grid;
    }
}
