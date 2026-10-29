using System.Diagnostics;
using System.Text.Json.Nodes;

namespace StreamerBot.Native.Core;

/// <summary>Versioned, serialized stdio boundary. The worker performs voice work only.</summary>
public sealed class VoiceWorker(string python, string script, string dataRoot, string? audioRoot = null, string? runtimeRoot = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1);
    private Process? _process;
    public async Task<JsonObject> CallAsync(string operation, JsonObject payload, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_process is null || _process.HasExited)
            {
                if (!File.Exists(python)) throw new InvalidOperationException("Prepare voice engines first using Audio → Install / repair voice engines.");
                var start = new ProcessStartInfo(python) { WorkingDirectory = dataRoot, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("-u"); start.ArgumentList.Add(script); start.ArgumentList.Add("--data-root"); start.ArgumentList.Add(dataRoot);
                start.Environment["PYTHONUTF8"] = "1";
                start.Environment["PLAYWRIGHT_BROWSERS_PATH"] = Path.Combine(runtimeRoot ?? Path.Combine(dataRoot, "Runtime"), "original", "browsers");
                _process?.Dispose();
                _process = Process.Start(start) ?? throw new IOException("Could not start the voice worker.");
                // Drain logs; protocol stdout remains bounded and contains no provider logs.
                _process.ErrorDataReceived += (_, _) => { };
                _process.BeginErrorReadLine();
            }
            var id = Guid.NewGuid().ToString("N");
            payload = payload.DeepClone().AsObject(); payload["id"] = id; payload["protocol"] = 1; payload["operation"] = operation;
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(TimeSpan.FromMinutes(10));
            using var cancellation = limit.Token.Register(Interrupt);
            await _process.StandardInput.WriteLineAsync(payload.ToJsonString().AsMemory(), limit.Token);
            await _process.StandardInput.FlushAsync(limit.Token);
            var line = await _process.StandardOutput.ReadLineAsync(limit.Token);
            if (line is null || line.Length > 1024 * 1024) throw new IOException("Voice worker stopped or returned an oversized response.");
            var result = JsonNode.Parse(line) as JsonObject ?? throw new IOException("Invalid voice response.");
            if (result["id"]?.GetValue<string>() != id || result["protocol"]?.GetValue<int>() != 1) throw new IOException("Voice response does not match the request.");
            if (result["ok"]?.GetValue<bool>() != true) throw new InvalidOperationException(result["error"]?.GetValue<string>() ?? "Voice generation failed.");
            if (result["audio"] is JsonValue value)
            {
                var audio = Path.GetFullPath(value.GetValue<string>());
                var boundary = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if ((!audio.StartsWith(boundary, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && (audioRoot is null || !ResourcePaths.IsWithin(audio, audioRoot))) || !File.Exists(audio)) throw new IOException("Voice worker returned a missing file or a path outside app data.");
            }
            return result;
        }
        catch { Interrupt(); if (_process is not null) { try { await _process.WaitForExitAsync(); } catch (InvalidOperationException) { } } throw; }
        finally { _gate.Release(); }
    }
    public void Interrupt()
    {
        try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
    public async ValueTask DisposeAsync()
    {
        Interrupt(); await _gate.WaitAsync();
        try { if (_process is not null) { await _process.WaitForExitAsync(); _process.Dispose(); _process = null; } }
        finally { _gate.Release(); _gate.Dispose(); }
    }
}
