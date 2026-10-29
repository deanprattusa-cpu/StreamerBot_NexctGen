using System.Net;
using StreamerBot.Native.Core;

internal static class OriginalCacheChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var original = Path.Combine(root, "original streamer bot"); Directory.CreateDirectory(original);
        var config = Path.Combine(original, "config.json"); File.WriteAllText(config, "{}");
        var profile = new Profile(Path.Combine(root, "native-cache-check", "profile.json")); profile.Import(config);
        OriginalEnvironment.Add(profile, "Showcase Gaming", "owner"); profile.Set("game_name", "Shadow Of The Colossus"); profile.Set("tts_mode", "bark");
        var credentials = Path.Combine(original, "credential"); Directory.CreateDirectory(credentials);
        File.WriteAllText(Path.Combine(credentials, "channel1_token_1.json"), "{}");
        check(OriginalEnvironment.CredentialsDirectory(profile) == credentials && OriginalEnvironment.TokenFiles(profile).Length == 1, "singular credential folder beside imported original config is automatic");
        profile.Save(); check(OriginalEnvironment.TokenFiles(new Profile(profile.StoragePath)).Length == 1, "automatic credential discovery survives native restart without token import");
        var directory = Path.Combine(original, "bark_cache", "Showcase_Gaming", "Shadow_Of_The_Colossus", "v2en_speaker_6", "death"); Directory.CreateDirectory(directory);
        var a = Path.Combine(directory, "death_old-md5-a.wav"); var b = Path.Combine(directory, "death_old-md5-b.wav");
        File.WriteAllBytes(a, [1, 2, 3]); File.WriteAllBytes(b, [4, 5, 6]); File.WriteAllText(Path.ChangeExtension(a, ".txt"), "Old phrase A"); File.WriteAllText(Path.ChangeExtension(b, ".txt"), "Old phrase B");
        var cache = new OriginalVoiceCache();
        var first = cache.Next(profile, root, "hk_death")!; var second = cache.Next(profile, root, "hk_death")!;
        check(new[] { a, b }.Contains(first.Path) && first.Path != second.Path && first.Text.StartsWith("Old phrase"), "old engine/channel/game/voice/category cache plays without hash conversion and shuffles without repetition");
        profile.Set("cache_playback_mode", "repeat_one");
        check(cache.Next(profile, root, "hk_death")!.Path == cache.Next(profile, root, "hk_death")!.Path, "original repeat-one pins one existing clip");
        profile.Set("cache_playback_mode", "repeat_all");
        var sequence = Enumerable.Range(0, 3).Select(_ => cache.Next(profile, root, "hk_death")!.Path).ToArray();
        check(sequence[0] != sequence[1] && sequence[0] == sequence[2], "original repeat-all cycles the existing clips");
        profile.Set("tts_mode", "rvc"); check(cache.Next(profile, root, "hk_death") is null, "cache lookup never silently switches voice engine");
        profile.Set("tts_mode", "bark"); profile.Set("game_name", "Other Game"); profile.Set("cache_fallback_any_game", false);
        check(cache.Next(profile, root, "hk_death") is null, "disabled any-game fallback preserves original game boundary");
        profile.Set("cache_fallback_any_game", true); check(cache.Next(profile, root, "hk_death") is not null, "attached controller any-game fallback finds original clips when enabled");
        profile.Set("game_name", "Shadow Of The Colossus"); profile.Set("force_cache_only", true); profile.Set("hk_death_mode", "V"); profile.Set("hk_death_custom", "An override must not trigger synthesis in cache-only mode");
        using var http = new HttpClient(new NeverNetwork()); await using var controller = new StreamController(profile, new AiClient(http));
        string? played = null; var synthesized = false;
        controller.Speak += (_, _, _) => { synthesized = true; return Task.CompletedTask; };
        controller.PlayCachedAudio += (path, _) => { played = path; return Task.CompletedTask; };
        controller.CacheRequested += async (key, token) => { var clip = cache.Next(profile, root, key)!; await controller.DeliverAsync(clip.Text, key, token, cachedAudio: clip.Path); };
        await controller.TriggerAsync("hk_death");
        check(played is not null && !synthesized, "force-cache hotkey delivers existing audio without AI, Python or voice model initialization");
        profile.Set("voice_cache_path", Path.Combine(original, "bark_cache"));
        check(cache.Find(profile, root, "hk_death").Length == 2, "selected engine cache folder works directly without an extra engine_cache nesting");
        profile.Set("voice_cache_path", Path.Combine(root, "native indexed cache")); Directory.CreateDirectory(profile.Text("voice_cache_path"));
        var audio = Path.Combine(profile.Text("voice_cache_path"), "new.wav"); File.WriteAllBytes(audio, [1]); OriginalVoiceCache.Record(profile, audio, "hk_help", "new cache text");
        check(cache.Next(profile, root, "hk_help")?.Text == "new cache text", "new native cache sidecars retain text and hotkey identity for later cache-only playback");
        profile.Set("game_name", "Another session"); OriginalVoiceCache.Record(profile, audio, "hk_help", "new cache text");
        profile.Set("game_name", "Shadow Of The Colossus");
        check(cache.Next(profile, root, "hk_help") is not null, "shared native audio retains both session identities when reused");
        var session = Path.Combine(root, "saved_game.json");
        File.WriteAllText(session, "{\"game_name\":\"Shadow Of The  Colossus\",\"tts_mode\":\"typecast\",\"chat_directive_enabled\":true,\"chat_directive_text\":\"synthetic game directive\",\"hk_death_prompt\":\"synthetic game reaction\",\"bark_voice\":\"v2/en_speaker_6\"}");
        var sharedCache = profile.Text("voice_cache_path");
        OriginalEnvironment.ImportProfile(profile, session); profile.Save();
        var reopened = new Profile(profile.StoragePath);
        check(reopened.Text("voice_cache_path") == sharedCache && reopened.Text("game_name") == "Shadow Of The  Colossus" && reopened.Text("tts_mode") == "typecast" && reopened.Text("hk_death_prompt") == "synthetic game reaction" && reopened.Flag("chat_directive_enabled"), "game session restores saved prompts and engine while retaining shared cache path");
    }
    private sealed class NeverNetwork : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new Exception("Force-cache attempted a network request"); }
}
