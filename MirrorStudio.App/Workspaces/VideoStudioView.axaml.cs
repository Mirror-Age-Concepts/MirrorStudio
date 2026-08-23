using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MirrorStudio.Core.Models;
using MirrorStudio.Timeline;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IOPath = System.IO.Path;

namespace MirrorStudio.App.Workspaces
{
    public partial class VideoStudioView : UserControl
    {
        private const double TrackHeight = 28.0;
        private const int VideoRows = 4;

        private readonly SessionState _session;
        private DispatcherTimer? _playTimer;
        private readonly Dictionary<string, Bitmap?> _bitmapCache = new();

        // media library selection (entry object, not an index)
        private MediaEntry? _selectedMediaEntry;

        // drag from media library
        private const string MediaDropFormat = Services.MediaCardFactory.MediaDropFormat;

        // async video frame cache: key = "path|time(0.25s)" → bitmap; extraction dedup set
        private readonly Dictionary<string, Bitmap?> _frameCache = new();
        private readonly HashSet<string> _framesInFlight = new();
        private string? _pendingFrameKey;

        /// <summary>FFprobe built from the user's settings, with auto-detection fallback.</summary>
        private MirrorStudio.Video.Services.FfprobeService CreateProbe() =>
            new(MirrorStudio.Video.Services.MediaEngineLocator.ResolveFfprobe(_session.Settings.FFprobePath));

        /// <summary>Thumbnail service built from the user's settings.</summary>
        private MirrorStudio.Video.Services.ThumbnailService CreateThumbs() =>
            new(MirrorStudio.Video.Services.MediaEngineLocator.ResolveFfmpeg(_session.Settings.FFmpegPath));

        // timeline drag state
        private Border? _dragClipBorder;
        private ClipModel? _dragClip;
        private Point _dragStartPoint;
        private double _dragOrigStart;
        private int _dragOrigTrack;
        private bool _draggingTimeline;
        private bool _syncing;

        // trim state
        private enum TrimEdge { None, Left, Right }
        private TrimEdge _trimEdge = TrimEdge.None;
        private double _trimOrigStart, _trimOrigDuration, _trimOrigIn, _trimOrigOut;
        private const double EdgeZonePx = 7.0;

        // MirrorStudio.Timeline services (Milestone 3)
        private readonly MirrorStudio.Timeline.Services.TimelineService _timelineService = new();
        private readonly MirrorStudio.Timeline.Services.ClipService _clipService = new();
        private readonly MirrorStudio.Timeline.Services.SnapService _snapService = new();
        private readonly MirrorStudio.Timeline.Services.ActiveClipResolver _activeClipResolver = new();
        public MirrorStudio.Timeline.ViewModels.TimelineViewModel TimelineViewModel { get; }
        private const double BasePps = 30.0;

        // Milestone 4: preview/playback — engine owns the clock while playing,
        // renderer is behind an interface so a GPU renderer can replace it later.
        public MirrorStudio.Rendering.Interfaces.IPlaybackEngine Playback { get; }
        public ViewModels.MainWindowViewModel PlaybackVm { get; } = new();
        private Services.AvaloniaPreviewRenderer? _previewRenderer;

        // Milestone 7: canonical editing history (MirrorStudio.Core.Commands)
        private readonly MirrorStudio.Core.Commands.CommandManager _undo = new();

        /// <summary>All snap candidates: other clips' edges, playhead and timeline start.</summary>
        private double[] SnapPoints(ClipModel? exclude)
        {
            var others = (Tl?.Clips ?? Enumerable.Empty<ClipModel>())
                .Where(c => c != exclude)
                .Select(c => (c.StartTime, c.StartTime + c.Duration));
            return MirrorStudio.Timeline.Services.SnapService
                .CollectPoints(others, _session.PlayheadSeconds)
                .ToArray();
        }

        /// <summary>Snap a start position; also tries snapping by the clip's end edge.</summary>
        private double SnapStart(double newStart, double duration, double[] points)
        {
            var s = _snapService.Snap(newStart, points);
            var e = _snapService.Snap(newStart + duration, points) - duration;
            return Math.Max(0, Math.Abs(e - newStart) < Math.Abs(s - newStart) ? e : s);
        }

        private static readonly string[] VideoExts = { ".mp4", ".mov", ".avi", ".mkv", ".webm", ".wmv" };
        private static readonly string[] AudioExts = { ".mp3", ".wav", ".aac", ".flac", ".ogg", ".m4a", ".wma" };
        private static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };
        private static readonly string[] GraphicExts = { ".png", ".svg", ".webp", ".ico" };

        public VideoStudioView(SessionState session)
        {
            InitializeComponent();
            _session = session;
            TimelineViewModel = new MirrorStudio.Timeline.ViewModels.TimelineViewModel(_timelineService.Timeline);
            Playback = new MirrorStudio.Rendering.Services.PlaybackEngine(
                MirrorStudio.Video.Services.MediaEngineLocator.ResolveFfmpeg(_session.Settings.FFmpegPath));
            _previewRenderer = new Services.AvaloniaPreviewRenderer(PreviewImage);

            FontCombo.ItemsSource = Avalonia.Media.FontManager.Current.SystemFonts.Select(f => f.Name).ToList();

            _session.ProjectChanged += OnProjectChangedInternal;
            _session.PlayheadChanged += OnPlayheadChangedInternal;
            _session.SelectionChanged += OnSelectionChangedInternal;

            _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _playTimer.Tick += (_, _) => AdvancePlayback();

            // drag & drop targets (media library → timeline / program monitor)
            ClipsCanvas.AddHandler(DragDrop.DragOverEvent, OnTimelineDragOver);
            ClipsCanvas.AddHandler(DragDrop.DropEvent, OnTimelineDrop);
            StageBorder.AddHandler(DragDrop.DragOverEvent, OnPreviewDragOver);
            StageBorder.AddHandler(DragDrop.DropEvent, OnPreviewDrop);

            // keyboard shortcuts (Space/J/K/L/S/I/O/Delete/arrows/Home/End)
            this.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);

            DetachAllHandlers();
            AttachInspectorHandlers();

            RefreshProjectState();
        }

        // ---------- project / refresh ----------

        private void OnProjectChangedInternal()
        {
            // clear undo history when the underlying project changes identity
            if (!ReferenceEquals(_loadedProjectForUndo, _session.CurrentProject))
            {
                _loadedProjectForUndo = _session.CurrentProject;
                _undo.Clear();
            }
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshProjectState); return; }
            RefreshProjectState();
        }

        private MirrorStudio.Core.Models.ProjectModel? _loadedProjectForUndo;

        private void RefreshProjectState()
        {
            StopPlayback();
            if (_session.CurrentProject is { } p)
                Playback.FrameRate = Math.Max(1, p.FrameRate);   // project fps drives the playback clock
            RebuildMediaList();
            RebuildTrackHeaders();
            RebuildTimeline();
            SyncPreviewSize();
            RenderPreviewFrame();
            UpdateSequenceInfo();
            PushSelectionToInspector();
        }

        private void OnPlayheadChangedInternal()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnPlayheadChangedInternal); return; }
            TimelineViewModel.CurrentTime = _session.PlayheadSeconds;
            UpdateTimecode();
            PositionPlayhead();
            RenderPreviewFrame();
            if (!_draggingTimeline && SeekSlider != null && !_seekSyncing)
                SyncSeekSlider();
        }

        private bool _seekSyncing;

        private void SyncSeekSlider()
        {
            _seekSyncing = true;
            var dur = _session.Timeline?.SequenceDuration ?? 0;
            SeekSlider.Maximum = Math.Max(1, dur);
            SeekSlider.Value = Math.Min(_session.PlayheadSeconds, SeekSlider.Maximum);
            _seekSyncing = false;
        }

        private void OnSelectionChangedInternal()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnSelectionChangedInternal); return; }
            PushSelectionToInspector();
            RebuildTimeline();  // update highlight
            RenderPreviewFrame();
        }

        private TimelineData? Tl => _session.Timeline;
        private double Pps => TimelineZoomSlider?.Value ?? 30.0;

        private static IBrush Res(string key)
        {
            if (Application.Current?.Resources.TryGetResource(key, null, out var v) == true && v is IBrush b)
                return b;
            return Brushes.Transparent;
        }

        // ---------- media browser ----------

        private IEnumerable<MediaEntry> FilteredMedia()
        {
            var media = _session.CurrentProject?.Media ?? new List<MediaEntry>();
            var cat = (CategoryCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "All";
            if (cat == "All") return media;
            return media.Where(m => m.MediaType.Equals(cat, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Builds the grouped thumbnail-card library: VIDEO / AUDIO / IMAGES sections.</summary>
        private void RebuildMediaList()
        {
            if (CategoryCombo == null || MediaListHost == null) return;
            MediaListHost.Children.Clear();

            var groups = new (string Title, string[] Categories)[]
            {
                ("VIDEO", new[] { "Video" }),
                ("AUDIO", new[] { "Audio" }),
                ("IMAGES", new[] { "Images", "Graphics" }),
                ("OTHER", new[] { "Other" })
            };

            foreach (var (title, categories) in groups)
            {
                var items = FilteredMedia()
                    .Where(m => categories.Contains(m.MediaType))
                    .ToList();
                if (items.Count == 0) continue;

                MediaListHost.Children.Add(new TextBlock
                {
                    Text = title,
                    Foreground = Res("BTxtMid"),
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(2, 10, 0, 6)
                });

                var wrap = new WrapPanel { ItemWidth = 106, Orientation = Avalonia.Layout.Orientation.Horizontal };
                _mediaCards.Clear();
                foreach (var entry in items)
                {
                    var card = BuildMediaCard(entry);
                    _mediaCards[entry] = card;
                    wrap.Children.Add(card);
                }
                MediaListHost.Children.Add(wrap);
            }

            var total = (_session.CurrentProject?.Media ?? new List<MediaEntry>()).Count;
            if (total == 0)
            {
                MediaListHost.Children.Add(new TextBlock
                {
                    Text = "No media yet.\nUse Import… to add video,\naudio and images.",
                    Foreground = Res("BTxtLow"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 8, 0, 0)
                });
            }
        }

        private Border BuildMediaCard(MediaEntry entry)
        {
            bool selected = ReferenceEquals(entry, _selectedMediaEntry);

            var thumbPath = string.IsNullOrEmpty(entry.ThumbnailRelativePath)
                ? null
                : _session.ResolvePath(entry.ThumbnailRelativePath);

            Control content;
            bool hasVisualThumb = thumbPath != null &&
                (entry.MediaType is "Video" or "Images" or "Graphics");
            if (hasVisualThumb)
            {
                var bmp = GetBitmap(thumbPath!);
                content = new Avalonia.Controls.Image
                {
                    Source = bmp,
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
                };
            }
            else
            {
                content = new TextBlock
                {
                    Text = IconFor(entry.MediaType),
                    FontSize = 26,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
            }

            double dur = entry.DurationSec;
            string subText = dur > 0.05 ? FormatTimecode(dur, 25) : entry.MediaType;

            var card = new Border
            {
                Height = 92,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Background = Res("BInset"),
                BorderThickness = new Thickness(selected ? 1.5 : 0.75),
                BorderBrush = selected ? Res("BAccent") : Res("BBorder"),
                Tag = entry,
                Child = new Grid
                {
                    RowDefinitions = new RowDefinitions("*,Auto")
                }
            };
            ((Grid)card.Child!).Children.Add(content);
            Grid.SetRow(content, 0);
            var label = new StackPanel
            {
                Margin = new Thickness(3, 1, 3, 2)
            };
            label.Children.Add(new TextBlock
            {
                Text = IOPath.GetFileNameWithoutExtension(entry.FileName),
                Foreground = Res("BTxtHigh"),
                FontSize = 9.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            label.Children.Add(new TextBlock
            {
                Text = subText,
                Foreground = Res("BTxtLow"),
                FontSize = 8.5
            });
            ((Grid)card.Child).Children.Add(label);
            Grid.SetRow(label, 1);

            // shared gesture wiring: drag starts after a 6px move, double-click
            // inserts; tapping only restyles selection (never rebuilds the list,
            // which would detach this card mid-gesture and break dragging)
            Services.MediaCardFactory.MakeInteractive(
                card,
                entry.RelativePath,
                tap: () =>
                {
                    _selectedMediaEntry = entry;
                    _session.SelectMedia(_session.ResolvePath(entry.RelativePath) ?? entry.RelativePath);
                    RestyleMediaSelection();
                },
                doubleTap: () =>
                {
                    _selectedMediaEntry = entry;
                    RestyleMediaSelection();
                    AddEntryToTimeline(entry);
                });

            return card;
        }

        private readonly Dictionary<MediaEntry, Border> _mediaCards = new();

        private void RestyleMediaSelection()
        {
            foreach (var (entry, card) in _mediaCards)
            {
                bool selected = ReferenceEquals(entry, _selectedMediaEntry);
                card.BorderBrush = selected ? Res("BAccent") : Res("BBorder");
                card.BorderThickness = new Thickness(selected ? 1.5 : 0.75);
            }
        }

        private static string IconFor(string type) => type switch
        {
            "Video" => "🎬",
            "Audio" => "🎵",
            "Images" => "🖼",
            "Graphics" => "🎨",
            "Fonts" => "🔤",
            _ => "📄"
        };

        private void OnCategoryChanged(object? sender, SelectionChangedEventArgs e) => RebuildMediaList();

        // ---------- public API for menu bar ----------

        public Task ImportMediaAsync() => OnImportMediaCoreAsync();

        public void SplitAtPlayhead() => OnSplitClip(this, new RoutedEventArgs());

        public void DeleteSelectedClip() => OnDeleteClip(this, new RoutedEventArgs());

        public void GoToSequenceStart() => OnGotoStart(this, new RoutedEventArgs());

        public void GoToSequenceEnd() => OnGotoEnd(this, new RoutedEventArgs());

        public void ZoomTimeline(double delta)
        {
            if (TimelineZoomSlider == null) return;
            TimelineZoomSlider.Value = Math.Clamp(TimelineZoomSlider.Value + delta, TimelineZoomSlider.Minimum, TimelineZoomSlider.Maximum);
        }

        /// <summary>Re-probes media and regenerates missing/failed thumbnails.</summary>
        public async Task RegenerateThumbnailsAsync()
        {
            var proj = _session.CurrentProject;
            if (proj == null || !_session.HasProject) return;

            var thumbs = CreateThumbs();
            var thumbDir = IOPath.Combine(_session.ProjectFolder, "Thumbnails");
            Directory.CreateDirectory(thumbDir);
            int created = 0;

            foreach (var entry in proj.Media.Where(m => m.MediaType is "Video" or "Images" or "Graphics"))
            {
                bool missing = string.IsNullOrEmpty(entry.ThumbnailRelativePath) ||
                               !File.Exists(_session.ResolvePath(entry.ThumbnailRelativePath));
                if (!missing) continue;

                var src = _session.ResolvePath(entry.RelativePath);
                if (src == null || !File.Exists(src)) continue;

                var name = $"{IOPath.GetFileNameWithoutExtension(entry.FileName):N}.jpg";
                var written = await thumbs.TryGenerateAsync(src, IOPath.Combine(thumbDir, name), 0);
                if (written != null)
                {
                    entry.ThumbnailRelativePath = $"Thumbnails/{name}";
                    created++;
                }
            }

            RebuildMediaList();
            if (TopLevel.GetTopLevel(this) is Window w)
                MessageBox.Show(w, "Thumbnails", created == 0
                    ? "All thumbnails are up to date."
                    : $"Generated {created} thumbnail(s).");
        }

        private async void OnImportMedia(object? sender, RoutedEventArgs e) => await ImportMediaAsync();

        private async Task OnImportMediaCoreAsync()
        {
            var proj = _session.CurrentProject;
            if (proj == null || !_session.HasProject)
            {
                MessageBox.Show(TopLevel.GetTopLevel(this) as Window, "No Project", "Open or create a project first.");
                return;
            }

            var dialogs = new Services.DialogService(TopLevel.GetTopLevel(this) as Window
                ?? throw new InvalidOperationException("Video studio is not attached to a window."));
            var files = await new Services.ProjectDialogService(dialogs).ImportMediaAsync();
            if (files == null || files.Length == 0) return;

            // full import workflow: validation → copy → FFprobe → thumbnail → MediaEntry
            var importer = new MirrorStudio.Media.Services.MediaImportService(
                _session.Settings.FFmpegPath, _session.Settings.FFprobePath);

            foreach (var file in files)
            {
                try
                {
                    var asset = await importer.ImportAsync(file, _session.ProjectFolder);
                    if (asset.Type == MirrorStudio.Core.Enums.MediaType.Unknown && !asset.HasAudio && !asset.HasVideo)
                        continue; // unsupported file

                    if (!proj.Media.Any(m => m.RelativePath.Equals(asset.RelativePath, StringComparison.OrdinalIgnoreCase)))
                        proj.Media.Add(asset.ToMediaEntry());

                    RebuildMediaList();
                }
                catch (FileNotFoundException)
                {
                    // file vanished between dialog and copy — skip silently
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    MessageBox.Show(TopLevel.GetTopLevel(this) as Window, "Import Failed",
                        $"{IOPath.GetFileName(file)}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    // never let one bad file crash the app
                    MessageBox.Show(TopLevel.GetTopLevel(this) as Window, "Import Failed",
                        $"{IOPath.GetFileName(file)}: {ex.Message}");
                }
            }

            proj.UpdatedAt = DateTime.UtcNow;
            RebuildMediaList();
        }

        private MediaEntry? SelectedMediaEntry() => _selectedMediaEntry;

        private void AddSelectedMediaToTimeline()
        {
            var entry = SelectedMediaEntry();
            if (entry != null)
            {
                AddEntryToTimeline(entry);
            }
        }

        private void AddEntryToTimeline(MediaEntry entry)
        {
            var tl = Tl;
            if (tl == null || !_session.HasProject)
            {
                MessageBox.Show(TopLevel.GetTopLevel(this) as Window, "No Project", "Open a project with a timeline first.");
                return;
            }
            DoAddClip(entry, tl);
        }

        private void DoAddClip(MediaEntry entry, TimelineData tl)
        {
            bool isAudio = entry.MediaType == "Audio";
            int track = FindFreeTrack(tl, isAudio);
            if (track < 0) { MessageBox.Show(TopLevel.GetTopLevel(this) as Window, "Timeline Full", $"All {(isAudio ? "audio" : "video")} tracks are occupied at the end."); return; }

            double trackEnd = ClipsOn(tl, isAudio, track).Any()
                ? ClipsOn(tl, isAudio, track).Max(c => c.StartTime + c.Duration) : 0;
            bool still = entry.MediaType == "Images" || entry.MediaType == "Graphics";
            // use probed duration when available; images default to 5s
            double duration = still ? 5.0 : (entry.DurationSec > 0.05 ? entry.DurationSec : 10.0);

            var clip = new ClipModel
            {
                Name = entry.FileName,
                SourcePath = entry.RelativePath,
                MediaType = entry.MediaType,
                TrackKind = isAudio ? TrackKind.Audio : TrackKind.Video,
                TrackIndex = track,
                StartTime = trackEnd,
                Duration = duration,
                InPoint = 0,
                OutPoint = duration
            };
            _undo.Execute(new Services.AddClipCommand(tl, clip));   // undoable
            _session.SetPlayhead(clip.StartTime);
            _session.SelectClip(clip);
        }

        private static IEnumerable<ClipModel> ClipsOn(TimelineData tl, bool audio, int track) =>
            tl.Clips.Where(c => c.TrackKind == (audio ? TrackKind.Audio : TrackKind.Video) && c.TrackIndex == track);

        private static int FindFreeTrack(TimelineData tl, bool audio)
        {
            for (int i = 0; i < 4; i++)
                if (!ClipsOn(tl, audio, i).Any()) return i;
            return -1;
        }

        // ---------- timeline rendering ----------

        private void RebuildTrackHeaders()
        {
            if (TrackHeadersStack == null) return;
            TrackHeadersStack.Children.Clear();
            string[] names = { "V4", "V3", "V2", "V1", "A1", "A2", "A3", "A4" };
            foreach (var n in names)
            {
                var b = new TextBlock
                {
                    Text = n,
                    Foreground = n.StartsWith('V') ? Res("BAccent") : Brushes.Green,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 11
                };
                var host = new Border
                {
                    Height = TrackHeight - 2,
                    Width = 60,
                    Background = Res("BTrackHead"),
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(0, 1, 6, 1),
                    Child = b
                };
                TrackHeadersStack.Children.Add(host);
            }
        }

        private void RebuildTimeline()
        {
            if (ClipsCanvas == null) return;
            var tl = Tl;

            // lane backgrounds
            var laneBrushDark = Res("BLaneDark");
            var laneBrushLight = Res("BLaneLight");

            double totalDur = Math.Max(60, tl?.SequenceDuration + 20 ?? 60);
            double width = totalDur * Pps;

            ClipsCanvas.Children.Clear();
            ClipsCanvas.Width = width;

            for (int row = 0; row < 8; row++)
            {
                var lane = new Rectangle
                {
                    Width = width,
                    Height = TrackHeight - 2,
                    Fill = row % 2 == 0 ? laneBrushDark : laneBrushLight,
                    Stroke = Res("BBorder"),
                    StrokeThickness = 0.5
                };
                Canvas.SetLeft(lane, 0);
                Canvas.SetTop(lane, row * TrackHeight + 1);
                ClipsCanvas.Children.Add(lane);
            }

            if (tl != null)
            {
                foreach (var clip in tl.Clips.OrderBy(c => c.TrackKind == TrackKind.Video ? 0 : 1))
                {
                    int row = clip.TrackKind == TrackKind.Video
                        ? (VideoRows - 1 - Math.Clamp(clip.TrackIndex, 0, VideoRows - 1))
                        : VideoRows + Math.Clamp(clip.TrackIndex, 0, 3);

                    var selected = ReferenceEquals(clip, _session.SelectedClip);
                    var handleColor = new SolidColorBrush(Color.Parse(selected ? "#CCFFFFFF" : "#55FFFFFF"));

                    // spec M7 §11: visible left/right trim handles at the clip edges
                    var leftHandle = new Rectangle
                    {
                        Width = 6,
                        Fill = handleColor,
                        IsHitTestVisible = false,
                        Cursor = new Cursor(StandardCursorType.SizeWestEast)
                    };
                    var rightHandle = new Rectangle
                    {
                        Width = 6,
                        Fill = handleColor,
                        IsHitTestVisible = false,
                        Cursor = new Cursor(StandardCursorType.SizeWestEast)
                    };

                    var border = new Border
                    {
                        Height = TrackHeight - 6,
                        MinWidth = 8,
                        CornerRadius = new CornerRadius(2),
                        Background = new SolidColorBrush(clip.TrackKind == TrackKind.Video
                            ? Color.Parse(selected ? "#FF2F81F7" : "#FF27508E")
                            : Color.Parse(selected ? "#FF2FA76B" : "#FF22633F")),
                        BorderThickness = new Thickness(selected ? 1.5 : 0.75),
                        BorderBrush = selected ? Brushes.White : new SolidColorBrush(Color.Parse("#FF444444")),
                        Tag = clip,
                        Cursor = new Cursor(StandardCursorType.SizeAll),
                        Child = new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                            Children =
                            {
                                leftHandle,
                                new TextBlock
                                {
                                    Text = clip.DisplayName,
                                    Foreground = Brushes.White,
                                    FontSize = 10,
                                    Margin = new Thickness(4, 1, 4, 1),
                                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                                    TextTrimming = TextTrimming.CharacterEllipsis
                                },
                                rightHandle
                            }
                        }
                    };

                    Grid.SetColumn(leftHandle, 0);
                    Grid.SetColumn((TextBlock)((Grid)border.Child!).Children[1], 1);
                    Grid.SetColumn(rightHandle, 2);

                    border.PointerPressed += OnClipPointerPressed;
                    border.PointerMoved += OnClipPointerMoved;
                    border.PointerReleased += OnClipPointerReleased;

                    Canvas.SetLeft(border, clip.StartTime * Pps);
                    Canvas.SetTop(border, row * TrackHeight + 3);
                    ClipsCanvas.Children.Add(border);
                }
            }

            // playhead line on top
            var playhead = new Rectangle
            {
                Width = 2,
                Height = 8 * TrackHeight,
                Fill = Brushes.Red,
                IsHitTestVisible = false
            };
            Canvas.SetTop(playhead, 0);
            ClipsCanvas.Children.Add(playhead);
            _playheadRect = playhead;

            PositionPlayhead();
            UpdateTimecode();
            SyncSeekSlider();
            UpdateSequenceInfo();
        }

        private Rectangle? _playheadRect;

        private void PositionPlayhead()
        {
            if (_playheadRect == null) return;
            Canvas.SetLeft(_playheadRect, _session.PlayheadSeconds * Pps);
        }

        private void OnTimelineZoomChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            TimelineViewModel.Zoom = e.NewValue / BasePps;   // zoom 1.0 = 30 px/s
            Dispatcher.UIThread.Post(RebuildTimeline);
        }

        // ---------- timeline interaction ----------

        private void OnTimelinePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var p = e.GetCurrentPoint(ClipsCanvas);
            if (!p.Properties.IsLeftButtonPressed) return;
            _draggingTimeline = true;
            SeekToCanvasX(p.Position.X);
            e.Pointer.Capture(ClipsCanvas);
        }

        private void OnTimelinePointerMoved(object? sender, PointerEventArgs e)
        {
            if (_draggingTimeline && _dragClipBorder == null)
            {
                var p = e.GetCurrentPoint(ClipsCanvas);
                if (p.Properties.IsLeftButtonPressed) SeekToCanvasX(p.Position.X);
            }
        }

        private void OnTimelinePointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_dragClipBorder == null)
            {
                _draggingTimeline = false;
                e.Pointer.Capture(null);
            }
        }

        private void SeekToCanvasX(double x)
        {
            double t = x / Pps;
            _session.SetPlayhead(Math.Max(0, t));
        }

        private void OnClipPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border border || border.Tag is not ClipModel clip) return;
            var p = e.GetCurrentPoint(ClipsCanvas);
            if (!p.Properties.IsLeftButtonPressed) return;

            _dragClipBorder = border;
            _dragClip = clip;
            _dragStartPoint = p.Position;
            _dragOrigStart = clip.StartTime;
            _dragOrigTrack = clip.TrackIndex;
            _draggingTimeline = false;

            // edge zones → trim instead of move
            double x = p.Position.X;
            double w = border.Bounds.Width;
            _trimEdge = x <= EdgeZonePx ? TrimEdge.Left
                      : x >= w - EdgeZonePx ? TrimEdge.Right
                      : TrimEdge.None;

            if (_trimEdge != TrimEdge.None)
            {
                _trimOrigStart = clip.StartTime;
                _trimOrigDuration = clip.Duration;
                _trimOrigIn = clip.InPoint;
                _trimOrigOut = clip.OutPoint;
                _session.SelectClip(clip);
                border.Cursor = new Cursor(_trimEdge == TrimEdge.Left
                    ? StandardCursorType.LeftSide
                    : StandardCursorType.RightSide);
            }
            else
            {
                border.Cursor = new Cursor(StandardCursorType.SizeAll);
            }

            e.Pointer.Capture(border);
            e.Handled = true;
        }

        private void OnClipPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_dragClipBorder == null || _dragClip == null) return;
            var p = e.GetCurrentPoint(ClipsCanvas);
            if (!p.Properties.IsLeftButtonPressed) return;

            double dx = p.Position.X - _dragStartPoint.X;

            if (_trimEdge == TrimEdge.Left)
            {
                double newStart = Math.Max(0, _trimOrigStart + dx / Pps);
                // cannot pass the right edge; leave a minimum sliver
                newStart = Math.Min(newStart, _trimOrigStart + _trimOrigDuration - 0.1);

                // snap to other clips' edges / playhead / start
                newStart = SnapStart(newStart, _trimOrigDuration - (newStart - _trimOrigStart), SnapPoints(_dragClip));

                double delta = newStart - _trimOrigStart;

                // source bounds: InPoint can't go below 0
                if (_trimOrigIn + delta < 0)
                {
                    delta = -_trimOrigIn;
                    newStart = _trimOrigStart + delta;
                }

                // don't overlap the previous clip on this track
                var prevEnd = Tl?.Clips
                    .Where(c => c != _dragClip && c.TrackKind == _dragClip.TrackKind && c.TrackIndex == _dragClip.TrackIndex)
                    .Where(c => c.StartTime < _trimOrigStart)
                    .Select(c => c.StartTime + c.Duration)
                    .DefaultIfEmpty(0)
                    .Max() ?? 0;
                if (newStart < prevEnd) { newStart = prevEnd; delta = newStart - _trimOrigStart; }

                _dragClip.StartTime = newStart;
                _dragClip.Duration = _trimOrigDuration - delta;
                _dragClip.InPoint = _trimOrigIn + delta;

                Canvas.SetLeft(_dragClipBorder, newStart * Pps);
                _dragClipBorder.Width = Math.Max(8, _dragClip.Duration * Pps);
                e.Handled = true;
                return;
            }

            if (_trimEdge == TrimEdge.Right)
            {
                double newEnd = Math.Max(0, _trimOrigStart * Pps + dx) / Pps;   // seconds
                newEnd = Math.Max(newEnd, _trimOrigStart + 0.1);                // minimum sliver

                // snap the clip's end edge
                newEnd = _snapService.Snap(newEnd, SnapPoints(_dragClip));

                double newDur = newEnd - _trimOrigStart;

                // source bounds: OutPoint beyond available media is only allowed for images/titles
                bool still = _dragClip.MediaType is "Images" or "Graphics" or "Title";
                if (!still)
                {
                    double srcLen = SourceLengthOf(_dragClip);
                    if (srcLen > 0)
                        newDur = Math.Min(newDur, srcLen - _trimOrigIn);
                }

                // don't overlap the next clip on this track
                var nextStart = Tl?.Clips
                    .Where(c => c != _dragClip && c.TrackKind == _dragClip.TrackKind && c.TrackIndex == _dragClip.TrackIndex)
                    .Where(c => c.StartTime >= _trimOrigStart + _trimOrigDuration - 0.001 || (c.StartTime > _trimOrigStart && c.StartTime < _trimOrigStart + _trimOrigDuration))
                    .Select(c => (double?)c.StartTime)
                    .Min();
                if (nextStart.HasValue && _trimOrigStart + newDur > nextStart.Value)
                    newDur = nextStart.Value - _trimOrigStart;

                newDur = Math.Max(0.1, newDur);
                _dragClip.Duration = newDur;
                _dragClip.OutPoint = _trimOrigIn + newDur;

                _dragClipBorder.Width = Math.Max(8, newDur * Pps);
                e.Handled = true;
                return;
            }

            // move drag
            double dy = p.Position.Y - _dragStartPoint.Y;
            double newLeft = Math.Max(0, _dragOrigStart * Pps + dx);
            Canvas.SetLeft(_dragClipBorder, newLeft);

            int rowDelta = (int)Math.Round(dy / TrackHeight);
            int origRow = RowOf(_dragClip);
            int newRow = Math.Clamp(origRow + rowDelta, 0, 7);
            Canvas.SetTop(_dragClipBorder, newRow * TrackHeight + 3);
            e.Handled = true;
        }

        /// <summary>Total length of the clip's source media in seconds (probed metadata), or 0 if unknown.</summary>
        private double SourceLengthOf(ClipModel clip)
        {
            var entry = (_session.CurrentProject?.Media ?? new List<MediaEntry>())
                .FirstOrDefault(m => m.RelativePath == clip.SourcePath);
            return entry?.DurationSec ?? 0;
        }

        private void OnClipPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_dragClipBorder == null || _dragClip == null) return;
            var tl = Tl;

            if (_trimEdge != TrimEdge.None)
            {
                // trim already applied live; clamp final values and finish
                if (tl != null)
                {
                    _undo.Push(new Services.TrimClipCommand(_dragClip,
                        _trimOrigStart, _trimOrigDuration, _trimOrigIn, _trimOrigOut,
                        _dragClip.StartTime,
                        Math.Max(0.1, _dragClip.Duration),
                        _dragClip.InPoint,
                        Math.Max(_dragClip.InPoint + 0.1, _dragClip.OutPoint)));
                    _dragClip.Duration = Math.Max(0.1, _dragClip.Duration);
                    _dragClip.OutPoint = Math.Max(_dragClip.InPoint + 0.1, _dragClip.OutPoint);
                    _session.SelectClip(_dragClip);
                    _session.NotifyChanged();
                }
                e.Pointer.Capture(null);
                _dragClipBorder = null;
                _dragClip = null;
                _trimEdge = TrimEdge.None;
                RebuildTimeline();
                return;
            }

            if (tl != null && Canvas.GetLeft(_dragClipBorder) >= 0)
            {
                double newStart = Math.Max(0, Canvas.GetLeft(_dragClipBorder) / Pps);

                // snap to other clips' edges, playhead, timeline start
                newStart = SnapStart(newStart, _dragClip.Duration, SnapPoints(_dragClip));
                Canvas.SetLeft(_dragClipBorder, newStart * Pps);

                int newRow = (int)Math.Round(Canvas.GetTop(_dragClipBorder) / TrackHeight);

                int origRow = RowOf(_dragClip);
                int deltaRow = newRow - origRow;

                int newTrack = _dragClip.TrackIndex;
                bool toVideo = newRow < VideoRows;
                bool wasVideo = _dragClip.TrackKind == TrackKind.Video;

                if (deltaRow != 0)
                {
                    // crossing V/A boundary changes kind only when moving within same section; we clamp moves to same kind
                    int maxIdx = toVideo == wasVideo ? 3 : 0;
                    if (toVideo == wasVideo)
                    {
                        if (wasVideo) newTrack = Math.Clamp((VideoRows - 1 - origRow) + deltaRow, 0, 3);
                        else newTrack = Math.Clamp((origRow - VideoRows) + deltaRow, 0, 3);
                    }
                }

                bool moved = Math.Abs(newStart - _dragOrigStart) > 0.001 || newTrack != _dragOrigTrack;
                if (moved)
                {
                    bool ok = TimelineEngine.MoveTo(tl, _dragClip, newStart, newTrack);
                    if (!ok)
                    {
                        // overlap rejected — snap back
                        Canvas.SetLeft(_dragClipBorder, _dragOrigStart * Pps);
                        Canvas.SetTop(_dragClipBorder, origRow * TrackHeight + 3);
                    }
                    else
                    {
                        _undo.Push(new Services.MoveClipCommand(_dragClip,
                            _dragOrigStart, _dragOrigTrack, _dragClip.StartTime, _dragClip.TrackIndex));
                        _session.SelectClip(_dragClip);
                    }
                }
            }

            e.Pointer.Capture(null);
            _dragClipBorder = null;
            _dragClip = null;
            RebuildTimeline();
        }

        private int RowOf(ClipModel clip) => clip.TrackKind == TrackKind.Video
            ? (VideoRows - 1 - Math.Clamp(clip.TrackIndex, 0, VideoRows - 1))
            : VideoRows + Math.Clamp(clip.TrackIndex, 0, 3);

        // ---------- drag & drop from media library ----------

        private MediaEntry? EntryFromDrop(DragEventArgs e) =>
            e.Data?.Get(MediaDropFormat) is string rel
                ? _session.CurrentProject?.Media.FirstOrDefault(m => m.RelativePath == rel)
                : null;

        private void OnTimelineDragOver(object? sender, DragEventArgs e) =>
            e.DragEffects = EntryFromDrop(e) != null ? DragDropEffects.Copy : DragDropEffects.None;

        private void OnTimelineDrop(object? sender, DragEventArgs e)
        {
            var entry = EntryFromDrop(e);
            var tl = Tl;
            if (entry == null || tl == null || !_session.HasProject) return;

            var p = e.GetPosition(ClipsCanvas);
            double t = Math.Max(0, p.X / Pps);
            int row = (int)Math.Clamp(p.Y / TrackHeight, 0, 7);
            bool toVideo = row < VideoRows;
            int trackIdx = toVideo ? (VideoRows - 1 - row) : (row - VideoRows);

            bool still = entry.MediaType is "Images" or "Graphics";
            double duration = still ? 5.0 : (entry.DurationSec > 0.05 ? entry.DurationSec : 10.0);

            var clip = new ClipModel
            {
                Name = entry.FileName,
                SourcePath = entry.RelativePath,
                MediaType = entry.MediaType,
                TrackKind = toVideo ? TrackKind.Video : TrackKind.Audio,
                TrackIndex = Math.Clamp(trackIdx, 0, 3),
                StartTime = t,
                Duration = duration,
                InPoint = 0,
                OutPoint = duration
            };

            // resolve overlaps: slide right into the first gap that fits
            double pos = clip.StartTime;
            foreach (var c in tl.Clips
                .Where(c => c.TrackKind == clip.TrackKind && c.TrackIndex == clip.TrackIndex)
                .OrderBy(c => c.StartTime))
            {
                if (pos + duration <= c.StartTime + 0.001) break;
                if (pos < c.StartTime + c.Duration)
                    pos = c.StartTime + c.Duration;
            }
            clip.StartTime = pos;

            _undo.Execute(new Services.AddClipCommand(tl, clip));   // undoable
            _session.SetPlayhead(clip.StartTime);
            _session.SelectClip(clip);
            RebuildTimeline();
        }

        private void OnPreviewDragOver(object? sender, DragEventArgs e) =>
            e.DragEffects = EntryFromDrop(e) != null ? DragDropEffects.Copy : DragDropEffects.None;

        private void OnPreviewDrop(object? sender, DragEventArgs e)
        {
            var entry = EntryFromDrop(e);
            if (entry == null) return;
            AddEntryToTimeline(entry);   // appends to the first free track at its end
        }

        // ---------- editing ops ----------

        private void OnSplitClip(object? sender, RoutedEventArgs e)
        {
            var tl = Tl;
            if (tl == null) return;

            var target = _session.SelectedClip;
            if (target == null || !(target.StartTime < _session.PlayheadSeconds && _session.PlayheadSeconds < target.StartTime + target.Duration))
            {
                // find any clip under playhead, prefer video topmost
                target = tl.Clips
                    .Where(c => c.StartTime < _session.PlayheadSeconds && _session.PlayheadSeconds < c.StartTime + c.Duration)
                    .OrderByDescending(c => c.TrackKind == TrackKind.Video ? 1 : 0)
                    .ThenBy(c => c.TrackIndex)
                    .FirstOrDefault();
            }
            if (target == null) return;

            double origStart = target.StartTime, origDur = target.Duration;
            double origIn = target.InPoint, origOut = target.OutPoint;

            var right = TimelineEngine.SplitAt(tl, target, _session.PlayheadSeconds);
            if (right != null)
            {
                _undo.Push(new Services.SplitClipCommand(tl, target, right,
                    origStart, origDur, origIn, origOut));   // engine already applied it
                _session.SelectClip(right);
            }
            else RebuildTimeline();
        }

        private void OnDeleteClip(object? sender, RoutedEventArgs e)
        {
            var tl = Tl;
            var clip = _session.SelectedClip;
            if (tl == null || clip == null) return;
            bool ripple = RippleChk?.IsChecked == true;
            _undo.Execute(new Services.DeleteClipCommand(tl, clip, ripple));
            _session.SelectClip(null);
        }

        private void OnAddTitle(object? sender, RoutedEventArgs e)
        {
            var tl = Tl;
            if (tl == null) return;

            var clip = _session.SelectedClip;
            if (clip == null)
            {
                clip = new ClipModel
                {
                    Name = "Title",
                    SourcePath = "",
                    MediaType = "Title",
                    TrackKind = TrackKind.Video,
                    TrackIndex = 3, // V4 overlay layer
                    StartTime = _session.PlayheadSeconds,
                    Duration = 5,
                    InPoint = 0,
                    OutPoint = 5,
                    Title = NewTitleFromUi()
                };
                tl.Clips.Add(clip);
            }
            else if (clip.Title == null)
            {
                clip.Title = NewTitleFromUi();
            }

            _session.SelectClip(clip);
        }

        private TitleModel NewTitleFromUi() => new()
        {
            Text = string.IsNullOrWhiteSpace(TitleTextBox?.Text) ? "Title" : TitleTextBox.Text!,
            FontFamily = FontCombo?.SelectedItem as string ?? "Inter",
            FontSize = FontSizeB == null ? 42 : Num(FontSizeB),
            Bold = BoldTgl?.IsChecked == true,
            Italic = ItalicTgl?.IsChecked == true,
            Alignment = AlignCombo?.SelectedIndex switch { 0 => "Left", 2 => "Right", _ => "Center" },
            X = 50,
            Y = 50,
            ColorHex = string.IsNullOrWhiteSpace(ColorHexBox?.Text) ? "#FFFFFF" : ColorHexBox.Text!.Trim(),
            Opacity = TOpacitySlider?.Value ?? 100,
            Shadow = ShadowChk?.IsChecked == true,
            Outline = OutlineChk?.IsChecked == true
        };

        // ---------- preview engine ----------

        private void AdvancePlayback()
        {
            double fps = Math.Max(1, _session.CurrentProject?.FrameRate ?? 30);
            // PlaybackEngine owns the clock while playing; push it into the session
            _session.SetPlayhead(Playback.CurrentTime);
            PlaybackVm.UpdateTime(_session.PlayheadSeconds, fps);
            PlaybackVm.IsPlaying = true;
            var dur = Tl?.SequenceDuration ?? 0;
            if (dur > 0 && _session.PlayheadSeconds >= dur) StopPlayback();
        }

        private void Play()
        {
            if (_playTimer == null) return;
            StartEngineClock();
            _playTimer.Start();
            if (PlayBtn != null) PlayBtn.Content = "⏸ Pause";
        }

        private async void StartEngineClock()
        {
            await Playback.SeekAsync(_session.PlayheadSeconds);
            await Playback.PlayAsync();
        }

        private void StopPlayback()
        {
            _playTimer?.Stop();
            Playback.Pause();
            PlaybackVm.IsPlaying = false;
            if (PlayBtn != null) PlayBtn.Content = "▶ Play";
        }

        private void OnPlayPause(object? sender, RoutedEventArgs e)
        {
            if (_playTimer == null) return;
            if (_playTimer.IsEnabled) StopPlayback();
            else Play();
        }

        private void OnGotoStart(object? sender, RoutedEventArgs e) => _session.SetPlayhead(0);
        private void OnGotoEnd(object? sender, RoutedEventArgs e) => _session.SetPlayhead(Tl?.SequenceDuration ?? 0);

        private void OnPrevFrame(object? sender, RoutedEventArgs e)
        {
            double fps = Math.Max(1, _session.CurrentProject?.FrameRate ?? 30);
            _session.MovePlayhead(-1.0 / fps);
            _ = Playback.PreviousFrameAsync();   // keep engine clock aligned
        }

        private void OnNextFrame(object? sender, RoutedEventArgs e)
        {
            double fps = Math.Max(1, _session.CurrentProject?.FrameRate ?? 30);
            _session.MovePlayhead(1.0 / fps);
            _ = Playback.NextFrameAsync();
        }

        private void OnRecordMark(object? sender, RoutedEventArgs e)
        {
            RecordBtn.Content = RecordBtn.Content as string == "●" ? "◼" : "●";
            RecordBtn.Foreground = RecordBtn.Foreground == Brushes.Red ? Brushes.White : Brushes.Red;
        }

        private void OnToggleFullscreen(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is Window w)
                w.WindowState = w.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        }

        private void OnSeekChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_seekSyncing) return;
            _session.SetPlayhead(e.NewValue);
        }

        private void OnPreviewZoomChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (StageBorder == null) return;
            int idx = ZoomCombo.SelectedIndex;
            if (idx <= 0) { SyncPreviewSize(); return; }   // Fit

            var proj = _session.CurrentProject;
            double w = proj?.Width ?? 1920, h = proj?.Height ?? 1080;
            double scale = idx == 1 ? 0.5 : idx == 2 ? 1.0 : 2.0;
            StageBorder.Width = Math.Max(120, w * scale * 0.35);
            StageBorder.Height = StageBorder.Width * h / w;
        }

        private void SyncPreviewSize()
        {
            if (StageBorder == null) return;
            var proj = _session.CurrentProject;
            double w = proj?.Width ?? 1920, h = proj?.Height ?? 1080;
            double targetW = 620;
            StageBorder.Width = targetW;
            StageBorder.Height = targetW * h / w;
        }

        private void RenderPreviewFrame()
        {
            if (PreviewImage == null || VideoPlaceText == null || TitleOverlayText == null) return;
            var tl = Tl;
            double t = _session.PlayheadSeconds;

            var active = tl?.Clips
                .Where(c => c.TrackKind == TrackKind.Video && t >= c.StartTime && t < c.StartTime + c.Duration)
                .OrderByDescending(c => c.TrackIndex)
                .FirstOrDefault();

            if (active == null)
            {
                _previewRenderer?.Clear();
                VideoPlaceText.IsVisible = true;
                VideoPlaceText.Text = tl == null ? "NO PROJECT" : "PROGRAM MONITOR";
                TitleOverlayText.IsVisible = false;
                DipOverlayRect.Opacity = 0;
                return;
            }

            // base visual
            var srcPath = _session.ResolvePath(active.SourcePath);
            Bitmap? bmp = null;
            if (!string.IsNullOrEmpty(srcPath))
            {
                if (active.MediaType == "Images" || active.MediaType == "Graphics")
                {
                    bmp = GetBitmap(srcPath!);
                }
                else if (active.MediaType == "Video")
                {
                    // real frame at the playhead, extracted by ffmpeg (async, cached)
                    // timeline position → source timestamp via SourceTimeCalculator
                    double sourceTime = MirrorStudio.Timeline.Services.SourceTimeCalculator
                        .GetSourceTime(t, active.StartTime, active.InPoint);

                    // preview quality → decode height (export always uses originals)
                    int? decodeHeight = null;
                    var entry = (_session.CurrentProject?.Media ?? new List<MediaEntry>())
                        .FirstOrDefault(m => m.RelativePath == active.SourcePath);
                    if (entry is { Height: > 0 })
                        decodeHeight = MirrorStudio.Core.Enums.PreviewQualityExtensions.DecodeHeight(_previewQuality, entry.Height);

                    bmp = GetOrRequestVideoFrame(srcPath!, sourceTime, decodeHeight);
                }
                else if (!string.IsNullOrEmpty(active.SourcePath))
                {
                    // audio/title-less fallback: show thumbnail poster if we have one
                    var entry = (_session.CurrentProject?.Media ?? new List<MediaEntry>())
                        .FirstOrDefault(m => m.RelativePath == active.SourcePath);
                    if (entry != null && !string.IsNullOrEmpty(entry.ThumbnailRelativePath))
                    {
                        var thumbAbs = _session.ResolvePath(entry.ThumbnailRelativePath);
                        if (thumbAbs != null) bmp = GetBitmap(thumbAbs);
                    }
                }
            }

            if (bmp != null)
            {
                _previewRenderer?.Render(bmp);
                VideoPlaceText.IsVisible = false;
            }
            else
            {
                _previewRenderer?.Clear();
                VideoPlaceText.IsVisible = true;
                VideoPlaceText.Text = active.MediaType == "Title" ? active.Title?.Text ?? "" : $"▶ {active.Name}\n(extracting frame...)";
            }

            // transform
            var group = new TransformGroup();
            group.Children.Add(new TranslateTransform(active.PositionX, active.PositionY));
            group.Children.Add(new ScaleTransform(active.ScaleX / 100.0, active.ScaleY / 100.0));
            group.Children.Add(new RotateTransform(active.Rotation));
            PreviewImage.RenderTransform = group;
            VideoPlaceText.RenderTransform = group;

            // opacity + transitions
            double opacity = active.Opacity / 100.0;
            double dipOverlay = 0;
            Color dipColor = Colors.Black;

            if (active.TransitionIn.Kind != TransitionKind.Cut)
            {
                var prev = tl!.Clips.FirstOrDefault(c => c.TrackKind == TrackKind.Video
                    && c.TrackIndex == active.TrackIndex
                    && Math.Abs(c.StartTime + c.Duration - active.StartTime) < 0.001);

                if (prev != null)
                {
                    double window = Math.Min(1.0, active.Duration / 2.0);
                    if (window > 0.01 && t < active.StartTime + window)
                    {
                        double prog = (t - active.StartTime) / window;
                        switch (active.TransitionIn.Kind)
                        {
                            case TransitionKind.Fade:
                            case TransitionKind.CrossDissolve:
                                opacity *= Math.Clamp(prog, 0, 1);
                                break;
                            case TransitionKind.DipToBlack:
                                dipColor = Colors.Black;
                                dipOverlay = Math.Clamp(1 - prog * 2, 0, 1) * 0.9;
                                break;
                            case TransitionKind.DipToWhite:
                                dipColor = Colors.White;
                                dipOverlay = Math.Clamp(1 - prog * 2, 0, 1) * 0.9;
                                break;
                            case TransitionKind.Wipe:
                                opacity *= Math.Clamp(prog, 0, 1);
                                break;
                        }
                    }
                }
            }

            // audio fades affect nothing visually but keep model live
            PreviewImage.Opacity = opacity;
            VideoPlaceText.Opacity = Math.Max(opacity, 0.15);
            DipOverlayRect.Fill = new SolidColorBrush(dipColor);
            DipOverlayRect.Opacity = dipOverlay;

            // title overlay
            if (active.Title != null && !string.IsNullOrWhiteSpace(active.Title.Text))
            {
                var title = active.Title;
                TitleOverlayText.Text = title.Text;
                TitleOverlayText.FontFamily = new FontFamily(title.FontFamily);
                TitleOverlayText.FontSize = title.FontSize;
                TitleOverlayText.FontWeight = title.Bold ? FontWeight.Bold : FontWeight.Normal;
                TitleOverlayText.FontStyle = title.Italic ? FontStyle.Italic : FontStyle.Normal;
                TitleOverlayText.HorizontalAlignment = title.Alignment switch
                {
                    "Left" => Avalonia.Layout.HorizontalAlignment.Left,
                    "Right" => Avalonia.Layout.HorizontalAlignment.Right,
                    _ => Avalonia.Layout.HorizontalAlignment.Center
                };
                try { TitleOverlayText.Foreground = new SolidColorBrush(Color.Parse(title.ColorHex)); }
                catch { TitleOverlayText.Foreground = Brushes.White; }
                TitleOverlayText.Opacity = Math.Clamp(title.Opacity / 100.0, 0, 1) * opacity;
                if (title.Shadow)
                    TitleOverlayText.Effect = new DropShadowEffect { BlurRadius = 8, OffsetX = 2, OffsetY = 2, Opacity = 0.8 };
                else if (title.Outline)
                    TitleOverlayText.Effect = new DropShadowEffect { BlurRadius = 2, OffsetX = 0, OffsetY = 0, Opacity = 1, Color = Colors.Black };
                else
                    TitleOverlayText.Effect = null;
                TitleOverlayText.IsVisible = true;
            }
            else
            {
                TitleOverlayText.IsVisible = false;
            }

            // blur effect live approximation
            double blurSigma = active.Effects.Where(e => e.Type == "Blur").Sum(e => e.Amount);
            PreviewImage.Effect = blurSigma > 0.05 ? new BlurEffect { Radius = blurSigma } : null;
        }

        private Bitmap? GetBitmap(string path)
        {
            if (_bitmapCache.TryGetValue(path, out var cached)) return cached;
            try
            {
                using var fs = File.OpenRead(path);
                var bmp = new Bitmap(fs);
                _bitmapCache[path] = bmp;
                return bmp;
            }
            catch
            {
                _bitmapCache[path] = null;
                return null;
            }
        }

        // ---------- async video frame extraction ----------

        private const double FrameGranularity = 0.25;   // seconds between cached frames
        private const int MaxCachedFrames = 240;

        // preview quality (Full/Half/Quarter/Proxy) — export always uses originals
        private MirrorStudio.Core.Enums.PreviewQuality _previewQuality = MirrorStudio.Core.Enums.PreviewQuality.Full;

        // one decoder per media file, reused across frames (probe once, decode many)
        private readonly Dictionary<string, MirrorStudio.Video.Services.FfmpegVideoDecoder> _decoders = new();

        public void OnPreviewQualityChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (QualityCombo == null) return;
            _previewQuality = (MirrorStudio.Core.Enums.PreviewQuality)Math.Clamp(QualityCombo.SelectedIndex, 0, 3);
            _frameCache.Clear();   // frames at the old scale are stale
            RenderPreviewFrame();
        }

        private Bitmap? GetOrRequestVideoFrame(string absPath, double sourceTime) =>
            GetOrRequestVideoFrame(absPath, sourceTime, null);

        private Bitmap? GetOrRequestVideoFrame(string absPath, double sourceTime, int? decodeHeight)
        {
            double snapped = Math.Max(0, Math.Round(sourceTime / FrameGranularity) * FrameGranularity);
            string key = $"{absPath}|{snapped.ToString("0.###", CultureInfo.InvariantCulture)}|{decodeHeight ?? 0}";

            if (_frameCache.TryGetValue(key, out var cached))
                return cached;

            RequestFrameExtraction(absPath, snapped, key, decodeHeight);

            // while the frame extracts, show nearest cached frame if any
            foreach (var offset in new[] { FrameGranularity, -FrameGranularity, 2 * FrameGranularity })
            {
                var nearKey = $"{absPath}|{(snapped + offset).ToString("0.###", CultureInfo.InvariantCulture)}|{decodeHeight ?? 0}";
                if (_frameCache.TryGetValue(nearKey, out var near) && near != null) return near;
            }
            return null;
        }

        private async Task<MirrorStudio.Video.Services.FfmpegVideoDecoder> GetDecoderAsync(string absPath)
        {
            var ffmpegExe = MirrorStudio.Video.Services.MediaEngineLocator.ResolveFfmpeg(_session.Settings.FFmpegPath);
            var ffprobeExe = MirrorStudio.Video.Services.MediaEngineLocator.ResolveFfprobe(_session.Settings.FFprobePath);

            if (!_decoders.TryGetValue(absPath, out var dec))
            {
                dec = new MirrorStudio.Video.Services.FfmpegVideoDecoder(ffmpegExe, ffprobeExe);
                await dec.OpenAsync(absPath);   // probes stream info once
                _decoders[absPath] = dec;
            }
            return dec;
        }

        /// <summary>Throttled single-frame decode: dedup per key, off UI thread.</summary>
        private void RequestFrameExtraction(string absPath, double sourceTime, string key, int? decodeHeight)
        {
            if (!_framesInFlight.Add(key)) return;   // scrubbing throttle: one request per frame slot
            _pendingFrameKey = key;

            _ = Task.Run(async () =>
            {
                Bitmap? bmp = null;
                try
                {
                    var decoder = await GetDecoderAsync(absPath);
                    decoder.DecodeHeight = decodeHeight;

                    var frame = await decoder.DecodeFrameAsync(sourceTime);
                    if (frame != null) bmp = FrameToBitmap(frame);
                }
                catch { bmp = null; }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _framesInFlight.Remove(key);
                    if (bmp != null)
                    {
                        if (_frameCache.Count >= MaxCachedFrames)
                        {
                            foreach (var stale in _frameCache.Take(MaxCachedFrames / 4).ToList())
                                _frameCache.Remove(stale.Key);
                        }
                        _frameCache[key] = bmp;
                        RenderPreviewFrame();   // repaint now that the frame is ready
                    }
                    else
                    {
                        _frameCache[key] = null;    // negative cache: engine missing or bad seek
                    }
                });
            });
        }

        /// <summary>BGRA bytes → Avalonia WriteableBitmap (zero-copy lock blit).</summary>
        private static Bitmap? FrameToBitmap(MirrorStudio.Rendering.Models.VideoFrame f)
        {
            try
            {
                var wb = new WriteableBitmap(
                    new PixelSize(f.Width, f.Height),
                    new Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Bgra8888);

                unsafe
                {
                    fixed (byte* src = f.Data)
                    {
                        using var l = wb.Lock();
                        int rowBytes = Math.Min(f.Stride, l.RowBytes);
                        for (int y = 0; y < f.Height; y++)
                        {
                            Buffer.MemoryCopy(src + (long)y * f.Stride,
                                (void*)IntPtr.Add(l.Address, y * l.RowBytes),
                                rowBytes, rowBytes);
                        }
                    }
                }
                return wb;
            }
            catch
            {
                return null;
            }
        }

        // ---------- inspector sync ----------

        private bool _inspectorAttached;

        private void DetachAllHandlers() { /* handlers attached once in ctor below */ }

        private void AttachInspectorHandlers()
        {
            _inspectorAttached = true;
        }

        private void OnInspNum(object? sender, NumericUpDownValueChangedEventArgs e) => ApplyInspectorToClip();
        private void OnInspRange(object? sender, RangeBaseValueChangedEventArgs e) => ApplyInspectorToClip();
        private void OnInspCheck(object? sender, RoutedEventArgs e) => ApplyInspectorToClip();

        private static double Num(NumericUpDown n) => (double)(n.Value ?? 0);
        private static void SetNum(NumericUpDown n, double v) => n.Value = (decimal)v;

        private void OnAppliedEffectSelected(object? sender, SelectionChangedEventArgs e)
        {
            var fx = SelectedEffectModel();
            if (fx != null && EffectAmountSlider != null && !_syncing)
            {
                _syncing = true;
                EffectAmountSlider.Value = Math.Clamp(fx.Amount, EffectAmountSlider.Minimum, EffectAmountSlider.Maximum);
                EffectAmountText.Text = $"Amount: {fx.Amount:0}%";
                _syncing = false;
            }
        }

        private EffectModel? SelectedEffectModel()
        {
            int idx = AppliedEffectsList?.SelectedIndex ?? -1;
            var clip = _session.SelectedClip;
            if (idx < 0 || clip?.Effects == null) return null;
            var list = clip.Effects.ToList();
            return idx < list.Count ? list[idx] : null;
        }

        private void OnEffectAmountChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (EffectAmountText != null)
                EffectAmountText.Text = $"Amount: {e.NewValue:0}%";
            if (_syncing) return;
            var fx = SelectedEffectModel();
            if (fx != null)
            {
                fx.Amount = e.NewValue;
                RenderPreviewFrame();
                RebuildEffectsList(false);
            }
        }

        private void RebuildEffectsList(bool resetSelection = true)
        {
            var clip = _session.SelectedClip;
            AppliedEffectsList.ItemsSource = clip?.Effects?
                .Select(fx => fx.Type == "Lut" && fx.Settings.TryGetValue("path", out var p)
                    ? $"Lut: {System.IO.Path.GetFileName(p)}"
                    : $"{fx.Type}: {fx.Amount:0}%")
                .ToList() ?? new List<string>();
            if (resetSelection) AppliedEffectsList.SelectedIndex = -1;
        }

        private void OnAddEffect(object? sender, RoutedEventArgs e)
        {
            var clip = _session.SelectedClip;
            if (clip == null) { PromptSelectClip(); return; }
            string display = (EffectCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Blur";
            string type = display switch
            {
                "LUT" => "Lut",
                "Chroma Key" => "ChromaKey",
                _ => display,
            };

            var fx = new EffectModel { Type = type, Amount = EffectAmountSlider.Value };

            if (type == "Lut")
            {
                var dialogs = new Services.DialogService(TopLevel.GetTopLevel(this) as Window
                    ?? throw new InvalidOperationException("No top-level window"));
                var path = dialogs.PickFileAsync("Select LUT (.cube)",
                    ("Cube LUT", ["cube"])).GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(path)) return;
                fx.Settings["path"] = path;
            }

            clip.Effects.Add(fx);
            RebuildEffectsList();
            AppliedEffectsList.SelectedIndex = clip.Effects.Count - 1;
            RenderPreviewFrame();
        }

        private void OnRemoveEffect(object? sender, RoutedEventArgs e)
        {
            var fx = SelectedEffectModel();
            var clip = _session.SelectedClip;
            if (fx == null || clip == null) return;
            clip.Effects.Remove(fx);
            RebuildEffectsList();
            RenderPreviewFrame();
        }

        private void TransitionsList_SelectionChanged(object? sender, SelectionChangedEventArgs e) { /* applied on button */ }

        private void OnApplyTransition(object? sender, RoutedEventArgs e)
        {
            var clip = _session.SelectedClip;
            if (clip == null) { PromptSelectClip(); return; }
            string name = (TransitionsList.SelectedItem as ListBoxItem)?.Content as string ?? "Cut";
            double duration = 0.5;
            if (TransitionDurationBox != null &&
                !double.TryParse(TransitionDurationBox.Text, System.Globalization.CultureInfo.InvariantCulture, out duration) ||
                duration <= 0)
                duration = 0.5;

            clip.TransitionIn = name switch
            {
                "Fade" => new ClipTransition { Kind = TransitionKind.Fade },
                "Cross Dissolve" => new ClipTransition { Kind = TransitionKind.CrossDissolve },
                "Dip to Black" => new ClipTransition { Kind = TransitionKind.DipToBlack },
                "Dip to White" => new ClipTransition { Kind = TransitionKind.DipToWhite },
                "Wipe" => new ClipTransition { Kind = TransitionKind.Wipe },
                "Slide" => new ClipTransition { Kind = TransitionKind.Slide },
                "Zoom" => new ClipTransition { Kind = TransitionKind.Zoom },
                _ => null
            };

            if (clip.TransitionIn is { } t)
                t.DurationSeconds = duration;

            RenderPreviewFrame();
        }

        private void OnApplyTitle(object? sender, RoutedEventArgs e)
        {
            var clip = _session.SelectedClip;
            if (clip == null) { PromptSelectClip(); return; }
            clip.Title = NewTitleFromUi();
            RenderPreviewFrame();
        }

        private void OnRemoveTitle(object? sender, RoutedEventArgs e)
        {
            var clip = _session.SelectedClip;
            if (clip == null) return;
            clip.Title = null;
            RenderPreviewFrame();
        }

        private void PromptSelectClip()
        {
            MessageBox.Show(TopLevel.GetTopLevel(this) as Window, "No Clip",
                "Select a clip on the timeline first.");
        }

        private void PushSelectionToInspector()
        {
            if (PosXB == null) return;
            var clip = _session.SelectedClip;
            _syncing = true;

            InspNameText.Text = clip == null ? "-" : clip.DisplayName;
            RebuildEffectsList();

            if (clip != null)
            {
                SetNum(PosXB, clip.PositionX); SetNum(PosYB, clip.PositionY);
                SetNum(ScaleXB, clip.ScaleX); SetNum(ScaleYB, clip.ScaleY);
                SetNum(RotB, clip.Rotation);
                OpacitySlider.Value = clip.Opacity;
                VolSlider.Value = clip.Volume; PanSlider.Value = clip.Pan;
                SetNum(GainDbB, clip.GainDb); SetNum(FadeInB, clip.FadeIn); SetNum(FadeOutB, clip.FadeOut);
                MuteChk.IsChecked = clip.IsMuted; SoloChk.IsChecked = clip.IsSolo;

                if (TransitionsList != null)
                {
                    string display = clip.TransitionIn?.Kind switch
                    {
                        TransitionKind.Fade => "Fade",
                        TransitionKind.CrossDissolve => "Cross Dissolve",
                        TransitionKind.DipToBlack => "Dip to Black",
                        TransitionKind.DipToWhite => "Dip to White",
                        TransitionKind.Wipe => "Wipe",
                        TransitionKind.Slide => "Slide",
                        TransitionKind.Zoom => "Zoom",
                        _ => "Cut",
                    };
                    TransitionsList.SelectedIndex = -1;
                    var items = TransitionsList.Items.ToList();
                    for (int i = 0; i < items.Count; i++)
                    {
                        if ((items[i] as ListBoxItem)?.Content as string == display)
                        {
                            TransitionsList.SelectedIndex = i;
                            break;
                        }
                    }

                    if (TransitionDurationBox != null)
                        TransitionDurationBox.Text =
                            (clip.TransitionIn?.DurationSeconds ?? 0.5).ToString("0.##");
                }
                if (clip.Title != null)
                {
                    TitleTextBox.Text = clip.Title.Text;
                    FontCombo.SelectedItem = clip.Title.FontFamily;
                    SetNum(FontSizeB, clip.Title.FontSize);
                    BoldTgl.IsChecked = clip.Title.Bold;
                    ItalicTgl.IsChecked = clip.Title.Italic;
                    AlignCombo.SelectedIndex = clip.Title.Alignment switch { "Left" => 0, "Right" => 2, _ => 1 };
                    ColorHexBox.Text = clip.Title.ColorHex;
                    TOpacitySlider.Value = clip.Title.Opacity;
                    ShadowChk.IsChecked = clip.Title.Shadow;
                    OutlineChk.IsChecked = clip.Title.Outline;
                }
            }
            else
            {
                SetNum(PosXB, 0); SetNum(PosYB, 0); SetNum(ScaleXB, 100); SetNum(ScaleYB, 100);
                SetNum(RotB, 0); OpacitySlider.Value = 100;
                VolSlider.Value = 100; PanSlider.Value = 0;
                SetNum(GainDbB, 0); SetNum(FadeInB, 0); SetNum(FadeOutB, 0);
                MuteChk.IsChecked = false; SoloChk.IsChecked = false;
            }
            _syncing = false;
        }

        private void ApplyInspectorToClip()
        {
            if (_syncing) return;
            var clip = _session.SelectedClip;
            if (clip == null || PosXB == null) return;

            // snapshot → apply → record as one undoable step
            var cmd = new Services.PropertySetCommand(clip)
                .Track(nameof(ClipModel.PositionX), clip.PositionX, Num(PosXB))
                .Track(nameof(ClipModel.PositionY), clip.PositionY, Num(PosYB))
                .Track(nameof(ClipModel.ScaleX), clip.ScaleX, Num(ScaleXB))
                .Track(nameof(ClipModel.ScaleY), clip.ScaleY, Num(ScaleYB))
                .Track(nameof(ClipModel.Rotation), clip.Rotation, Num(RotB))
                .Track(nameof(ClipModel.Opacity), clip.Opacity, OpacitySlider.Value)
                .Track(nameof(ClipModel.Volume), clip.Volume, VolSlider.Value)
                .Track(nameof(ClipModel.Pan), clip.Pan, PanSlider.Value)
                .Track(nameof(ClipModel.GainDb), clip.GainDb, Num(GainDbB))
                .Track(nameof(ClipModel.FadeIn), clip.FadeIn, Num(FadeInB))
                .Track(nameof(ClipModel.FadeOut), clip.FadeOut, Num(FadeOutB))
                .TrackFlag(nameof(ClipModel.IsMuted), clip.IsMuted, MuteChk.IsChecked == true)
                .TrackFlag(nameof(ClipModel.IsSolo), clip.IsSolo, SoloChk.IsChecked == true);

            cmd.Execute();
            if (cmd.HasChanges) _undo.Push(cmd);

            RenderPreviewFrame();
        }

        // ---------- keyboard shortcuts (professional JKL editing) ----------

        private static bool IsTextEntryFocus(Avalonia.Input.IInputElement? c) =>
            c is TextBox or NumericUpDown or ComboBox or AutoCompleteBox;

        private void OnEditorKeyDown(object? sender, KeyEventArgs e)
        {
            if (IsTextEntryFocus(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement())) return;
            if (Services.MediaCardFactory.IsDragging) return;   // mid library-drag

            var tl = Tl;
            double fps = Math.Max(1, _session.CurrentProject?.FrameRate ?? 30);

            // undo/redo first — Ctrl+Z, Ctrl+Shift+Z (and Ctrl+Y)
            if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) RedoEdit();
                else UndoEdit();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Y && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                RedoEdit();
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.Space:
                    OnPlayPause(this, new RoutedEventArgs());
                    break;

                case Key.K:   // pause
                    StopPlayback();
                    break;

                case Key.J:   // reverse shuttle: jump back 1s
                    StopPlayback();
                    _session.MovePlayhead(-1.0);
                    break;

                case Key.L:   // forward shuttle: jump forward 1s
                    StopPlayback();
                    _session.MovePlayhead(+1.0);
                    break;

                case Key.S:
                    SplitAtPlayhead();
                    break;

                case Key.Delete:
                case Key.Back:
                    if (_session.SelectedClip != null) OnDeleteClip(this, new RoutedEventArgs());
                    break;

                case Key.Left:
                    StopPlayback();
                    _session.MovePlayhead(-1.0 / fps);
                    _ = Playback.PreviousFrameAsync();
                    break;

                case Key.Right:
                    StopPlayback();
                    _session.MovePlayhead(1.0 / fps);
                    _ = Playback.NextFrameAsync();
                    break;

                case Key.Home:
                    _session.SetPlayhead(0);
                    break;

                case Key.End:
                    _session.SetPlayhead(tl?.SequenceDuration ?? 0);
                    break;

                case Key.I when e.KeyModifiers == KeyModifiers.None && _session.SelectedClip is { } cin:
                {
                    // set in-point at playhead (trim from the left)
                    double t = Math.Clamp(_session.PlayheadSeconds,
                        cin.StartTime + 0.05, cin.StartTime + cin.Duration - 0.1);
                    var cmd = new Services.TrimClipCommand(cin,
                        cin.StartTime, cin.Duration, cin.InPoint, cin.OutPoint,
                        t, cin.StartTime + cin.Duration - t,
                        cin.InPoint + (t - cin.StartTime), cin.OutPoint);
                    cmd.Execute();
                    _undo.Push(cmd);
                    RebuildTimeline();
                    break;
                }

                case Key.O when e.KeyModifiers == KeyModifiers.None && _session.SelectedClip is { } cout:
                {
                    // set out-point at playhead (trim from the right)
                    double dur = Math.Max(0.1, _session.PlayheadSeconds - cout.StartTime);
                    var cmd = new Services.TrimClipCommand(cout,
                        cout.StartTime, cout.Duration, cout.InPoint, cout.OutPoint,
                        cout.StartTime, dur, cout.InPoint, cout.InPoint + dur);
                    cmd.Execute();
                    _undo.Push(cmd);
                    RebuildTimeline();
                    break;
                }

                default:
                    return;   // not handled — let it bubble
            }
            e.Handled = true;
        }

        /// <summary>Edit ▸ Undo / Ctrl+Z entry point.</summary>
        public bool UndoEdit()
        {
            if (!_undo.CanUndo) return false;
            _undo.Undo();
            var sel = _session.SelectedClip;
            if (sel != null && Tl != null && !Tl.Clips.Contains(sel)) _session.SelectClip(null);
            RebuildTimeline();
            RenderPreviewFrame();
            return true;
        }

        /// <summary>Edit ▸ Redo / Ctrl+Shift+Z / Ctrl+Y entry point.</summary>
        public bool RedoEdit()
        {
            if (!_undo.CanRedo) return false;
            _undo.Redo();
            RebuildTimeline();
            RenderPreviewFrame();
            return true;
        }

        public bool CanUndoEdit => _undo.CanUndo;
        public bool CanRedoEdit => _undo.CanRedo;

        // ---------- info ----------

        private void UpdateTimecode()
        {
            PlaybackVm.UpdateTime(_session.PlayheadSeconds, _session.CurrentProject?.FrameRate ?? 30);
            TimecodeText.Text = PlaybackVm.TimeDisplay;
        }

        private static string FormatTimecode(double seconds, double fps)
        {
            if (seconds < 0) seconds = 0;
            int totalFrames = (int)Math.Round(seconds * fps);
            int f = totalFrames % (int)Math.Max(1, Math.Round(fps));
            int s = (int)(seconds % 60);
            int m = (int)(seconds / 60) % 60;
            int h = (int)(seconds / 3600);
            return $"{h:00}:{m:00}:{s:00}:{f:00}";
        }

        private void UpdateSequenceInfo()
        {
            var proj = _session.CurrentProject;
            var tl = Tl;
            InspSeqText.Text = proj == null
                ? "No project loaded"
                : $"{proj.Width}×{proj.Height} @ {proj.FrameRate:0.###} fps ({proj.AspectRatio})\n" +
                  $"Tracks: {TimelineData.VideoTrackCount}V + {TimelineData.AudioTrackCount}A\n" +
                  $"Clips: {tl?.Clips.Count ?? 0}   Duration: {(tl?.SequenceDuration ?? 0):0.0#}s\n" +
                  $"Export default: {proj.DefaultExportFormat}";
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            StopPlayback();
            _session.ProjectChanged -= OnProjectChangedInternal;
            _session.PlayheadChanged -= OnPlayheadChangedInternal;
            _session.SelectionChanged -= OnSelectionChangedInternal;
            base.OnDetachedFromVisualTree(e);
        }
    }
}



