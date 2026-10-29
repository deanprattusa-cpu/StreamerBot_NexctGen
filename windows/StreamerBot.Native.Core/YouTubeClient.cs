using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace StreamerBot.Native.Core;

public sealed record ChatMessage(string Id, string Author, string Text, string PublishedAt);
public sealed class YouTubeClient(HttpClient http, string credentialFile)
{
    private string _access = "";
    private DateTimeOffset _expires;
    public string? ChatId { get; private set; }
    public string? VideoId { get; private set; }
    private string? _page;
    private string? _ownerChannel;
    private readonly HashSet<string> _seen = [];
    private bool _initial = true;
    private async Task<string> AccessAsync(CancellationToken token)
    {
        if (_access.Length > 0 && _expires > DateTimeOffset.UtcNow.AddMinutes(1)) return _access;
        var credentials = JsonNode.Parse(await File.ReadAllTextAsync(credentialFile, token))?.AsObject() ?? throw new InvalidDataException("Invalid YouTube token file.");
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["client_id"] = credentials["client_id"]?.GetValue<string>() ?? "", ["client_secret"] = credentials["client_secret"]?.GetValue<string>() ?? "", ["refresh_token"] = credentials["refresh_token"]?.GetValue<string>() ?? "", ["grant_type"] = "refresh_token" }), token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"YouTube authorization failed (HTTP {(int)response.StatusCode}). Reconnect the account.");
        var data = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: token);
        _access = data?["access_token"]?.GetValue<string>() ?? throw new InvalidDataException("Missing YouTube access token.");
        _expires = DateTimeOffset.UtcNow.AddSeconds(data?["expires_in"]?.GetValue<int>() ?? 3600);
        return _access;
    }
    private async Task<JsonObject> GetAsync(string query, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessAsync(token));
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"YouTube request failed (HTTP {(int)response.StatusCode}). Check permissions, quota and stream status.");
        return await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: token) ?? throw new InvalidDataException("Empty YouTube response.");
    }
    public async Task<bool> FindLiveAsync(CancellationToken token)
    {
        var root = await GetAsync("liveBroadcasts?part=id,snippet,status&mine=true&maxResults=50", token);
        foreach (var item in root["items"]?.AsArray() ?? [])
        {
            if (item?["status"]?["lifeCycleStatus"]?.GetValue<string>() != "live") continue;
            VideoId = item["id"]!.GetValue<string>();
            _ownerChannel = item["snippet"]?["channelId"]?.GetValue<string>();
            var video = await GetAsync("videos?part=liveStreamingDetails&id=" + Uri.EscapeDataString(VideoId), token);
            ChatId = video["items"]?[0]?["liveStreamingDetails"]?["activeLiveChatId"]?.GetValue<string>();
            if (ChatId is not null) return true;
        }
        return false;
    }
    public async Task<(ChatMessage[] Messages, int Delay)> PollAsync(CancellationToken token)
    {
        if (ChatId is null) return ([], 10000);
        var query = $"liveChat/messages?part=snippet,authorDetails&maxResults=200&liveChatId={Uri.EscapeDataString(ChatId)}";
        if (_page is not null) query += "&pageToken=" + Uri.EscapeDataString(_page);
        var root = await GetAsync(query, token);
        _page = root["nextPageToken"]?.GetValue<string>();
        var output = new List<ChatMessage>();
        foreach (var item in root["items"]?.AsArray() ?? [])
        {
            var id = item?["id"]?.GetValue<string>() ?? "";
            if (!_seen.Add(id)) continue;
            if (item?["authorDetails"]?["isChatOwner"]?.GetValue<bool>() == true || (_ownerChannel is not null && item?["authorDetails"]?["channelId"]?.GetValue<string>() == _ownerChannel)) continue;
            if (_initial) continue; // Do not reply to the pre-existing initial history.
            var message = item?["snippet"]?["displayMessage"]?.GetValue<string>() ?? "";
            if (message.Length > 0) output.Add(new ChatMessage(id, item?["authorDetails"]?["displayName"]?.GetValue<string>() ?? "Viewer", message, item?["snippet"]?["publishedAt"]?.GetValue<string>() ?? ""));
        }
        _initial = false;
        if (_seen.Count > 10000) _seen.Clear();
        return (output.ToArray(), Math.Clamp(root["pollingIntervalMillis"]?.GetValue<int>() ?? 5000, 1000, 60000));
    }
    public async Task SendAsync(string text, CancellationToken token)
    {
        if (ChatId is null) throw new InvalidOperationException("No live YouTube chat is connected.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/youtube/v3/liveChat/messages?part=snippet");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessAsync(token));
        request.Content = JsonContent.Create(new { snippet = new { liveChatId = ChatId, type = "textMessageEvent", textMessageDetails = new { messageText = text.Length > 200 ? text[..200] : text } } });
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"YouTube send failed (HTTP {(int)response.StatusCode}).");
    }
    public async Task<JsonObject> StatisticsAsync(CancellationToken token) => VideoId is null ? new JsonObject() : await GetAsync("videos?part=statistics,liveStreamingDetails&id=" + Uri.EscapeDataString(VideoId), token);
}
