using System.Net;
using System.Text.Json.Nodes;
using StreamerBot.Native.Core;

internal static class CredentialPoolChecks
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "shared credentials"); Directory.CreateDirectory(folder);
        var profile = new Profile(Path.Combine(root, "pool-profile.json"));
        var channel = OriginalEnvironment.Add(profile, "Selected", "owner");
        profile.Set("native_credentials_directory", folder);
        var paths = new[] { 1, 2 }.Select(n => Path.Combine(folder, channel.TokenPrefix + "_token_" + n + ".json")).ToArray();
        for (var i = 0; i < paths.Length; i++)
            File.WriteAllText(paths[i], new JsonObject { ["token"] = "saved-" + i, ["expiry"] = DateTimeOffset.UtcNow.AddHours(1).ToString("O"), ["client_id"] = "fixture", ["client_secret"] = "fixture", ["refresh_token"] = "fixture", ["unknown"] = "keep" }.ToJsonString());
        File.WriteAllText(Path.Combine(folder, "other_token_1.json"), "{}");
        check(OriginalEnvironment.TokenFiles(profile).SequenceEqual(paths), "shared folder discovers all selected channel tokens without unrelated accounts");
        File.WriteAllText(Path.Combine(folder, "client_secret_2.json"), "{}"); File.WriteAllText(Path.Combine(folder, "client_secret_10.json"), "{}");
        check(OriginalEnvironment.TokenForSecret(profile, Path.Combine(folder, "client_secret_10.json")) == paths[0], "secret pairing preserves original Python lexicographic ordinal");
        profile.Save(); check(OriginalEnvironment.CredentialsDirectory(new Profile(profile.StoragePath)) == folder, "shared credential folder survives restart");
        var imported = Path.Combine(root, "pool-import.json"); File.WriteAllText(imported, "{}"); OriginalEnvironment.ImportProfile(profile, imported);
        check(OriginalEnvironment.CredentialsDirectory(profile) == folder, "original profile import retains selected shared folder");
        using var handler = new PoolHandler(); using var http = new HttpClient(handler);
        var client = new YouTubeClient(http, paths);
        check(!await client.FindLiveAsync(default) && handler.Bearers.SequenceEqual(new[] { "saved-0", "saved-1" }), "daily quota rotates to next ready token without OAuth refresh");
        handler.Reason = "forbidden"; handler.Bearers.Clear();
        try { await new YouTubeClient(http, paths).FindLiveAsync(default); throw new Exception("permission error accepted"); }
        catch (YouTubeApiException e) { check(!e.DailyQuota && handler.Bearers.Count == 1, "ordinary 403 is reported without consuming another credential"); }
        handler.Reason = "dailyLimitExceeded"; handler.AllQuota = true; handler.Bearers.Clear();
        using var cancel = new CancellationTokenSource();
        var exhausted = new YouTubeClient(http, paths); exhausted.Status += s => { if (s.StartsWith("All")) cancel.Cancel(); };
        try { await exhausted.FindLiveAsync(cancel.Token); throw new Exception("quota wait not cancelled"); }
        catch (OperationCanceledException) { check(handler.Bearers.Count == 2, "all credentials exhausted enters cancellable cooldown without retry storm"); }
        handler.AllQuota = false; handler.Reason = ""; handler.Bearers.Clear();
        var expired = JsonNode.Parse(File.ReadAllText(paths[0]))!.AsObject(); expired["expiry"] = "2000-01-01T00:00:00Z"; File.WriteAllText(paths[0], expired.ToJsonString());
        await new YouTubeClient(http, paths).FindLiveAsync(default);
        var refreshed = JsonNode.Parse(File.ReadAllText(paths[0]))!;
        check(refreshed["token"]!.GetValue<string>() == "refreshed" && refreshed["unknown"]!.GetValue<string>() == "keep" && handler.Refreshes == 1, "expired token refresh persists atomically in original folder preserving fields");
    }
    private sealed class PoolHandler : HttpMessageHandler
    {
        public string Reason = "quotaExceeded";
        public bool AllQuota;
        public int Refreshes;
        public List<string> Bearers = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
            { Refreshes++; return Task.FromResult(Response(HttpStatusCode.OK, "{\"access_token\":\"refreshed\",\"expires_in\":3600}")); }
            var bearer = request.Headers.Authorization!.Parameter!; Bearers.Add(bearer);
            return Task.FromResult(Reason.Length > 0 && (AllQuota || bearer == "saved-0")
                ? Response(HttpStatusCode.Forbidden, new JsonObject { ["error"] = new JsonObject { ["errors"] = new JsonArray(new JsonObject { ["reason"] = Reason }) } }.ToJsonString())
                : Response(HttpStatusCode.OK, "{\"items\":[]}"));
        }
        private static HttpResponseMessage Response(HttpStatusCode code, string body) => new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}
