using System.Text.Json.Nodes;
using StreamerBot.Native.Core;

internal static class OriginalWorkflowChecks
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var original = Path.Combine(root, "original account folder"); Directory.CreateDirectory(original);
        var env = Path.Combine(original, ".env");
        var content = "export CHANNEL_2_NAME=\"First # Channel\" # comment\nCHANNEL_2_ID=owner-two\nCHANNEL_2_TOKEN_PREFIX=first\nCHANNEL_9_NAME='Second Channel'\nCHANNEL_9_ID=owner-nine\nCHANNEL_9_TOKEN_PREFIX=second\nGROQ_API_KEYS=synthetic-one,synthetic-two\nFUTURE_SETTING=keep me # comment\n";
        File.WriteAllText(env, content);
        var profile = new Profile(Path.Combine(root, "owned accounts", "profile.json"));
        profile.Set("channel_index", 1);
        OriginalEnvironment.Import(profile, env);
        var channels = OriginalEnvironment.Channels(profile);
        check(channels.Length == 2 && channels[0].Number == 2 && channels[0].Name == "First # Channel" && channels[1].Number == 9, "original .env numbered channels, quotes and comments populate ordered accounts");
        check(OriginalEnvironment.Selected(profile)?.Number == 9, "original zero-based channel_index selects nonconsecutive channel numbers");
        OriginalEnvironment.Select(profile, channels[1]); profile.Save();
        var reopened = new Profile(profile.StoragePath);
        check(OriginalEnvironment.Selected(reopened)?.ChannelId == "owner-nine" && reopened.Text("channel_name") == "Second Channel" && reopened.Text("token_prefix") == "second", "selected channel identity and token prefix survive restart");
        check(reopened.Keys("groq_keys").SequenceEqual(new[] { "synthetic-one", "synthetic-two" }) && reopened.Data[OriginalEnvironment.ValuesKey]?["FUTURE_SETTING"]?.GetValue<string>() == "keep me" && File.ReadAllText(env) == content, "environment import preserves multiple API keys, unknown fields and original file");
        var before = reopened.Data.DeepClone();
        var invalid = Path.Combine(root, "bad.env"); File.WriteAllText(invalid, "CHANNEL_1_NAME=\"unterminated");
        try { OriginalEnvironment.Import(reopened, invalid); throw new Exception("invalid env accepted"); } catch (InvalidDataException) { }
        check(JsonNode.DeepEquals(before, reopened.Data), "invalid environment import makes no partial profile changes");
        var credentials = Path.Combine(original, "credentials"); Directory.CreateDirectory(credentials);
        File.WriteAllText(Path.Combine(credentials, "first_token_1.json"), "synthetic first");
        File.WriteAllText(Path.Combine(credentials, "second_token_2.json"), "synthetic second");
        File.WriteAllText(Path.Combine(credentials, "second_token_10.json"), "synthetic tenth");
        check(OriginalEnvironment.TokenFile(reopened) == Path.Combine(credentials, "second_token_2.json"), "selected channel discovers only matching original token prefix in numeric order");
        OriginalEnvironment.Select(reopened, channels[0]);
        check(OriginalEnvironment.TokenFile(reopened) == Path.Combine(credentials, "first_token_1.json"), "switching channels switches credentials");
        var copied = Path.Combine(root, "explicit-token.json"); OriginalEnvironment.SetToken(reopened, channels[0], copied);
        check(OriginalEnvironment.TokenFile(reopened) == copied, "explicit token assignment belongs to selected channel");
        OriginalEnvironment.Select(reopened, channels[1]);
        check(OriginalEnvironment.TokenFile(reopened) != copied, "another channel never uses the selected channel's explicit token");
        OriginalEnvironment.Add(reopened, "New Account", "new-id");
        check(OriginalEnvironment.Selected(reopened)?.Name == "New Account" && OriginalEnvironment.TokenFile(reopened) == "", "adding channel selects it without silently reusing an unrelated account");

        var config = Path.Combine(original, "config.json"); File.WriteAllText(config, "{\"channel_index\":1,\"unknown_original\":true}");
        OriginalEnvironment.ImportProfile(reopened, config);
        check(OriginalEnvironment.Channels(reopened).Length == 3 && OriginalEnvironment.Selected(reopened)?.Number == 2 && reopened.Flag("unknown_original"), "importing original JSON after .env retains accounts and uses its channel_index");

        var handler = new AccountHandler(); using var http = new HttpClient(handler);
        var tokenFile = Path.Combine(root, "synthetic-account-token.json"); File.WriteAllText(tokenFile, "{\"client_id\":\"synthetic\",\"client_secret\":\"synthetic\",\"refresh_token\":\"synthetic\"}");
        var youtube = new YouTubeClient(http, tokenFile, "owner-nine");
        check(await youtube.FindLiveAsync(default) && youtube.VideoId == "right-video", "YouTube chooses the selected channel when multiple broadcasts are live");
        var mismatch = new YouTubeClient(http, tokenFile, "missing-owner");
        check(!await mismatch.FindLiveAsync(default) && mismatch.ChatId is null, "mismatched token/broadcast cannot attach to another channel's chat");

        var googleClient = Path.Combine(root, "desktop-client.json"); File.WriteAllText(googleClient, "{\"installed\":{\"client_id\":\"synthetic\",\"client_secret\":\"synthetic\"}}");
        using var authHttp = new HttpClient(new AuthorizationHandler()); using var callbackHttp = new HttpClient();
        Task? callback = null; var sawPkce = false;
        var authorized = await YouTubeAuthorization.AuthorizeAsync(authHttp, googleClient, uri =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            sawPkce = query["code_challenge_method"] == "S256" && query["code_challenge"]?.Length > 30;
            callback = Task.Run(async () =>
            {
                using var invalidState = await callbackHttp.GetAsync(query["redirect_uri"] + "?state=wrong&code=wrong");
                if (invalidState.StatusCode != System.Net.HttpStatusCode.BadRequest) throw new Exception("OAuth state mismatch was accepted");
                using var valid = await callbackHttp.GetAsync(query["redirect_uri"] + "?state=" + Uri.EscapeDataString(query["state"]!) + "&code=synthetic-code"); valid.EnsureSuccessStatusCode();
            });
        }, default);
        await callback!;
        check(sawPkce && authorized["refresh_token"]?.GetValue<string>() == "synthetic-refresh", "native Google authorization uses PKCE, rejects wrong state and exchanges the loopback code");

        var reviewProfile = new Profile(Path.Combine(root, "review.json")); reviewProfile.Set("review_mode", true);
        await using var controller = new StreamController(reviewProfile, new AiClient(http));
        var spoken = new List<string>(); var captions = new List<string>();
        controller.Speak += (text, category, cancel) => { spoken.Add(text); return Task.CompletedTask; }; controller.Caption += captions.Add;
        controller.Review += (request, cancel) => Task.FromResult<ReviewDecision?>(new ReviewDecision("edited", "T"));
        await controller.DeliverAsync("draft", "manual", mode: "V+T");
        check(spoken.Count == 0 && captions.SequenceEqual(new[] { "edited" }), "review text-only approval preserves edited text and suppresses voice");
        reviewProfile.Set("global_ai_mute", true);
        await controller.DeliverAsync("muted draft", "manual");
        check(captions.Count == 1 && spoken.Count == 0, "original global AI mute suppresses both chat and voice output");
        reviewProfile.Set("global_ai_mute", false);
        var rejected = new StreamController(reviewProfile, new AiClient(http)); var delivered = false;
        rejected.Review += (request, cancel) => Task.FromResult<ReviewDecision?>(null); rejected.Caption += _ => delivered = true;
        await rejected.DeliverAsync("reject", "manual"); await rejected.DisposeAsync();
        check(!delivered, "review rejection produces no output");
    }
    private sealed class AuthorizationHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = await request.Content!.ReadAsStringAsync(token);
            if (request.RequestUri!.AbsoluteUri != "https://oauth2.googleapis.com/token" || !body.Contains("code_verifier=") || !body.Contains("code=synthetic-code")) throw new Exception("Invalid authorization exchange");
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"refresh_token\":\"synthetic-refresh\"}") };
        }
    }
    private sealed class AccountHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.Host == "oauth2.googleapis.com" ? "{\"access_token\":\"synthetic\",\"expires_in\":3600}" : request.RequestUri.AbsolutePath.EndsWith("liveBroadcasts")
                ? "{\"items\":[{\"id\":\"wrong-video\",\"status\":{\"lifeCycleStatus\":\"live\"},\"snippet\":{\"channelId\":\"owner-two\"}},{\"id\":\"right-video\",\"status\":{\"lifeCycleStatus\":\"live\"},\"snippet\":{\"channelId\":\"owner-nine\"}}]}"
                : "{\"items\":[{\"liveStreamingDetails\":{\"activeLiveChatId\":\"synthetic-chat\"}}]}";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
