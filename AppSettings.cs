using System;
using System.IO;
using System.Text.Json;

namespace Image2Text
{
    public class AppSettings
    {
        public bool DebugMode { get; set; } = false;
        public string PsmMode { get; set; } = "Auto";   // Auto | SingleColumn | SingleBlock | SparseText
        public bool AutoEnhance { get; set; } = true;

        private static string DirPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Image2Text");
        private static string FilePath => Path.Combine(DirPath, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"settings load failed: {ex.Message}");
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DirPath);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Logger.Warn($"settings save failed: {ex.Message}");
            }
        }
    }
}
