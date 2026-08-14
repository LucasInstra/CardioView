using System;
using System.IO;
using System.Text.Json;

namespace CardioView.Services;

public sealed class AppSettings
{
    public bool AlarmSystemEnabled { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public bool AutoNibp { get; set; }
    public double HrHigh { get; set; } = 120;
    public double HrLow { get; set; } = 55;
    public double Spo2Low { get; set; } = 90;
    public double SysHigh { get; set; } = 165;
}

public static class SettingsStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CardioView", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }
}
