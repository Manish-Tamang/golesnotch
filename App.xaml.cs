using System.IO;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Notch.Core;
using Notch.Services;
using Notch.Views;

namespace Notch;

/// <summary>Start-up: one instance only, create the services, show the notch.</summary>
public partial class App : Application
{
    Mutex? _singleInstance;

    static App()
    {
        // WPF caps animations at 60 fps by default; let them follow high-refresh screens.
        Timeline.DesiredFrameRateProperty.OverrideMetadata(
            typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = 120 });
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Only one notch at a time.
        _singleInstance = new Mutex(true, "Notch.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var services = new AppServices();
        Theme.ApplyAccent(services.Settings.Current.Accent);

        var notch = new NotchWindow(services);
        MainWindow = notch;
        notch.Show();

        await services.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// A glitch in one widget shouldn't take the whole notch down. The error is written
    /// to crash.log next to the settings file and the app carries on.
    /// </summary>
    static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            File.AppendAllText(
                Path.Combine(SettingsStore.Folder, "crash.log"),
                $"{DateTime.Now:u}\n{e.Exception}\n\n");
        }
        catch
        {
            // Nowhere to write; nothing more to do.
        }

        e.Handled = true;
    }
}
