using System;
using System.Collections.Generic;

namespace CardioView.Simulation;

public abstract class WaveformGeneratorBase
{
    public const double SampleRate = 250.0;

    private readonly RingBuffer<double> _samples;

    public IReadOnlyList<double> Samples => _samples;

    protected WaveformGeneratorBase(double seconds = 8.0)
    {
        _samples = new RingBuffer<double>((int)(SampleRate * seconds));
    }

    public void Step(double dt)
    {
        int n = Math.Max(1, (int)Math.Ceiling(dt * SampleRate));
        for (int i = 0; i < n; i++)
            _samples.Add(NextSample());
    }

    protected abstract double NextSample();
}
