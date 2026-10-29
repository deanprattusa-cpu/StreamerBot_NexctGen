using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace StreamerBot.Native.Core;

/// <summary>Native installed-app authorization, using the original Google desktop client JSON.</summary>
public static class YouTubeAuthorization
{
    private static string RandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static async Task<JsonObject> AuthorizeAsync(HttpClient http, string clientFile, Action<Uri> openBrowser, CancellationToken cancellationToken)
    {
        var json = JsonNode.Parse(await File.ReadAllTextAsync(clientFile, cancellationToken));
        var client = json?["installed"] as JsonObject ?? throw new InvalidDataException("Select a Google Desktop app client_secret JSON containing an installed client.");
        var id = client["client_id"]?.GetValue<string>(); var secret = client["client_secret"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)) throw new InvalidDataException("Google desktop client JSON is missing client_id or client_secret.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirect = $"http://127.0.0.1:{port}/";
        var state = RandomToken(); var verifier = RandomToken();
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        const string scopes = "https://www.googleapis.com/auth/youtube.force-ssl https://www.googleapis.com/auth/youtube.readonly https://www.googleapis.com/auth/cloud-platform";
        var query = new Dictionary<string, string> { ["client_id"] = id, ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = scopes, ["access_type"] = "offline", ["prompt"] = "consent", ["state"] = state, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256" };
        openBrowser(new Uri("https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&", query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)))));
        string code;
        while (true)
        {
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            var firstLine = await reader.ReadLineAsync(timeout.Token) ?? "";
            var fields = firstLine.Split(' ');
            var requestBytes = firstLine.Length;
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(timeout.Token)))
            { requestBytes += line.Length; if (requestBytes > 16384) throw new InvalidDataException("Oversized authorization callback."); }
            var callback = fields.Length == 3 && fields[0] == "GET" && fields[1].StartsWith('/') ? new Uri(new Uri(redirect), fields[1]) : new Uri(redirect);
            var values = System.Web.HttpUtility.ParseQueryString(callback.Query);
            var valid = values["state"] == state;
            var received = values["code"];
            var denied = values["error"] is not null;
            var body = Encoding.UTF8.GetBytes(valid ? "StreamerBot received your authorization response. You can close this tab and return to the app." : "This authorization response does not match StreamerBot. Return to the sign-in tab.");
            var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {(valid ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, timeout.Token); await stream.WriteAsync(body, timeout.Token); await stream.FlushAsync(timeout.Token);
            if (!valid) continue;
            if (denied) throw new InvalidOperationException("Google authorization was declined. No credentials were changed.");
            if (string.IsNullOrWhiteSpace(received)) throw new InvalidDataException("Google authorization returned no code.");
            code = received; break;
        }
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = id, ["client_secret"] = secret, ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = redirect, ["grant_type"] = "authorization_code" }), timeout.Token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Google authorization exchange failed (HTTP {(int)response.StatusCode}).");
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token))?.AsObject() ?? throw new InvalidDataException("Google authorization returned invalid data.");
        if (string.IsNullOrWhiteSpace(result["refresh_token"]?.GetValue<string>())) throw new InvalidDataException("Google returned no refresh token. Reconnect and grant offline access.");
        return new JsonObject { ["client_id"] = id, ["client_secret"] = secret, ["refresh_token"] = result["refresh_token"]!.DeepClone(), ["token_uri"] = "https://oauth2.googleapis.com/token", ["scopes"] = new JsonArray(scopes.Split(' ').Select(s => JsonValue.Create(s)).ToArray()) };
    }
}
