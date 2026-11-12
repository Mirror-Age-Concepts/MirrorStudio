using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.Threading.Tasks;

namespace MirrorStudio.App
{
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // last-resort diagnostics: log fatal exceptions with full stack
                // (Dispatcher.UIThread.UnhandledException only exists in 11.1+)
                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                    Services.DndLog.W($"FATAL {e.ExceptionObject.GetType().Name}: {e.ExceptionObject}");
                TaskScheduler.UnobservedTaskException += (_, e) =>
                {
                    Services.DndLog.W($"UNOBSERVED TASK {e.Exception.GetType().Name}: {e.Exception}");
                    e.SetObserved();
                };

                // LibVLC is initialized lazily/guarded (see LibVlcLoader) so a native
                // loader failure can never crash app startup. No media player is wired
                // to VLC yet, so this is intentionally deferred.
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
