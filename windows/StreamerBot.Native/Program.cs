using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;

namespace StreamerBot.Native;

internal static class Program
{
    internal static readonly bool SmokeTest = Environment.GetCommandLineArgs() is [_, "--smoke-test", _];
    internal static readonly string DataRoot = SmokeTest ? Path.GetFullPath(Environment.GetCommandLineArgs()[2]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamerBotNative");
    internal static void Trace(string message)
    {
        if (SmokeTest) File.AppendAllText(Path.Combine(DataRoot, "startup.log"), DateTimeOffset.Now + " " + message + Environment.NewLine);
    }
    [STAThread]
    private static void Main()
    {
        Directory.CreateDirectory(DataRoot);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => File.AppendAllText(Path.Combine(DataRoot, "native-crash.log"), DateTimeOffset.Now + " " + (SmokeTest ? args.ExceptionObject.ToString() : (args.ExceptionObject as Exception)?.GetType().Name) + Environment.NewLine);
        Trace("Main: initializing COM wrappers");
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Trace("Main: starting XAML application");
        Application.Start(initialization =>
        {
            Trace("Application.Start callback");
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            try { _ = new NativeApplication(); }
            catch (Exception error)
            {
                File.AppendAllText(Path.Combine(DataRoot, "native-crash.log"), SmokeTest ? error.ToString() : error.GetType().Name);
                Environment.Exit(1);
            }
        });
    }
}
internal sealed class NativeApplication : Application, IXamlMetadataProvider
{
    private NativeWindow? _window;
    private readonly Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsXamlMetaDataProvider _metadata = new();
    public IXamlType GetXamlType(Type type) => _metadata.GetXamlType(type);
    public IXamlType GetXamlType(string name) => _metadata.GetXamlType(name);
    public XmlnsDefinition[] GetXmlnsDefinitions() => _metadata.GetXmlnsDefinitions();
    public NativeApplication()
    {
        UnhandledException += (_, args) =>
            File.AppendAllText(Path.Combine(Program.DataRoot, "native-crash.log"), DateTimeOffset.Now + " " + (Program.SmokeTest ? args.Exception.ToString() : args.Exception.GetType().Name) + Environment.NewLine);
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Program.Trace("OnLaunched: initializing native control resources");
            var resources = new ResourceDictionary();
            Resources = resources;
            resources.MergedDictionaries.Add(new Microsoft.UI.Xaml.Controls.XamlControlsResources());
            Program.Trace("OnLaunched: resources loaded");
            LaunchWindow();
        }
        catch (Exception error)
        {
            File.AppendAllText(Path.Combine(Program.DataRoot, "native-crash.log"), Program.SmokeTest ? error.ToString() : error.GetType().Name);
            Environment.Exit(1);
        }
    }
    private void LaunchWindow()
    {
        Program.Trace("OnLaunched: constructing window");
        _window = new NativeWindow();
        Program.Trace("OnLaunched: activating window");
        _window.Activate();
    }
}
