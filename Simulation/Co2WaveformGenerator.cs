using System;

namespace CardioView.Simulation;

public sealed class Co2WaveformGenerator : WaveformGeneratorBase
{
    private double _phase;

    public double Rr { get; set; } = 36;
    public double EtCo2 { get; set; } = 100;

    public Co2WaveformGenerator() : base(8.0)
    {
    }

    protected override double NextSample()
    {
        double breath = 60.0 / Rr;
        double u = _phase / breath;
        double a = Math.Clamp(EtCo2 / 100.0, 0, 1);

        double v;
        if (u < 0.04)
            v = 0;
        else if (u < 0.10)
            v = a * (u - 0.04) / 0.06;
        else if (u < 0.62)
            v = a * (1.0 + 0.03 * Math.Sin(u * 40));
        else if (u < 0.70)
            v = a * (1.0 - (u - 0.62) / 0.08);
        else
            v = 0;

        _phase += 1.0 / SampleRate;
        if (_phase >= breath)
            _phase -= breath;

        return v;
    }
}
