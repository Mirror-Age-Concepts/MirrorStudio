namespace MirrorStudio.Image
{
    public interface IImageProcessor
    {
        void ApplyFilter(string imagePath, string filterName);
    }
}
