using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StreamerBot.Native.Core;

public sealed record ChannelAccount(int Number, string Name, string ChannelId, string TokenPrefix)
{
    public override string ToString() => $"{Number}: {Name}";
}

/// <summary>Imports original dotenv data without changing the source or the process environment.</summary>
public static class OriginalEnvironment
{
    public const string ValuesKey = "native_environment";
    public static Dictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var at = line.IndexOf('=');
            if (at <= 0) continue;
            var key = line[..at].Trim();
            if (!Regex.IsMatch(key, @"^[A-Za-z_][A-Za-z0-9_]*$")) continue;
            var value = line[(at + 1)..].Trim();
            if (value.StartsWith('"') || value.StartsWith('\''))
            {
                var quote = value[0]; var end = 1;
                for (; end < value.Length; end++)
                {
                    if (value[end] == '\\' && quote == '"') { end++; continue; }
                    if (value[end] == quote) break;
                }
                if (end >= value.Length) throw new InvalidDataException($"Unterminated quoted .env value for {key}.");
                value = value[1..end];
                if (quote == '"') value = Regex.Replace(value, @"\\([nrt""\\])", m => m.Groups[1].Value switch { "n" => "\n", "r" => "\r", "t" => "\t", var c => c });
            }
            else
            {
                var comment = Regex.Match(value, @"\s+#");
                if (comment.Success) value = value[..comment.Index].TrimEnd();
            }
            result[key] = value;
        }
        return result;
    }
    public static int Import(Profile profile, string path)
    {
        // Parse completely before touching the active profile. Keep unknown future entries.
        var entries = Parse(File.ReadAllText(path));
        var values = profile.Data[ValuesKey]?.DeepClone() as JsonObject ?? new JsonObject();
        foreach (var (key, value) in entries) values[key] = value;
        profile.Set(ValuesKey, values);
        profile.Set("native_environment_directory", Path.GetDirectoryName(Path.GetFullPath(path))!);
        foreach (var (prefix, target) in new Dictionary<string, string> { ["GROQ"] = "groq_keys", ["GEMINI"] = "gemini_keys", ["GOOGLE"] = "gemini_keys", ["OPENROUTER"] = "openrouter_keys", ["SAMBANOVA"] = "sambanova_keys", ["ELEVENLABS"] = "elevenlabs_keys", ["JINA"] = "jina_keys" })
        {
            var keys = entries.Where(e => Regex.IsMatch(e.Key, "^" + prefix + @"_API_KEYS?(?:_\d+)?$")).SelectMany(e => e.Value.Split([',', '\n', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Distinct().ToArray();
            if (keys.Length > 0) profile.Set(target, new JsonArray(keys.Select(k => JsonValue.Create(k)).ToArray()));
        }
        foreach (var (source, target) in new Dictionary<string, string> { ["HF_TOKEN"] = "hf_token", ["TIKTOK_SESSION_ID"] = "tiktok_session_id", ["GLOBAL_MODEL_PATH"] = "global_model_path" })
            if (entries.TryGetValue(source, out var value)) profile.Set(target, value);
        return entries.Count;
    }
    public static void ImportProfile(Profile profile, string path)
    {
        var retained = new[] { ValuesKey, "native_environment_directory", "native_channel_tokens", "native_youtube_token_path" }.ToDictionary(k => k, k => profile.Data[k]?.DeepClone());
        profile.Import(path);
        foreach (var (key, value) in retained) if (!profile.Data.ContainsKey(key) && value is not null) profile.Set(key, value);
    }
    public static ChannelAccount[] Channels(Profile profile)
    {
        if (profile.Data[ValuesKey] is not JsonObject values) return [];
        string Value(string key) => values[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        return values.Select(e => Regex.Match(e.Key, @"^CHANNEL_(\d+)_NAME$"))
            .Where(m => m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > 0)
            .Select(m => int.Parse(m.Groups[1].Value)).Distinct().Order()
            .Select(n => new ChannelAccount(n, Value($"CHANNEL_{n}_NAME"), Value($"CHANNEL_{n}_ID"), Value($"CHANNEL_{n}_TOKEN_PREFIX")))
            .Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToArray();
    }
    public static ChannelAccount? Selected(Profile profile)
    {
        var channels = Channels(profile);
        return channels.FirstOrDefault(c => c.Number == profile.Number("native_channel_number"))
            ?? channels.ElementAtOrDefault(Math.Clamp((int)profile.Number("channel_index"), 0, Math.Max(0, channels.Length - 1)));
    }
    public static void Select(Profile profile, ChannelAccount channel)
    {
        profile.Set("native_channel_number", channel.Number);
        profile.Set("channel_index", Array.FindIndex(Channels(profile), c => c.Number == channel.Number));
        profile.Set("channel_name", channel.Name); profile.Set("channel_id", channel.ChannelId); profile.Set("token_prefix", channel.TokenPrefix);
    }
    public static ChannelAccount Add(Profile profile, string name, string id)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Enter a channel name and YouTube channel ID.");
        var channels = Channels(profile); var number = 1;
        while (channels.Any(c => c.Number == number)) number++;
        var values = profile.Data[ValuesKey]?.DeepClone() as JsonObject ?? new JsonObject();
        values[$"CHANNEL_{number}_NAME"] = name.Trim(); values[$"CHANNEL_{number}_ID"] = id.Trim();
        values[$"CHANNEL_{number}_TOKEN_PREFIX"] = "channel" + number;
        profile.Set(ValuesKey, values);
        var channel = Channels(profile).Single(c => c.Number == number); Select(profile, channel); return channel;
    }
    public static void SetToken(Profile profile, ChannelAccount channel, string path)
    {
        var paths = profile.Data["native_channel_tokens"]?.DeepClone() as JsonObject ?? new JsonObject();
        paths[channel.Number.ToString()] = Path.GetFullPath(path); profile.Set("native_channel_tokens", paths);
    }
    public static string TokenFile(Profile profile)
    {
        var channel = Selected(profile);
        if (channel is null) return profile.Text("native_youtube_token_path");
        if (profile.Data["native_channel_tokens"]?[channel.Number.ToString()] is JsonValue explicitPath && explicitPath.TryGetValue<string>(out var path) && path.Length > 0) return profile.Resolve(path);
        var directory = profile.Text("native_environment_directory");
        if (directory.Length == 0 || channel.TokenPrefix.Length == 0) return "";
        var credentials = Path.Combine(directory, "credentials");
        if (!Directory.Exists(credentials)) return "";
        // Compare file names; a prefix is never interpreted as a path or a wildcard.
        var pattern = "^" + Regex.Escape(channel.TokenPrefix) + @"_token_(\d+)\.json$";
        return Directory.EnumerateFiles(credentials, "*.json").Select(p => (Path: p, Match: Regex.Match(Path.GetFileName(p), pattern)))
            .Where(p => p.Match.Success).OrderBy(p => p.Match.Groups[1].Value.Length).ThenBy(p => p.Match.Groups[1].Value, StringComparer.Ordinal).Select(p => p.Path).FirstOrDefault() ?? "";
    }
}
