using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CardioView.Models;

namespace CardioView.Services;

public enum VitalKind
{
    HeartRate,
    Spo2,
    BloodPressure,
    RespiratoryRate,
    Temperature,
    EtCo2
}

public sealed class AlarmInfo
{
    public required VitalKind Kind { get; init; }
    public required string Message { get; init; }
    public required bool IsHigh { get; init; }
    public required double Value { get; init; }
    public required double Threshold { get; init; }

    public string Key => Kind + (IsHigh ? "+" : "-");
}

public sealed class AlarmService
{
    private readonly HashSet<string> _previous = new();
    private DateTime _lastSoundUtc = DateTime.MinValue;
    private static readonly TimeSpan SoundInterval = TimeSpan.FromSeconds(1);

    public bool Enabled { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;

    public double HrHigh { get; set; } = 120;
    public double HrLow { get; set; } = 55;
    public double Spo2Low { get; set; } = 90;
    public double SysHigh { get; set; } = 165;
    public double SysLow { get; set; } = 80;
    public double RespLow { get; set; } = 8;
    public double RespHigh { get; set; } = 45;
    public double TempLow { get; set; } = 35.0;
    public double TempHigh { get; set; } = 38.5;
    public double EtCo2Low { get; set; } = 20;
    public double EtCo2High { get; set; } = 130;

    public event EventHandler? AlarmStateChanged;

    public List<AlarmInfo> Evaluate(VitalSigns v)
    {
        var active = new List<AlarmInfo>();

        if (Enabled)
        {
            AddLow(active, VitalKind.HeartRate, "HR LOW", v.HeartRate, HrLow);
            AddHigh(active, VitalKind.HeartRate, "HR HIGH", v.HeartRate, HrHigh);
            AddLow(active, VitalKind.Spo2, "SpO2 LOW", v.Spo2, Spo2Low);
            AddHigh(active, VitalKind.BloodPressure, "PNI HIGH", v.Systolic, SysHigh);
            AddLow(active, VitalKind.BloodPressure, "PNI LOW", v.Systolic, SysLow);
            AddLow(active, VitalKind.RespiratoryRate, "RESP LOW", v.RespiratoryRate, RespLow);
            AddHigh(active, VitalKind.RespiratoryRate, "RESP HIGH", v.RespiratoryRate, RespHigh);
            AddLow(active, VitalKind.Temperature, "TEMP LOW", v.Temp1, TempLow);
            AddHigh(active, VitalKind.Temperature, "TEMP HIGH", v.Temp1, TempHigh);
            AddLow(active, VitalKind.EtCo2, "EtCO2 LOW", v.EtCo2, EtCo2Low);
            AddHigh(active, VitalKind.EtCo2, "EtCO2 HIGH", v.EtCo2, EtCo2High);
        }

        var keys = active.Select(a => a.Key).ToHashSet();
        bool newAlarm = keys.Any(k => !_previous.Contains(k));
        _previous.Clear();
        foreach (var k in keys)
            _previous.Add(k);

        if (active.Count > 0 && SoundEnabled && DateTime.UtcNow - _lastSoundUtc >= SoundInterval)
        {
            _lastSoundUtc = DateTime.UtcNow;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Console.Beep(880, 150);
                Thread.Sleep(60);
                Console.Beep(1180, 150);
            });
        }

        if (newAlarm)
        {
            AlarmStateChanged?.Invoke(this, EventArgs.Empty);
        }

        return active;
    }

    private static void AddLow(List<AlarmInfo> list, VitalKind kind, string message, double value, double threshold)
    {
        if (value < threshold)
            list.Add(new AlarmInfo { Kind = kind, Message = message, IsHigh = false, Value = value, Threshold = threshold });
    }

    private static void AddHigh(List<AlarmInfo> list, VitalKind kind, string message, double value, double threshold)
    {
        if (value > threshold)
            list.Add(new AlarmInfo { Kind = kind, Message = message, IsHigh = true, Value = value, Threshold = threshold });
    }
}
