using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StreamerBot.Native.Core;

public static class Catalog
{
    private static readonly JsonObject Document = JsonNode.Parse(new StreamReader(
        typeof(Catalog).Assembly.GetManifestResourceStream("StreamerBot.Native.Core.Defaults.json")!).ReadToEnd())!.AsObject();
    public static JsonObject Defaults() => Document["settings"]!.DeepClone().AsObject();
    public static string[] SmartKeys => Document["smart_hotkeys"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
    public static string[] SystemKeys => Document["system_hotkeys"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
    public static readonly string[] Voices = ["elevenlabs", "fish", "kokoro", "voicebox", "tiktok", "typecast", "elevenlabs_ghost", "rvc", "soprano", "edge", "google", "bark", "offline"];
}

/// <summary>Preserves every imported JSON property. Import never writes the original profile.</summary>
public sealed class Profile
{
    public JsonObject Data { get; private set; } = Catalog.Defaults();
    public string SourceDirectory { get; private set; }
    public string StoragePath { get; }
    public Profile(string storagePath)
    {
        StoragePath = Path.GetFullPath(storagePath);
        SourceDirectory = Path.GetDirectoryName(StoragePath)!;
        if (File.Exists(StoragePath))
        {
            Data = Read(StoragePath);
            var origin = StoragePath + ".origin";
            if (File.Exists(origin) && Directory.Exists(File.ReadAllText(origin))) SourceDirectory = File.ReadAllText(origin);
        }
    }
    public string Text(string key, string fallback = "") => Data[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : fallback;
    public bool Flag(string key, bool fallback = false) => Data[key] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;
    public double Number(string key, double fallback = 0) => Data[key] is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) ? number : fallback;
    public string[] Keys(string key) => Data[key] switch
    {
        JsonArray array => array.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s : "").Where(s => !string.IsNullOrWhiteSpace(s)).ToArray(),
        JsonValue value when value.TryGetValue<string>(out var s) => s.Split(['\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        _ => []
    };
    public void Set(string key, JsonNode? value) => Data[key] = value?.DeepClone();
    public void Set(string key, string value) => Data[key] = value;
    public void Set(string key, bool value) => Data[key] = value;
    public void Set(string key, double value) => Data[key] = value;
    public void Import(string path)
    {
        var imported = Read(path); // Validate fully before changing the active document.
        SourceDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Data = imported;
    }
    private static JsonObject Read(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > 16 * 1024 * 1024) throw new InvalidDataException("Profile exceeds 16 MB.");
        return JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { MaxDepth = 64 }) as JsonObject
            ?? throw new InvalidDataException("An original profile must be a JSON object.");
    }
    public string Resolve(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(SourceDirectory, path));
    public void Save() { Export(StoragePath); AtomicWrite(StoragePath + ".origin", SourceDirectory); }
    public void Export(string path) => AtomicWrite(Path.GetFullPath(path), Data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    private static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file)) { writer.Write(content); writer.Flush(); file.Flush(true); }
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
