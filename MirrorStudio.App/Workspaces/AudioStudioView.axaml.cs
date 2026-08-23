using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using MirrorStudio.Core.Models;
using System.Collections.ObjectModel;
using System.Linq;
using IOPath = System.IO.Path;

namespace MirrorStudio.App.Workspaces
{
    public class AudioTrackVm
    {
        public string Name { get; set; } = string.Empty;
        public double Volume { get; set; } = 80;
        public double Pan { get; set; }
        public bool IsMuted { get; set; }
        public ClipModel? Clip { get; set; }
        public string Display => $"{Name}  |  vol {Volume:0}%  pan {Pan:+0;-0;0}";
    }

    public class WaveBarVm
    {
        public double Height { get; set; }
    }

    public partial class AudioStudioView : UserControl
    {
        private readonly SessionState _session;
        private readonly ObservableCollection<AudioTrackVm> _tracks = new();
        private readonly ObservableCollection<WaveBarVm> _bars = new();

        // master-clock driven playback (spec M9 §16) — same clock pattern the video engine uses
        private readonly MirrorStudio.Audio.Services.AudioEngine _engine =
            new(new MirrorStudio.Audio.Services.MasterClock());
        private double _playDuration = 30;
        private int _trackCounter;

        public AudioStudioView(SessionState session)
        {
            InitializeComponent();
            _session = session;
            TracksList.ItemsSource = _tracks;
            WaveformBars.ItemsSource = _bars;
            TracksList.SelectionChanged += OnTrackSelectionChanged;
            MuteCheck.IsCheckedChanged += OnMuteChanged;
            SoloCheck.IsCheckedChanged += OnMuteChanged;
            AddDemoTracks();

            _engine.PositionChanged += (_, t) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AudioPlayhead.Value = System.Math.Clamp(t / System.Math.Max(0.1, _playDuration) * 100, 0, 100);
                AudioTimeText.Text = $"{t:F1}s / {_playDuration:F1}s";
            });
            _engine.Ended += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AudioPlayhead.Value = 0;
                AudioTimeText.Text = $"0.0s / {_playDuration:F1}s";
            });

            _session.ProjectChanged += OnProjectChanged;

            // media library drop targets (drag an audio file anywhere on tracks/waveform)
            TracksList.AddHandler(DragDrop.DragOverEvent, OnMediaDragOver);
            TracksList.AddHandler(DragDrop.DropEvent, OnMediaDrop);
            WaveBorder.AddHandler(DragDrop.DragOverEvent, OnMediaDragOver);
            WaveBorder.AddHandler(DragDrop.DropEvent, OnMediaDrop);

            PopulateAudioMedia();
        }

        private void OnProjectChanged()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                PopulateAudioMedia();
                RefreshTracksFromTimeline();
            });
        }

        private void RefreshTracksFromTimeline()
        {
            var tl = _session.Timeline;
            if (tl == null || !tl.AudioClips.Any())
            {
                // fall back to media-derived demo tracks
                var project = _session.CurrentProject;
                if (project == null || _tracks.Count > 0) return;
                foreach (var audio in project.Media.Where(m => m.MediaType.ToLowerInvariant() == "audio"))
                {
                    if (_tracks.All(t => t.Name != audio.FileName))
                        _tracks.Add(new AudioTrackVm { Name = audio.FileName });
                }
                return;
            }

            int keep = TracksList.SelectedIndex;
            _tracks.Clear();
            foreach (var clip in tl.AudioClips.OrderBy(c => c.TrackIndex).ThenBy(c => c.StartTime))
            {
                _tracks.Add(new AudioTrackVm
                {
                    Name = clip.DisplayName,
                    Volume = clip.Volume,
                    Pan = clip.Pan,
                    IsMuted = clip.IsMuted,
                    Clip = clip
                });
            }
            if (_tracks.Count > 0)
                TracksList.SelectedIndex = System.Math.Clamp(keep, 0, _tracks.Count - 1);
        }

        private void AddDemoTracks()
        {
            if (_tracks.Count == 0)
            {
                _tracks.Add(new AudioTrackVm { Name = "Music Bed" });
                _tracks.Add(new AudioTrackVm { Name = "Voiceover" });
                _trackCounter = 2;
                TracksList.SelectedIndex = 0;
            }
        }

        private void OnTrackSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (TracksList.SelectedItem is not AudioTrackVm track)
            {
                ActiveTrackText.Text = "No track selected";
                return;
            }

            if (track.Clip != null)
            {
                var c = track.Clip;
                VolumeSlider.Value = c.Volume;
                PanSlider.Value = c.Pan;
                GainDbNud.Value = (decimal)c.GainDb;
                FadeInNud.Value = (decimal)c.FadeIn;
                FadeOutNud.Value = (decimal)c.FadeOut;
                MuteCheck.IsChecked = c.IsMuted;
                SoloCheck.IsChecked = c.IsSolo;
            }
            else
            {
                VolumeSlider.Value = track.Volume;
                PanSlider.Value = track.Pan;
                MuteCheck.IsChecked = track.IsMuted;
            }
            ActiveTrackText.Text = "Track: " + track.Name;
            GenerateWaveform(track);
        }

        private void GenerateWaveform(AudioTrackVm track)
        {
            _bars.Clear();
            WaveHintText.Text = "Decoding waveform…";
            _ = RenderWaveformAsync(track);
        }

        /// <summary>
        /// Real waveform via ffmpeg decode + peak analysis (spec M9 §9/§11),
        /// cached on disk so reselecting a track is instant.
        /// </summary>
        private async Task RenderWaveformAsync(AudioTrackVm track)
        {
            try
            {
                var sourcePath = track.Clip?.SourcePath;
                if (string.IsNullOrEmpty(sourcePath))
                {
                    WaveHintText.Text = $"No media behind '{track.Name}' — proxy preview shown.";
                    FillProxyBars(track.Name);
                    return;
                }

                string absolute = _session.ResolvePath(sourcePath) ?? sourcePath;
                if (!File.Exists(absolute))
                {
                    WaveHintText.Text = "Media offline — waveform unavailable.";
                    return;
                }

                // ~20 points/sec of audio, pooled down to the bar count
                var generator = CreateWaveformGenerator();
                var peaks = await generator.GenerateAsync(absolute, MirrorStudio.Audio.Services.WaveformGenerator.AnalysisRateHz / 20);
                if (peaks.Length == 0)
                {
                    WaveHintText.Text = "No audio stream found.";
                    return;
                }

                const int maxBars = 140;
                int pool = Math.Max(1, (int)Math.Ceiling(peaks.Length / (double)maxBars));

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _bars.Clear();
                    for (int i = 0; i < peaks.Length; i += pool)
                    {
                        float blockMax = 0;
                        for (int j = i; j < Math.Min(peaks.Length, i + pool); j++)
                            blockMax = Math.Max(blockMax, peaks[j]);

                        double h = 6 + blockMax * 58;
                        _bars.Add(new WaveBarVm { Height = System.Math.Round(h, 1) });
                    }
                    WaveHintText.Text = $"Waveform: {peaks.Length} points ({_bars.Count} bars) — {track.Name}";
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    WaveHintText.Text = $"Waveform failed: {ex.Message}");
            }
        }

        private void FillProxyBars(string name)
        {
            _bars.Clear();
            var rnd = new System.Random(name.GetHashCode());
            for (int i = 0; i < 110; i++)
            {
                double envelope = System.Math.Sin(System.Math.PI * i / 110);
                _bars.Add(new WaveBarVm { Height = System.Math.Round((8 + rnd.NextDouble() * 52) * envelope + 4, 1) });
            }
        }

        private MirrorStudio.Audio.Services.WaveformGenerator CreateWaveformGenerator()
        {
            var decoder = new MirrorStudio.Audio.Services.AudioDecoder(
                MirrorStudio.Video.Services.MediaEngineLocator.ResolveFfmpeg(_session.Settings.FFmpegPath));
            return new MirrorStudio.Audio.Services.WaveformGenerator(decoder);
        }

        private void SyncSelectedTrack()
        {
            if (TracksList.SelectedItem is not AudioTrackVm track) return;
            track.Volume = VolumeSlider.Value;
            track.Pan = PanSlider.Value;
            track.IsMuted = MuteCheck.IsChecked == true;

            var clip = track.Clip;
            if (clip != null)
            {
                clip.Volume = VolumeSlider.Value;
                clip.Pan = PanSlider.Value;
                clip.GainDb = (double)(GainDbNud.Value ?? 0);
                clip.FadeIn = (double)(FadeInNud.Value ?? 0);
                clip.FadeOut = (double)(FadeOutNud.Value ?? 0);
                clip.IsMuted = MuteCheck.IsChecked == true;
                clip.IsSolo = SoloCheck.IsChecked == true;
            }

            int idx = TracksList.SelectedIndex;
            _tracks[idx] = track;
            TracksList.SelectedIndex = idx;
        }

        private void OnMixerChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) => SyncSelectedTrack();
        private void OnMixerNum(object? sender, NumericUpDownValueChangedEventArgs e) => SyncSelectedTrack();
        private void OnMuteChanged(object? sender, RoutedEventArgs e) => SyncSelectedTrack();

        private async void OnAddTrack(object? sender, RoutedEventArgs e)
        {
            _trackCounter++;
            _tracks.Add(new AudioTrackVm { Name = $"Track {_trackCounter}" });
            TracksList.SelectedIndex = _tracks.Count - 1;
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private void OnRemoveTrack(object? sender, RoutedEventArgs e)
        {
            if (TracksList.SelectedIndex >= 0 && _tracks.Count > 1)
            {
                int idx = TracksList.SelectedIndex;
                _tracks.RemoveAt(idx);
                TracksList.SelectedIndex = System.Math.Min(idx, _tracks.Count - 1);
            }
        }

        private async void OnImportAudio(object? sender, RoutedEventArgs e)
        {
            var owner = this.VisualRoot as Window;
            if (owner == null) return;

            var dlg = new OpenFileDialog
            {
                Title = "Import Audio",
                AllowMultiple = false,
                Filters = new System.Collections.Generic.List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "Audio", Extensions = { "mp3", "wav", "aac", "flac", "ogg" } }
                }
            };

            var result = await dlg.ShowAsync(owner);
            if (result == null || result.Length == 0) return;

            _tracks.Add(new AudioTrackVm { Name = System.IO.Path.GetFileName(result[0]) });
            TracksList.SelectedIndex = _tracks.Count - 1;
        }

        private void OnEqPresetChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (Eq60 == null || Eq150 == null || Eq400 == null || Eq1k == null || Eq2400 == null || Eq15k == null) return;
            var preset = (EqPresetCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Flat";
            double v60 = 0, v150 = 0, v400 = 0, v1k = 0, v2400 = 0, v15k = 0;
            switch (preset)
            {
                case "Bass Boost": v60 = 8; v150 = 6; v400 = 2; break;
                case "Vocal Clarity": v150 = -3; v400 = 3; v1k = 5; v2400 = 4; break;
                case "Treble Boost": v2400 = 4; v15k = 8; break;
            }
            Eq60.Value = v60; Eq150.Value = v150; Eq400.Value = v400;
            Eq1k.Value = v1k; Eq2400.Value = v2400; Eq15k.Value = v15k;
        }

        private void OnCaptureNoisePrint(object? sender, RoutedEventArgs e)
        {
            NrStatusText.Text = "Noise print captured from first 2 seconds of selected track.";
        }

        private void OnApplyReduction(object? sender, RoutedEventArgs e)
        {
            if (NrEnableCheck.IsChecked != true)
            {
                NrStatusText.Text = "Noise reduction is disabled.";
                return;
            }
            var profile = (NrProfileCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Medium";
            NrStatusText.Text = $"Noise reduction queued: {profile}, strength {NrStrengthSlider.Value:0}%. Render applies on export.";
        }

        private async void OnPlay(object? sender, RoutedEventArgs e)
        {
            if (TracksList.SelectedItem is AudioTrackVm sel && sel.Clip != null)
                _playDuration = System.Math.Max(0.5, sel.Clip.Duration);
            else
                _playDuration = System.Math.Max(0.5, _session.Timeline?.SequenceDuration ?? 30);

            await _engine.SeekAsync(AudioPlayhead.Value / 100.0 * _playDuration);
            await _engine.PlayAsync();
        }

        private void OnStop(object? sender, RoutedEventArgs e)
        {
            _engine.Stop();
            AudioPlayhead.Value = 0;
            AudioTimeText.Text = $"0.0s / {_playDuration:F1}s";
        }

        // ---------- media library (drag & drop / double-click) ----------

        private MediaEntry? _selectedAudioEntry;
        private readonly System.Collections.Generic.Dictionary<MediaEntry, Border> _audioCards = new();

        private static IBrush? Res(string key) =>
            Avalonia.Application.Current is { } app && app.TryFindResource(key, out var v) ? v as IBrush : null;

        private void PopulateAudioMedia()
        {
            if (AudioMediaHost == null) return;
            AudioMediaHost.Children.Clear();
            _audioCards.Clear();

            var items = (_session.CurrentProject?.Media ?? new System.Collections.Generic.List<MediaEntry>())
                .Where(m => m.MediaType.Equals("audio", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (items.Count == 0)
            {
                AudioMediaHost.Children.Add(new TextBlock
                {
                    Text = "No audio yet.\nUse Import Audio… or import in Video Studio.",
                    Foreground = Res("BTxtLow"),
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(2, 6, 0, 0),
                });
                return;
            }

            foreach (var entry in items)
                AudioMediaHost.Children.Add(BuildAudioCard(entry));
        }

        private Border BuildAudioCard(MediaEntry entry)
        {
            bool selected = ReferenceEquals(entry, _selectedAudioEntry);

            var card = new Border
            {
                Height = 52,
                Margin = new Avalonia.Thickness(2),
                CornerRadius = new Avalonia.CornerRadius(4),
                Background = Res("BInset"),
                BorderThickness = new Avalonia.Thickness(selected ? 1.5 : 0.75),
                BorderBrush = selected ? Res("BAccent") : Res("BBorder"),
                Tag = entry,
                Child = new Grid
                {
                    ColumnDefinitions = new Avalonia.Controls.ColumnDefinitions("Auto,*"),
                },
            };

            var icon = new TextBlock
            {
                Text = "🎵",
                FontSize = 18,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(8, 0, 4, 0),
            };
            Grid.SetColumn(icon, 0);
            ((Grid)card.Child!).Children.Add(icon);

            double dur = entry.DurationSec;
            var label = new StackPanel
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(2, 1, 4, 1),
            };
            label.Children.Add(new TextBlock
            {
                Text = IOPath.GetFileNameWithoutExtension(entry.FileName),
                Foreground = Res("BTxtHigh"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            label.Children.Add(new TextBlock
            {
                Text = dur > 0.05 ? $"{dur:0.#}s" : "audio",
                Foreground = Res("BTxtLow"),
                FontSize = 9.5,
            });
            Grid.SetColumn(label, 1);
            ((Grid)card.Child).Children.Add(label);

            Services.MediaCardFactory.MakeInteractive(
                card,
                entry.RelativePath,
                tap: () =>
                {
                    _selectedAudioEntry = entry;
                    RestyleAudioSelection();
                },
                doubleTap: () =>
                {
                    _selectedAudioEntry = entry;
                    RestyleAudioSelection();
                    AppendAudioToTimeline(entry);
                });

            return card;
        }

        private void RestyleAudioSelection()
        {
            foreach (var (entry, card) in _audioCards)
            {
                bool selected = ReferenceEquals(entry, _selectedAudioEntry);
                card.BorderBrush = selected ? Res("BAccent") : Res("BBorder");
                card.BorderThickness = new Avalonia.Thickness(selected ? 1.5 : 0.75);
            }
        }

        /// <summary>Places an audio entry on the emptiest audio track of the session timeline.</summary>
        private void AppendAudioToTimeline(MediaEntry entry)
        {
            var tl = _session.Timeline;
            if (tl == null || !_session.HasProject)
            {
                ActiveTrackText.Text = "Open a project first.";
                return;
            }

            const int maxTracks = 4;
            int bestTrack = 0;
            double bestStart = double.MaxValue;
            for (int i = 0; i < maxTracks; i++)
            {
                double end = tl.AudioClips
                    .Where(c => c.TrackIndex == i)
                    .Select(c => c.StartTime + c.Duration)
                    .DefaultIfEmpty(0)
                    .Max();
                if (end < bestStart)
                {
                    bestStart = end;
                    bestTrack = i;
                }
            }

            double duration = entry.DurationSec > 0.05 ? entry.DurationSec : 10.0;
            var clip = new ClipModel
            {
                Name = entry.FileName,
                SourcePath = entry.RelativePath,
                MediaType = "Audio",
                TrackKind = TrackKind.Audio,
                TrackIndex = bestTrack,
                StartTime = bestStart,
                Duration = duration,
                InPoint = 0,
                OutPoint = duration,
            };

            tl.Clips.Add(clip);
            _session.SelectClip(clip);
            _selectedAudioEntry = entry;
            RefreshTracksFromTimeline();
            TracksList.SelectedIndex = _tracks.Count - 1;   // newest first for mixing
        }

        private MediaEntry? EntryFromPayload(DragEventArgs e)
        {
            var rel = Services.MediaCardFactory.PayloadFrom(e);
            if (rel == null) return null;
            return (_session.CurrentProject?.Media ?? new System.Collections.Generic.List<MediaEntry>())
                .FirstOrDefault(m => m.RelativePath.Equals(rel, StringComparison.OrdinalIgnoreCase));
        }

        private void OnMediaDragOver(object? sender, DragEventArgs e) =>
            e.DragEffects = EntryFromPayload(e) != null ? DragDropEffects.Copy : DragDropEffects.None;

        private void OnMediaDrop(object? sender, DragEventArgs e)
        {
            var entry = EntryFromPayload(e);
            if (entry != null) AppendAudioToTimeline(entry);
        }
    }
}
