namespace MirrorStudio.Core.Enums
{
    public enum ProjectResolution
    {
        HD_720p = 0,      // 1280 x 720
        FullHD_1080p = 1, // 1920 x 1080
        QHD_1440p = 2,    // 2560 x 1440
        UHD_4K = 3,       // 3840 x 2160
        Vertical_1080x1920 = 4,
        Square_1080x1080 = 5,
        Custom = 99
    }

    public static class ProjectResolutionExtensions
    {
        public static (int Width, int Height) Dimensions(this ProjectResolution res) => res switch
        {
            ProjectResolution.HD_720p => (1280, 720),
            ProjectResolution.FullHD_1080p => (1920, 1080),
            ProjectResolution.QHD_1440p => (2560, 1440),
            ProjectResolution.UHD_4K => (3840, 2160),
            ProjectResolution.Vertical_1080x1920 => (1080, 1920),
            ProjectResolution.Square_1080x1080 => (1080, 1080),
            _ => (1920, 1080)
        };

        public static ProjectResolution FromDimensions(int width, int height)
        {
            foreach (ProjectResolution r in new[]
                     {
                         ProjectResolution.HD_720p, ProjectResolution.FullHD_1080p,
                         ProjectResolution.QHD_1440p, ProjectResolution.UHD_4K,
                         ProjectResolution.Vertical_1080x1920, ProjectResolution.Square_1080x1080
                     })
            {
                var (w, h) = r.Dimensions();
                if (w == width && h == height) return r;
            }
            return ProjectResolution.Custom;
        }
    }
}
