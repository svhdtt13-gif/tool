using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using InputSync.Core.Controller;
using InputSync.Core.Models;
using InputSync.Win32;

namespace InputSync.UI;

public partial class MainWindow : Window
{
    private readonly ISyncController _controller;

    public MainWindow(ISyncController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        CoordinateModes = Enum.GetValues<CoordinateMode>();
        Backends = Enum.GetValues<TargetBackend>();
        InitializeComponent();
        DataContext = controller;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public IReadOnlyList<CoordinateMode> CoordinateModes { get; }

    public IReadOnlyList<TargetBackend> Backends { get; }

    private void RefreshWindows_Click(object sender, RoutedEventArgs e) => _controller.RefreshWindows();

    private void Start_Click(object sender, RoutedEventArgs e) => _controller.Start();

    private void Pause_Click(object sender, RoutedEventArgs e) => _controller.Pause();

    private void Stop_Click(object sender, RoutedEventArgs e) => _controller.Stop();

    private void EmergencyStop_Click(object sender, RoutedEventArgs e) => _controller.EmergencyStop();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F8 && _controller is RealSyncController real)
        {
            real.TogglePauseResume();
            e.Handled = true;
        }
        else if (e.Key == Key.F9)
        {
            _controller.Stop();
            e.Handled = true;
        }
        else if (e.Key == Key.F10)
        {
            _controller.EmergencyStop();
            e.Handled = true;
        }
    }
}

public sealed class OnOffConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "ON" : "OFF";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
