using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CardioView.Services;

namespace CardioView.Controls;

public sealed class WaveformControl : FrameworkElement
{
    public static readonly DependencyProperty TraceColorProperty = DependencyProperty.Register(
        nameof(TraceColor), typeof(Color), typeof(WaveformControl),
        new FrameworkPropertyMetadata(Color.FromRgb(0x2B, 0xFF, 0x5A), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridDottedProperty = DependencyProperty.Register(
        nameof(GridDotted), typeof(bool), typeof(WaveformControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public Color TraceColor
    {
        get => (Color)GetValue(TraceColorProperty);
        set => SetValue(TraceColorProperty, value);
    }

    public bool GridDotted
    {
        get => (bool)GetValue(GridDottedProperty);
        set => SetValue(GridDottedProperty, value);
    }

    public IReadOnlyList<double>? Samples { get; set; }
    public IReadOnlyList<double>? HorizontalRefs { get; set; }
    public IReadOnlyList<MitBihAnnotation>? Annotations { get; set; }
    public int SamplesStart { get; set; }
    public bool ShowAnnotations { get; set; } = true;
    public double SamplesPerSecond { get; set; } = 250;
    public double SecondsVisible { get; set; } = 6;
    public double PixelsPerUnit { get; set; } = 100;
    public double ZeroYRatio { get; set; } = 0.5;

    private Color _cachedTraceColor;
    private bool _tracePensReady;
    private Pen? _glowPen;
    private Pen? _corePen;
    private Pen? _refPen;
    private readonly Dictionary<Color, Pen> _annotationPens = new();

    public void Refresh() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 1 || h <= 1) return;

        if (GridDotted)
            DrawDottedGrid(dc, w, h);

        DrawRefs(dc, w, h);
        DrawWave(dc, w, h);
        if (ShowAnnotations)
            DrawAnnotations(dc, w, h);
    }

    private DrawingGroup? _gridCache;
    private string _gridKey = "";

    private void DrawDottedGrid(DrawingContext dc, double w, double h)
    {
        string key = $"{w:0.0}|{h:0.0}|{SecondsVisible:0.00}|{PixelsPerUnit:0.0}|{ZeroYRatio:0.00}";
        if (_gridCache is null || key != _gridKey)
        {
            _gridCache = BuildGrid(w, h);
            _gridKey = key;
        }
        dc.DrawDrawing(_gridCache);
    }

    private DrawingGroup BuildGrid(double w, double h)
    {
        var group = new DrawingGroup();
        using (var ctx = group.Open())
        {
            var minorBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            minorBrush.Freeze();
            var majorBrush = new SolidColorBrush(Color.FromRgb(0x34, 0x34, 0x34));
            majorBrush.Freeze();
            var minorPen = new Pen(minorBrush, 1) { DashStyle = new DashStyle(new double[] { 1, 4 }, 0) };
            minorPen.Freeze();
            var majorPen = new Pen(majorBrush, 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
            majorPen.Freeze();

            double smallT = 0.04;
            double bigT = 0.2;
            double sdx = w / SecondsVisible;
            for (double t = smallT; t < SecondsVisible; t += smallT)
            {
                double x = w - t * sdx;
                bool isMajor = Math.Abs(t / bigT - Math.Round(t / bigT)) < 1e-9;
                ctx.DrawLine(isMajor ? majorPen : minorPen, new Point(x, 0), new Point(x, h));
            }

            double minorV = 0.1;
            double majorV = 0.5;
            double topV = ZeroYRatio * h / PixelsPerUnit;
            double bottomV = topV - h / PixelsPerUnit;
            for (double v = Math.Floor(bottomV / minorV) * minorV; v <= topV + 1e-9; v += minorV)
            {
                double y = ZeroYRatio * h - v * PixelsPerUnit;
                bool isMajor = Math.Abs(v / majorV - Math.Round(v / majorV)) < 1e-9;
                ctx.DrawLine(isMajor ? majorPen : minorPen, new Point(0, y), new Point(w, y));
            }
        }
        group.Freeze();
        return group;
    }

    private void DrawRefs(DrawingContext dc, double w, double h)
    {
        var refs = HorizontalRefs;
        if (refs is null || refs.Count == 0) return;

        EnsureTracePens(TraceColor);

        foreach (double v in refs)
        {
            double y = ZeroYRatio * h - v * PixelsPerUnit;
            dc.DrawLine(_refPen!, new Point(0, y), new Point(w, y));
        }
    }

    private void DrawWave(DrawingContext dc, double w, double h)
    {
        var samples = Samples;
        int n = samples?.Count ?? 0;
        if (samples is null || n < 2) return;

        double sps = SamplesPerSecond > 0 ? SamplesPerSecond : 250;
        int window = (int)Math.Max(2, SecondsVisible * sps);
        int start = Math.Max(0, n - window);
        int count = n - start;
        double dx = w / Math.Max(1, window - 1);
        double zeroY = ZeroYRatio * h;
        double ppm = PixelsPerUnit > 0 ? PixelsPerUnit : h / 2.0;

        int step = 1;
        int maxPts = Math.Max(64, (int)(w * 1.5));
        if (count > maxPts)
            step = (count + maxPts - 1) / maxPts;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            bool first = true;
            for (int i = 0; i < count; i += step)
            {
                double x = w - (count - 1 - i) * dx;
                double y = zeroY - samples[start + i] * ppm;
                if (first)
                {
                    ctx.BeginFigure(new Point(x, y), false, false);
                    first = false;
                }
                else
                {
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
        }

        Color trace = TraceColor;
        EnsureTracePens(trace);

        geo.Freeze();
        dc.DrawGeometry(null, _glowPen!, geo);
        dc.DrawGeometry(null, _corePen!, geo);
    }

    private void EnsureTracePens(Color trace)
    {
        if (_tracePensReady && _cachedTraceColor == trace)
            return;

        _cachedTraceColor = trace;

        var glowBrush = new SolidColorBrush(Color.FromArgb(40, trace.R, trace.G, trace.B));
        glowBrush.Freeze();
        _glowPen = new Pen(glowBrush, 6) { LineJoin = PenLineJoin.Round };
        _glowPen.Freeze();

        var coreBrush = new SolidColorBrush(trace);
        coreBrush.Freeze();
        _corePen = new Pen(coreBrush, 2) { LineJoin = PenLineJoin.Round };
        _corePen.Freeze();

        var refBrush = new SolidColorBrush(Color.FromArgb(130, trace.R, trace.G, trace.B));
        refBrush.Freeze();
        _refPen = new Pen(refBrush, 1) { DashStyle = new DashStyle(new double[] { 4, 4 }, 0) };
        _refPen.Freeze();

        _tracePensReady = true;
    }

    private Pen AnnotationPen(Color col)
    {
        if (_annotationPens.TryGetValue(col, out var pen))
            return pen;

        var brush = new SolidColorBrush(col);
        brush.Freeze();
        pen = new Pen(brush, 1);
        pen.Freeze();
        _annotationPens[col] = pen;
        return pen;
    }

    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static readonly Typeface BoldLabelTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly SolidColorBrush AuxBrush = CreateFrozenBrush(0x8E, 0xC9, 0xD0);

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void DrawAnnotations(DrawingContext dc, double w, double h)
    {
        var anns = Annotations;
        var samples = Samples;
        if (anns is null || samples is null || samples.Count < 2 || anns.Count == 0) return;

        double sps = SamplesPerSecond > 0 ? SamplesPerSecond : 250;
        int window = (int)Math.Max(2, SecondsVisible * sps);
        int n = samples.Count;
        int start = Math.Max(0, n - window);
        int count = n - start;
        if (count < 2) return;
        double dx = w / Math.Max(1, window - 1);
        double zeroY = ZeroYRatio * h;
        double ppm = PixelsPerUnit > 0 ? PixelsPerUnit : h / 2.0;
        int baseIdx = SamplesStart;

        foreach (var ann in anns)
        {
            int rel = ann.Sample - baseIdx;
            if (rel < start || rel >= n) continue;

            int i = rel - start;
            double x = w - (count - 1 - i) * dx;
            double y = zeroY - samples[rel] * ppm;
            var pen = AnnotationPen(AnnotationPalette.For(ann));

            if (ann.IsQuality)
            {
                dc.DrawLine(pen, new Point(x, y + 4), new Point(x, y + 10));
                continue;
            }

            bool rhythm = ann.Code == 28;
            double tickLen = rhythm ? 20 : 13;
            dc.DrawLine(pen, new Point(x, y), new Point(x, y + tickLen));

            string text = rhythm ? "+" : ann.Symbol.ToString();
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var ft = new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, BoldLabelTypeface, 11, pen.Brush, ppd) { MaxTextWidth = 48 };
            double ty = y - ft.Height - 1;
            if (ty < 0) ty = y + tickLen + 1;
            dc.DrawText(ft, new Point(x + 2, ty));

            if (rhythm && !string.IsNullOrEmpty(ann.Aux))
            {
                var at = new FormattedText(ann.Aux, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, LabelTypeface, 9, AuxBrush, ppd) { MaxTextWidth = 60 };
                dc.DrawText(at, new Point(x + 2, y + tickLen + 1));
            }
        }
    }

    private ToolTip? _tagTip;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateHoverTip(e.GetPosition(this));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_tagTip is not null) _tagTip.IsOpen = false;
        base.OnMouseLeave(e);
    }

    private void UpdateHoverTip(Point pos)
    {
        var ann = HitTestAnnotation(pos);
        if (ann is null)
        {
            if (_tagTip is not null) _tagTip.IsOpen = false;
            return;
        }
        if (_tagTip is null)
        {
            _tagTip = new ToolTip { Placement = PlacementMode.Mouse, PlacementTarget = this };
        }
        _tagTip.Content = $"{ann.Symbol} — {ann.Meaning}" + Environment.NewLine +
            $"Amostra {ann.Sample}  ·  {ann.Sample / (double)SamplesPerSecond:F1} s";
        _tagTip.IsOpen = true;
    }

    private MitBihAnnotation? HitTestAnnotation(Point pos)
    {
        var anns = Annotations;
        var samples = Samples;
        if (anns is null || samples is null || samples.Count < 2) return null;

        double sps = SamplesPerSecond > 0 ? SamplesPerSecond : 250;
        int window = (int)Math.Max(2, SecondsVisible * sps);
        int n = samples.Count;
        int start = Math.Max(0, n - window);
        int count = n - start;
        if (count < 2) return null;
        double w = ActualWidth;
        double dx = w / Math.Max(1, window - 1);
        double zeroY = ZeroYRatio * ActualHeight;
        double ppm = PixelsPerUnit > 0 ? PixelsPerUnit : ActualHeight / 2.0;
        int baseIdx = SamplesStart;

        MitBihAnnotation? best = null;
        double bestDist = 14;
        foreach (var ann in anns)
        {
            if (ann.IsQuality) continue;
            int rel = ann.Sample - baseIdx;
            if (rel < start || rel >= n) continue;

            int i = rel - start;
            double x = w - (count - 1 - i) * dx;
            double y = zeroY - samples[rel] * ppm;
            if (Math.Abs(pos.X - x) < bestDist && pos.Y > y - 22 && pos.Y < y + 22)
            {
                bestDist = Math.Abs(pos.X - x);
                best = ann;
            }
        }
        return best;
    }
}
