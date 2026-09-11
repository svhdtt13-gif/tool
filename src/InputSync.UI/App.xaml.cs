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
        MainWindow = new MainWindow(new SampleSyncController(debugEnabled));
        MainWindow.Show();
    }
}
