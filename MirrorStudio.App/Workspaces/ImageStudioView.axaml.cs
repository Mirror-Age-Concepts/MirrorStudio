using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using System.Collections.ObjectModel;

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

            try
            {
                _imagePath = result[0];
                _bitmap = new Bitmap(_imagePath);
                CanvasImage.Source = _bitmap;
                CanvasHintText.Text = "";
                var fileName = System.IO.Path.GetFileName(_imagePath);
                _layers.Add(fileName);
                LayersList.SelectedIndex = _layers.Count - 1;
                PropDocText.Text = $"File: {_imagePath}\nSize: {_bitmap.Size.Width:0} x {_bitmap.Size.Height:0}px";
                SetActiveTool(_activeTool);
            }
            catch (System.Exception ex)
            {
                await MessageBox.Show(owner, "Open image error", ex.Message);
            }
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
