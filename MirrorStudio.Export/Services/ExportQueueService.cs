using MirrorStudio.Core.Projects;
using MirrorStudio.Export.Interfaces;
using MirrorStudio.Export.Models;

namespace MirrorStudio.Export.Services;

public enum ExportJobState { Waiting, Rendering, Completed, Failed, Cancelled }

/// <summary>One entry in the export queue (spec M8 §21).</summary>
public sealed class ExportJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public required Project Project { get; init; }
    public required ExportSettings Settings { get; init; }

    public ExportJobState State { get; internal set; } = ExportJobState.Waiting;
    public double Percentage { get; internal set; }
    public string StatusText { get; internal set; } = "Waiting...";

    internal CancellationTokenSource Cancellation { get; } = new();
}

/// <summary>
/// Non-blocking export queue: editing continues while jobs render one after
/// another on a background worker.
/// </summary>
public sealed class ExportQueueService : IDisposable
{
    private readonly IRenderEngine _engine;
    private readonly object _gate = new();
    private readonly List<ExportJob> _jobs = [];
    private Task? _worker;

    /// <summary>Raised on a worker thread whenever any job changes.</summary>
    public event Action? Changed;

    public ExportQueueService(IRenderEngine engine) => _engine = engine;

    public ExportJob Enqueue(Project project, ExportSettings settings, string? name = null)
    {
        var job = new ExportJob
        {
            Project = project,
            Settings = settings,
            Name = name ?? Path.GetFileName(settings.OutputPath),
        };

        lock (_gate)
        {
            _jobs.Add(job);
            if (_worker is null || _worker.IsCompleted)
                _worker = Task.Run(() => PumpAsync());
        }

        Changed?.Invoke();
        return job;
    }

    public IReadOnlyList<ExportJob> Snapshot()
    {
        lock (_gate)
            return [.. _jobs];
    }

    public bool Cancel(Guid jobId)
    {
        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job == null) return false;
            if (job.State is ExportJobState.Completed or ExportJobState.Cancelled) return false;
            job.Cancellation.Cancel();
            return true;
        }
    }

    public void ClearFinished()
    {
        lock (_gate)
            _jobs.RemoveAll(j => j.State is ExportJobState.Completed or ExportJobState.Failed or ExportJobState.Cancelled);
        Changed?.Invoke();
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            ExportJob? next;
            lock (_gate)
                next = _jobs.FirstOrDefault(j => j.State == ExportJobState.Waiting);

            if (next is null) return;

            next.State = ExportJobState.Rendering;
            next.StatusText = "Starting...";
            Changed?.Invoke();

            try
            {
                var reporter = new Progress<ExportProgress>(p =>
                {
                    next.Percentage = p.Percentage;
                    next.StatusText = $"{p.Percentage:0}% — {p.EstimatedRemaining:hh\\:mm\\:ss} remaining";
                    Changed?.Invoke();
                });

                await _engine.RenderAsync(next.Project, next.Settings, reporter, next.Cancellation.Token);

                next.State = ExportJobState.Completed;
                next.Percentage = 100;
                next.StatusText = "Completed";
            }
            catch (OperationCanceledException)
            {
                next.State = ExportJobState.Cancelled;
                next.StatusText = "Cancelled";
            }
            catch (Exception ex)
            {
                next.State = ExportJobState.Failed;
                next.StatusText = ex.Message;
            }

            Changed?.Invoke();
        }
    }

    public void Dispose() => CancelAll();

    private void CancelAll()
    {
        lock (_gate)
            foreach (var j in _jobs.Where(j => j.State is ExportJobState.Waiting or ExportJobState.Rendering))
                j.Cancellation.Cancel();
    }
}
