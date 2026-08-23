using MirrorStudio.Core.Projects;
using MirrorStudio.Export.Interfaces;
using MirrorStudio.Export.Models;

namespace MirrorStudio.Export.Services;

/// <summary>
/// High-level facade: validates settings, ensures the output directory,
/// delegates the encode to <see cref="FfmpegRenderEngine"/> and annotates
/// failures with plan diagnostics (offline media etc.).
/// </summary>
public class RenderEngine : IRenderEngine
{
    private readonly FfmpegRenderEngine _inner;
    private readonly FilterGraphBuilder _builder;

    public RenderEngine(FfmpegRenderEngine inner, FilterGraphBuilder? builder = null)
    {
        _inner = inner;
        _builder = builder ?? new();
    }

    /// <summary>Diagnostics of the most recent render attempt.</summary>
    public IReadOnlyList<string> LastOfflineMedia { get; private set; } = [];

    public async Task RenderAsync(
        Project project,
        ExportSettings settings,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.OutputPath))
            throw new InvalidOperationException("No output path configured.");

        var dir = Path.GetDirectoryName(Path.GetFullPath(settings.OutputPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var plan = _builder.Build(project, settings);
        LastOfflineMedia = plan.OfflineMedia;

        try
        {
            await _inner.RenderAsync(project, settings, progress, cancellationToken);
        }
        catch (InvalidOperationException) when (plan.OfflineMedia.Count > 0)
        {
            throw new InvalidOperationException(
                "Render failed. Offline media was substituted with placeholders: "
                + string.Join(", ", plan.OfflineMedia));
        }
    }
}
