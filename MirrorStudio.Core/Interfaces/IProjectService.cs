namespace MirrorStudio.Core.Interfaces
{
    public interface IProjectService
    {
        string CreateProject(string name, int width, int height, double frameRate);
        bool SaveProject(string projectPath);
        bool OpenProject(string projectPath);
    }
}
