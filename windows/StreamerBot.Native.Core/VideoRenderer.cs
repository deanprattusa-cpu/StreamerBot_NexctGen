using System.Diagnostics;
using System.Text.Json.Nodes;

namespace StreamerBot.Native.Core;

/// <summary>Owns a SadTalker process tree; the native app remains independent of its Python environment.</summary>
public sealed class VideoRenderer(string bridge, string dataRoot)
{
    public async Task<string> RenderAsync(Profile profile, string audio, CancellationToken token)
    {
        var project = ResourcePaths.Get(profile, "sadtalker_path", Path.Combine(ResourcePaths.Models(profile, dataRoot), "SadTalker"));
        var image = ResourcePaths.Get(profile, "avatar_path");
        if (!File.Exists(Path.Combine(project, "inference.py"))) throw new InvalidOperationException("Select the SadTalker folder containing inference.py in Tools → Paths.");
        if (!File.Exists(image)) throw new InvalidOperationException("Select an avatar image in Tools → Paths.");
        var python = ResourcePaths.VideoPython(profile, project, dataRoot);
        if (!File.Exists(python)) throw new InvalidOperationException("Select the Python executable from SadTalker's environment in Tools → Paths.");
        var output = ResourcePaths.VideoOutput(profile, dataRoot);
        Directory.CreateDirectory(output);
        var job = Path.Combine(output, "native-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(job);
        var response = Path.Combine(job, "response.json"); var log = Path.Combine(job, "bridge.log");
        var start = new ProcessStartInfo(python) { WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-u", bridge, "--project", project, "--audio", Path.GetFullPath(audio), "--image", image, "--output", job, "--response", response }) start.ArgumentList.Add(argument);
        foreach (var (option, key) in new[] { ("--checkpoints", "video_checkpoints_path"), ("--ffmpeg", "ffmpeg_path") })
        { var path = ResourcePaths.Get(profile, key); if (path.Length > 0) { start.ArgumentList.Add(option); start.ArgumentList.Add(path); } }
        if (profile.Flag("video_use_cpu")) start.ArgumentList.Add("--cpu");
        start.Environment["PYTHONUTF8"] = "1";
        using var process = Process.Start(start) ?? throw new IOException("Could not start SadTalker.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(TimeSpan.FromMinutes(22));
        using var cancellation = limit.Token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        await using var outputLog = new StreamWriter(log);
        var logGate = new SemaphoreSlim(1);
        async Task DrainAsync(StreamReader stream)
        {
            while (await stream.ReadLineAsync() is { } line) { await logGate.WaitAsync(); try { await outputLog.WriteLineAsync(line); } finally { logGate.Release(); } }
        }
        var stdout = DrainAsync(process.StandardOutput); var stderr = DrainAsync(process.StandardError);
        try { await process.WaitForExitAsync(limit.Token); }
        catch { try { await process.WaitForExitAsync(); } catch (InvalidOperationException) { } await Task.WhenAll(stdout, stderr); throw; }
        await Task.WhenAll(stdout, stderr); await outputLog.FlushAsync();
        var result = File.Exists(response) ? JsonNode.Parse(await File.ReadAllTextAsync(response, token)) : null;
        if (process.ExitCode != 0) throw new InvalidOperationException((result?["error"]?.GetValue<string>() ?? "SadTalker failed.") + " Detailed renderer log: " + (result?["log"]?.GetValue<string>() ?? log));
        var video = result?["video"]?.GetValue<string>();
        if (video is null || !ResourcePaths.IsWithin(video, job) || !File.Exists(video)) throw new IOException("SadTalker returned no video for this render. Log: " + log);
        return video;
    }
}
