using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StreamerBot.Native.Core;

public sealed record CachedVoice(string Path, string Text);

/// <summary>Reads the original controller's cache layout without moving or regenerating clips.</summary>
public sealed class OriginalVoiceCache
{
    private readonly Dictionary<string, List<string>> _bags = [];
    private readonly Dictionary<string, string> _last = [];
    public static string Category(string key) => Regex.Replace(key.StartsWith("hk_") ? key[3..] : key, @"[^a-zA-Z0-9_\-]", "");
    private static string Safe(string value, string fallback)
    { var result = Regex.Replace(value, @"[^a-zA-Z0-9_\- ]", "").Trim().Replace(' ', '_'); return result.Length > 0 ? result : fallback; }
    private static string Engine(Profile profile) => profile.Text("tts_mode", "bark").Replace("_cpu", "");
    private static IEnumerable<string> Roots(Profile profile, string dataRoot)
    {
        var engine = Engine(profile) + "_cache";
        foreach (var root in new[] { ResourcePaths.VoiceCache(profile, dataRoot), profile.SourceDirectory, profile.Text("native_environment_directory"), AppContext.BaseDirectory })
        {
            if (root.Length == 0) continue;
            yield return Path.GetFileName(Path.TrimEndingDirectorySeparator(root)).Equals(engine, StringComparison.OrdinalIgnoreCase) ? root : Path.Combine(root, engine);
        }
    }
    private static string[] Clips(string root, string category, bool recursive = true)
    {
        if (!Directory.Exists(root)) return [];
        var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        return Directory.EnumerateFiles(root, "*", options).Where(p => Path.GetFileName(Path.GetDirectoryName(p)) == category && new[] { ".wav", ".mp3" }.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray();
    }
    public string[] Find(Profile profile, string dataRoot, string key)
    {
        var category = Category(key); var channel = Safe(profile.Text("channel_name"), "DefaultChannel"); var game = Safe(profile.Text("game_name"), "General");
        var roots = Roots(profile, dataRoot).Distinct().ToArray();
        string[] Search(Func<string, string> scope, bool recursive = true) => roots.SelectMany(root => Clips(scope(root), category, recursive)).Distinct().Order().ToArray();
        var clips = Search(root => Path.Combine(root, channel, game));
        if (clips.Length == 0 && game is not "General" and not "Just_Chatting")
            foreach (var fallback in new[] { "General", "Just_Chatting" }) { clips = Search(root => Path.Combine(root, channel, fallback)); if (clips.Length > 0) break; }
        if (clips.Length == 0) clips = Search(root => Path.Combine(root, category), false);
        if (clips.Length == 0 && profile.Flag("cache_fallback_any_game", true))
        { clips = Search(root => Path.Combine(root, channel)); if (clips.Length == 0) clips = Search(root => root); }
        // Native-generated clips have identity sidecars; never guess the identity of an arbitrary flat audio file.
        if (clips.Length == 0)
        {
            var cache = ResourcePaths.VoiceCache(profile, dataRoot);
            if (Directory.Exists(cache))
                clips = Directory.EnumerateFiles(cache, "*.cache.json").Where(path =>
                {
                    try { var metadata = JsonNode.Parse(File.ReadAllText(path));
                        var identities = metadata is JsonArray list ? list : new JsonArray(metadata);
                        return identities.Any(item => item?["engine"]?.GetValue<string>() == Engine(profile) && item?["channel"]?.GetValue<string>() == channel && item?["game"]?.GetValue<string>() == game && item?["category"]?.GetValue<string>() == category); }
                    catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidOperationException) { return false; }
                }).Select(path => path[..^11]).Where(File.Exists).Order().ToArray();
        }
        return clips;
    }
    private readonly object _gate = new();
    public CachedVoice? Next(Profile profile, string dataRoot, string key)
    { lock (_gate) return NextCore(profile, dataRoot, key); }
    private CachedVoice? NextCore(Profile profile, string dataRoot, string key)
    {
        var clips = Find(profile, dataRoot, key); if (clips.Length == 0) return null;
        var identity = string.Join('\n', clips); var mode = profile.Text("cache_playback_mode", "shuffle");
        if (!_bags.TryGetValue(identity, out var bag) || bag.Count == 0)
        {
            bag = clips.ToList(); Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bag));
            if (bag.Count > 1 && _last.TryGetValue(identity, out var previous) && bag[0] == previous) (bag[0], bag[1]) = (bag[1], bag[0]);
            _bags[identity] = bag;
        }
        var chosen = bag[0];
        if (mode != "repeat_one") { bag.RemoveAt(0); if (mode == "repeat_all") bag.Add(chosen); }
        _last[identity] = chosen;
        var textFile = Path.ChangeExtension(chosen, ".txt");
        return new CachedVoice(chosen, File.Exists(textFile) ? File.ReadAllText(textFile).Trim() : "");
    }
    public static void Record(Profile profile, string audio, string key, string text)
    {
        File.WriteAllText(Path.ChangeExtension(audio, ".txt"), text);
        var metadata = new JsonObject { ["engine"] = Engine(profile), ["channel"] = Safe(profile.Text("channel_name"), "DefaultChannel"), ["game"] = Safe(profile.Text("game_name"), "General"), ["category"] = Category(key) };
        var path = audio + ".cache.json";
        JsonArray identities = [];
        if (File.Exists(path))
        {
            try { var previous = JsonNode.Parse(File.ReadAllText(path)); identities = previous as JsonArray ?? new JsonArray(previous); }
            catch (System.Text.Json.JsonException) { }
        }
        if (!identities.Any(item => JsonNode.DeepEquals(item, metadata))) identities.Add(metadata);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, identities.ToJsonString()); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
