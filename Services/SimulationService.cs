using System;
using System.Windows.Threading;
using CardioView.Models;
using CardioView.Simulation;

namespace CardioView.Services;

public sealed class SimulationService
{
    private readonly DispatcherTimer _timer;
    private long _lastTick;

    public Patient Patient { get; } = new();
    public VitalSigns Vitals { get; } = new();
    public PatientSimulator Simulator { get; }
    public AlarmService Alarms { get; } = new();

    public IReadOnlyList<AlarmInfo> LastAlarms { get; private set; } = Array.Empty<AlarmInfo>();
    public double LastDt { get; private set; }
    public bool Paused { get; set; }

    public PatientState State
    {
        get => Simulator.State;
        set => Simulator.State = value;
    }

    public event EventHandler? Tick;

    public SimulationService()
    {
        Simulator = new PatientSimulator(Patient, Vitals);
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        _lastTick = Environment.TickCount64;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;
        double dt = Math.Min(0.2, (now - _lastTick) / 1000.0);
        _lastTick = now;
        LastDt = Paused ? 0 : dt;

        if (!Paused)
            Simulator.Step(dt);
        LastAlarms = Alarms.Evaluate(Vitals);
        Tick?.Invoke(this, EventArgs.Empty);
    }
}
