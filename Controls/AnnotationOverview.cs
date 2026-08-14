using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CardioView.Services;

namespace CardioView.Controls;

public sealed class AnnotationOverview : FrameworkElement
{
    public IReadOnlyList<MitBihAnnotation>? Annotations { get; set; }
    public int SampleCount { get; set; }
    public int ViewStart { get; set; }
    public int ViewLength { get; set; }

    public event EventHandler<int>? SeekRequested;

    public void Refresh() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 1 || h <= 1) return;

        var bg = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0B));
        bg.Freeze();
        dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));

        var anns = Annotations;
        if (anns is null || anns.Count == 0 || SampleCount <= 0)
        {
            DrawViewport(dc, w, h);
            return;
        }

        double xScale = w / SampleCount;
        double midY = h / 2.0;

        foreach (var ann in anns)
        {
            if (ann.Sample < 0 || ann.Sample >= SampleCount) continue;
            double x = ann.Sample * xScale;
            var brush = new SolidColorBrush(AnnotationPalette.For(ann));
            brush.Freeze();
            var pen = new Pen(brush, ann.Code == 5 || ann.Code == 28 ? 2.5 : 1.2);
            pen.Freeze();
            dc.DrawLine(pen, new Point(x, 2), new Point(x, h - 2));
        }

        DrawViewport(dc, w, h);
    }

    private void DrawViewport(DrawingContext dc, double w, double h)
    {
        if (SampleCount <= 0 || ViewLength <= 0) return;

        double x0 = ViewStart / (double)SampleCount * w;
        double x1 = Math.Min(w, (ViewStart + ViewLength) / (double)SampleCount * w);
        var fill = new SolidColorBrush(Color.FromArgb(60, 90, 160, 255));
        fill.Freeze();
        var border = new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0xA0, 0xFF)), 1);
        border.Freeze();
        dc.DrawRectangle(fill, border, new Rect(x0, 0, Math.Max(1, x1 - x0), h));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        SeekFrom(e.GetPosition(this).X);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            SeekFrom(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        base.OnMouseLeftButtonUp(e);
    }

    private void SeekFrom(double x)
    {
        if (SampleCount <= 0 || ActualWidth <= 0) return;
        double ratio = Math.Clamp(x / ActualWidth, 0.0, 1.0);
        SeekRequested?.Invoke(this, (int)(ratio * SampleCount));
    }
}