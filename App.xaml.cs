using System.Windows;
using System.Windows.Threading;

namespace MeshCaddy;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrashLog(args.ExceptionObject as Exception);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var path = WriteCrashLog(e.Exception);
        MessageBox.Show($"MeshCaddy could not continue.\n\n{e.Exception.Message}\n\nA diagnostic log was written to:\n{path}",
            "MeshCaddy", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown(1);
    }

    private static string WriteCrashLog(Exception? exception)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeshCaddy");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "crash.log");
        File.WriteAllText(path, $"MeshCaddy crash — {DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
        return path;
    }
}
