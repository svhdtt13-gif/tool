using System.Windows;

namespace InputSync.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
#if DEBUG
        const bool debugEnabled = true;
#else
        const bool debugEnabled = false;
#endif
        var controller = new Win32.RealSyncController(
            debugEnabled,
            action => Dispatcher.BeginInvoke(action));
        MainWindow = new MainWindow(controller);
        Exit += (_, _) => (MainWindow.DataContext as IDisposable)?.Dispose();
        MainWindow.Show();
    }
}
