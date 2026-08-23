using Avalonia.Controls;
using Avalonia.Interactivity;
using MirrorStudio.Core.Models;
using System.Collections.ObjectModel;
using System.Linq;

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
        private int _trackCounter;
        private bool _playing;

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
            _session.ProjectChanged += OnProjectChanged;
        }

        private void OnProjectChanged()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var tl = _session.Timeline;
                if (tl == null || !tl.AudioClips.Any())
                {
                    // fall back to media-derived demo tracks
                    var project = _session.CurrentProject;
                    if (project == null) return;
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
            });
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
            int count = 110;
            int seed = track.Name.GetHashCode();
            var rnd = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                double envelope = System.Math.Sin(System.Math.PI * i / count);
                double h = (8 + rnd.NextDouble() * 52) * envelope + 4;
                _bars.Add(new WaveBarVm { Height = System.Math.Round(h, 1) });
            }
            WaveHintText.Text = $"Waveform: {track.Name} (proxy preview)";
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

        private void OnPlay(object? sender, RoutedEventArgs e)
        {
            if (_playing) return;
            _playing = true;
            var timer = new Avalonia.Threading.DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(50) };
            timer.Tick += (s, args) =>
            {
                AudioPlayhead.Value += 100.0 / 600.0;
                AudioTimeText.Text = $"{AudioPlayhead.Value * 0.3:F1}s / 30.0s";
                if (!_playing || AudioPlayhead.Value >= 100)
                {
                    timer.Stop();
                    _playing = false;
                    AudioPlayhead.Value = 0;
                    AudioTimeText.Text = "0.0s / 30.0s";
                }
            };
            timer.Start();
        }

        private void OnStop(object? sender, RoutedEventArgs e)
        {
            _playing = false;
            AudioPlayhead.Value = 0;
            AudioTimeText.Text = "0.0s / 30.0s";
        }
    }
}
