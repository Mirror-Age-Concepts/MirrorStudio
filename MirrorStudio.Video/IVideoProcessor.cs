namespace MirrorStudio.Video
{
    public interface IVideoProcessor
    {
        void GenerateProxy(string sourcePath, string outputPath);
    }
}
