using System.Globalization;
using System.Windows;
using System.Windows.Data;
using InputSync.Core.Controller;
using InputSync.Core.Models;

namespace InputSync.UI;

public partial class MainWindow : Window
{
    private readonly ISyncController _controller;

    public MainWindow(ISyncController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        CoordinateModes = Enum.GetValues<CoordinateMode>();
        InitializeComponent();
        DataContext = controller;
    }

    public IReadOnlyList<CoordinateMode> CoordinateModes { get; }

    private void RefreshWindows_Click(object sender, RoutedEventArgs e) => _controller.RefreshWindows();

    private void Start_Click(object sender, RoutedEventArgs e) => _controller.Start();

    private void Pause_Click(object sender, RoutedEventArgs e) => _controller.Pause();

    private void Stop_Click(object sender, RoutedEventArgs e) => _controller.Stop();

    private void EmergencyStop_Click(object sender, RoutedEventArgs e) => _controller.EmergencyStop();
}

public sealed class OnOffConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "ON" : "OFF";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
