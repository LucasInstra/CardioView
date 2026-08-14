using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using CardioView.Controls;
using CardioView.Services;

namespace CardioView.ViewModels;

public sealed class TagLegendItem
{
    public char Symbol { get; init; }
    public string Meaning { get; init; } = "";
    public int Count { get; init; }
    public Color Color { get; init; }
}

public sealed class EcgViewModel : INotifyPropertyChanged
{
    private const double SecondsVisible = 6.0;
    private const int MaxViewSamples = 8000;

    private readonly DispatcherTimer _timer;
    private readonly List<double> _view = new();

    private IReadOnlyList<MitBihSignal> _signals = Array.Empty<MitBihSignal>();
    private double[]? _signal;
    private List<int> _peaks = new();
    private List<MitBihAnnotation> _annotations = new();
    private ObservableCollection<TagLegendItem> _legend = new();
    private string[] _leadNames = Array.Empty<string>();
    private string _recordName = "";
    private int _selectedLead;
    private int _sampleRate = 360;
    private int _pos;
    private int _viewStart;
    private long _lastTick;
    private bool _playing;
    private bool _loaded;
    private bool _showAnnotations = true;
    private int _hr;
    private string _status = "Clique em 'CARREGAR ECG' e selecione um arquivo .hea (MIT-BIH).";
    private string _annotationText = "";
    private RhythmReport _report = RhythmAnalyzer.Analyze(Array.Empty<MitBihAnnotation>(), 360);
    private bool _isDiagnosisOpen;

    public event EventHandler? Updated;

    public EcgViewModel()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        BuildLegend();
    }

    public IReadOnlyList<double> View => _view;
    public double PixelsPerUnit { get; private set; } = 90;
    public int SampleRate => _sampleRate;
    public int ViewStart => _viewStart;
    public IReadOnlyList<MitBihAnnotation>? Annotations => _loaded ? _annotations : null;
    public IReadOnlyList<TagLegendItem> Legend => _legend;
    public bool ShowAnnotations => _showAnnotations;
    public int TotalSamples => _signal?.Length ?? 0;
    public int VisibleSamples => _sampleRate > 0 ? (int)(SecondsVisible * _sampleRate) : 0;

    public string RecordText => _loaded
        ? $"Registro {_recordName} · {_sampleRate} Hz · {_signals.Count} sinais"
        : "Nenhum arquivo carregado";

    public string HrText => _loaded ? $"FC {_hr} bpm" : "FC --";
    public string PlayButtonText => _playing ? "PAUSAR" : "REPRODUZIR";
    public string TagsButtonText => _showAnnotations ? "TAGS: LIG." : "TAGS: DESL.";
    public string AnnotationText => _annotationText;
    public string StatusText => _status;
    public IReadOnlyList<string> LeadNames => _leadNames;

    public string DiagnosisSummary => _loaded ? _report.Summary : "";
    public IReadOnlyList<RhythmFinding> DiagnosisFindings => _loaded ? _report.Findings : Array.Empty<RhythmFinding>();
    public bool DiagnosisHasData => _loaded && _report.HasData;
    public bool IsDiagnosisOpen => _isDiagnosisOpen;

    public void ToggleDiagnosis()
    {
        _isDiagnosisOpen = !_isDiagnosisOpen;
        OnPropertyChanged(nameof(IsDiagnosisOpen));
    }

    public int SelectedLead
    {
        get => _selectedLead;
        set
        {
            if (_selectedLead == value) return;
            _selectedLead = value;
            OnPropertyChanged(nameof(SelectedLead));
            if (_loaded)
            {
                LoadSignal();
                _playing = true;
                _status = "Reproduzindo...";
                NotifyState();
            }
        }
    }

    public void LoadFile(string heaPath)
    {
        try
        {
            var rec = MitBihReader.Load(heaPath);
            if (rec.Signals.Count == 0)
            {
                _status = "Nenhum sinal encontrado no cabeçalho.";
                OnPropertyChanged(nameof(StatusText));
                return;
            }

            _recordName = rec.Name;
            _sampleRate = rec.SampleRate;
            _signals = rec.Signals;
            _leadNames = rec.Signals
                .Select((s, i) => string.IsNullOrEmpty(s.Description) ? $"Sinal {i + 1}" : s.Description)
                .ToArray();
            _selectedLead = 0;
            _loaded = true;

            _annotations = LoadAnnotations(heaPath, rec.Name);
            _report = RhythmAnalyzer.Analyze(_annotations, _sampleRate);
            _annotationText = BuildAnnotationText();
            BuildLegend();

            OnPropertyChanged(nameof(LeadNames));
            OnPropertyChanged(nameof(SelectedLead));
            OnPropertyChanged(nameof(RecordText));
            OnPropertyChanged(nameof(HrText));
            OnPropertyChanged(nameof(AnnotationText));
            OnPropertyChanged(nameof(Legend));
            OnPropertyChanged(nameof(DiagnosisSummary));
            OnPropertyChanged(nameof(DiagnosisFindings));
            OnPropertyChanged(nameof(DiagnosisHasData));

            LoadSignal();
            _playing = true;
            _status = "Carregado. Reproduzindo...";
            NotifyState();
        }
        catch (Exception ex)
        {
            _status = "Erro ao carregar: " + ex.Message;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public void TogglePlay()
    {
        if (_signal is null) return;
        if (!_playing && _pos >= _signal.Length)
            Restart();
        _playing = !_playing;
        if (_playing) _lastTick = Environment.TickCount64;
        _status = _playing ? "Reproduzindo..." : "Pausado";
        NotifyState();
    }

    public void Restart()
    {
        if (_signal is null) return;
        FillView(0);
        _playing = true;
        _lastTick = Environment.TickCount64;
        _status = "Reproduzindo...";
        UpdateHr();
        NotifyState();
    }

    public void ToggleTags()
    {
        _showAnnotations = !_showAnnotations;
        OnPropertyChanged(nameof(TagsButtonText));
        Updated?.Invoke(this, EventArgs.Empty);
    }

    public bool IsLegendOpen { get; private set; }

    public bool ShowLegendHint => _legend.Count == 0;

    public void ToggleLegend()
    {
        IsLegendOpen = !IsLegendOpen;
        OnPropertyChanged(nameof(IsLegendOpen));
    }

    public void Seek(int sample)
    {
        if (_signal is null) return;
        int window = (int)(SecondsVisible * _sampleRate);
        int from = Math.Max(0, sample - window);
        int to = Math.Min(_signal.Length, sample);
        _view.Clear();
        for (int i = from; i < to; i++)
            _view.Add(_signal[i]);
        _viewStart = from;
        _pos = to;
        _playing = false;
        _lastTick = Environment.TickCount64;
        _status = "Pausado — arraste na linha inferior para navegar.";
        UpdateHr();
        NotifyState();
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private void Tick()
    {
        long now = Environment.TickCount64;
        double dt = _lastTick == 0 ? 0.033 : Math.Min(0.2, (now - _lastTick) / 1000.0);
        _lastTick = now;
        if (!_playing || _signal is null) return;

        int advance = Math.Max(1, (int)(dt * _sampleRate));
        int limit = _signal.Length;
        for (int k = 0; k < advance && _pos < limit; k++)
            _view.Add(_signal[_pos++]);

        if (_view.Count > MaxViewSamples)
        {
            int removed = _view.Count - (MaxViewSamples / 2);
            _view.RemoveRange(0, removed);
            _viewStart += removed;
        }

        if (_pos >= limit)
        {
            _playing = false;
            _status = "Fim do arquivo — clique em REPRODUZIR para reiniciar.";
            OnPropertyChanged(nameof(StatusText));
        }

        UpdateHr();
        OnPropertyChanged(nameof(HrText));
        OnPropertyChanged(nameof(PlayButtonText));
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private void LoadSignal()
    {
        _signal = _signals[_selectedLead].Samples;
        _peaks = QrsDetector.DetectPeaks(_signal, _sampleRate);

        double amp = RobustAmplitude(_signal);
        PixelsPerUnit = 120 / Math.Max(0.3, amp);

        FillView(0);
        _lastTick = Environment.TickCount64;
        OnPropertyChanged(nameof(PixelsPerUnit));
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private static double RobustAmplitude(double[] sig)
    {
        double maxAbs = 0;
        foreach (var v in sig)
        {
            double a = Math.Abs(v);
            if (a > maxAbs) maxAbs = a;
        }
        if (maxAbs <= 0) return 0.1;

        const int bins = 500;
        var hist = new int[bins];
        for (int i = 0; i < sig.Length; i++)
        {
            double a = Math.Abs(sig[i]);
            int idx = (int)(a / maxAbs * (bins - 1));
            if (idx >= bins) idx = bins - 1;
            hist[idx]++;
        }

        long target = (long)(sig.Length * 0.99);
        long acc = 0;
        for (int i = 0; i < bins; i++)
        {
            acc += hist[i];
            if (acc >= target)
                return Math.Max(0.05, maxAbs * (i + 1) / bins);
        }
        return maxAbs;
    }

    private void FillView(int startPos)
    {
        _view.Clear();
        _pos = startPos;
        _viewStart = startPos;
        if (_signal is null) return;
        int prefill = Math.Min(_signal.Length, (int)(SecondsVisible * _sampleRate));
        for (int i = 0; i < prefill; i++)
            _view.Add(_signal[i]);
        _pos = prefill;
    }

    private void UpdateHr()
    {
        if (_signal is null)
        {
            _hr = 0;
            return;
        }
        int win = (int)(SecondsVisible * _sampleRate);
        int start = Math.Max(0, _pos - win);
        int count = 0;

        if (_annotations.Count > 0)
        {
            foreach (var a in _annotations)
            {
                if (a.Sample < start) continue;
                if (a.Sample > _pos) break;
                if (a.IsBeat) count++;
            }
        }
        else
        {
            foreach (var p in _peaks)
            {
                if (p >= start && p <= _pos) count++;
                else if (p > _pos) break;
            }
        }

        double secs = (_pos - start) / (double)_sampleRate;
        _hr = secs > 0.5 ? (int)Math.Round(60.0 * count / secs) : 0;
    }

    private static List<MitBihAnnotation> LoadAnnotations(string heaPath, string recordName)
    {
        string dir = Path.GetDirectoryName(heaPath) ?? "";
        string baseName = Path.GetFileNameWithoutExtension(heaPath);
        string atrPath = Path.Combine(dir, baseName + ".atr");
        if (!File.Exists(atrPath))
            atrPath = Path.Combine(dir, recordName + ".atr");
        if (!File.Exists(atrPath))
            return new List<MitBihAnnotation>();
        try
        {
            return MitBihAnnotations.Load(atrPath);
        }
        catch
        {
            return new List<MitBihAnnotation>();
        }
    }

    private string BuildAnnotationText()
    {
        if (!_loaded || _annotations.Count == 0)
            return "";

        var counts = new Dictionary<int, int>();
        int noise = 0;
        foreach (var a in _annotations)
        {
            if (a.IsBeat)
            {
                counts.TryGetValue(a.Code, out int c);
                counts[a.Code] = c + 1;
            }
            else if (a.IsQuality)
            {
                noise++;
            }
        }

        if (counts.Count == 0 && noise == 0)
            return "";

        var parts = counts.OrderBy(kv => kv.Key)
            .Select(kv => $"{MitBihAnnotations.SymbolFor(kv.Key)} {kv.Value}")
            .ToList();
        if (noise > 0) parts.Add($"n {noise}");
        return "Tags: " + string.Join(" · ", parts);
    }

    private void BuildLegend()
    {
        _legend.Clear();
        var counts = new Dictionary<int, int>();
        foreach (var a in _annotations)
        {
            counts.TryGetValue(a.Code, out int c);
            counts[a.Code] = c + 1;
        }

        foreach (var kv in counts.OrderBy(k => k.Key))
        {
            _legend.Add(new TagLegendItem
            {
                Symbol = MitBihAnnotations.SymbolFor(kv.Key),
                Meaning = MitBihAnnotations.MeaningFor(kv.Key),
                Count = kv.Value,
                Color = AnnotationPalette.ForCode(kv.Key),
            });
        }

        OnPropertyChanged(nameof(Legend));
        OnPropertyChanged(nameof(ShowLegendHint));
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HrText));
        OnPropertyChanged(nameof(PlayButtonText));
        OnPropertyChanged(nameof(StatusText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}