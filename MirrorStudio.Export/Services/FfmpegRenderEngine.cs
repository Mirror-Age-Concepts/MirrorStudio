using System.Diagnostics;
using System.Globalization;
using System.Text;
using MirrorStudio.Core.Projects;
using MirrorStudio.Export.Interfaces;
using MirrorStudio.Export.Models;

namespace MirrorStudio.Export.Services;

/// <summary>
/// Turns a <see cref="RenderPlan"/> into the actual ffmpeg command line and
/// runs it, translating stderr into <see cref="ExportProgress"/>.
/// </summary>
public class FfmpegRenderEngine : IRenderEngine
{
    private readonly FfmpegProcessRunner _runner;
    private readonly FilterGraphBuilder _builder;
    private readonly HardwareEncoderDetector? _hardware;

    public FfmpegRenderEngine(
        FfmpegProcessRunner runner,
        FilterGraphBuilder? builder = null,
        HardwareEncoderDetector? hardware = null)
    {
        _runner = runner;
        _builder = builder ?? new();
        _hardware = hardware;
    }

    /// <summary>The last command line built (exposed for logging/UI).</summary>
    public string LastArguments { get; private set; } = "";

    public async Task RenderAsync(
        Project project,
        ExportSettings settings,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = _builder.Build(project, settings);

        if (!settings.ExportAudioOnly && plan.VideoClipCount == 0 && plan.AudioClipCount == 0)
            throw new InvalidOperationException("Timeline is empty — nothing to render.");

        var args = BuildArguments(settings, plan);
        LastArguments = $"ffmpeg {args}";

        var clock = Stopwatch.StartNew();
        var sink = progress is null ? null : new Progress<string>(line =>
        {
            var p = ExportProgressParser.ToProgress(line, plan.TotalDuration, clock);
            if (p != null) progress.Report(p);
        });

        int exitCode = await _runner.RunAsync(args, sink, cancellationToken);

        if (exitCode != 0)
            throw new InvalidOperationException($"FFmpeg exited with code {exitCode}.");

        progress?.Report(new ExportProgress
        {
            Percentage = 100,
            RenderedDuration = TimeSpan.FromSeconds(plan.TotalDuration),
            EstimatedRemaining = TimeSpan.Zero,
            Status = "Done",
        });
    }

    public string BuildArguments(ExportSettings settings, RenderPlan plan)
    {
        var sb = new StringBuilder("-hide_banner -y ");

        foreach (var input in plan.Inputs)
            sb.Append(input.PreInputArgs).Append(" -i ").Append(input.Path).Append(' ');

        sb.Append("-filter_complex \"").Append(plan.FilterGraph).Append("\" ");

        // ---------- mapping ----------
        bool audioOnly = settings.ExportAudioOnly;
        if (!audioOnly && plan.HasVideo)
            sb.Append("-map [outv] ");
        if (plan.HasAudio)
            sb.Append("-map [outa] ");
        else if (!audioOnly)
            sb.Append("-an ");

        // ---------- video codec ----------
        string videoCodec = settings.VideoCodec;
        if (settings.UseHardwareEncoding && _hardware is not null)
        {
            // resolve at build time; falls back to software when unsupported
            videoCodec = settings.VideoCodec switch
            {
                "libx265" => "hevc_nvenc",
                _ => "h264_nvenc",
            };
            if (!_hardware.SupportsAsync(videoCodec).GetAwaiter().GetResult())
                videoCodec = settings.VideoCodec;
        }

        bool nvenc = videoCodec.EndsWith("_nvenc", StringComparison.OrdinalIgnoreCase)
                     || videoCodec.Contains("amf", StringComparison.OrdinalIgnoreCase)
                     || videoCodec.Contains("qsv", StringComparison.OrdinalIgnoreCase);

        if (!audioOnly && plan.HasVideo)
        {
            sb.Append($"-c:v {videoCodec} ");
            if (nvenc)
                sb.Append($"-b:v {settings.VideoBitrate}k -preset p4 ");
            else
                sb.Append($"-preset medium -crf {settings.Crf} ");

            sb.Append($"-pix_fmt yuv420p ");
            sb.Append($"-r {settings.FrameRate.ToString("0.###", CultureInfo.InvariantCulture)} ");
        }

        // ---------- audio codec ----------
        if (plan.HasAudio || audioOnly)
        {
            sb.Append($"-c:a {settings.AudioCodec} ");
            sb.Append($"-b:a {settings.AudioBitrate}k ");
            sb.Append($"-ar {ProjectSampleRateOrDefault()} ");
        }
        if (audioOnly)
            sb.Append("-vn ");

        sb.Append('"').Append(settings.OutputPath).Append('"');
        return sb.ToString();

        string ProjectSampleRateOrDefault() => "48000";
    }
}
