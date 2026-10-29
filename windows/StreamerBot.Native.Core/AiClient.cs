using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace StreamerBot.Native.Core;

public sealed class AiClient(HttpClient http)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _blocked = new();
    public async Task<string> CompleteAsync(Profile profile, string prompt, CancellationToken token, string? image = null)
    {
        var preferred = profile.Text("ai_mode", "local");
        var providers = new[] { preferred, "groq", "gemini", "openrouter", "sambanova", "local" }.Distinct();
        var system = $"You are the gamer speaking on a livestream. Game: {profile.Text("game_name")}. Persona: {profile.Text("persona")}. Respond naturally and briefly. Do not invent events or actions that are not in the supplied context.";
        if (profile.Flag("global_directive_enabled")) system += "\n" + profile.Text("global_directive_text");
        if (profile.Flag("chat_directive_enabled")) system += "\n" + profile.Text("chat_directive_text");
        system += "\n" + profile.Text("native_live_context");
        var attempted = new List<string>();
        foreach (var provider in providers)
        {
            token.ThrowIfCancellationRequested();
            var keys = provider == "local" ? new[] { "" } : profile.Keys(provider + "_keys");
            foreach (var key in keys)
            {
                var keyId = provider + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
                if (_blocked.TryGetValue(keyId, out var until) && until > DateTimeOffset.UtcNow) continue;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(45));
                    using var request = BuildRequest(profile, provider, key, system, prompt, image);
                    using var response = await http.SendAsync(request, timeout.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        if ((int)response.StatusCode is 401 or 403 or 429) _blocked[keyId] = DateTimeOffset.UtcNow.AddMinutes(15);
                        attempted.Add($"{provider}: HTTP {(int)response.StatusCode}");
                        continue; // Never copy response bodies/API keys into UI diagnostics.
                    }
                    var root = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: timeout.Token);
                    var text = provider == "gemini"
                        ? string.Concat(root?["candidates"]?[0]?["content"]?["parts"]?.AsArray().Select(p => p?["text"]?.GetValue<string>() ?? "") ?? [])
                        : root?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
                    attempted.Add(provider + ": empty answer");
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { attempted.Add(provider + ": timeout"); }
                catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
                { attempted.Add(provider + ": unavailable or invalid response"); }
            }
        }
        throw new InvalidOperationException("No AI backend returned an answer. " + string.Join("; ", attempted.Distinct()) + ". Configure provider keys or start LM Studio.");
    }
    private static HttpRequestMessage BuildRequest(Profile p, string provider, string key, string system, string prompt, string? image)
    {
        if (provider == "gemini")
        {
            var model = p.Text("gemini_model", "gemini-2.0-flash");
            var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
            request.Headers.Add("x-goog-api-key", key);
            var parts = new JsonArray(new JsonObject { ["text"] = prompt });
            if (image is not null) parts.Add(new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = "image/jpeg", ["data"] = image[(image.IndexOf(',') + 1)..] } });
            request.Content = JsonContent.Create(new { systemInstruction = new { parts = new[] { new { text = system } } }, contents = new[] { new { role = "user", parts } } });
            return request;
        }
        var url = provider switch
        {
            "groq" => "https://api.groq.com/openai/v1/chat/completions",
            "openrouter" => "https://openrouter.ai/api/v1/chat/completions",
            "sambanova" => "https://api.sambanova.ai/v1/chat/completions",
            _ => p.Text("lm_studio_url", "http://127.0.0.1:1234/v1").TrimEnd('/') + "/chat/completions"
        };
        var modelName = p.Text(provider + "_model", provider switch { "groq" => "openai/gpt-oss-20b", "openrouter" => "openrouter/auto", "sambanova" => "Meta-Llama-3.3-70B-Instruct", _ => "local-model" });
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (key.Length > 0) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        JsonNode content = image is null ? JsonValue.Create(prompt)! : new JsonArray(new JsonObject { ["type"] = "text", ["text"] = prompt }, new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = image } });
        req.Content = JsonContent.Create(new { model = modelName, messages = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system }, new JsonObject { ["role"] = "user", ["content"] = content }), max_tokens = 220, temperature = 0.8 });
        return req;
    }
}
