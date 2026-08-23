using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MirrorStudio.Core.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using IOPath = System.IO.Path;

namespace MirrorStudio.App.Workspaces
{
    public partial class ImageStudioView : UserControl
    {
        private readonly SessionState _session;
        private readonly ObservableCollection<string> _layers = new();
        private int _layerCounter;
        private string? _imagePath;
        private Bitmap? _bitmap;
        private string _activeTool = "none";

        public ImageStudioView(SessionState session)
        {
            InitializeComponent();
            _session = session;
            _layers.Add("Background");
            _layerCounter = 1;
            LayersList.ItemsSource = _layers;
            LayersList.SelectedIndex = 0;
            LayersList.SelectionChanged += OnLayerSelectionChanged;
            _session.ProjectChanged += OnProjectChanged;

            // media library: double-click or drag an image onto the canvas
            CanvasBorder.AddHandler(DragDrop.DragOverEvent, OnMediaDragOver);
            CanvasBorder.AddHandler(DragDrop.DropEvent, OnMediaDrop);

            PopulateImageMedia();
        }

        private void OnProjectChanged()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_session.CurrentProject != null)
                {
                    CanvasHintText.Text = $"CANVAS {_session.CurrentProject.Width}x{_session.CurrentProject.Height}";
                    PropDocText.Text = $"Canvas: {_session.CurrentProject.Width} x {_session.CurrentProject.Height}\nProject: {_session.CurrentProject.Name}";
                }
                PopulateImageMedia();
            });
        }

        private void SetActiveTool(string tool)
        {
            _activeTool = tool;
            if (BrushSizeSlider == null || ActiveToolText == null) return;
            ActiveToolText.Text = $"Tool: {tool}  |  size {BrushSizeSlider.Value:0}  opacity {BrushOpacitySlider.Value:0}%";
        }

        private void ApplyFilter(string name)
        {
            ActiveToolText.Text = $"Filter applied: {name}";
        }

        private void OnBrushSizeChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) => SetActiveTool(_activeTool);
        private void OnLayerSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var layer = LayersList.SelectedItem as string ?? "-";
            PropLayerText.Text = $"Name: {layer}\nBlend: Normal\nOpacity: 100%\nVisible: yes";
        }

        private void OnBrushTool(object? sender, RoutedEventArgs e) => SetActiveTool("brush");
        private void OnPencilTool(object? sender, RoutedEventArgs e) => SetActiveTool("pencil");
        private void OnEraserTool(object? sender, RoutedEventArgs e) => SetActiveTool("eraser");
        private void OnFillTool(object? sender, RoutedEventArgs e) => SetActiveTool("fill");
        private void OnCropTool(object? sender, RoutedEventArgs e) => SetActiveTool("crop");
        private void OnMoveTool(object? sender, RoutedEventArgs e) => SetActiveTool("move");
        private void OnCloneTool(object? sender, RoutedEventArgs e) => SetActiveTool("clone stamp");
        private void OnHealTool(object? sender, RoutedEventArgs e) => SetActiveTool("heal");
        private void OnDodgeTool(object? sender, RoutedEventArgs e) => SetActiveTool("dodge");
        private void OnBurnTool(object? sender, RoutedEventArgs e) => SetActiveTool("burn");

        private void OnBlurFilter(object? sender, RoutedEventArgs e) => ApplyFilter("blur");
        private void OnSharpenFilter(object? sender, RoutedEventArgs e) => ApplyFilter("sharpen");
        private void OnGrayscaleFilter(object? sender, RoutedEventArgs e) => ApplyFilter("grayscale");
        private void OnInvertFilter(object? sender, RoutedEventArgs e) => ApplyFilter("invert");
        private void OnSepiaFilter(object? sender, RoutedEventArgs e) => ApplyFilter("sepia");
        private void OnVintageFilter(object? sender, RoutedEventArgs e) => ApplyFilter("vintage");

        private void OnApplyAdjustments(object? sender, RoutedEventArgs e) => ApplyFilter("brightness/contrast/saturation");

        public async void OnOpenImage(object? sender, RoutedEventArgs e)
        {
            var owner = this.VisualRoot as Window;
            if (owner == null) return;

            var dlg = new OpenFileDialog
            {
                Title = "Open Image",
                AllowMultiple = false,
                Filters = new System.Collections.Generic.List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "Images", Extensions = { "jpg", "jpeg", "png", "bmp", "webp" } }
                }
            };

            var result = await dlg.ShowAsync(owner);
            if (result == null || result.Length == 0) return;

            OpenImageFile(result[0], owner);
        }

        private void OpenImageFile(string path, Window? owner)
        {
            try
            {
                _imagePath = path;
                _bitmap = new Bitmap(_imagePath);
                CanvasImage.Source = _bitmap;
                CanvasHintText.Text = "";
                var fileName = System.IO.Path.GetFileName(_imagePath);
                if (!_layers.Contains(fileName))
                    _layers.Add(fileName);
                LayersList.SelectedIndex = _layers.Count - 1;
                PropDocText.Text = $"File: {_imagePath}\nSize: {_bitmap.Size.Width:0} x {_bitmap.Size.Height:0}px";
                SetActiveTool(_activeTool);
            }
            catch (System.Exception ex)
            {
                _ = MessageBox.Show(owner ?? (this.VisualRoot as Window), "Open image error", ex.Message);
            }
        }

        // ---------- media library (drag & drop / double-click) ----------

        private MediaEntry? _selectedImageEntry;
        private readonly System.Collections.Generic.Dictionary<MediaEntry, Border> _imageCards = new();

        private static IBrush? Res(string key) =>
            Avalonia.Application.Current is { } app && app.TryFindResource(key, out var v) ? v as IBrush : null;

        private void PopulateImageMedia()
        {
            if (ImageMediaHost == null) return;
            ImageMediaHost.Children.Clear();
            _imageCards.Clear();

            var items = (_session.CurrentProject?.Media ?? new System.Collections.Generic.List<MediaEntry>())
                .Where(m => m.MediaType is "Images" or "Graphics")
                .ToList();

            if (items.Count == 0)
            {
                ImageMediaHost.Children.Add(new TextBlock
                {
                    Text = "No images yet.\nImport media in Video Studio.",
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#808080")),
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(2, 6, 0, 0),
                });
                return;
            }

            foreach (var entry in items)
            {
                var card = BuildImageCard(entry);
                _imageCards[entry] = card;
                ImageMediaHost.Children.Add(card);
            }
        }

        private Border BuildImageCard(MediaEntry entry)
        {
            bool selected = ReferenceEquals(entry, _selectedImageEntry);

            Control content;
            var thumbPath = string.IsNullOrEmpty(entry.ThumbnailRelativePath)
                ? null
                : _session.ResolvePath(entry.ThumbnailRelativePath);

            if (thumbPath != null && File.Exists(thumbPath))
            {
                try
                {
                    content = new Avalonia.Controls.Image
                    {
                        Source = new Bitmap(thumbPath),
                        Stretch = Stretch.UniformToFill,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    };
                }
                catch
                {
                    content = ToolIcon();
                }
            }
            else
            {
                content = ToolIcon();
            }

            var card = new Border
            {
                Height = 64,
                Margin = new Avalonia.Thickness(2),
                CornerRadius = new Avalonia.CornerRadius(4),
                Background = Res("BInset"),
                BorderThickness = new Avalonia.Thickness(selected ? 1.5 : 0.75),
                BorderBrush = selected ? Res("BAccent") : Res("BBorder"),
                Tag = entry,
                Child = new Grid { RowDefinitions = new Avalonia.Controls.RowDefinitions("*,Auto") },
            };

            Grid.SetRow(content, 0);
            ((Grid)card.Child!).Children.Add(content);
            var label = new TextBlock
            {
                Text = IOPath.GetFileNameWithoutExtension(entry.FileName),
                FontSize = 9.5,
                Margin = new Avalonia.Thickness(4, 1, 4, 2),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Res("BTxtHigh"),
            };
            Grid.SetRow(label, 1);
            ((Grid)card.Child).Children.Add(label);

            Services.MediaCardFactory.MakeInteractive(
                card,
                entry.RelativePath,
                tap: () =>
                {
                    _selectedImageEntry = entry;
                    RestyleImageSelection();
                },
                doubleTap: () =>
                {
                    _selectedImageEntry = entry;
                    RestyleImageSelection();
                    OpenEntry(entry);
                });

            return card;
        }

        private void RestyleImageSelection()
        {
            foreach (var (item, c) in _imageCards)
            {
                bool selected = ReferenceEquals(item, _selectedImageEntry);
                c.BorderBrush = selected ? Res("BAccent") : Res("BBorder");
                c.BorderThickness = new Avalonia.Thickness(selected ? 1.5 : 0.75);
            }
        }

        private static TextBlock ToolIcon() => new()
        {
            Text = "🖼",
            FontSize = 22,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };

        private void OpenEntry(MediaEntry entry)
        {
            var path = _session.ResolvePath(entry.RelativePath) ?? entry.RelativePath;
            if (!File.Exists(path)) return;
            OpenImageFile(path, this.VisualRoot as Window);
            ActiveToolText.Text = $"Opened from library: {IOPath.GetFileName(path)}";
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
            if (entry != null) OpenEntry(entry);
        }

        public async void OnSaveImage(object? sender, RoutedEventArgs e)
        {
            if (_bitmap == null || _imagePath == null)
            {
                await MessageBox.Show((this.VisualRoot as Window)!, "Save image", "No image is open.");
                return;
            }

            var dir = System.IO.Path.GetDirectoryName(_imagePath)!;
            var name = System.IO.Path.GetFileNameWithoutExtension(_imagePath) + "_edited.png";
            var outPath = System.IO.Path.Combine(dir, name);
            try
            {
                using var fs = System.IO.File.Create(outPath);
                _bitmap.Save(fs);
                ActiveToolText.Text = "Saved: " + outPath;
            }
            catch (System.Exception ex)
            {
                await MessageBox.Show((this.VisualRoot as Window)!, "Save image error", ex.Message);
            }
        }

        private void OnAddLayer(object? sender, RoutedEventArgs e)
        {
            _layerCounter++;
            _layers.Insert(0, "Layer " + _layerCounter);
            LayersList.SelectedIndex = 0;
        }

        private void OnDeleteLayer(object? sender, RoutedEventArgs e)
        {
            if (LayersList.SelectedIndex >= 0 && _layers.Count > 1)
            {
                int idx = LayersList.SelectedIndex;
                _layers.RemoveAt(idx);
                LayersList.SelectedIndex = System.Math.Min(idx, _layers.Count - 1);
            }
        }

        private void OnMoveLayerUp(object? sender, RoutedEventArgs e)
        {
            var idx = LayersList.SelectedIndex;
            if (idx > 0)
            {
                var item = _layers[idx];
                _layers.RemoveAt(idx);
                _layers.Insert(idx - 1, item);
                LayersList.SelectedIndex = idx - 1;
            }
        }

        private void OnMoveLayerDown(object? sender, RoutedEventArgs e)
        {
            var idx = LayersList.SelectedIndex;
            if (idx >= 0 && idx < _layers.Count - 1)
            {
                var item = _layers[idx];
                _layers.RemoveAt(idx);
                _layers.Insert(idx + 1, item);
                LayersList.SelectedIndex = idx + 1;
            }
        }
    }
}
