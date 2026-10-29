using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StreamerBot.Native.Core;
using Windows.Storage.Pickers;

namespace StreamerBot.Native;

internal static class PathSettingsDialog
{
    internal static async Task<bool> ShowAsync(Window owner, XamlRoot root, Profile profile)
    {
        var rows = new StackPanel { Spacing = 14, MinWidth = 620 };
        rows.Children.Add(new TextBlock { Text = "Choose resources on any drive. Blank uses automatic defaults. Existing files are kept in place; output folders are created when used. Saving restarts the voice worker. Models still need their original engines and dependencies.", TextWrapping = TextWrapping.Wrap });
        var fields = new Dictionary<string, TextBox>();
        foreach (var entry in ResourcePaths.Entries)
        {
            var text = new TextBox { Header = entry.Label, Text = ResourcePaths.Value(profile, entry.Key), PlaceholderText = "Automatic default", MinWidth = 500 };
            fields[entry.Key] = text;
            var row = new StackPanel { Spacing = 6 };
            row.Children.Add(text);
            var browse = new Button { Content = entry.Kind == "file" ? "Select file…" : "Select folder…" };
            browse.Click += async (_, _) =>
            {
                try
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
                    if (entry.Kind == "file")
                    {
                        var picker = new FileOpenPicker(); WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                        foreach (var extension in entry.Extensions ?? ["*"]) picker.FileTypeFilter.Add(extension);
                        var file = await picker.PickSingleFileAsync(); if (file is not null) text.Text = file.Path;
                    }
                    else
                    {
                        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                        var folder = await picker.PickSingleFolderAsync(); if (folder is not null) text.Text = folder.Path;
                    }
                }
                catch (Exception error) { text.Description = "Picker unavailable: " + error.GetType().Name + ". You can type the full path."; }
            };
            row.Children.Add(browse); rows.Children.Add(row);
        }
        var cpu = new CheckBox { Content = "Render video on CPU (slower; otherwise CUDA is required)", IsChecked = profile.Flag("video_use_cpu") };
        rows.Children.Add(cpu);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; rows.Children.Add(status);
        var dialog = new ContentDialog { XamlRoot = root, Title = "Paths — models, voices & video", PrimaryButtonText = "Save paths", CloseButtonText = "Cancel", Content = new ScrollViewer { Content = rows, MaxHeight = 560 } };
        dialog.Resources["ContentDialogMaxWidth"] = 850d;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
            var draft = new Profile(Path.Combine(Path.GetTempPath(), "StreamerBot-path-validation-" + Guid.NewGuid().ToString("N"), "unused.json"));
            foreach (var field in fields) draft.Set(field.Key, field.Value.Text.Trim() is { Length: > 0 } value ? profile.Resolve(Environment.ExpandEnvironmentVariables(value)) : "");
            var errors = ResourcePaths.Validate(draft);
            if (errors.Length > 0) { args.Cancel = true; status.Text = string.Join(Environment.NewLine, errors); return; }
            foreach (var field in fields) profile.Set(field.Key, draft.Text(field.Key));
            profile.Set("video_use_cpu", cpu.IsChecked == true);
            profile.Save();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { args.Cancel = true; status.Text = "Paths could not be saved: " + error.Message; }
        };
        if (Program.SmokeTest)
        {
            dialog.Opened += (_, _) =>
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (rows.ActualHeight > 0 && fields.Count == ResourcePaths.Entries.Length) File.WriteAllText(Path.Combine(Program.DataRoot, "paths-ready.txt"), "Native paths dialog loaded all resource fields.");
                    dialog.Hide();
                };
                timer.Start();
            };
        }
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
