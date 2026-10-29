using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace StreamerBot.Native.Core;

public sealed record AppUpdate(Version Version, Uri Download, Uri Checksums);

/// <summary>Public releases only. Downloads are validated before an installer is launched.</summary>
public sealed class UpdateClient(HttpClient http)
{
    public const string Repository = "deanprattusa-cpu/StreamerBot_NexctGen";
    public async Task<AppUpdate?> CheckAsync(Version current, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + Repository + "/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("StreamerBotNative", current.ToString()));
        using var response = await http.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!.AsObject();
        var tag = document["tag_name"]?.GetValue<string>() ?? "";
        if (document["draft"]?.GetValue<bool>() == true || document["prerelease"]?.GetValue<bool>() == true || !Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        version = new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        if (version <= current) return null;
        var prefix = "https://github.com/" + Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/";
        Uri Asset(string name)
        {
            var asset = document["assets"]?.AsArray().FirstOrDefault(a => a?["name"]?.GetValue<string>() == name);
            var url = asset?["browser_download_url"]?.GetValue<string>();
            if (url != prefix + name) throw new InvalidDataException("The update release has a missing or unexpected download: " + name);
            return new Uri(url);
        }
        return new AppUpdate(version, Asset("StreamerBot-Native-Update.exe"), Asset("SHA256SUMS.txt"));
    }
    public async Task<string> DownloadAsync(AppUpdate update, string directory, CancellationToken token)
    {
        // Only the application repository may supply executable updates.
        var prefix = "https://github.com/" + Repository + "/releases/download/";
        if (!update.Download.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal) || !update.Checksums.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Unexpected update origin.");
        using var sums = await http.GetAsync(update.Checksums, HttpCompletionOption.ResponseHeadersRead, token);
        sums.EnsureSuccessStatusCode();
        using var manifestStream = await sums.Content.ReadAsStreamAsync(token);
        using var manifest = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await manifestStream.ReadAsync(buffer, token)) > 0)
        {
            if (manifest.Length + read > 65536) throw new InvalidDataException("Update checksum file is too large.");
            manifest.Write(buffer, 0, read);
        }
        var line = System.Text.Encoding.UTF8.GetString(manifest.ToArray()).Split('\n').Select(l => l.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).SingleOrDefault(parts => parts.Length == 2 && parts[1] == "StreamerBot-Native-Update.exe");
        if (line is null || line[0].Length != 64 || !line[0].All(Uri.IsHexDigit)) throw new InvalidDataException("No valid checksum for the update installer.");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "StreamerBot-Native-Update.exe");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var response = await http.GetAsync(update.Download, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            using var source = await response.Content.ReadAsStreamAsync(token);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                long size = 0;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    size += read;
                    if (size > 512L * 1024 * 1024) throw new InvalidDataException("Update installer is too large.");
                    digest.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), token);
                }
                if (size == 0 || !Convert.ToHexString(digest.GetHashAndReset()).Equals(line[0], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update checksum failed. Please download the update again.");
            }
            File.Move(temporary, target, true);
            return target;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
