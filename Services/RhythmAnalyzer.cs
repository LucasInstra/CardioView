using System;
using System.Collections.Generic;
using System.Linq;

namespace CardioView.Services;

public enum FindingSeverity
{
    Info,
    Attention,
    Critical,
}

public sealed class RhythmFinding
{
    public required string Condition { get; init; }
    public required string Evidence { get; init; }
    public required FindingSeverity Severity { get; init; }
    public int Count { get; init; } = 1;
    public bool HasMultiple => Count > 1;
    public string CountBadge => Count > 1 ? $"×{Count}" : "";
    public string SeverityText => Severity switch
    {
        FindingSeverity.Critical => "CRÍTICO",
        FindingSeverity.Attention => "ATENÇÃO",
        _ => "INFO",
    };
}

public sealed class RhythmReport
{
    public required string Summary { get; init; }
    public required IReadOnlyList<RhythmFinding> Findings { get; init; }
    public int TotalBeats { get; init; }
    public int HeartRateAvg { get; init; }
    public int HeartRateMin { get; init; }
    public int HeartRateMax { get; init; }
    public bool HasData { get; init; }
}

/// <summary>
/// Interpreta as anotações de um arquivo .atr (MIT-BIH) e sugere possíveis
/// condições de ritmo. Análise puramente didática, baseada em regras — não
/// substitui avaliação médica.
/// </summary>
public static class RhythmAnalyzer
{
    // Códigos de ritmo (annotation type 28) → nome da condição.
    private static readonly Dictionary<string, string> RhythmNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["(N"] = "Ritmo sinusal normal",
        ["(AFIB"] = "Fibrilação atrial",
        ["(AFL"] = "Flutter atrial",
        ["(VT"] = "Taquicardia ventricular",
        ["(VFL"] = "Flutter ventricular",
        ["(SVTA"] = "Taquiarritmia supraventricular",
        ["(B"] = "Bigeminismo ventricular",
        ["(T"] = "Trigeminismo ventricular",
        ["(IVR"] = "Ritmo idioventricular",
        ["(SBR"] = "Bradicardia sinusal",
        ["(NOD"] = "Ritmo nodal (juncional)",
        ["(P"] = "Ritmo de marcapasso",
        ["(AB"] = "Bloqueio atrioventricular",
        ["(BII"] = "Bloqueio AV de 2º grau",
        ["(BIII"] = "Bloqueio AV de 3º grau",
        ["(PREX"] = "Pré-excitação ventricular (WPW)",
    };

    private static readonly Dictionary<string, FindingSeverity> RhythmSeverity = new(StringComparer.OrdinalIgnoreCase)
    {
        ["(N"] = FindingSeverity.Info,
        ["(SBR"] = FindingSeverity.Attention,
        ["(NOD"] = FindingSeverity.Attention,
        ["(P"] = FindingSeverity.Attention,
        ["(AB"] = FindingSeverity.Attention,
        ["(BII"] = FindingSeverity.Attention,
        ["(BIII"] = FindingSeverity.Attention,
        ["(PREX"] = FindingSeverity.Attention,
        ["(AFIB"] = FindingSeverity.Attention,
        ["(AFL"] = FindingSeverity.Attention,
        ["(SVTA"] = FindingSeverity.Attention,
        ["(B"] = FindingSeverity.Attention,
        ["(T"] = FindingSeverity.Attention,
        ["(IVR"] = FindingSeverity.Attention,
        ["(VT"] = FindingSeverity.Critical,
        ["(VFL"] = FindingSeverity.Critical,
    };

    public static RhythmReport Analyze(IReadOnlyList<MitBihAnnotation> annotations, int sampleRate)
    {
        if (annotations == null || annotations.Count == 0 || sampleRate <= 0)
            return new RhythmReport
            {
                Summary = "",
                Findings = Array.Empty<RhythmFinding>(),
                HasData = false,
            };

        var beats = annotations.Where(a => a.IsBeat).OrderBy(a => a.Sample).ToList();
        int total = beats.Count;

        // Intervalos RR (ms) → FC mín/máx/média.
        var rrs = new List<double>();
        for (int i = 1; i < beats.Count; i++)
        {
            int diff = beats[i].Sample - beats[i - 1].Sample;
            if (diff <= 0) continue;
            double ms = diff * 1000.0 / sampleRate;
            if (ms >= 200 && ms <= 3000) // 20–300 bpm fisiológicos
                rrs.Add(ms);
        }

        int hrMin = 0, hrMax = 0, hrAvg = 0;
        if (rrs.Count > 0)
        {
            hrMin = (int)Math.Round(60000.0 / rrs.Max());
            hrMax = (int)Math.Round(60000.0 / rrs.Min());
            hrAvg = (int)Math.Round(60000.0 / (rrs.Sum() / rrs.Count));
        }

        // Contagem por tipo de batimento.
        var count = new Dictionary<int, int>();
        foreach (var b in beats)
            count[b.Code] = count.GetValueOrDefault(b.Code) + 1;

        int pvc = count.GetValueOrDefault(5);
        int apc = count.GetValueOrDefault(8);
        int svpb = count.GetValueOrDefault(9);
        int aberr = count.GetValueOrDefault(4);
        int lbbb = count.GetValueOrDefault(2);
        int rbbb = count.GetValueOrDefault(3);
        int juncPrem = count.GetValueOrDefault(7);
        int ventEscape = count.GetValueOrDefault(10);
        int juncEscape = count.GetValueOrDefault(11);
        int atrialEscape = count.GetValueOrDefault(34);
        int svEscape = count.GetValueOrDefault(35);
        int paced = count.GetValueOrDefault(12) + count.GetValueOrDefault(38);
        int paceSpike = count.GetValueOrDefault(26);
        int fusion = count.GetValueOrDefault(6);
        int rOnT = count.GetValueOrDefault(41);
        int blockedApc = count.GetValueOrDefault(37);
        int unclass = count.GetValueOrDefault(13) + count.GetValueOrDefault(15);

        int noise = annotations.Count(a => a.IsQuality);
        int artifacts = annotations.Count(a => a.Code == 16);

        // Salvas de batimentos iguais consecutivos (dupletos/tripletos V…).
        bool vCouplet = false, vTriplet = false, vRun = false;
        int vRunLen = 0, maxVRun = 0, lastCode = -1;
        foreach (var b in beats)
        {
            if (b.Code == lastCode && b.Code == 5)
            {
                vRunLen++;
                if (vRunLen == 2) vCouplet = true;
                if (vRunLen == 3) vTriplet = true;
                if (vRunLen >= 3) vRun = true;
            }
            else if (b.Code == 5)
            {
                vRunLen = 1;
            }
            else
            {
                vRunLen = 0;
            }
            lastCode = b.Code;
            if (vRunLen > maxVRun) maxVRun = vRunLen;
        }

        // Alternância regular V-N-V-N… (bigeminismo) — apenas se não houver
        // anotação de ritmo específica.
        bool hasBigeminyRhythm = annotations.Any(a => a.Code == 28 && a.Aux.Trim().Equals("(B", StringComparison.OrdinalIgnoreCase));
        bool bigeminy = false;
        if (!hasBigeminyRhythm)
        {
            int bestLen = 0;
            for (int i = 0; i < beats.Count - 1; i++)
            {
                int j = i;
                while (j + 1 < beats.Count && beats[j].Code != beats[j + 1].Code) j++;
                int len = j - i + 1;
                if (len > bestLen) bestLen = len;
            }
            if (bestLen >= 6)
            {
                bool hasV = false, hasN = false;
                for (int i = 0; i < beats.Count - bestLen + 1; i++)
                {
                    bool alt = true;
                    for (int k = i; k < i + bestLen - 1; k++)
                        if (beats[k].Code == beats[k + 1].Code) { alt = false; break; }
                    if (!alt) continue;
                    for (int k = i; k < i + bestLen; k++)
                    {
                        if (beats[k].Code == 5) hasV = true;
                        if (beats[k].Code == 1) hasN = true;
                    }
                    if (hasV && hasN) break;
                }
                bigeminy = hasV && hasN;
            }
        }

        // Segmentos de ritmo anotados (tipo 28 + texto auxiliar "(..."). 
        var rhythmStarts = new List<(int Sample, string Aux)>();
        foreach (var a in annotations)
        {
            if (a.Code == 28 && !string.IsNullOrWhiteSpace(a.Aux))
                rhythmStarts.Add((a.Sample, a.Aux.Trim()));
        }

        int lastSample = annotations[^1].Sample;
        var rhythmSegments = new List<(string Name, string Aux, int Start, int End, FindingSeverity Severity)>();
        string? primaryRhythm = null;
        long primaryDuration = -1;
        foreach (var seg in rhythmStarts)
        {
            if (!RhythmNames.TryGetValue(seg.Aux, out var name)) continue;
            int end = rhythmStarts
                .Where(s => s.Sample > seg.Sample)
                .Select(s => s.Sample)
                .DefaultIfEmpty(lastSample)
                .Min();
            int dur = Math.Max(0, end - seg.Sample);
            rhythmSegments.Add((name, seg.Aux, seg.Sample, end, RhythmSeverity[seg.Aux]));
            if (dur > primaryDuration)
            {
                primaryDuration = dur;
                primaryRhythm = name;
            }
        }

        var findings = new List<RhythmFinding>();

        // 1) Segmentos de ritmo anotados (fonte mais confiável), agrupados por
        //    condição: registros longos repetem o mesmo marcador várias vezes,
        //    então um único item mostra o nº de episódios e a duração total.
        foreach (var g in rhythmSegments.GroupBy(s => s.Name))
        {
            var segs = g.ToList();
            findings.Add(new RhythmFinding
            {
                Condition = g.Key,
                Evidence = BuildRhythmGroupEvidence(segs, sampleRate),
                Severity = (FindingSeverity)segs.Max(s => (int)s.Severity),
                Count = segs.Count,
            });
        }

        // 2) Achados por batimento.
        if (rOnT > 0)
            findings.Add(new RhythmFinding
            {
                Condition = "PVC R-sobre-T (R-on-T)",
                Evidence = $"{rOnT} batimento(s) sobre a onda T — risco de taquicardia ventricular",
                Severity = FindingSeverity.Critical,
            });

        if (vRun)
            findings.Add(new RhythmFinding
            {
                Condition = "Taquicardia ventricular (salva)",
                Evidence = $"salva de {maxVRun} batimentos V consecutivos",
                Severity = FindingSeverity.Critical,
            });
        else if (vTriplet)
            findings.Add(new RhythmFinding
            {
                Condition = "Tripletos ventriculares",
                Evidence = "3 batimentos V consecutivos",
                Severity = FindingSeverity.Critical,
            });
        else if (vCouplet)
            findings.Add(new RhythmFinding
            {
                Condition = "Dupletos ventriculares",
                Evidence = "2 batimentos V consecutivos",
                Severity = FindingSeverity.Attention,
            });

        if (pvc > 0)
        {
            double pct = total > 0 ? 100.0 * pvc / total : 0;
            findings.Add(new RhythmFinding
            {
                Condition = "Extrassístoles ventriculares (PVC)",
                Evidence = $"{pvc} de {total} batimentos ({pct:0.0}%)",
                Severity = pct >= 10 ? FindingSeverity.Critical : FindingSeverity.Attention,
            });
        }

        if (bigeminy)
            findings.Add(new RhythmFinding
            {
                Condition = "Bigeminismo ventricular",
                Evidence = "alternância regular V-N (≥ 6 batimentos)",
                Severity = FindingSeverity.Attention,
            });

        if (apc > 0)
            findings.Add(new RhythmFinding
            {
                Condition = "Contrações atriais prematuras (APC)",
                Evidence = $"{apc} batimento(s)",
                Severity = FindingSeverity.Info,
            });

        if (blockedApc > 0)
            findings.Add(new RhythmFinding
            {
                Condition = "Ondas P não conduzidas (APB bloqueado)",
                Evidence = $"{blockedApc} ocorrência(s)",
                Severity = FindingSeverity.Info,
            });

        if (svpb > 0)
            findings.Add(new RhythmFinding
            {
                Condition = "Extrassístoles supraventriculares",
                Evidence = $"{svpb} batimento(s)",
                Severity = FindingSeverity.Info,
            });

        if (aberr > 0)
            findings.Add(new RhythmFinding
            {
                Condition = "Extrassístoles atriais aberrantes",
                Evidence = $"{aberr} batimento(s)",
                Severity = FindingSeverity.Info,
            });

        if (juncPrem > 0)
            findings.Add(new RhythmFinding
            {
                Condition = "Extrassístoles juncionais",
                Evidence = $"{juncPrem} batimento(s)",
                Severity = FindingSeverity.Info,
            });

        if (lbbb > 0)
            findings.Add(new RhythmFinding
            {
                Condition = "Bloqueio de ramo esquerdo (LBBB)",
                Evidence = $"{lbbb} batimento(s) com padrão LBBB",
                Severity = FindingSeverity.Attention,
            });

        if (rbbb > 0)
            findings.Add(new RhythmFinding
            {
                Condition = "Bloqueio de ramo direito (RBBB)",
                Evidence = $"{rbbb} batimento(s) com padrão RBBB",
                Severity = FindingSeverity.Attention,
            });

        int escapes = ventEscape + juncEscape + atrialEscape + svEscape;
        if (escapes > 0)
            findings.Add(new RhythmFinding
            {
                Condition = "Batimentos de escape",
                Evidence = $"{escapes} batimento(s) — sugere bloqueio AV ou bradicardia",
                Severity = FindingSeverity.Attention,
            });

        if (paced > 0 || paceSpike > 0)
            findings.Add(new RhythmFinding
            {
                Condition = "Ritmo de marcapasso",
                Evidence = $"{paced + paceSpike} batimento(s) estimulado(s)",
                Severity = FindingSeverity.Attention,
            });

        if (fusion > 0)
            findings.Add(new RhythmFinding
            {
                Condition = "Batimentos de fusão",
                Evidence = $"{fusion} batimento(s) (fusão normal + ventricular)",
                Severity = FindingSeverity.Info,
            });

        if (unclass > 0)
            findings.Add(new RhythmFinding
            {
                Condition = "Batimentos não classificáveis",
                Evidence = $"{unclass} batimento(s)",
                Severity = FindingSeverity.Info,
            });

        // 3) Frequência cardíaca.
        if (total > 0 && hrAvg > 0 && primaryRhythm == null)
        {
            if (hrAvg < 60)
                findings.Add(new RhythmFinding
                {
                    Condition = "Bradicardia",
                    Evidence = $"FC média {hrAvg} bpm (mín {hrMin}, máx {hrMax})",
                    Severity = FindingSeverity.Attention,
                });
            else if (hrAvg > 100)
                findings.Add(new RhythmFinding
                {
                    Condition = "Taquicardia",
                    Evidence = $"FC média {hrAvg} bpm (mín {hrMin}, máx {hrMax})",
                    Severity = FindingSeverity.Attention,
                });
        }

        // 4) Ruído / artefato.
        int badMarks = noise + artifacts;
        if (badMarks > 0)
            findings.Add(new RhythmFinding
            {
                Condition = "Ruído / artefato no sinal",
                Evidence = $"{noise} mudança(s) de qualidade, {artifacts} artefato(s) QRS — reduz a confiabilidade da leitura",
                Severity = badMarks >= 50 ? FindingSeverity.Attention : FindingSeverity.Info,
            });

        if (total > 0 && findings.Count == 0)
            findings.Add(new RhythmFinding
            {
                Condition = "Nenhuma anormalidade significativa",
                Evidence = $"{total} batimentos sem desvios relevantes",
                Severity = FindingSeverity.Info,
            });

        // Mesma condição detectada por fontes diferentes (ex.: ritmo de
        // marcapasso pelos batimentos e pelo marcador de ritmo) → funde em um item.
        var merged = new List<RhythmFinding>();
        foreach (var f in findings)
        {
            int idx = merged.FindIndex(m => m.Condition == f.Condition);
            if (idx < 0)
            {
                merged.Add(f);
                continue;
            }
            var ex = merged[idx];
            merged[idx] = new RhythmFinding
            {
                Condition = ex.Condition,
                Evidence = ex.Evidence + "; " + f.Evidence,
                Severity = (FindingSeverity)Math.Max((int)ex.Severity, (int)f.Severity),
                Count = ex.Count + f.Count,
            };
        }

        var ordered = merged
            .OrderByDescending(f => (int)f.Severity)
            .ToList();

        return new RhythmReport
        {
            Summary = BuildSummary(ordered, total, pvc, primaryRhythm, vRun, apc, lbbb, rbbb, paced, escapes, hrAvg, badMarks),
            Findings = ordered,
            TotalBeats = total,
            HeartRateAvg = hrAvg,
            HeartRateMin = hrMin,
            HeartRateMax = hrMax,
            HasData = total > 0,
        };
    }

    private static string BuildSummary(
        IReadOnlyList<RhythmFinding> findings,
        int total,
        int pvc,
        string? primaryRhythm,
        bool vRun,
        int apc,
        int lbbb,
        int rbbb,
        int paced,
        int escapes,
        int hrAvg,
        int badMarks)
    {
        if (total == 0)
            return "Sem batimentos anotados no arquivo (.atr) — análise limitada.";

        var criticals = findings
            .Where(f => f.Severity == FindingSeverity.Critical)
            .Select(f => f.Condition)
            .Distinct()
            .ToList();

        var parts = new List<string>();

        // 1) Condições críticas vêm primeiro no resumo — o ritmo de base
        //    (ex.: sinusal normal) não deve ofuscar algo agudo.
        if (criticals.Count > 0)
        {
            parts.Add(string.Join("; ", criticals.Select(LeadName)));
        }
        else if (primaryRhythm != null)
        {
            parts.Add(primaryRhythm);
        }
        else if (paced > 0)
        {
            parts.Add("Ritmo de marcapasso");
        }
        else if (escapes > 0)
        {
            parts.Add("Ritmo com batimentos de escape");
        }
        else
        {
            parts.Add("Ritmo sem marcação específica");
        }

        // 2) Ritmo de base, quando há destaque agudo.
        if (criticals.Count > 0 && primaryRhythm != null &&
            !criticals.Contains(primaryRhythm))
        {
            parts.Add("em " + primaryRhythm.ToLowerInvariant());
        }

        // 3) Detalhes (evita repetir o que já foi dito no destaque).
        bool pvcMentioned = criticals.Contains("Extrassístoles ventriculares (PVC)");
        if (pvc > 0 && !pvcMentioned)
        {
            double pct = 100.0 * pvc / total;
            string freq = pct < 2 ? "ocasionais" : pct < 10 ? "frequentes" : "muito frequentes";
            parts.Add($"extrassístoles ventriculares {freq} ({pvc})");
        }

        bool vtMentioned = criticals.Any(c => c.StartsWith("Taquicardia ventricular", StringComparison.Ordinal));
        if (vRun && !vtMentioned) parts.Add("episódios de taquicardia ventricular");
        if (apc > 0) parts.Add($"contrações atriais prematuras ({apc})");
        if (lbbb > 0) parts.Add("bloqueio de ramo esquerdo");
        if (rbbb > 0) parts.Add("bloqueio de ramo direito");
        if (hrAvg > 0 && hrAvg < 60) parts.Add($"bradicardia (FC média {hrAvg} bpm)");
        if (hrAvg > 100) parts.Add($"taquicardia (FC média {hrAvg} bpm)");
        if (badMarks >= 50) parts.Add("sinal com ruído considerável");

        string body = string.Join("; ", parts);
        return "Possível condição: " + char.ToUpperInvariant(body[0]) + body.Substring(1);
    }

    private static string LeadName(string condition) => condition switch
    {
        "Taquicardia ventricular" => "episódios de taquicardia ventricular",
        "Taquicardia ventricular (salva)" => "salvas de taquicardia ventricular",
        "Tripletos ventriculares" => "tripletos ventriculares",
        "Dupletos ventriculares" => "dupletos ventriculares",
        "Flutter ventricular" => "flutter ventricular",
        "PVC R-sobre-T (R-on-T)" => "PVC R-sobre-T (R-on-T)",
        _ => condition,
    };

    private static string FormatDuration(int samples, int sampleRate)
    {
        double secs = samples / (double)sampleRate;
        if (secs < 1) return $"{secs:0.0} s";
        int total = (int)Math.Round(secs);
        if (total >= 60)
        {
            int min = total / 60, s = total % 60;
            return s > 0 ? $"{min} min {s} s" : $"{min} min";
        }
        return $"{total} s";
    }

    private static string BuildRhythmGroupEvidence(
        List<(string Name, string Aux, int Start, int End, FindingSeverity Severity)> segs,
        int sampleRate)
    {
        if (segs.Count == 1)
            return $"ritmo marcado '{segs[0].Aux}' por {FormatDuration(segs[0].End - segs[0].Start, sampleRate)}";

        int total = segs.Sum(s => s.End - s.Start);
        int first = segs.Min(s => s.Start);
        return $"{segs.Count} episódios · total {FormatDuration(total, sampleRate)} · 1º às {FormatClock(first, sampleRate)}";
    }

    private static string FormatClock(int sample, int sampleRate)
    {
        var ts = TimeSpan.FromSeconds(sample / (double)sampleRate);
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }
}
