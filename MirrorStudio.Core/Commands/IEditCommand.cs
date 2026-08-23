namespace MirrorStudio.Core.Commands;

public interface IEditCommand
{
    void Execute();

    void Undo();
}
