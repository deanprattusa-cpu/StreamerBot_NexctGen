using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace StreamerBot.Native.Core;

public sealed record ChatMessage(string Id, string Author, string Text, string PublishedAt);
public sealed class YouTubeApiException(int status, string reason) : Exception($"YouTube request failed (HTTP {status}, {reason}).")
{
    public bool DailyQuota => reason is "quotaExceeded" or "dailyLimitExceeded";
    public bool ChatGone => reason is "liveChatEnded" or "liveChatNotFound" or "liveChatDisabled";
}
public sealed class YouTubeClient(HttpClient http, string credentialFile, string? expectedChannelId = null)
{
    private string[] _credentialFiles = [credentialFile];
    private int _credentialIndex;
    private readonly SemaphoreSlim _requests = new(1);
    public event Action<string>? Status;
    public YouTubeClient(HttpClient http, string[] credentialFiles, string? expectedChannelId = null)
        : this(http, credentialFiles.FirstOrDefault() ?? throw new InvalidDataException("No channel tokens found."), expectedChannelId)
    { _credentialFiles = credentialFiles.ToArray(); }
    private string _access = "";
    private DateTimeOffset _expires;
    public string CurrentCredentialFile => _credentialFiles[_credentialIndex];
    public string? ChatId { get; private set; }
    public string? VideoId { get; private set; }
    private string? _page;
    private string? _ownerChannel;
    private readonly HashSet<string> _seen = [];
    private bool _initial = true;
    private async Task<string> AccessAsync(CancellationToken token)
    {
        if (_access.Length > 0 && _expires > DateTimeOffset.UtcNow.AddMinutes(1)) return _access;
        var credentials = JsonNode.Parse(await File.ReadAllTextAsync(_credentialFiles[_credentialIndex], token))?.AsObject() ?? throw new InvalidDataException("Invalid YouTube token file.");
        if (DateTimeOffset.TryParse(credentials["expiry"]?.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var expiry) && expiry > DateTimeOffset.UtcNow.AddMinutes(1)
            && credentials["token"]?.GetValue<string>() is { Length: > 0 } saved)
        { _access = saved; _expires = expiry; return _access; }
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["client_id"] = credentials["client_id"]?.GetValue<string>() ?? "", ["client_secret"] = credentials["client_secret"]?.GetValue<string>() ?? "", ["refresh_token"] = credentials["refresh_token"]?.GetValue<string>() ?? "", ["grant_type"] = "refresh_token" }), token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"YouTube authorization failed (HTTP {(int)response.StatusCode}). Reconnect the account.");
        var data = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: token);
        _access = data?["access_token"]?.GetValue<string>() ?? throw new InvalidDataException("Missing YouTube access token.");
        _expires = DateTimeOffset.UtcNow.AddSeconds(data?["expires_in"]?.GetValue<int>() ?? 3600);
        credentials["token"] = _access;
        credentials["expiry"] = _expires.UtcDateTime.ToString("O");
        var path = _credentialFiles[_credentialIndex];
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, credentials.ToJsonString(), token); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return _access;
    }
    private async Task<JsonObject> RequestAsync(HttpMethod method, string query, object? body, CancellationToken token)
    {
        await _requests.WaitAsync(token);
        try
        {
            while (true)
            {
                using var request = new HttpRequestMessage(method, "https://www.googleapis.com/youtube/v3/" + query);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessAsync(token));
                if (body is not null) request.Content = JsonContent.Create(body);
                using var response = await http.SendAsync(request, token);
                var data = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: token) ?? new JsonObject();
                if (response.IsSuccessStatusCode) return data;
                var reason = data["error"]?["errors"]?[0]?["reason"]?.GetValue<string>() ?? "unknown";
                var error = new YouTubeApiException((int)response.StatusCode, reason);
                if (!error.DailyQuota) throw error;
                _access = ""; _expires = default;
                _credentialIndex++;
                if (_credentialIndex >= _credentialFiles.Length)
                {
                    _credentialIndex = 0;
                    Status?.Invoke("All channel credentials reached daily quota. Retrying in 10 minutes; Stop cancels the wait.");
                    await Task.Delay(TimeSpan.FromMinutes(10), token);
                }
                else Status?.Invoke($"Daily quota reached. Switching to channel credential {_credentialIndex + 1} of {_credentialFiles.Length}.");
            }
        }
        finally { _requests.Release(); }
    }
    private Task<JsonObject> GetAsync(string query, CancellationToken token) => RequestAsync(HttpMethod.Get, query, null, token);
    public void ResetLive()
    { ChatId = null; VideoId = null; _page = null; _ownerChannel = null; _seen.Clear(); _initial = true; }
    public async Task<bool> FindLiveAsync(CancellationToken token)
    {
        var root = await GetAsync("liveBroadcasts?part=id,snippet,status&mine=true&maxResults=50", token);
        foreach (var item in root["items"]?.AsArray() ?? [])
        {
            if (item?["status"]?["lifeCycleStatus"]?.GetValue<string>() != "live") continue;
            if (!string.IsNullOrEmpty(expectedChannelId) && item?["snippet"]?["channelId"]?.GetValue<string>() != expectedChannelId) continue;
            VideoId = item!["id"]!.GetValue<string>();
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
    public async Task<ChatMessage[]> HistoryAsync(CancellationToken token)
    {
        if (ChatId is null) return [];
        var data = await GetAsync("liveChat/messages?part=snippet,authorDetails&maxResults=200&liveChatId=" + Uri.EscapeDataString(ChatId), token);
        return (data["items"]?.AsArray() ?? []).Select(item => new ChatMessage(item?["id"]?.GetValue<string>() ?? "", item?["authorDetails"]?["displayName"]?.GetValue<string>() ?? "Viewer", item?["snippet"]?["displayMessage"]?.GetValue<string>() ?? "", item?["snippet"]?["publishedAt"]?.GetValue<string>() ?? "")).Where(m => m.Text.Length > 0).ToArray();
    }
    public async Task SendAsync(string text, CancellationToken token)
    {
        if (ChatId is null) throw new InvalidOperationException("No live YouTube chat is connected.");
        await RequestAsync(HttpMethod.Post, "liveChat/messages?part=snippet",
            new { snippet = new { liveChatId = ChatId, type = "textMessageEvent", textMessageDetails = new { messageText = text.Length > 200 ? text[..200] : text } } }, token);
    }
    public async Task<JsonObject> StatisticsAsync(CancellationToken token) => VideoId is null ? new JsonObject() : await GetAsync("videos?part=statistics,liveStreamingDetails&id=" + Uri.EscapeDataString(VideoId), token);
}
