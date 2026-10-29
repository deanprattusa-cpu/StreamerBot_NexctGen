using System.Text.Json;
using System.Text.Json.Nodes;

namespace StreamerBot.Native.Core;

public sealed record ResourcePathEntry(string Key, string Label, string Kind, string[]? Extensions, bool OriginalOnly = false);
public static class ResourcePaths
{
    public static readonly ResourcePathEntry[] Entries = JsonSerializer.Deserialize<ResourcePathEntry[]>(
        new StreamReader(typeof(ResourcePaths).Assembly.GetManifestResourceStream("StreamerBot.Native.Core.ResourcePaths.json")!).ReadToEnd(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Where(entry => !entry.OriginalOnly).ToArray();
    public static string Value(Profile profile, string key)
    {
        if (profile.Data.ContainsKey(key)) return profile.Text(key);
        return profile.Data["resource_paths"] is JsonObject paths && paths[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
    }

    public static string Get(Profile profile, string key, string fallback = "")
    {
        var value = Value(profile, key);
        return value.Length == 0 ? fallback : profile.Resolve(Environment.ExpandEnvironmentVariables(value));
    }
    public static string Runtime(Profile profile, string dataRoot) => Get(profile, "native_runtime_path", Path.Combine(dataRoot, "Runtime"));
    public static string VoicePython(Profile profile, string dataRoot) => Get(profile, "voice_python_path", Path.Combine(Runtime(profile, dataRoot), "original", "venv", "Scripts", "python.exe"));
    public static string VoiceCache(Profile profile, string dataRoot) => Get(profile, "voice_cache_path", Path.Combine(dataRoot, "VoiceCache"));
    public static string Models(Profile profile, string dataRoot) => Get(profile, "global_model_path", Path.Combine(dataRoot, "Models"));
    public static string VideoOutput(Profile profile, string dataRoot) => Get(profile, "video_output_path", Path.Combine(dataRoot, "Videos"));
    public static string VideoPython(Profile profile, string project, string dataRoot)
    {
        var explicitPath = Get(profile, "video_python_path");
        if (explicitPath.Length > 0) return explicitPath;
        foreach (var environment in new[] { "venv", ".venv", "env" })
            foreach (var relative in new[] { "Scripts/python.exe", "bin/python" })
            { var candidate = Path.Combine(project, environment, relative); if (File.Exists(candidate)) return candidate; }
        return VoicePython(profile, dataRoot);
    }
    public static bool IsWithin(string file, string directory)
    {
        var boundary = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(file).StartsWith(boundary, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
    /// <summary>Save edited selections without making unrelated stale imported paths a save blocker.</summary>
    public static void SaveSelections(Profile profile, IReadOnlyDictionary<string, string> selections, bool? videoUseCpu = null)
    {
        var normalized = new Dictionary<string, string>();
        var errors = new List<string>();
        foreach (var (key, text) in selections)
        {
            var entry = Entries.SingleOrDefault(e => e.Key == key) ?? throw new ArgumentException("Unknown resource path: " + key);
            var value = text.Trim();
            value = value.Length == 0 ? "" : profile.Resolve(Environment.ExpandEnvironmentVariables(value));
            normalized[key] = value;
            if (value != Get(profile, key) && ValidateValue(entry, value) is string error) errors.Add(error);
        }
        if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        var before = profile.Data.DeepClone().AsObject();
        try
        {
            foreach (var (key, value) in normalized) profile.Set(key, value);
            if (videoUseCpu.HasValue) profile.Set("video_use_cpu", videoUseCpu.Value);
            profile.Save();
        }
        catch
        {
            profile.Data.Clear();
            foreach (var field in before) profile.Set(field.Key, field.Value);
            throw;
        }
    }
    private static string? ValidateValue(ResourcePathEntry entry, string value)
    {
        if (value.Length == 0) return null;
        if (entry.Kind == "file" && !File.Exists(value)) return entry.Label + ": file not found";
        if (entry.Kind == "folder" && !Directory.Exists(value)) return entry.Label + ": folder not found";
        if (entry.Kind == "output" && File.Exists(value)) return entry.Label + ": select a folder";
        if (entry.Key == "sadtalker_path" && !File.Exists(Path.Combine(value, "inference.py"))) return "SadTalker folder must contain inference.py";
        return null;
    }
    public static string[] Validate(Profile profile) => Entries.Select(entry => ValidateValue(entry, Get(profile, entry.Key)))
        .Where(message => message is not null).Cast<string>().ToArray();
}
