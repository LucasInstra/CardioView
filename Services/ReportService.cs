using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CardioView.Services;

public sealed record EcgReportData
{
    public required string RecordName { get; init; }
    public required int SampleRate { get; init; }
    public required int SignalCount { get; init; }
    public required int TotalSamples { get; init; }
    public required RhythmReport Report { get; init; }
    public required IReadOnlyList<MitBihAnnotation> Annotations { get; init; }
    public required byte[] WaveformPng { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
}

/// <summary>
/// Gera um relatório PDF (A4) com o resumo da análise de ritmo de um registro
/// MIT-BIH, os achados detalhados e um trecho do traçado (snapshot).
/// </summary>
public static class ReportService
{
    private const string Accent = "#2B7A42";
    private const string Ink = "#111111";
    private const string Muted = "#777777";

    public static byte[] BuildEcgPdf(EcgReportData data)
    {
        Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(26);
                page.DefaultTextStyle(x => x.FontFamily("Segoe UI").FontSize(10).FontColor("#333333"));

                page.Header()
                    .PaddingBottom(12)
                    .Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("CardioView").FontSize(9).Bold().FontColor(Accent);
                            col.Item().Text("Relatório de Análise de ECG").FontSize(19).Bold().FontColor(Ink);
                        });
                        row.ConstantItem(170).AlignRight().Column(col =>
                        {
                            col.Item().Text(data.GeneratedAt.ToString("dd/MM/yyyy HH:mm"))
                                .FontSize(9).FontColor(Muted);
                            col.Item().Text("MIT-BIH Arrhythmia Database")
                                .FontSize(8).FontColor("#AAAAAA");
                        });
                    });

                page.Content().PaddingTop(2).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.Info("Registro", string.IsNullOrEmpty(data.RecordName) ? "—" : data.RecordName);
                        row.Info("Amostragem", $"{data.SampleRate} Hz");
                        row.Info("Sinais", data.SignalCount.ToString());
                        row.Info("Duração", data.SampleRate > 0
                            ? FormatDuration((long)(data.TotalSamples * 1000.0 / data.SampleRate))
                            : "—");
                    });

                    col.Item().PaddingTop(12).Border(1).BorderColor(Accent)
                        .Background("#F1F8F3").Padding(10)
                        .Column(box =>
                        {
                            box.Item().Text(x =>
                            {
                                x.Span("Resumo: ").Bold().FontColor(Ink);
                                string summary = string.IsNullOrWhiteSpace(data.Report.Summary)
                                    ? "Sem anotações (.atr) — análise de ritmo indisponível para este registro."
                                    : data.Report.Summary;
                                x.Span(summary);
                            });

                            if (data.Report.HasData)
                            {
                                box.Item().PaddingTop(8).Row(row =>
                                {
                                    row.Info("FC média", $"{data.Report.HeartRateAvg} bpm");
                                    row.Info("FC mínima", $"{data.Report.HeartRateMin} bpm");
                                    row.Info("FC máxima", $"{data.Report.HeartRateMax} bpm");
                                    row.Info("Total de batimentos", data.Report.TotalBeats.ToString());
                                });

                                int critical = data.Report.Findings.Count(f => f.Severity == FindingSeverity.Critical);
                                int attention = data.Report.Findings.Count(f => f.Severity == FindingSeverity.Attention);
                                int info = data.Report.Findings.Count(f => f.Severity == FindingSeverity.Info);

                                box.Item().PaddingTop(6).Text(x =>
                                {
                                    x.Span("Achados: ").Bold().FontColor(Ink).FontSize(9);
                                    x.Span(critical.ToString()).Bold().FontColor("#C62828");
                                    x.Span(" crítico(s) · ").FontColor(Muted).FontSize(9);
                                    x.Span(attention.ToString()).Bold().FontColor("#EF8C00");
                                    x.Span(" de atenção · ").FontColor(Muted).FontSize(9);
                                    x.Span(info.ToString()).Bold().FontColor("#2E7D32");
                                    x.Span(" informativos").FontColor(Muted).FontSize(9);
                                });
                            }
                        });

                    col.Item().PaddingTop(14).Text("Interpretação clínica").FontSize(13).Bold().FontColor(Ink);

                    foreach (var paragraph in InterpretationService.Build(data.Report, data.Annotations, data.SampleRate))
                    {
                        col.Item().PaddingTop(4).Text(paragraph).FontSize(10).FontColor("#333333");
                    }

                    col.Item().PaddingTop(14).Text("Achados").FontSize(13).Bold().FontColor(Ink);

                    if (data.Report.Findings.Count == 0)
                    {
                        col.Item().PaddingTop(4).Text("Nenhum achado a exibir.")
                            .FontColor(Muted).FontSize(10);
                    }
                    else
                    {
                        col.Item().PaddingTop(6).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1.35f);
                                columns.ConstantColumn(78);
                                columns.RelativeColumn(1.9f);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background("#2B2B2B").Padding(6)
                                    .Text("Condição").Bold().FontColor("#FFFFFF").FontSize(9);
                                header.Cell().Background("#2B2B2B").Padding(6)
                                    .Text("Gravidade").Bold().FontColor("#FFFFFF").FontSize(9);
                                header.Cell().Background("#2B2B2B").Padding(6)
                                    .Text("Evidência").Bold().FontColor("#FFFFFF").FontSize(9);
                            });

                            foreach (var f in data.Report.Findings)
                            {
                                string bg = f.Severity switch
                                {
                                    FindingSeverity.Critical => "#C62828",
                                    FindingSeverity.Attention => "#EF8C00",
                                    _ => "#2E7D32",
                                };

                                table.Cell().BorderBottom(0.5f).BorderColor("#E0E0E0").Padding(6)
                                    .Text(f.Condition);
                                table.Cell().BorderBottom(0.5f).BorderColor("#E0E0E0").Padding(6)
                                    .Background(bg)
                                    .Text(f.SeverityText).Bold().FontSize(8).FontColor("#FFFFFF");
                                table.Cell().BorderBottom(0.5f).BorderColor("#E0E0E0").Padding(6)
                                    .Text(f.Evidence).FontColor("#444444").FontSize(9);
                            }
                        });
                    }

                    col.Item().PaddingTop(12).Text("Contagem de batimentos por tipo")
                        .FontSize(13).Bold().FontColor(Ink);

                    var beatGroups = data.Annotations
                        .Where(a => a.IsBeat)
                        .GroupBy(a => a.Code)
                        .OrderByDescending(g => g.Count())
                        .ToList();

                    if (beatGroups.Count == 0)
                    {
                        col.Item().PaddingTop(4).Text("Nenhum batimento anotado.")
                            .FontColor(Muted).FontSize(10);
                    }
                    else
                    {
                        col.Item().PaddingTop(6).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(40);
                                columns.RelativeColumn();
                                columns.ConstantColumn(90);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background("#2B2B2B").Padding(4)
                                    .Text("Símbolo").Bold().FontColor("#FFFFFF").FontSize(9);
                                header.Cell().Background("#2B2B2B").Padding(4)
                                    .Text("Tipo").Bold().FontColor("#FFFFFF").FontSize(9);
                                header.Cell().Background("#2B2B2B").Padding(4)
                                    .Text("Quantidade").Bold().FontColor("#FFFFFF").FontSize(9);
                            });

                            foreach (var g in beatGroups)
                            {
                                table.Cell().BorderBottom(0.5f).BorderColor("#E0E0E0").Padding(4)
                                    .Text(MitBihAnnotations.SymbolFor(g.Key).ToString()).Bold();
                                table.Cell().BorderBottom(0.5f).BorderColor("#E0E0E0").Padding(4)
                                    .Text(MitBihAnnotations.MeaningFor(g.Key)).FontColor("#444444").FontSize(9);
                                table.Cell().BorderBottom(0.5f).BorderColor("#E0E0E0").Padding(4)
                                    .Text(g.Count().ToString()).FontSize(9);
                            }
                        });
                    }

                    if (data.WaveformPng.Length > 0)
                    {
                        col.Item().PaddingTop(14).Text("Trecho do traçado (leitura atual)")
                            .FontSize(13).Bold().FontColor(Ink);
                        col.Item().PaddingTop(4).Image(data.WaveformPng).FitArea();
                    }

                    col.Item().PaddingTop(16).LineHorizontal(1).LineColor("#DDDDDD");
                    col.Item().PaddingTop(8)
                        .Text("Análise automática baseada nas anotações (.atr) — apenas para estudo e demonstração. Não substitui avaliação médica.")
                        .FontSize(8).FontColor("#AAAAAA");
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Página ").FontSize(9).FontColor("#999999");
                    x.CurrentPageNumber().FontSize(9).FontColor("#999999");
                    x.Span(" de ").FontSize(9).FontColor("#999999");
                    x.TotalPages().FontSize(9).FontColor("#999999");
                });
            });
        }).GeneratePdf();
    }

    private static void Info(this RowDescriptor row, string label, string value)
    {
        row.RelativeItem().Column(col =>
        {
            col.Item().Text(label).FontSize(8).FontColor("#888888");
            col.Item().Text(value).FontSize(10).Bold().FontColor(Ink);
        });
    }

    private static string FormatDuration(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        if (t.TotalSeconds < 60)
            return $"{Math.Max(0, (int)t.TotalSeconds)} s";
        if (t.TotalMinutes < 60)
            return $"{(int)t.TotalMinutes} min {(t.Seconds > 0 ? t.Seconds + " s" : "")}".TrimEnd();
        return $"{(int)t.TotalHours} h {(int)t.TotalMinutes % 60:00} min";
    }
}
