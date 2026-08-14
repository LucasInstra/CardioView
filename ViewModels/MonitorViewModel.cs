using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using CardioView.Models;
using CardioView.Services;

namespace CardioView.ViewModels;

public sealed class MonitorViewModel : INotifyPropertyChanged
{
    private const double NibpCycleSeconds = 8.0;
    private const double AutoNibpInterval = 30.0;
    private const double TrendSampleSeconds = 5.0;
    private const int MaxTrendRows = 40;

    private readonly SimulationService _service;
    private PatientState _state = PatientState.Normal;
    private bool _alarmFlash;
    private long _tick;

    private bool _nibpMeasuring;
    private double _nibpCountdown;
    private bool _autoNibp;
    private double _sinceLastNibp;

    private bool _settingsOpen;
    private bool _trendsOpen;

    private readonly List<string> _trend = new();
    private double _trendClock;

    private string _statusMessage = "";
    private double _statusClock;

    private readonly Dictionary<string, object?> _lastValues = new();

    public MonitorViewModel(SimulationService service)
    {
        _service = service;
        _service.Tick += (_, _) => Refresh();

        var s = SettingsStore.Load();
        _service.Alarms.Enabled = s.AlarmSystemEnabled;
        _service.Alarms.SoundEnabled = s.SoundEnabled;
        _autoNibp = s.AutoNibp;
        _service.Alarms.HrHigh = s.HrHigh;
        _service.Alarms.HrLow = s.HrLow;
        _service.Alarms.Spo2Low = s.Spo2Low;
        _service.Alarms.SysHigh = s.SysHigh;
    }

    public IReadOnlyList<double> EcgSamples => _service.Simulator.Ecg.Samples;
    public IReadOnlyList<double> Spo2Samples => _service.Simulator.Spo2.Samples;
    public IReadOnlyList<double> P1Samples => _service.Simulator.P1.Samples;
    public IReadOnlyList<double> P2Samples => _service.Simulator.P2.Samples;
    public IReadOnlyList<double> Co2Samples => _service.Simulator.Co2.Samples;

    public double[] P1Refs { get; } = new double[2];
    public double[] P2Refs { get; } = new double[2];

    public IReadOnlyList<PatientState> States { get; } = Enum.GetValues<PatientState>();

    public PatientState State
    {
        get => _state;
        set
        {
            if (Set(ref _state, value))
                _service.State = value;
        }
    }

    public string TimeText => DateTime.Now.ToString("HH:mm");
    public string DateText => DateTime.Now.ToString("dd/MM/yyyy");
    public string PatientLabel => _service.Patient.DisplayName;

    public string HrText => ((int)Math.Round(_service.Vitals.HeartRate)).ToString();
    public string Spo2Text => ((int)Math.Round(_service.Vitals.Spo2)).ToString();
    public string RespText => ((int)Math.Round(_service.Vitals.RespiratoryRate)).ToString();
    public string Temp1Text => Dec(_service.Vitals.Temp1);
    public string Temp2Text => Dec(_service.Vitals.Temp2);
    public string StText => "2.3";
    public string MapText => $"{Round(_service.Vitals.Systolic)}/{Round(_service.Vitals.Diastolic)}";
    public string P1Text => Pressure();
    public string P2Text => Pressure();
    public string NibpText => _nibpMeasuring ? "MEDINDO" : Pressure();
    public string Etco2Text => ((int)Math.Round(_service.Vitals.EtCo2)).ToString();
    public string Fico2Text => ((int)Math.Round(_service.Vitals.Fico2)).ToString("D3");
    public string DeltaText => ((int)(_tick * 0.0033)).ToString("D2");

    public string SoundText => _service.Alarms.SoundEnabled ? "Som Alarme Ligado" : "Som Alarme Desligado";
    public string AlarmStatus => AlarmActive
        ? AlarmText
        : _service.Alarms.Enabled ? "Alarmes Ligados" : "Alarmes Desligados";
    public string AlarmText => AlarmActive
        ? "ALARM: " + string.Join("  ", _service.LastAlarms.Select(a => a.Message))
        : "";

    public bool AlarmActive => _service.LastAlarms.Count > 0;
    public bool HrAlarm => _service.LastAlarms.Any(a => a.Kind == VitalKind.HeartRate);
    public bool Spo2Alarm => _service.LastAlarms.Any(a => a.Kind == VitalKind.Spo2);
    public bool NibpAlarm => _service.LastAlarms.Any(a => a.Kind == VitalKind.BloodPressure);
    public bool RespAlarm => _service.LastAlarms.Any(a => a.Kind == VitalKind.RespiratoryRate);
    public bool TempAlarm => _service.LastAlarms.Any(a => a.Kind == VitalKind.Temperature);
    public bool Etco2Alarm => _service.LastAlarms.Any(a => a.Kind == VitalKind.EtCo2);

    public bool HrNormal => In(_service.Vitals.HeartRate, _service.Alarms.HrLow, _service.Alarms.HrHigh);
    public bool Spo2Normal => _service.Vitals.Spo2 >= _service.Alarms.Spo2Low;
    public bool NibpNormal => In(_service.Vitals.Systolic, _service.Alarms.SysLow, _service.Alarms.SysHigh);
    public bool RespNormal => In(_service.Vitals.RespiratoryRate, _service.Alarms.RespLow, _service.Alarms.RespHigh);
    public bool TempNormal => In(_service.Vitals.Temp1, _service.Alarms.TempLow, _service.Alarms.TempHigh);
    public bool Etco2Normal => In(_service.Vitals.EtCo2, _service.Alarms.EtCo2Low, _service.Alarms.EtCo2High);

    private static bool In(double v, double lo, double hi) => v >= lo && v <= hi;

    public bool AlarmFlash
    {
        get => _alarmFlash;
        private set => Set(ref _alarmFlash, value);
    }

    public bool AlarmSystemEnabled
    {
        get => _service.Alarms.Enabled;
        set
        {
            _service.Alarms.Enabled = value;
            OnPropertyChanged(nameof(AlarmSystemEnabled));
            OnPropertyChanged(nameof(AlarmStatus));
            SaveSettings();
        }
    }

    public bool SoundEnabled
    {
        get => _service.Alarms.SoundEnabled;
        set
        {
            _service.Alarms.SoundEnabled = value;
            OnPropertyChanged(nameof(SoundEnabled));
            OnPropertyChanged(nameof(SoundText));
            OnPropertyChanged(nameof(AlarmStatus));
            SaveSettings();
        }
    }

    public double HrHighLimit
    {
        get => _service.Alarms.HrHigh;
        set { _service.Alarms.HrHigh = value; OnPropertyChanged(nameof(HrHighLimit)); SaveSettings(); }
    }

    public double HrLowLimit
    {
        get => _service.Alarms.HrLow;
        set { _service.Alarms.HrLow = value; OnPropertyChanged(nameof(HrLowLimit)); SaveSettings(); }
    }

    public double Spo2LowLimit
    {
        get => _service.Alarms.Spo2Low;
        set { _service.Alarms.Spo2Low = value; OnPropertyChanged(nameof(Spo2LowLimit)); SaveSettings(); }
    }

    public double SysHighLimit
    {
        get => _service.Alarms.SysHigh;
        set { _service.Alarms.SysHigh = value; OnPropertyChanged(nameof(SysHighLimit)); SaveSettings(); }
    }

    public bool NibpMeasuring => _nibpMeasuring;
    public string NibpButtonText => _nibpMeasuring ? "MEDINDO..." : "PNI";

    public bool AutoNibp
    {
        get => _autoNibp;
        set
        {
            _autoNibp = value;
            _sinceLastNibp = 0;
            OnPropertyChanged(nameof(AutoNibp));
            SetStatus(value ? "PNI automático ativado" : "PNI automático desativado");
            SaveSettings();
        }
    }

    public bool Paused
    {
        get => _service.Paused;
        set
        {
            _service.Paused = value;
            OnPropertyChanged(nameof(Paused));
            OnPropertyChanged(nameof(PauseButtonText));
            SetStatus(value ? "Simulação congelada" : "Simulação retomada");
        }
    }

    public string PauseButtonText => _service.Paused ? "RETOMAR" : "CONGELAR";

    public IReadOnlyList<string> TrendRows => _trend;

    public bool IsSettingsOpen => _settingsOpen;
    public bool IsTrendsOpen => _trendsOpen;
    public bool IsOverlayOpen => _settingsOpen || _trendsOpen;
    public string OverlayTitle => _settingsOpen ? "AJUSTES" : "TENDÊNCIA";

    public string StatusMessage => _statusMessage;
    public string ToolbarRightText => string.IsNullOrEmpty(_statusMessage)
        ? "SIM v0.3 · F11 tela cheia"
        : _statusMessage;

    public event EventHandler? WaveformsUpdated;

    public void ToggleSound()
    {
        _service.Alarms.SoundEnabled = !_service.Alarms.SoundEnabled;
        OnPropertyChanged(nameof(SoundEnabled));
        OnPropertyChanged(nameof(SoundText));
        OnPropertyChanged(nameof(AlarmStatus));
    }

    public void StartNibp()
    {
        if (_nibpMeasuring) return;
        _nibpMeasuring = true;
        _nibpCountdown = NibpCycleSeconds;
        RaiseNibp();
        SetStatus("Medindo PNI...");
    }

    public void OpenSettings()
    {
        _settingsOpen = true;
        _trendsOpen = false;
        OnChangedOverlay();
    }

    public void OpenTrends()
    {
        _trendsOpen = true;
        _settingsOpen = false;
        OnChangedOverlay();
        OnPropertyChanged(nameof(TrendRows));
    }

    public void CloseOverlay()
    {
        _settingsOpen = false;
        _trendsOpen = false;
        OnChangedOverlay();
    }

    public void ClearTrend()
    {
        _trend.Clear();
        OnPropertyChanged(nameof(TrendRows));
    }

    public void AddMarker()
    {
        _trend.Add($"{DateTime.Now:HH:mm:ss}   MARCA DE EVENTO");
        TrimTrend();
        OnPropertyChanged(nameof(TrendRows));
        SetStatus("Marca registrada");
    }

    public void SetStatus(string message)
    {
        _statusMessage = message;
        _statusClock = 0;
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(ToolbarRightText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Refresh()
    {
        _tick++;
        double dt = _service.LastDt;
        var v = _service.Vitals;
        P1Refs[0] = v.Diastolic;
        P1Refs[1] = v.Systolic;
        P2Refs[0] = v.Diastolic;
        P2Refs[1] = v.Systolic;

        TickNibp(dt);
        TickTrend(dt);
        TickStatus(dt);

        NotifyIfChanged(nameof(TimeText), TimeText);
        NotifyIfChanged(nameof(DateText), DateText);
        NotifyIfChanged(nameof(PatientLabel), PatientLabel);
        NotifyIfChanged(nameof(HrText), HrText);
        NotifyIfChanged(nameof(Spo2Text), Spo2Text);
        NotifyIfChanged(nameof(RespText), RespText);
        NotifyIfChanged(nameof(Temp1Text), Temp1Text);
        NotifyIfChanged(nameof(Temp2Text), Temp2Text);
        NotifyIfChanged(nameof(StText), StText);
        NotifyIfChanged(nameof(MapText), MapText);
        NotifyIfChanged(nameof(P1Text), P1Text);
        NotifyIfChanged(nameof(P2Text), P2Text);
        NotifyIfChanged(nameof(NibpText), NibpText);
        NotifyIfChanged(nameof(Etco2Text), Etco2Text);
        NotifyIfChanged(nameof(Fico2Text), Fico2Text);
        NotifyIfChanged(nameof(DeltaText), DeltaText);
        NotifyIfChanged(nameof(AlarmStatus), AlarmStatus);
        NotifyIfChanged(nameof(AlarmText), AlarmText);
        NotifyIfChanged(nameof(AlarmActive), AlarmActive);
        NotifyIfChanged(nameof(HrAlarm), HrAlarm);
        NotifyIfChanged(nameof(Spo2Alarm), Spo2Alarm);
        NotifyIfChanged(nameof(NibpAlarm), NibpAlarm);
        NotifyIfChanged(nameof(RespAlarm), RespAlarm);
        NotifyIfChanged(nameof(TempAlarm), TempAlarm);
        NotifyIfChanged(nameof(Etco2Alarm), Etco2Alarm);
        NotifyIfChanged(nameof(HrNormal), HrNormal);
        NotifyIfChanged(nameof(Spo2Normal), Spo2Normal);
        NotifyIfChanged(nameof(NibpNormal), NibpNormal);
        NotifyIfChanged(nameof(RespNormal), RespNormal);
        NotifyIfChanged(nameof(TempNormal), TempNormal);
        NotifyIfChanged(nameof(Etco2Normal), Etco2Normal);
        NotifyIfChanged(nameof(SoundText), SoundText);
        NotifyIfChanged(nameof(ToolbarRightText), ToolbarRightText);

        AlarmFlash = AlarmActive && _tick % 12 < 6;

        WaveformsUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyIfChanged(string name, object? value)
    {
        if (_lastValues.TryGetValue(name, out var previous) && Equals(previous, value))
            return;
        _lastValues[name] = value;
        OnPropertyChanged(name);
    }

    private void TickNibp(double dt)
    {
        _sinceLastNibp += dt;

        if (_nibpMeasuring)
        {
            _nibpCountdown -= dt;
            if (_nibpCountdown <= 0)
            {
                _nibpMeasuring = false;
                _sinceLastNibp = 0;
                _service.Simulator.MeasureNibp();
                RaiseNibp();
                SetStatus("PNI medido");
            }
        }
        else if (_autoNibp && _sinceLastNibp >= AutoNibpInterval)
        {
            StartNibp();
        }
    }

    private void TickTrend(double dt)
    {
        _trendClock += dt;
        if (_trendClock < TrendSampleSeconds) return;
        _trendClock = 0;

        _trend.Add($"{DateTime.Now:HH:mm:ss}   HR {HrText}  SpO2 {Spo2Text}%  P {P1Text}  RESP {RespText}");
        TrimTrend();
        OnPropertyChanged(nameof(TrendRows));
    }

    private void TickStatus(double dt)
    {
        if (string.IsNullOrEmpty(_statusMessage)) return;
        _statusClock += dt;
        if (_statusClock <= 4.0) return;
        _statusMessage = "";
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(ToolbarRightText));
    }

    private void RaiseNibp()
    {
        OnPropertyChanged(nameof(NibpMeasuring));
        OnPropertyChanged(nameof(NibpButtonText));
        OnPropertyChanged(nameof(NibpText));
    }

    private void OnChangedOverlay()
    {
        OnPropertyChanged(nameof(IsSettingsOpen));
        OnPropertyChanged(nameof(IsTrendsOpen));
        OnPropertyChanged(nameof(IsOverlayOpen));
        OnPropertyChanged(nameof(OverlayTitle));
    }

    private void TrimTrend()
    {
        while (_trend.Count > MaxTrendRows)
            _trend.RemoveAt(0);
    }

    private string Pressure()
    {
        var v = _service.Vitals;
        return $"{Round(v.Systolic)}/{Round(v.Diastolic)}({Round(v.Map)})";
    }

    private static int Round(double v) => (int)Math.Round(v);

    private static string Dec(double v)
        => v.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');

    private void SaveSettings()
    {
        SettingsStore.Save(new AppSettings
        {
            AlarmSystemEnabled = _service.Alarms.Enabled,
            SoundEnabled = _service.Alarms.SoundEnabled,
            AutoNibp = _autoNibp,
            HrHigh = _service.Alarms.HrHigh,
            HrLow = _service.Alarms.HrLow,
            Spo2Low = _service.Alarms.Spo2Low,
            SysHigh = _service.Alarms.SysHigh,
        });
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
