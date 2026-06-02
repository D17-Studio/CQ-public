using System;
using System.IO;
using System.Text.Json;

namespace CQ_ChartMaker.Models;

public static class UserSettingsService
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CQ-ChartMaker");

    private static readonly string FilePath = Path.Combine(DataDir, "settings.json");

    public static UserSettingsModel Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<UserSettingsModel>(json) ?? new UserSettingsModel();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserSettings] Load failed: {ex.Message}");
        }
        return new UserSettingsModel();
    }

    public static void Save(UserSettingsModel settingsModel)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var json = JsonSerializer.Serialize(settingsModel);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserSettings] Save failed: {ex.Message}");
        }
    }
}
