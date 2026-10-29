using System.Net;
using System.Text.Json.Nodes;
using StreamerBot.Native.Core;

var root = Path.Combine(Path.GetTempPath(), "streamerbot-native-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passes = 0;
void Check(bool result, string name) { if (!result) throw new Exception(name); Console.WriteLine("PASS " + name); passes++; }
try
{
    Check(Catalog.Defaults().Count == 521 && Catalog.SmartKeys.Length == 49 && Catalog.SystemKeys.Length == 32 && Catalog.Voices.Length == 13, "original field / key / engine inventories");
    var original = Path.Combine(root, "import", "original.json"); Directory.CreateDirectory(Path.GetDirectoryName(original)!);
    var document = Catalog.Defaults(); document["future_unknown"] = new JsonObject { ["nested"] = new JsonArray(1, "kept", true) };
    File.WriteAllText(original, document.ToJsonString()); var bytes = File.ReadAllBytes(original);
    var profile = new Profile(Path.Combine(root, "owned", "profile.json")); profile.Import(original); profile.Save();
    Check(File.ReadAllBytes(original).SequenceEqual(bytes), "import never modifies original profile");
    var reopened = new Profile(profile.StoragePath); Check(JsonNode.DeepEquals(document, reopened.Data), "all fields including unknown nested fields round-trip");
    Check(reopened.Resolve("voice.wav") == Path.Combine(Path.GetDirectoryName(original)!, "voice.wav"), "relative paths retain import origin after restart");
    var invalid = Path.Combine(root, "bad.json"); File.WriteAllText(invalid, "[]"); try { reopened.Import(invalid); throw new Exception("accepted invalid profile"); } catch (InvalidDataException) { }
    Check(JsonNode.DeepEquals(document, reopened.Data), "invalid import retains active profile");
    Check(KeyChord.TryParse("ctrl+alt+F12", out _) && KeyChord.TryParse("shift+x", out _) && !KeyChord.TryParse("ctrl+a+b", out _) && !KeyChord.TryParse("ctrl+made-up-key", out _), "supported and invalid Windows chords");
    var handler = new FakeHandler(); using var http = new HttpClient(handler); var ai = new AiClient(http);
    profile.Set("ai_mode", "groq"); profile.Set("groq_keys", new JsonArray("synthetic-bad", "synthetic-good")); profile.Set("native_live_context", "Synthetic game context");
    Check(await ai.CompleteAsync(profile, "hello", default) == "Hello gamer" && handler.Calls == 2 && handler.SawContext, "provider failure rotates keys and includes supplied context");
    await ai.CompleteAsync(profile, "again", default); Check(handler.Calls == 3, "rejected key temporarily benched");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); try { await ai.CompleteAsync(profile, "cancelled", cancelled.Token); throw new Exception("cancellation ignored"); } catch (OperationCanceledException) { Check(true, "AI cancellation"); }
    await using (var controller = new StreamController(profile, ai))
    {
        await controller.StopAsync(); var got = ""; controller.Caption += line => got = line;
        await controller.GenerateAsync("manual after stop"); Check(got == "Hello gamer", "manual generation works after session stop");
        controller.Start(null, true); await controller.StopAsync(); controller.Start(null, true); await controller.StopAsync(); Check(!controller.Running, "simulation can start, stop and restart");
    }
    var credential = Path.Combine(root, "synthetic-youtube.json"); File.WriteAllText(credential, "{\"client_id\":\"synthetic\",\"client_secret\":\"synthetic\",\"refresh_token\":\"synthetic\"}");
    var youtubeHandler = new YouTubeHandler(); using var youtubeHttp = new HttpClient(youtubeHandler); var youtube = new YouTubeClient(youtubeHttp, credential);
    Check(await youtube.FindLiveAsync(default), "native YouTube broadcast lookup and token refresh");
    Check((await youtube.PollAsync(default)).Messages.Length == 0, "YouTube initial history is not auto-replied to");
    var poll = await youtube.PollAsync(default); Check(poll.Messages.Length == 1 && poll.Messages[0].Author == "Viewer" && poll.Delay == 5000, "YouTube filters own messages and duplicates and respects poll interval");
    await youtube.SendAsync("test", default); Check(youtubeHandler.Sent && youtubeHandler.Refreshes == 1, "native YouTube posting and cached access token");
    if (args.Length == 2)
    {
        await using var worker = new VoiceWorker(args[0], args[1], root);
        var health = await worker.CallAsync("health", new JsonObject(), default); Check(health["worker"]?.GetValue<string>() == "voice-only", "real voice-only subprocess health framing");
        try { await worker.CallAsync("invalid-operation", new JsonObject(), default); throw new Exception("worker accepted invalid operation"); } catch (InvalidOperationException) { Check(true, "worker reports protocol operation errors"); }
        health = await worker.CallAsync("health", new JsonObject(), default); Check(health["ok"]?.GetValue<bool>() == true, "worker restarts after protocol error");
    }
    var pathsProfile = new Profile(Path.Combine(root, "paths-profile.json"));
    var selectedModels = Path.Combine(root, "models on selected drive"); Directory.CreateDirectory(selectedModels);
    pathsProfile.Set("global_model_path", selectedModels);
    Check(ResourcePaths.Models(pathsProfile, root) == selectedModels, "selected model folder overrides app data without moving files");
    pathsProfile.Set("voice_cache_path", Path.Combine(root, "new voice cache"));
    Check(ResourcePaths.Validate(pathsProfile).Length == 0, "new output folders may be saved before creation");
    pathsProfile.Set("avatar_path", Path.Combine(root, "missing-image.png"));
    Check(ResourcePaths.Validate(pathsProfile).Any(error => error.Contains("Avatar")), "missing selected input file is diagnosed");
    pathsProfile.Set("avatar_path", "");
    pathsProfile.Data["resource_paths"] = new JsonObject { ["video_python_path"] = "relative/python.exe" };
    Check(ResourcePaths.Get(pathsProfile, "video_python_path") == Path.Combine(root, "relative", "python.exe"), "original nested resource selections resolve against profile origin");
    pathsProfile.Set("video_python_path", "");
    Check(ResourcePaths.Value(pathsProfile, "video_python_path") == "", "clearing a selected path restores automatic mode");
    Check(!ResourcePaths.IsWithin(Path.Combine(root, "cache-other", "escape.wav"), Path.Combine(root, "cache")), "path containment rejects sibling-prefix escapes");
    if (args.Length == 2)
    {
        var privateData = Path.Combine(root, "private"); Directory.CreateDirectory(privateData);
        var selectedCache = Path.Combine(root, "selected voice cache"); Directory.CreateDirectory(selectedCache);
        var selectedAudio = Path.Combine(selectedCache, "voice.wav"); File.WriteAllText(selectedAudio, "synthetic");
        var fixture = Path.Combine(root, "fixture_worker.py");
        File.WriteAllText(fixture, "import sys,json\nfor line in sys.stdin:\n r=json.loads(line); print(json.dumps({'protocol':1,'id':r['id'],'ok':True,'audio':r['test_audio']}),flush=True)\n");
        await using (var selectedWorker = new VoiceWorker(args[0], fixture, privateData, selectedCache))
        {
            var result = await selectedWorker.CallAsync("speak", new JsonObject { ["test_audio"] = selectedAudio }, default);
            Check(result["audio"]?.GetValue<string>() == selectedAudio, "worker accepts audio inside explicitly selected external cache");
        }
        await using (var restrictedWorker = new VoiceWorker(args[0], fixture, privateData))
        {
            try { await restrictedWorker.CallAsync("speak", new JsonObject { ["test_audio"] = selectedAudio }, default); throw new Exception("unselected output accepted"); }
            catch (IOException) { Check(true, "worker rejects an external output that was not selected"); }
        }
        var project = Path.Combine(root, "SadTalker with spaces"); Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "inference.py"), "import sys,pathlib\na=sys.argv; p=pathlib.Path(a[a.index('--result_dir')+1]); (p/'fixture.mp4').write_bytes(b'synthetic video')\n");
        var avatar = Path.Combine(root, "avatar image.png"); File.WriteAllText(avatar, "synthetic");
        var videoProfile = new Profile(Path.Combine(root, "video-profile.json"));
        videoProfile.Set("sadtalker_path", project); videoProfile.Set("video_python_path", args[0]); videoProfile.Set("avatar_path", avatar); videoProfile.Set("video_use_cpu", true);
        videoProfile.Set("video_output_path", Path.Combine(root, "videos on selected drive"));
        var repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(args[1])!, "../.."));
        var renderer = new VideoRenderer(Path.Combine(repositoryRoot, "sadtalker_bridge.py"), privateData);
        var video = await renderer.RenderAsync(videoProfile, selectedAudio, default);
        Check(File.ReadAllText(video) == "synthetic video" && ResourcePaths.IsWithin(video, ResourcePaths.VideoOutput(videoProfile, privateData)), "real native-to-bridge process uses selected Python, spaces and isolated output");
        File.WriteAllText(Path.Combine(project, "inference.py"), "import sys; print('synthetic renderer failure', file=sys.stderr); sys.exit(9)\n");
        try { await renderer.RenderAsync(videoProfile, selectedAudio, default); throw new Exception("failed renderer accepted"); }
        catch (InvalidOperationException error) { Check(error.Message.Contains("code 9") && error.Message.Contains("renderer.log"), "native renderer exposes exit failure and detailed log path"); }
    }
    await UpdateChecks.Run(root, Check);
    await KeyboardPathChecks.Run(root, Check);
    await OriginalWorkflowChecks.Run(root, Check);
    await CredentialPoolChecks.Run(root, Check);
    Console.WriteLine($"{passes} checks passed");
}
finally { Directory.Delete(root, true); }
sealed class FakeHandler : HttpMessageHandler
{
    public int Calls; public bool SawContext;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++; var body = await request.Content!.ReadAsStringAsync(cancellationToken); SawContext |= body.Contains("Synthetic game context");
        return request.Headers.Authorization?.Parameter == "synthetic-bad" ? new(HttpStatusCode.Unauthorized) { Content = new StringContent("never show synthetic-bad") } : new(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"Hello gamer\"}}]}", System.Text.Encoding.UTF8, "application/json") };
    }
}

sealed class YouTubeHandler : HttpMessageHandler
{
    public int Refreshes; public bool Sent; private int _poll;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body;
        if (request.RequestUri!.Host == "oauth2.googleapis.com") { Refreshes++; body = "{\"access_token\":\"synthetic-token\",\"expires_in\":3600}"; }
        else if (request.RequestUri.AbsolutePath.EndsWith("liveBroadcasts")) body = "{\"items\":[{\"id\":\"synthetic-video\",\"snippet\":{\"channelId\":\"synthetic-owner\"},\"status\":{\"lifeCycleStatus\":\"live\"}}]}";
        else if (request.RequestUri.AbsolutePath.EndsWith("videos")) body = "{\"items\":[{\"liveStreamingDetails\":{\"activeLiveChatId\":\"synthetic-chat\"}}]}";
        else if (request.Method == HttpMethod.Post) { Sent = request.Headers.Authorization?.Parameter == "synthetic-token"; body = "{}"; }
        else { _poll++; body = "{\"pollingIntervalMillis\":5000,\"nextPageToken\":\"page\",\"items\":[{\"id\":\"" + (_poll == 1 ? "old" : "new") + "\",\"snippet\":{\"displayMessage\":\"hello\"},\"authorDetails\":{\"displayName\":\"Viewer\",\"channelId\":\"viewer\"}},{\"id\":\"own\",\"snippet\":{\"displayMessage\":\"own reply\"},\"authorDetails\":{\"displayName\":\"Owner\",\"channelId\":\"synthetic-owner\",\"isChatOwner\":true}},{\"id\":\"old\",\"snippet\":{\"displayMessage\":\"duplicate\"},\"authorDetails\":{\"displayName\":\"Viewer\"}}]}"; }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
    }
}
