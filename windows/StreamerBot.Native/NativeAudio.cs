using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using Windows.Devices.Enumeration;
using StreamerBot.Native.Core;

namespace StreamerBot.Native;

internal sealed class NativeAudio : IDisposable
{
    private readonly MediaPlayer _player = new();
    private TaskCompletionSource? _completion;
    public NativeAudio()
    {
        _player.MediaEnded += (_, _) => _completion?.TrySetResult();
        _player.MediaFailed += (_, _) => _completion?.TrySetException(new IOException("Audio playback failed. Check the output device and audio format."));
    }
    public async Task SpeakOfflineAsync(string text, Profile profile, CancellationToken token)
    {
        using var synth = new SpeechSynthesizer();
        var selected = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == profile.Text("native_windows_voice"));
        if (selected is not null) synth.Voice = selected;
        using var speech = await synth.SynthesizeTextToStreamAsync(text).AsTask(token);
        _player.Source = MediaSource.CreateFromStream(speech, speech.ContentType);
        await PlayAsync(profile, token);
    }
    public async Task<string> SynthesizeOfflineFileAsync(string text, Profile profile, string cacheDirectory, CancellationToken token)
    {
        Directory.CreateDirectory(cacheDirectory);
        var identity = profile.Text("native_windows_voice") + "\n" + text;
        var path = Path.Combine(cacheDirectory, "offline-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity))) + ".wav");
        if (File.Exists(path)) return path;
        if (profile.Flag("force_cache_only")) throw new InvalidOperationException("No matching offline voice cache entry.");
        using var synth = new SpeechSynthesizer();
        var selected = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == profile.Text("native_windows_voice"));
        if (selected is not null) synth.Voice = selected;
        using var speech = await synth.SynthesizeTextToStreamAsync(text).AsTask(token);
        using var reader = new Windows.Storage.Streams.DataReader(speech.GetInputStreamAt(0));
        await reader.LoadAsync(checked((uint)speech.Size)).AsTask(token);
        var bytes = new byte[checked((int)speech.Size)]; reader.ReadBytes(bytes);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, bytes, token); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }
    public async Task PlayFileAsync(string path, Profile profile, CancellationToken token)
    {
        _player.Source = MediaSource.CreateFromUri(new Uri(Path.GetFullPath(path)));
        await PlayAsync(profile, token);
    }
    private async Task PlayAsync(Profile profile, CancellationToken token)
    {
        var device = profile.Text("native_speaker_id");
        if (device.Length > 0) _player.AudioDevice = await DeviceInformation.CreateFromIdAsync(device);
        _player.Volume = Math.Clamp(profile.Number("volume", 1), 0, 1);
        _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = token.Register(() => { _player.Pause(); _completion?.TrySetCanceled(token); });
        _player.Play(); await _completion.Task;
    }
    public void Stop() { _player.Pause(); _completion?.TrySetCanceled(); }
    public void Dispose() { Stop(); _player.Dispose(); }
}
