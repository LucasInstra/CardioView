using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using CardioView.ViewModels;

namespace CardioView.Views;

public partial class EcgViewerWindow : Window
{
    private readonly EcgViewModel _vm = new();

    public EcgViewerWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        EcgControl.Samples = _vm.View;

        _vm.Updated += (_, _) =>
        {
            EcgControl.SamplesPerSecond = _vm.SampleRate;
            EcgControl.PixelsPerUnit = _vm.PixelsPerUnit;
            EcgControl.ZeroYRatio = 0.5;
            EcgControl.SamplesStart = _vm.ViewStart;
            EcgControl.Annotations = _vm.Annotations;
            EcgControl.ShowAnnotations = _vm.ShowAnnotations;
            EcgControl.Refresh();

            Overview.Annotations = _vm.Annotations;
            Overview.SampleCount = _vm.TotalSamples;
            Overview.ViewStart = _vm.ViewStart;
            Overview.ViewLength = _vm.VisibleSamples;
            Overview.Refresh();
        };

        Overview.SeekRequested += (_, sample) => _vm.Seek(sample);

        Loaded += (_, _) => CenterWindow();
        SizeChanged += (_, _) => EcgControl.Refresh();
        KeyDown += OnWindowKeyDown;
    }

    private void CenterWindow()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + (wa.Height - Height) / 2;
    }

    private void OnLoadClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Selecionar arquivo .hea (MIT-BIH)",
            Filter = "Cabeçalho MIT-BIH (*.hea)|*.hea|Todos os arquivos (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) == true)
            _vm.LoadFile(dlg.FileName);
    }

    private void OnPlayClick(object sender, RoutedEventArgs e) => _vm.TogglePlay();

    private void OnRestartClick(object sender, RoutedEventArgs e) => _vm.Restart();

    private void OnTagsClick(object sender, RoutedEventArgs e) => _vm.ToggleTags();

    private void OnLegendClick(object sender, RoutedEventArgs e) => _vm.ToggleLegend();

    private void OnMonitorClick(object sender, RoutedEventArgs e)
    {
        var win = new MainWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
        win.Show();
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnTopBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}