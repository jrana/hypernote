using System.Windows;
using HyperNote.Services;
using Wpf.Ui.Appearance;

namespace HyperNote;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {

        // Global UI thread exception handler
        DispatcherUnhandledException += (sender, args) =>
        {
            var ex = args.Exception;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== UI EXCEPTION ===");
            while (ex != null)
            {
                sb.AppendLine($"Exception Type: {ex.GetType().FullName}");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine($"Stack Trace:\n{ex.StackTrace}");
                sb.AppendLine("------------------------------------");
                ex = ex.InnerException;
            }
            try
            {
                System.IO.File.WriteAllText("crash_log.txt", sb.ToString());
            }
            catch {}
            try
            {
                if (Current.MainWindow is MainWindow mw)
                {
                    mw.FlushAllBackupsForCrash();
                }
            }
            catch {}
            MessageBox.Show($"An unhandled UI exception occurred:\n{args.Exception.Message}\n\nStack Trace:\n{args.Exception.StackTrace}\n\nDetailed log written to crash_log.txt", 
                "Unhandled UI Exception", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true; // Prevent application crash
        };

        // Global background thread exception handler
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== FATAL EXCEPTION ===");
            while (ex != null)
            {
                sb.AppendLine($"Exception Type: {ex.GetType().FullName}");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine($"Stack Trace:\n{ex.StackTrace}");
                sb.AppendLine("------------------------------------");
                ex = ex.InnerException;
            }
            try
            {
                System.IO.File.WriteAllText("crash_log.txt", sb.ToString());
            }
            catch {}
            try
            {
                if (Current.MainWindow is MainWindow mw)
                {
                    mw.FlushAllBackupsForCrash();
                }
            }
            catch {}
            MessageBox.Show($"A fatal unhandled exception occurred:\n{ex?.Message}\n\nStack Trace:\n{ex?.StackTrace}", 
                "Fatal Exception", MessageBoxButton.OK, MessageBoxImage.Error);
        };


        base.OnStartup(e);
        // Register code pages provider for ANSI encoding support.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // Register global class handler for Window.Loaded to apply dark/light title bar
        EventManager.RegisterClassHandler(typeof(Window), Window.LoadedEvent, new RoutedEventHandler((sender, args) =>
        {
            if (sender is Window window)
            {
                if (ThemeManager.Current == AppTheme.Dark)
                {
                    WindowBackgroundManager.ApplyDarkThemeToWindow(window);
                }
                else
                {
                    WindowBackgroundManager.RemoveDarkThemeFromWindow(window);
                }
            }
        }));

        // Restore the saved theme before the main window renders.
        ThemeManager.Apply(SettingsService.Instance.Settings.DarkTheme
            ? AppTheme.Dark : AppTheme.Light);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsService.Instance.Save();
        base.OnExit(e);
    }
}
