using System.Text.Json.Nodes;
using StreamerBot.Native.Core;

internal static class KeyboardPathChecks
{
    internal static Task Run(string root, Action<bool, string> check)
    {
        var profile = new Profile(Path.Combine(root, "persisted-paths", "profile.json"));
        var stale = Path.Combine(root, "unavailable old avatar.png"); profile.Set("avatar_path", stale);
        profile.Data["future_setting"] = new JsonObject { ["retained"] = true };
        var cache = Path.Combine(root, "cache on another drive", "new folder");
        ResourcePaths.SaveSelections(profile, new Dictionary<string, string> { ["avatar_path"] = stale, ["voice_cache_path"] = cache });
        var reopened = new Profile(profile.StoragePath);
        check(ResourcePaths.VoiceCache(reopened, root) == cache && !Directory.Exists(cache), "cache selection survives profile restart even when unrelated old paths are unavailable");
        check(reopened.Text("avatar_path") == stale && reopened.Data["future_setting"]?["retained"]?.GetValue<bool>() == true, "saving cache retains existing unavailable paths and unknown settings");
        var before = File.ReadAllBytes(profile.StoragePath); var state = profile.Data.DeepClone();
        try { ResourcePaths.SaveSelections(profile, new Dictionary<string, string> { ["avatar_path"] = Path.Combine(root, "new missing image.png") }); throw new Exception("Invalid changed input accepted"); }
        catch (InvalidDataException) { }
        check(before.SequenceEqual(File.ReadAllBytes(profile.StoragePath)) && JsonNode.DeepEquals(state, profile.Data), "invalid changed paths do not partially update saved or in-memory selections");
        var file = Path.Combine(root, "cache-is-file"); File.WriteAllText(file, "fixture");
        try { ResourcePaths.SaveSelections(profile, new Dictionary<string, string> { ["voice_cache_path"] = file }); throw new Exception("Cache file accepted as folder"); }
        catch (InvalidDataException) { }
        check(ResourcePaths.VoiceCache(new Profile(profile.StoragePath), root) == cache, "cache file instead of folder is rejected and prior cache stays saved");
        ResourcePaths.SaveSelections(profile, new Dictionary<string, string> { ["voice_cache_path"] = "" });
        check(ResourcePaths.VoiceCache(new Profile(profile.StoragePath), root) == Path.Combine(root, "VoiceCache"), "clearing cache selection persists automatic location after restart");
        return Task.CompletedTask;
    }
}
