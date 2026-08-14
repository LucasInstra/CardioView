using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardioView.Models;
using CardioView.Services;
using CardioView.ViewModels;

namespace CardioView.Views;

public partial class MainWindow : Window
{
    private readonly SimulationService _service;
    private readonly MonitorViewModel _vm;

    public MainWindow()
    {
        _service = new SimulationService();
        _vm = new MonitorViewModel(_service);

        InitializeComponent();
        DataContext = _vm;

        EcgControl.Samples = _vm.EcgSamples;
        Spo2Control.Samples = _vm.Spo2Samples;
        P1Control.Samples = _vm.P1Samples;
        P2Control.Samples = _vm.P2Samples;
        Co2Control.Samples = _vm.Co2Samples;

        P1Control.HorizontalRefs = _vm.P1Refs;
        P2Control.HorizontalRefs = _vm.P2Refs;

        _vm.WaveformsUpdated += (_, _) => RefreshWaveforms();

        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Width = wa.Width * 0.96;
            Height = wa.Height * 0.94;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + (wa.Height - Height) / 2;
            UpdateScales();
            _service.Start();
        };

        StateChanged += (_, _) =>
        {
            Chrome.CornerRadius = WindowState == WindowState.Maximized
                ? new CornerRadius(0)
                : new CornerRadius(20);
        };
        SizeChanged += (_, _) => UpdateScales();
    }

    private void RefreshWaveforms()
    {
        EcgControl.Refresh();
        Spo2Control.Refresh();
        P1Control.Refresh();
        P2Control.Refresh();
        Co2Control.Refresh();
    }

    private void UpdateScales()
    {
        if (EcgControl.ActualHeight > 10)
        {
            EcgControl.PixelsPerUnit = EcgControl.ActualHeight / 5.0;
            EcgControl.ZeroYRatio = 0.5;
        }

        if (Spo2Control.ActualHeight > 10)
        {
            Spo2Control.PixelsPerUnit = Spo2Control.ActualHeight / 1.5;
            Spo2Control.ZeroYRatio = 0.75;
        }

        if (P1Control.ActualHeight > 10)
        {
            P1Control.PixelsPerUnit = P1Control.ActualHeight / 160.0;
            P1Control.ZeroYRatio = 1.0;
            P2Control.PixelsPerUnit = P2Control.ActualHeight / 160.0;
            P2Control.ZeroYRatio = 1.0;
        }

        if (Co2Control.ActualHeight > 10)
        {
            Co2Control.PixelsPerUnit = Co2Control.ActualHeight / 1.1;
            Co2Control.ZeroYRatio = 1.0;
        }

        RefreshWaveforms();
    }

    private void OnAjustesClick(object sender, RoutedEventArgs e) => _vm.OpenSettings();

    private void OnNibpClick(object sender, RoutedEventArgs e) => _vm.StartNibp();

    private void OnCapturaClick(object sender, RoutedEventArgs e) => CaptureScreen();

    private void OnAutoClick(object sender, RoutedEventArgs e) => _vm.AutoNibp = !_vm.AutoNibp;

    private void OnTrendClick(object sender, RoutedEventArgs e) => _vm.OpenTrends();

    private void OnMarcarClick(object sender, RoutedEventArgs e) => _vm.AddMarker();

    private void OnAlarmesClick(object sender, RoutedEventArgs e) => _vm.AlarmSystemEnabled = !_vm.AlarmSystemEnabled;

    private void OnPauseClick(object sender, RoutedEventArgs e) => _vm.Paused = !_vm.Paused;

    private void OnEcgClick(object sender, RoutedEventArgs e)
    {
        var win = new EcgViewerWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
        win.Show();
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnStateClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PatientState state)
            _vm.State = state;
    }

    private void OnOverlayCloseClick(object sender, RoutedEventArgs e) => _vm.CloseOverlay();

    private void OnTrendClearClick(object sender, RoutedEventArgs e) => _vm.ClearTrend();

    private void CaptureScreen()
    {
        try
        {
            var root = (FrameworkElement)Content;
            int width = (int)Math.Max(1, root.ActualWidth);
            int height = (int)Math.Max(1, root.ActualHeight);

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Capturas");
            Directory.CreateDirectory(dir);

            string file = Path.Combine(dir, $"Captura_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(file))
                encoder.Save(fs);

            _vm.SetStatus($"Captura salva: {Path.GetFileName(file)}");
        }
        catch
        {
            _vm.SetStatus("Falha na captura");
        }
    }

    private void OnTopBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnTopBarMouseUp(object sender, MouseButtonEventArgs e)
    {
    }

    private void OnSoundToggle(object sender, MouseButtonEventArgs e)
    {
        _vm.AlarmSystemEnabled = !_vm.AlarmSystemEnabled;
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
            ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
