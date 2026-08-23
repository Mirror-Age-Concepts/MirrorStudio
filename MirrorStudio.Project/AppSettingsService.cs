using System.Text.Json;

namespace MirrorStudio.Project
{
    public class AppSettings
    {
        public string FFmpegPath { get; set; } = "ffmpeg";
        public string FFprobePath { get; set; } = "ffprobe";
        public bool AutoSave { get; set; } = true;
        public string DefaultProjectFolder { get; set; } = string.Empty;
        public bool HardwareAcceleration { get; set; } = false;
        public string Theme { get; set; } = "Dark";
    }

    public class AppSettingsService
    {
        private static string ConfigDir =>
            System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "MirrorStudio");

        private static string FilePath => System.IO.Path.Combine(ConfigDir, "settings.json");

        public AppSettings Load()
        {
            try
            {
                if (System.IO.File.Exists(FilePath))
                {
                    return JsonSerializer.Deserialize<AppSettings>(System.IO.File.ReadAllText(FilePath)) ?? new AppSettings();
                }
            }
            catch
            {
            }
            return new AppSettings();
        }

        public void Save(AppSettings settings)
        {
            try
            {
                System.IO.Directory.CreateDirectory(ConfigDir);
                System.IO.File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }
    }
}
