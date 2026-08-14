using System;

namespace CardioView.Simulation;

public sealed class BloodPressureWaveformGenerator : WaveformGeneratorBase
{
    private double _phase;

    public double Hr { get; set; } = 100;
    public double Sys { get; set; } = 120;
    public double Dia { get; set; } = 100;

    public BloodPressureWaveformGenerator() : base(8.0)
    {
    }

    protected override double NextSample()
    {
        double beat = 60.0 / Hr;
        double u = _phase / beat;

        double shape;
        if (u < 0.1)
        {
            shape = u / 0.1;
        }
        else
        {
            double t = u - 0.1;
            shape = Math.Exp(-4.0 * t) - 0.16 * Math.Exp(-60.0 * (u - 0.30) * (u - 0.30));
        }

        _phase += 1.0 / SampleRate;
        if (_phase >= beat)
            _phase -= beat;

        return Math.Clamp(Dia + (Sys - Dia) * shape, 0, 200);
    }
}
