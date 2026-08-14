using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CardioView.Services;

/// <summary>
/// Gera um texto interpretativo (didático) sobre o que os achados de um
/// registro MIT-BIH podem significar clinicamente. Baseado em regras — não
/// substitui avaliação médica.
/// </summary>
public static class InterpretationService
{
    public static IReadOnlyList<string> Build(RhythmReport report, IReadOnlyList<MitBihAnnotation> annotations, int sampleRate)
    {
        if (!report.HasData)
            return new[]
            {
                "Não foi possível interpretar o traçado: o arquivo .atr não contém batimentos anotados para análise de ritmo.",
            };

        var beats = annotations.Where(a => a.IsBeat).OrderBy(a => a.Sample).ToList();
        int total = beats.Count;

        int pvc = beats.Count(b => b.Code == 5);
        int apc = beats.Count(b => b.Code == 8);
        int svpb = beats.Count(b => b.Code == 9);
        int aberr = beats.Count(b => b.Code == 4);
        int rOnT = beats.Count(b => b.Code == 41);
        int lbbb = beats.Count(b => b.Code == 2);
        int rbbb = beats.Count(b => b.Code == 3);
        int escapes = beats.Count(b => b.Code is 10 or 11 or 34 or 35);
        int paced = beats.Count(b => b.Code is 12 or 38);
        int paceSpike = beats.Count(b => b.Code == 26);
        int fusion = beats.Count(b => b.Code == 6);
        int blockedApc = beats.Count(b => b.Code == 37);

        int maxVRun = 0, run = 0;
        foreach (var b in beats)
        {
            if (b.Code == 5) { run++; if (run > maxVRun) maxVRun = run; }
            else run = 0;
        }

        int noise = annotations.Count(a => a.Code == 14);
        int artifacts = annotations.Count(a => a.Code == 16);
        int badMarks = noise + artifacts;

        bool hasVt = annotations.Any(a => a.Code == 28 && a.Aux.Trim().Equals("(VT", StringComparison.OrdinalIgnoreCase));
        bool hasVfl = annotations.Any(a => a.Code == 28 && a.Aux.Trim().Equals("(VFL", StringComparison.OrdinalIgnoreCase));
        bool hasAfib = annotations.Any(a => a.Code == 28 && a.Aux.Trim().Equals("(AFIB", StringComparison.OrdinalIgnoreCase));
        bool hasAfl = annotations.Any(a => a.Code == 28 && a.Aux.Trim().Equals("(AFL", StringComparison.OrdinalIgnoreCase));
        bool hasBradyRhythm = annotations.Any(a => a.Code == 28 && a.Aux.Trim().Equals("(SBR", StringComparison.OrdinalIgnoreCase));

        bool bradycardia = report.HeartRateAvg > 0 && report.HeartRateAvg < 60;
        bool tachycardia = report.HeartRateAvg > 100;

        var criticals = new List<string>();
        if (hasVt) criticals.Add("episódios de taquicardia ventricular");
        if (hasVfl) criticals.Add("flutter ventricular");
        if (maxVRun >= 3)
            criticals.Add($"salva de {maxVRun} batimentos ventriculares consecutivos (taquicardia ventricular não sustentada)");
        if (rOnT > 0)
            criticals.Add("extrassístoles ventriculares R-sobre-T");

        var attentions = new List<string>();
        if (pvc > 0)
            attentions.Add($"{pvc} extrassístoles ventriculares (PVC, {100.0 * pvc / total:0.0}% dos batimentos)");
        if (apc > 0) attentions.Add($"{apc} contrações atriais prematuras (APC)");
        if (blockedApc > 0) attentions.Add($"{blockedApc} onda(s) P bloqueada(s)");
        if (svpb > 0) attentions.Add($"{svpb} extrassístoles supraventriculares");
        if (aberr > 0) attentions.Add($"{aberr} extrassístoles atriais aberrantes");
        if (escapes > 0) attentions.Add($"{escapes} batimento(s) de escape");
        if (lbbb > 0) attentions.Add("bloqueio de ramo esquerdo (LBBB)");
        if (rbbb > 0) attentions.Add("bloqueio de ramo direito (RBBB)");
        if (paced > 0 || paceSpike > 0) attentions.Add("ritmo de marcapasso");
        if (fusion > 0) attentions.Add($"{fusion} batimento(s) de fusão");
        if (bradycardia) attentions.Add($"bradicardia (FC média {report.HeartRateAvg} bpm)");
        if (tachycardia) attentions.Add($"taquicardia (FC média {report.HeartRateAvg} bpm)");

        var paragraphs = new List<string>();

        // 1) Quadro geral.
        paragraphs.Add(
            $"O traçado registra {total} batimentos, com FC média de {report.HeartRateAvg} bpm " +
            $"(mínima {report.HeartRateMin}, máxima {report.HeartRateMax}).");

        // 2) Principais achados.
        var highlights = new List<string>();
        highlights.AddRange(criticals);
        highlights.AddRange(attentions);
        if (hasAfib) highlights.Add("fibrilação atrial (ritmo irregular, sem ondas P organizadas)");
        if (hasAfl) highlights.Add("flutter atrial");
        if (highlights.Count > 0)
        {
            var text = new StringBuilder();
            text.Append("Destaca(m)-se ");
            for (int i = 0; i < highlights.Count; i++)
            {
                text.Append(highlights[i]);
                if (i < highlights.Count - 2) text.Append(", ");
                else if (i == highlights.Count - 2) text.Append(" e ");
            }
            text.Append('.');
            paragraphs.Add(text.ToString());
        }
        else
        {
            paragraphs.Add("Não foram identificadas anormalidades relevantes nos batimentos anotados.");
        }

        // 3) Contexto clínico possível.
        var context = new List<string>();
        if (criticals.Count > 0 || pvc > 0)
        {
            context.Add(
                "O padrão de irritabilidade ventricular pode estar associado a cardiopatia isquêmica, " +
                "miocardiopatia ou distúrbios eletrolíticos (ex.: hipocalemia), e favorece o aparecimento " +
                "de taquiarritmias ventriculares.");
        }
        if (hasAfib || hasAfl)
        {
            context.Add(
                "A fibrilação/flutter atrial demandam avaliação do risco tromboembólico e do controle " +
                "da resposta ventricular.");
        }
        if (apc > 0 || blockedApc > 0 || svpb > 0)
        {
            context.Add(
                "As extrassístoles atriais costumam ser benignas, mas, quando numerosas, podem estar " +
                "ligadas a doença estrutural ou preceder fibrilação atrial.");
        }
        if (lbbb > 0 || rbbb > 0)
        {
            context.Add(
                "Bloqueios de ramo podem acompanhar doença cardíaca estrutural (isquêmica, hipertensiva " +
                "ou miocardiopatia).");
        }
        if (escapes > 0)
        {
            context.Add(
                "Batimentos de escape sugerem falha do marca-passo natural do coração, possivelmente por " +
                "bloqueio atrioventricular ou bradicardia significativa.");
        }
        if (bradycardia || hasBradyRhythm)
        {
            context.Add(
                "A bradicardia pode decorrer de disfunção do nó sinusal, bloqueio atrioventricular ou " +
                "efeito de medicações.");
        }
        if (paced > 0 || paceSpike > 0)
        {
            context.Add(
                "O ritmo de marcapasso indica dependência de estimulação artificial; convém verificar a " +
                "função e o limiar do dispositivo.");
        }
        if (context.Count > 0)
        {
            paragraphs.Add("Interpretação: " + string.Join(" ", context));
        }

        // 4) Gravidade / recomendação.
        if (criticals.Count > 0)
        {
            paragraphs.Add(
                "Trata-se de um quadro de maior gravidade — recomenda-se avaliação médica imediata e " +
                "correlação clínica com sintomas, exames complementares e medicações em uso.");
        }
        else if (attentions.Count > 0)
        {
            paragraphs.Add(
                "Quadro com alterações que merecem acompanhamento — recomenda-se avaliação médica para " +
                "correlação clínica e repetição do exame, se necessário.");
        }
        else
        {
            paragraphs.Add(
                "Traçado sem anormalidades relevantes — manter seguimento habitual.");
        }

        if (badMarks > 0)
        {
            paragraphs.Add(
                $"O sinal apresenta {noise} mudança(s) de qualidade e {artifacts} artefato(s), o que reduz " +
                "a confiabilidade desta interpretação.");
        }

        return paragraphs;
    }
}
