using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CardioView.Services;

public sealed class MitBihSignal
{
    public required string FileName { get; init; }
    public required int Format { get; init; }
    public required double Gain { get; init; }
    public required double Baseline { get; init; }
    public required string Units { get; init; }
    public required string Description { get; init; }
    public required double[] Samples { get; init; }
}

public sealed class MitBihRecord
{
    public required string Name { get; init; }
    public required int SampleRate { get; init; }
    public required int SampleCount { get; init; }
    public required IReadOnlyList<MitBihSignal> Signals { get; init; }
}

public static class MitBihReader
{
    public static MitBihRecord Load(string heaPath)
    {
        var lines = File.ReadAllLines(heaPath)
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
            .ToArray();
        if (lines.Length == 0)
            throw new InvalidDataException("arquivo .hea vazio");

        var first = lines[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (first.Length < 4)
            throw new InvalidDataException("cabeçalho inválido");

        string name = first[0];
        int signalCount = int.Parse(first[1], CultureInfo.InvariantCulture);
        int sampleRate = int.Parse(first[2], CultureInfo.InvariantCulture);
        int sampleCount = int.Parse(first[3], CultureInfo.InvariantCulture);
        string dir = Path.GetDirectoryName(heaPath) ?? "";

        var bytesByFile = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        var signals = new List<MitBihSignal>(signalCount);
        for (int i = 1; i <= signalCount && i < lines.Length; i++)
        {
            var f = lines[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 6)
                throw new InvalidDataException($"linha de sinal {i} inválida");

            string file = f[0];
            int format = int.Parse(f[1], CultureInfo.InvariantCulture);
            double gain = double.Parse(f[2], CultureInfo.InvariantCulture);

            int idx = 3;
            double baseline = gain;
            string units = "mV";
            if (f[idx].StartsWith('('))
            {
                baseline = double.Parse(f[idx].Trim('(', ')'), CultureInfo.InvariantCulture);
                idx++;
            }
            if (f[idx].Contains('/'))
            {
                units = f[idx].Substring(f[idx].IndexOf('/') + 1);
                idx++;
            }

            double.Parse(f[idx], CultureInfo.InvariantCulture); idx++;
            double adcZero = double.Parse(f[idx], CultureInfo.InvariantCulture); idx++;
            string description = string.Join(" ", f.Skip(idx + 3).Where(t => !long.TryParse(t, out _)));

            string datPath = Path.IsPathRooted(file) ? file : Path.Combine(dir, file);
            if (!File.Exists(datPath))
            {
                string alt = Path.Combine(dir, Path.GetFileNameWithoutExtension(heaPath) + ".dat");
                if (File.Exists(alt))
                    datPath = alt;
            }
            if (!File.Exists(datPath))
                throw new FileNotFoundException(
                    $"Arquivo de dados '{file}' não encontrado. Coloque o .dat na mesma pasta do .hea (ex.: 100.dat ao lado de 100.hea).");

            if (!bytesByFile.TryGetValue(datPath, out var bytes))
            {
                bytes = File.ReadAllBytes(datPath);
                bytesByFile[datPath] = bytes;
            }

            double[] adc = DecodeSignal(bytes, format, signalCount, sampleCount, i - 1);

            double center = adcZero != 0 ? adcZero : baseline;
            double[] samples = new double[adc.Length];
            for (int k = 0; k < adc.Length; k++)
                samples[k] = gain != 0 ? (adc[k] - center) / gain : 0;

            double mean = 0;
            foreach (var v in samples) mean += v;
            if (samples.Length > 0) mean /= samples.Length;
            for (int k = 0; k < samples.Length; k++)
                samples[k] -= mean;

            signals.Add(new MitBihSignal
            {
                FileName = file,
                Format = format,
                Gain = gain,
                Baseline = baseline,
                Units = units,
                Description = description,
                Samples = samples,
            });
        }

        return new MitBihRecord
        {
            Name = name,
            SampleRate = sampleRate,
            SampleCount = sampleCount,
            Signals = signals,
        };
    }

    private static double[] DecodeSignal(byte[] bytes, int format, int signalCount, int sampleCount, int signalIndex)
    {
        var result = new double[sampleCount];
        switch (format)
        {
            case 212:
            {
                for (int s = 0; s < sampleCount; s++)
                {
                    int flat = s * signalCount + signalIndex;
                    int off = (flat / 2) * 3;
                    if (off + 2 >= bytes.Length) break;
                    int v = (flat & 1) == 0
                        ? bytes[off] | ((bytes[off + 1] & 0x0F) << 8)
                        : bytes[off + 2] | ((bytes[off + 1] & 0xF0) << 4);
                    if ((v & 0x800) != 0) v -= 0x1000;
                    result[s] = v;
                }
                break;
            }
            case 16:
            {
                for (int s = 0; s < sampleCount; s++)
                {
                    int off = (s * signalCount + signalIndex) * 2;
                    if (off + 1 >= bytes.Length) break;
                    short v = (short)(bytes[off] | (bytes[off + 1] << 8));
                    result[s] = v;
                }
                break;
            }
            case 80:
            {
                for (int s = 0; s < sampleCount; s++)
                {
                    int off = s * signalCount + signalIndex;
                    if (off >= bytes.Length) break;
                    result[s] = (sbyte)bytes[off];
                }
                break;
            }
            default:
                throw new InvalidDataException($"formato {format} não suportado (212, 16 e 80 são suportados)");
        }
        return result;
    }
}