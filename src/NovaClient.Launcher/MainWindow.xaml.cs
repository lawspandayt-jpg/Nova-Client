using System.Windows;
using System.Windows.Input;

namespace NovaClient.Launcher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) =>
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";
            MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
            // Keep the window from covering the taskbar when maximized (borderless window quirk).
            MaxHeight = WindowState == WindowState.Maximized
                ? SystemParameters.MaximizedPrimaryScreenHeight
                : double.PositiveInfinity;
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            if (WindowState == WindowState.Maximized)
            {
                // Dragging a maximized borderless window: restore first, keep cursor position.
                var point = PointToScreen(e.GetPosition(this));
                WindowState = WindowState.Normal;
                Left = point.X - Width / 2;
                Top = point.Y - 16;
            }
            DragMove();
        }
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
