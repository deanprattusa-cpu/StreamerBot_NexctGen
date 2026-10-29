using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using StreamerBot.Native.Core;

internal static class UpdateChecks
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var payload = Encoding.UTF8.GetBytes("synthetic update executable fixture");
        var handler = new ReleaseHandler(payload);
        using var http = new HttpClient(handler); var client = new UpdateClient(http);
        var update = await client.CheckAsync(new Version(0, 3, 0, 0), default);
        check(update?.Version == new Version(0, 3, 1, 0), "public update check finds a newer stable release");
        check(await client.CheckAsync(new Version(0, 3, 1, 0), default) is null, "same-version release does not prompt an update");
        var directory = Path.Combine(root, "downloaded update");
        var file = await client.DownloadAsync(update!, directory, default);
        check(File.ReadAllBytes(file).SequenceEqual(payload), "update download is verified by SHA256 before installation");
        handler.BadHash = true;
        try { await client.DownloadAsync(update!, directory, default); throw new Exception("Bad checksum accepted"); }
        catch (InvalidDataException) { }
        check(File.ReadAllBytes(file).SequenceEqual(payload) && !Directory.GetFiles(directory, "*.download").Any(), "bad update checksum preserves prior download and removes temporary files");
        handler.WrongOrigin = true;
        try { await client.CheckAsync(new Version(0, 3, 0, 0), default); throw new Exception("Unexpected origin accepted"); }
        catch (InvalidDataException) { check(true, "update check rejects download outside the application repository"); }
        handler.Missing = true;
        check(await client.CheckAsync(new Version(0, 3, 0, 0), default) is null, "no published release is reported without inventing an update");
    }
    private sealed class ReleaseHandler(byte[] payload) : HttpMessageHandler
    {
        public bool BadHash, WrongOrigin, Missing;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host == "api.github.com")
            {
                if (Missing) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                var prefix = "https://github.com/" + UpdateClient.Repository + "/releases/download/v0.3.1/";
                var assets = new JsonArray();
                foreach (var name in new[] { "StreamerBot-Native-Update.exe", "SHA256SUMS.txt" }) assets.Add(new JsonObject { ["name"] = name, ["browser_download_url"] = (WrongOrigin ? "https://example.invalid/" : prefix) + name });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["tag_name"] = "v0.3.1", ["draft"] = false, ["prerelease"] = false, ["assets"] = assets }.ToJsonString()) });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri.AbsolutePath.EndsWith("SHA256SUMS.txt") ? new StringContent((BadHash ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(payload))) + "  StreamerBot-Native-Update.exe\n") : new ByteArrayContent(payload) });
        }
    }
}
