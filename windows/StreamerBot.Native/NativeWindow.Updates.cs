using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using StreamerBot.Native.Core;

namespace StreamerBot.Native;

internal sealed partial class NativeWindow
{
    private bool _checkingUpdate;
    private async Task CheckUpdatesAsync()
    {
        if (_checkingUpdate) return;
        _checkingUpdate = true;
        try
        {
            var version = typeof(NativeWindow).Assembly.GetName().Version!;
            var client = new UpdateClient(_http);
            WriteLog("Checking for StreamerBot updates…");
            var update = await client.CheckAsync(version, _lifetime.Token);
            if (update is null) { await NoticeAsync($"Installed version: {version.ToString(3)}. No newer published update is available."); return; }
            var dialog = new ContentDialog { XamlRoot = _root.XamlRoot, Title = "StreamerBot update available", Content = $"Version {update.Version.ToString(3)} is ready. Download and apply the update? StreamerBot will close. Your settings, credentials, cache and models are kept.", PrimaryButtonText = "Download and update", CloseButtonText = "Later" };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            WriteLog("Downloading update and verifying its checksum…");
            var file = await client.DownloadAsync(update, Path.Combine(Program.DataRoot, "Updates", update.Version.ToString(3)), _lifetime.Token);
            SaveToolbar(); _profile.Save();
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, Arguments = "/S /RESTART /APP_PID=" + Environment.ProcessId });
            await CloseApplicationAsync();
        }
        finally { _checkingUpdate = false; }
    }
}
