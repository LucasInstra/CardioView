using System;
using System.Collections.Generic;

namespace CardioView.Services;

public static class QrsDetector
{
    public static List<int> DetectPeaks(double[] signal, int sampleRate)
    {
        var peaks = new List<int>();
        int n = signal.Length;
        if (sampleRate <= 0 || n < sampleRate / 2) return peaks;

        int lp = Math.Max(3, sampleRate / 40);
        var low = new double[n];
        double run = 0;
        for (int i = 0; i < n; i++)
        {
            run += signal[i];
            if (i - lp >= 0) run -= signal[i - lp];
            low[i] = run / Math.Min(i + 1, lp);
        }

        var d = new double[n];
        for (int i = 1; i < n; i++)
            d[i] = low[i] - low[i - 1];

        var sq = new double[n];
        for (int i = 0; i < n; i++)
            sq[i] = d[i] * d[i];

        int ia = Math.Max(8, sampleRate / 8);
        var integ = new double[n];
        run = 0;
        for (int i = 0; i < n; i++)
        {
            run += sq[i];
            if (i - ia >= 0) run -= sq[i - ia];
            integ[i] = run / Math.Min(i + 1, ia);
        }

        double maxV = 0;
        foreach (var v in integ)
            if (v > maxV) maxV = v;
        if (maxV <= 0) return peaks;

        double thresh = 0.35 * maxV;
        int refr = Math.Max(1, sampleRate * 200 / 1000);
        int lastPeak = -refr;

        for (int i = 1; i < n - 1; i++)
        {
            if (integ[i] > thresh && integ[i] >= integ[i - 1] && integ[i] > integ[i + 1]
                && i - lastPeak >= refr)
            {
                peaks.Add(i);
                lastPeak = i;
            }
        }

        return peaks;
    }
}