namespace MirrorStudio.Core.Commands;

/// <summary>
/// Canonical undo/redo history for editing operations.
/// </summary>
public class CommandManager
{
    private readonly Stack<IEditCommand> _undo = [];
    private readonly Stack<IEditCommand> _redo = [];

    /// <summary>Raised after any history change (execute/undo/redo/clear).</summary>
    public event Action? HistoryChanged;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Executes a command and records it as the newest undo step.</summary>
    public void Execute(IEditCommand command)
    {
        command.Execute();

        _undo.Push(command);
        _redo.Clear();
        HistoryChanged?.Invoke();
    }

    /// <summary>
    /// Records an already-applied mutation (drag/trim preview pattern:
    /// preview live, push ONE command on mouse-up) as the newest undo step.
    /// </summary>
    public void Push(IEditCommand command)
    {
        _undo.Push(command);
        _redo.Clear();
        HistoryChanged?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0)
            return;

        var command = _undo.Pop();

        command.Undo();

        _redo.Push(command);
        HistoryChanged?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0)
            return;

        var command = _redo.Pop();

        command.Execute();

        _undo.Push(command);
        HistoryChanged?.Invoke();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        HistoryChanged?.Invoke();
    }
}
