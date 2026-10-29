using System.Text.RegularExpressions;

namespace StreamerBot.Native.Core;

public sealed class StreamController(Profile profile, AiClient ai) : IAsyncDisposable
{
    private readonly SemaphoreSlim _reply = new(1);
    private CancellationTokenSource _session = new();
    private CancellationTokenSource _action = new();
    private Task? _loop;
    private YouTubeClient? _youtube;
    private DateTimeOffset _lastReply;
    private readonly HashSet<string> _viewers = [];
    private int _generation;
    public bool Running { get; private set; }
    public bool Simulation { get; private set; }
    public event Action<string>? Log;
    public event Action<ChatMessage>? Chat;
    public event Action<string>? Caption;
    public event Func<string, string, CancellationToken, Task>? Speak;
    public event Func<string, CancellationToken, Task<string?>>? Review;
    public void Start(YouTubeClient? youtube, bool simulation)
    {
        if (Running) return;
        if (!simulation && youtube is null) throw new InvalidOperationException("Connect a YouTube credential first, or choose Simulate.");
        _session.Cancel(); _session.Dispose(); _session = new CancellationTokenSource();
        _youtube = youtube; Simulation = simulation; Running = true; _viewers.Clear();
        _loop = LoopAsync(_session.Token);
        Log?.Invoke(simulation ? "Simulation started. No messages will be posted to YouTube." : "Connecting to your live broadcast...");
    }
    private async Task LoopAsync(CancellationToken token)
    {
        try
        {
            var idle = DateTimeOffset.UtcNow;
            while (!token.IsCancellationRequested)
            {
                if (_youtube is not null)
                {
                    if (_youtube.ChatId is null && !await _youtube.FindLiveAsync(token)) { Log?.Invoke("No active live broadcast. Retrying in 30 seconds."); await Task.Delay(30000, token); continue; }
                    var (messages, delay) = await _youtube.PollAsync(token);
                    foreach (var message in messages)
                    {
                        Chat?.Invoke(message); idle = DateTimeOffset.UtcNow;
                        if (profile.Flag("hotkeys_only_mode")) continue;
                        var first = _viewers.Add(message.Author);
                        if (profile.Flag("automated_replies", true) && DateTimeOffset.UtcNow - _lastReply >= TimeSpan.FromSeconds(12))
                        {
                            _lastReply = DateTimeOffset.UtcNow;
                            var prompt = $"Reply to {message.Author}: {message.Text}";
                            if (first && profile.Flag("greet_viewers", true)) prompt += " Welcome them naturally.";
                            await GenerateAsync(prompt, "reply", token: token);
                        }
                    }
                    await Task.Delay(delay, token);
                }
                else await Task.Delay(1000, token);
                if (profile.Flag("idle_chat", true) && !profile.Flag("hotkeys_only_mode") && DateTimeOffset.UtcNow - idle >= TimeSpan.FromSeconds(45))
                { idle = DateTimeOffset.UtcNow; await GenerateAsync("Say one brief, natural line to the viewers while playing, without inventing an event.", "idle", token: token); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) { Log?.Invoke(e.Message); }
        finally { Running = false; Log?.Invoke("Session stopped."); }
    }
    public async Task GenerateAsync(string prompt, string category = "manual", string? image = null, CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _session.Token, _action.Token);
        if (profile.Flag("force_cache_only")) { Log?.Invoke("Cache-only mode: AI generation skipped. Use a cached fixed phrase."); return; }
        if (!await _reply.WaitAsync(0, linked.Token)) { Log?.Invoke("AI is busy; request skipped."); return; }
        var generation = Volatile.Read(ref _generation);
        try
        {
            var text = await ai.CompleteAsync(profile, prompt, linked.Token, image);
            linked.Token.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _generation)) return;
            await DeliverAsync(text, category, linked.Token);
        }
        finally { _reply.Release(); }
    }
    public async Task DeliverAsync(string text, string category, CancellationToken token = default, string? mode = null, bool silent = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (profile.Flag("review_mode") && Review is not null && !silent)
        {
            var approved = await Review(text, token);
            if (string.IsNullOrWhiteSpace(approved)) return;
            text = approved;
        }
        mode ??= category.StartsWith("hk_") ? profile.Text(category + "_mode", "V+T") : profile.Text("auto_reply_output_mode", "V+T");
        if (!silent) { Caption?.Invoke(text); Log?.Invoke("AI: " + text); }
        if (!silent && mode.Contains('T') && Running && !Simulation && _youtube is not null)
        {
            var clean = Regex.Replace(text, @"\[.*?\]|\*.*?\*|\(.*?\)", "").Trim();
            if (clean.Length > 0) await _youtube.SendAsync(clean, token);
        }
        if (mode.Contains('V') && (!profile.Flag("global_ai_mute") || silent) && Speak is not null) await Speak(text, silent ? "cache:" + category : category, token);
    }
    public async Task TriggerAsync(string key, CancellationToken token = default)
    {
        var phrases = profile.Text(key + "_custom");
        if (phrases.Length > 0)
        {
            var choices = phrases.Split(['|', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (choices.Length > 0) await DeliverAsync(choices[Random.Shared.Next(choices.Length)], key, token);
            return;
        }
        var prompt = profile.Text(key + "_prompt", "React naturally to " + key.Replace("hk_", "").Replace('_', ' '));
        if (profile.Flag(key + "_short")) prompt += " Use at most ten words.";
        if (profile.Flag("custom_length_enabled")) prompt += $" Use at most {profile.Number("custom_length_val", 30)} words.";
        await GenerateAsync(prompt, key, token: token);
    }
    public async Task StopAsync()
    {
        Interlocked.Increment(ref _generation); _session.Cancel();
        if (_loop is not null) await _loop;
        _loop = null; Running = false;
        await _reply.WaitAsync(); _reply.Release();
        _session.Dispose(); _session = new CancellationTokenSource();
    }
    public void Interrupt() { Interlocked.Increment(ref _generation); var previous = _action; _action = new CancellationTokenSource(); previous.Cancel(); previous.Dispose(); }
    public async ValueTask DisposeAsync() { await StopAsync(); _session.Dispose(); _action.Cancel(); _action.Dispose(); _reply.Dispose(); }
}
