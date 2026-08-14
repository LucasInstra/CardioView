using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using CardioView.Services;
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

    private void OnDiagnosisClick(object sender, RoutedEventArgs e) => _vm.ToggleDiagnosis();

    private void OnReportClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.HasLoaded)
        {
            MessageBox.Show(this, "Carregue um registro .hea antes de gerar o relatório.",
                "Relatório", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string safeName = string.IsNullOrWhiteSpace(_vm.RecordName)
            ? "ECG"
            : string.Concat(_vm.RecordName.Where(char.IsLetterOrDigit));
        if (safeName.Length == 0) safeName = "ECG";

        var dlg = new SaveFileDialog
        {
            Title = "Salvar relatório PDF",
            Filter = "Documento PDF (*.pdf)|*.pdf",
            FileName = $"CardioView_{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            byte[] png = RenderEcgStrip();
            var data = new EcgReportData
            {
                RecordName = _vm.RecordName,
                SampleRate = _vm.SampleRate,
                SignalCount = _vm.LeadNames.Count,
                TotalSamples = _vm.TotalSamples,
                Report = _vm.Report,
                Annotations = _vm.Annotations ?? Array.Empty<MitBihAnnotation>(),
                WaveformPng = png,
            };

            byte[] pdf = ReportService.BuildEcgPdf(data);
            File.WriteAllBytes(dlg.FileName, pdf);

            MessageBox.Show(this, $"Relatório salvo em:\n{dlg.FileName}",
                "Relatório", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao gerar o relatório:\n" + ex.Message,
                "Relatório", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Desenha um traçado de ECG em alta resolução (fundo branco) a partir dos
    /// dados do sinal, para o relatório PDF.
    /// </summary>
    private byte[] RenderEcgStrip()
    {
        var samples = _vm.View;
        int n = samples.Count;
        if (n < 2) return Array.Empty<byte>();

        double sps = _vm.SampleRate > 0 ? _vm.SampleRate : 360;
        const double secondsVisible = 6.0;
        int window = (int)Math.Max(2, secondsVisible * sps);
        int start = Math.Max(0, n - window);
        int count = n - start;
        if (count < 2) return Array.Empty<byte>();

        double ppm = Math.Max(90, Math.Min(_vm.PixelsPerUnit * 2.2, 260));

        double dataMin = double.MaxValue, dataMax = double.MinValue;
        for (int i = start; i < start + count; i++)
        {
            if (samples[i] < dataMin) dataMin = samples[i];
            if (samples[i] > dataMax) dataMax = samples[i];
        }
        double maxAbs = Math.Max(Math.Abs(dataMin), Math.Abs(dataMax));
        if (maxAbs <= 0) maxAbs = 1.0;

        const double headroom = 1.25;
        double h = Math.Max(320, 2 * maxAbs * ppm * headroom + 20);
        double w = Math.Max(700, count / (double)Math.Max(1, window) * 1500);
        double dx = w / Math.Max(1, window - 1);
        double zeroY = h / 2;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Colors.White), null, new Rect(0, 0, w, h));

            var minorBrush = new SolidColorBrush(Color.FromRgb(0xE9, 0xE9, 0xE9));
            minorBrush.Freeze();
            var majorBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));
            majorBrush.Freeze();
            var minorPen = new Pen(minorBrush, 1);
            minorPen.Freeze();
            var majorPen = new Pen(majorBrush, 1);
            majorPen.Freeze();
            var zeroBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
            zeroBrush.Freeze();
            var zeroPen = new Pen(zeroBrush, 1);
            zeroPen.Freeze();

            double smallT = 0.04, bigT = 0.2;
            double sdx = w / secondsVisible;
            for (double t = smallT; t < secondsVisible; t += smallT)
            {
                double x = w - t * sdx;
                bool isMajor = Math.Abs(t / bigT - Math.Round(t / bigT)) < 1e-9;
                dc.DrawLine(isMajor ? majorPen : minorPen, new Point(x, 0), new Point(x, h));
            }

            double minorV = 0.1, majorV = 0.5;
            double topV = zeroY / ppm;
            double bottomV = (zeroY - h) / ppm;
            for (double v = Math.Floor(bottomV / minorV) * minorV; v <= topV + 1e-9; v += minorV)
            {
                double y = zeroY - v * ppm;
                if (Math.Abs(v) < 1e-9)
                {
                    dc.DrawLine(zeroPen, new Point(0, y), new Point(w, y));
                    continue;
                }
                bool isMajor = Math.Abs(v / majorV - Math.Round(v / majorV)) < 1e-9;
                dc.DrawLine(isMajor ? majorPen : minorPen, new Point(0, y), new Point(w, y));
            }

            var geo = new StreamGeometry();
            using (var gctx = geo.Open())
            {
                bool first = true;
                int step = 1;
                int maxPts = Math.Max(64, (int)(w * 1.5));
                if (count > maxPts) step = (count + maxPts - 1) / maxPts;

                for (int i = 0; i < count; i += step)
                {
                    double x = w - (count - 1 - i) * dx;
                    double y = zeroY - samples[start + i] * ppm;
                    if (first) { gctx.BeginFigure(new Point(x, y), false, false); first = false; }
                    else gctx.LineTo(new Point(x, y), true, false);
                }
            }
            geo.Freeze();

            var glowBrush = new SolidColorBrush(Color.FromArgb(40, 0x10, 0x5A, 0x2C));
            glowBrush.Freeze();
            var glowPen = new Pen(glowBrush, 6) { LineJoin = PenLineJoin.Round };
            glowPen.Freeze();
            var traceBrush = new SolidColorBrush(Color.FromRgb(0x10, 0x5A, 0x2C));
            traceBrush.Freeze();
            var tracePen = new Pen(traceBrush, 2.2) { LineJoin = PenLineJoin.Round };
            tracePen.Freeze();
            dc.DrawGeometry(null, glowPen, geo);
            dc.DrawGeometry(null, tracePen, geo);

            if (_vm.ShowAnnotations && _vm.Annotations is { } anns && anns.Count > 0)
                DrawStripAnnotations(dc, anns, samples, start, count, w, dx, zeroY, ppm, _vm.ViewStart);
        }

        const double scale = 2.0;
        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static void DrawStripAnnotations(
        DrawingContext dc,
        IReadOnlyList<MitBihAnnotation> anns,
        IReadOnlyList<double> samples,
        int start,
        int count,
        double w,
        double dx,
        double zeroY,
        double ppm,
        int baseIdx)
    {
        var tickBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        tickBrush.Freeze();
        var tickPen = new Pen(tickBrush, 1.1);
        tickPen.Freeze();
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
        labelBrush.Freeze();
        var auxBrush = new SolidColorBrush(Color.FromRgb(0x1B, 0x6A, 0x8A));
        auxBrush.Freeze();
        var auxTypeface = new Typeface("Segoe UI");
        var boldTypeface = new Typeface(
            new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        for (int k = 0; k < anns.Count; k++)
        {
            var ann = anns[k];
            if (ann.IsQuality) continue;
            int rel = ann.Sample - baseIdx;
            if (rel < start || rel >= start + count) continue;

            int i = rel - start;
            double x = w - (count - 1 - i) * dx;
            double y = zeroY - samples[rel] * ppm;

            bool rhythm = ann.Code == 28;
            double tickLen = rhythm ? 22 : 14;
            dc.DrawLine(tickPen, new Point(x, y), new Point(x, y + tickLen));

            string text = rhythm ? "+" : ann.Symbol.ToString();
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                boldTypeface, 13, labelBrush, 1.0) { MaxTextWidth = 46 };
            double ty = y - ft.Height - 2;
            if (ty < 0) ty = y + tickLen + 2;
            dc.DrawText(ft, new Point(x + 2, ty));

            if (rhythm && !string.IsNullOrEmpty(ann.Aux))
            {
                var at = new FormattedText(ann.Aux, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    auxTypeface, 10, auxBrush, 1.0) { MaxTextWidth = 56 };
                dc.DrawText(at, new Point(x + 2, y + tickLen + 2));
            }
        }
    }

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