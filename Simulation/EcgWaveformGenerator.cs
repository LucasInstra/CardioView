using System;

namespace CardioView.Simulation;

public sealed class EcgWaveformGenerator : WaveformGeneratorBase
{
    private double _phase;

    public double Hr { get; set; } = 100;

    public EcgWaveformGenerator() : base(8.0)
    {
    }

    protected override double NextSample()
    {
        double beat = 60.0 / Hr;
        double u = _phase / beat;

        double v = G(u, 0.18, 0.03, -0.12)
                 + G(u, 0.30, 0.012, 1.30)
                 + G(u, 0.35, 0.014, -0.30)
                 + G(u, 0.50, 0.055, 0.38)
                 + 0.008 * Math.Sin(u * 2 * Math.PI * 12)
                 + 0.004 * Math.Sin(u * 2 * Math.PI * 31);

        _phase += 1.0 / SampleRate;
        if (_phase >= beat)
            _phase -= beat;

        return v;
    }

    private static double G(double u, double c, double s, double a)
        => a * Math.Exp(-0.5 * Math.Pow((u - c) / s, 2));
}
