using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StreamerBot.Native.Core;

namespace StreamerBot.Native;

internal sealed partial class NativeWindow
{
    private readonly TextBlock _viewerCount = new() { Text = "--", FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _likeCount = new() { Text = "--", FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center };
    private bool _updatingStatistics;
    private UIElement BuildStatistics()
    {
        var grid = new Grid();
        var tiles = new[] { ("VIEWERS", _viewerCount, Color(65, 0, 0)), ("LIKES", _likeCount, Color(0, 0, 65)), ("SUBS", new TextBlock { Text = "--", FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center }, Color(50, 45, 0)) };
        for (var i = 0; i < tiles.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition()); var (name, value, background) = tiles[i];
            var tile = new StackPanel { Background = background, Spacing = 20, Padding = new Thickness(8) };
            tile.Children.Add(new TextBlock { Text = name, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.Bold }); tile.Children.Add(value);
            FloatingWindow.AttachDrag(tile, () => _toolWindows["stats"]);
            Grid.SetColumn(tile, i); grid.Children.Add(tile);
        }
        return grid;
    }
    private async Task UpdateStatisticsAsync()
    {
        if (_updatingStatistics) return;
        _updatingStatistics = true;
        try
        {
            var path = OriginalEnvironment.TokenFile(_profile);
            if (!File.Exists(path)) { WriteLog("Statistics: authorize the selected channel to load live counts."); return; }
            var client = new YouTubeClient(_http, path, OriginalEnvironment.Selected(_profile)?.ChannelId);
            while (!_lifetime.IsCancellationRequested && _toolWindows.TryGetValue("stats", out var window) && window.IsShown)
            {
                if (await client.FindLiveAsync(_lifetime.Token))
                {
                    var result = await client.StatisticsAsync(_lifetime.Token);
                    var video = (result["items"] as System.Text.Json.Nodes.JsonArray)?.FirstOrDefault();
                    _viewerCount.Text = video?["liveStreamingDetails"]?["concurrentViewers"]?.ToString() ?? "--";
                    _likeCount.Text = video?["statistics"]?["likeCount"]?.ToString() ?? "--";
                }
                else { _viewerCount.Text = "--"; _likeCount.Text = "--"; }
                await Task.Delay(TimeSpan.FromSeconds(30), _lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { _updatingStatistics = false; }
    }
}
