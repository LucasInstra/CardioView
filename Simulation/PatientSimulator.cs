using System;
using CardioView.Models;

namespace CardioView.Simulation;

public sealed class PatientSimulator
{
    private const double Tau = 10.0;

    private readonly Patient _patient;
    private readonly VitalSigns _vitals;
    private readonly Random _rng = new();
    private double _gaussSpare;
    private bool _hasGaussSpare;

    public PatientState State { get; set; } = PatientState.Normal;

    public EcgWaveformGenerator Ecg { get; } = new();
    public Spo2WaveformGenerator Spo2 { get; } = new();
    public BloodPressureWaveformGenerator P1 { get; } = new();
    public BloodPressureWaveformGenerator P2 { get; } = new();
    public Co2WaveformGenerator Co2 { get; } = new();

    public PatientSimulator(Patient patient, VitalSigns vitals)
    {
        _patient = patient;
        _vitals = vitals;

        _vitals.HeartRate = 100;
        _vitals.Spo2 = 100;
        _vitals.Systolic = 120;
        _vitals.Diastolic = 100;
        _vitals.RespiratoryRate = 36;
        _vitals.Temp1 = 36.7;
        _vitals.Temp2 = 36.9;
        _vitals.EtCo2 = 100;
    }

    public void Step(double dt)
    {
        Drift(dt);

        Ecg.Hr = _vitals.HeartRate;
        Ecg.Step(dt);

        Spo2.Hr = _vitals.HeartRate;
        Spo2.Step(dt);

        P1.Hr = _vitals.HeartRate;
        P1.Sys = _vitals.Systolic;
        P1.Dia = _vitals.Diastolic;
        P1.Step(dt);

        P2.Hr = _vitals.HeartRate;
        P2.Sys = _vitals.Systolic;
        P2.Dia = _vitals.Diastolic;
        P2.Step(dt);

        Co2.Rr = _vitals.RespiratoryRate;
        Co2.EtCo2 = _vitals.EtCo2;
        Co2.Step(dt);
    }

    public void MeasureNibp()
    {
        _vitals.Systolic = Math.Clamp(_vitals.Systolic + NextGaussian() * 4.0, 70, 200);
        _vitals.Diastolic = Math.Clamp(_vitals.Diastolic + NextGaussian() * 3.0, 40, 120);
    }

    private void Drift(double dt)
    {
        var (hr, spo2, rr, sys, dia, temp, etco2) = Targets(State);

        _vitals.HeartRate = Walk(_vitals.HeartRate, hr, 3.0, dt);
        _vitals.Spo2 = Walk(_vitals.Spo2, spo2, 0.7, dt);
        _vitals.Systolic = Walk(_vitals.Systolic, sys, 2.5, dt);
        _vitals.Diastolic = Walk(_vitals.Diastolic, dia, 2.0, dt);
        _vitals.RespiratoryRate = Walk(_vitals.RespiratoryRate, rr, 1.5, dt);
        _vitals.Temp1 = Walk(_vitals.Temp1, temp, 0.08, dt);
        _vitals.Temp2 = _vitals.Temp1 + 0.2;
        _vitals.EtCo2 = Walk(_vitals.EtCo2, etco2, 2.0, dt);
    }

    private double Walk(double value, double target, double sigma, double dt)
        => value + (target - value) * (dt / Tau) + sigma * NextGaussian() * Math.Sqrt(dt) * 0.4;

    private static (double hr, double spo2, double rr, double sys, double dia, double temp, double etco2) Targets(PatientState state)
        => state switch
        {
            PatientState.Exercise => (125, 97, 30, 145, 85, 37.2, 100),
            PatientState.Tachycardia => (140, 98, 22, 120, 80, 36.8, 100),
            PatientState.Bradycardia => (50, 97, 14, 115, 75, 36.5, 100),
            PatientState.Hypoxia => (115, 86, 24, 125, 80, 36.9, 80),
            PatientState.Fever => (120, 97, 20, 130, 80, 38.8, 100),
            _ => (96, 98, 34, 122, 84, 36.9, 100),
        };

    private double NextGaussian()
    {
        if (_hasGaussSpare)
        {
            _hasGaussSpare = false;
            return _gaussSpare;
        }

        double u1 = 1.0 - _rng.NextDouble();
        double u2 = _rng.NextDouble();
        double r = Math.Sqrt(-2.0 * Math.Log(u1));
        double theta = 2.0 * Math.PI * u2;
        _gaussSpare = r * Math.Sin(theta);
        _hasGaussSpare = true;
        return r * Math.Cos(theta);
    }
}
