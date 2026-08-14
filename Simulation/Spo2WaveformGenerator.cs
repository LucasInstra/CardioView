using System;

namespace CardioView.Simulation;

public sealed class Spo2WaveformGenerator : WaveformGeneratorBase
{
    private double _phase;

    public double Hr { get; set; } = 100;

    public Spo2WaveformGenerator() : base(8.0)
    {
    }

    protected override double NextSample()
    {
        double beat = 60.0 / Hr;
        double u = _phase / beat;
        double envelope = Math.Exp(-5.5 * u);
        double pulse = Math.Sin(Math.PI * 2.2 * u) * envelope;

        _phase += 1.0 / SampleRate;
        if (_phase >= beat)
            _phase -= beat;

        return Math.Clamp(0.35 + 0.55 * pulse, 0.02, 1.0);
    }
}
