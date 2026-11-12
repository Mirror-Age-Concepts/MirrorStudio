using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MirrorStudio.Core.Models;
using MirrorStudio.Image.Adjustments;
using MirrorStudio.Image.Export;
using MirrorStudio.Image.Filters;
using MirrorStudio.Image.Models;
using MirrorStudio.Image.Rendering;
using MirrorStudio.Image.Tools;
using SkiaSharp;
using IOPath = System.IO.Path;

namespace MirrorStudio.App.Workspaces;

public partial class ImageStudioView : UserControl
{
    private readonly SessionState _session;
    private ImageDocument? _doc;
    private ImageCanvas? _canvas;
    private readonly ImageEditorContext _ctx = new()
    {
        Document = new ImageDocument { Width = 1, Height = 1 },
        Canvas = new ImageCanvas(new ImageDocument { Width = 1, Height = 1 })
    };
    private ImageLayer? _adjustLayer;
    private bool _pointerDown;

    private readonly ObservableCollection<string> _layerNames = new();

    public ImageStudioView(SessionState session)
    {
        InitializeComponent();
        _session = session;

        LayersList.ItemsSource = _layerNames;
        BrushPresetCombo.ItemsSource = MirrorStudio.Image.Brushes.BrushPreset.All.Select(p => p.Name).ToList();
        BrushPresetCombo.SelectedIndex = 0;
        CboLayerBlend.ItemsSource = new[] { "Normal", "Multiply", "Screen", "Overlay", "Darken", "Lighten",
            "ColorDodge", "ColorBurn", "HardLight", "SoftLight", "Difference", "Exclusion",
            "Hue", "Saturation", "Color", "Luminosity", "Add", "Xor" };
        CboLayerBlend.SelectedIndex = 0;

        StageCanvas.PointerPressed += OnCanvasPointerPressed;
        StageCanvas.PointerMoved += OnCanvasPointerMoved;
        StageCanvas.PointerReleased += OnCanvasPointerReleased;
        StageCanvas.PointerExited += (_, _) => { if (_pointerDown) { _pointerDown = false; RenderPreview(); } };

        DragDrop.SetAllowDrop(StageCanvas, true);
        StageCanvas.AddHandler(DragDrop.DragOverEvent, OnMediaDragOver);
        StageCanvas.AddHandler(DragDrop.DropEvent, OnMediaDrop);

        _session.ProjectChanged += OnProjectChanged;
        PopulateImageMedia();
        SetActiveToolText();
    }

    // ---------------- document lifecycle ----------------

    private void OnProjectChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_session.CurrentProject != null)
                CanvasHintText.Text = $"CANVAS {_session.CurrentProject.Width}x{_session.CurrentProject.Height}\n(Open or New Image to begin)";
            PopulateImageMedia();
        });
    }

    private void OnNewImage(object? sender, RoutedEventArgs e)
    {
        int w = _session.CurrentProject?.Width ?? 800;
        int h = _session.CurrentProject?.Height ?? 600;
        CreateDocument(w, h, whiteBackground: true);
        SetStatus($"New {w}x{h} image");
    }

    public async void OnOpenImage(object? sender, RoutedEventArgs e)
    {
        var owner = this.VisualRoot as Window;
        if (owner == null) return;
        var dlg = new OpenFileDialog
        {
            Title = "Open Image",
            AllowMultiple = false,
            Filters = new List<FileDialogFilter>
            {
                new() { Name = "Images", Extensions = { "jpg", "jpeg", "png", "bmp", "webp", "tif", "tiff" } }
            }
        };
        var result = await dlg.ShowAsync(owner);
        if (result == null || result.Length == 0) return;
        OpenImageFile(result[0]);
    }

    private void OpenImageFile(string path)
    {
        var bmp = LoadBitmap(path);
        if (bmp == null) { SetStatus("Could not load image."); return; }
        if (_doc == null) CreateDocument(bmp.Width, bmp.Height, whiteBackground: false);
        else { _doc.Width = bmp.Width; _doc.Height = bmp.Height; }
        _doc.Layers.Clear();
        var layer = new ImageLayer
        {
            Name = IOPath.GetFileName(path),
            Kind = ImageLayerKind.Raster,
            Bitmap = bmp,
            PositionX = 0,
            PositionY = 0
        };
        _doc.AddLayer(layer);
        _doc.ActiveLayer = layer;
        TxtResizeW.Text = bmp.Width.ToString();
        TxtResizeH.Text = bmp.Height.ToString();
        RefreshAll();
        SetStatus($"Opened {IOPath.GetFileName(path)}");
    }

    private void CreateDocument(int w, int h, bool whiteBackground)
    {
        _doc = new ImageDocument { Width = w, Height = h, Dpi = 96, BackgroundColor = "#00000000" };
        _canvas = new ImageCanvas(_doc) { Zoom = 1 };
        _ctx.Document = _doc;
        _ctx.Canvas = _canvas;
        _adjustLayer = null;

        var bg = new ImageLayer { Name = "Background", Kind = ImageLayerKind.Raster, PositionX = 0, PositionY = 0 };
        bg.Bitmap = ImageDocument.MakeBitmap(w, h);
        if (whiteBackground)
        {
            using var s = SKSurface.Create(bg.Bitmap.Info, bg.Bitmap.GetPixels());
            s.Canvas.Clear(SKColors.White);
            s.Flush();
        }
        _doc.AddLayer(bg);
        _doc.ActiveLayer = bg;
        ZoomSlider.Value = 1;
    }

    private static SKBitmap? LoadBitmap(string path)
    {
        try
        {
            using var orig = SKBitmap.Decode(path);
            if (orig == null) return null;
            var conv = ImageDocument.MakeBitmap(orig.Width, orig.Height);
            using var s = SKSurface.Create(conv.Info, conv.GetPixels());
            s.Canvas.Clear(SKColors.Transparent);
            s.Canvas.DrawBitmap(orig, 0, 0);
            s.Flush();
            return conv;
        }
        catch { return null; }
    }

    // ---------------- preview rendering ----------------

    private void RenderPreview()
    {
        if (_doc == null) { CanvasImage.Source = null; CanvasHintText.IsVisible = true; return; }
        CanvasHintText.IsVisible = false;
        var bmp = ImageCompositor.Composite(_doc);
        CanvasImage.Source = ToAvalonia(bmp);
        double z = _canvas?.Zoom ?? 1;
        CanvasImage.Width = _doc.Width * z;
        CanvasImage.Height = _doc.Height * z;
        StageCanvas.Width = _doc.Width * z;
        StageCanvas.Height = _doc.Height * z;
        if (ZoomLabel != null) ZoomLabel.Text = $"{(int)(z * 100)}%";
    }

    private static Avalonia.Media.Imaging.Bitmap ToAvalonia(SKBitmap bmp)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;
        return new Avalonia.Media.Imaging.Bitmap(ms);
    }

    private void DrawSelectionOverlay()
    {
        SelectionCanvas.Children.Clear();
        if (_doc?.Selection == null || _doc.Selection.Mode is ImageSelection.SelectionMode.None) return;
        if (_doc.Selection.Mode is ImageSelection.SelectionMode.Rectangle or ImageSelection.SelectionMode.Ellipse)
        {
            var b = _doc.Selection.Bounds;
            double z = _canvas?.Zoom ?? 1;
            var rect = new Rectangle
            {
                Width = b.Width * z,
                Height = b.Height * z,
                Stroke = new SolidColorBrush(Colors.Cyan),
                StrokeThickness = 1.5,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 }
            };
            Canvas.SetLeft(rect, b.X * z);
            Canvas.SetTop(rect, b.Y * z);
            SelectionCanvas.Children.Add(rect);
        }
    }

    // ---------------- pointer / tools ----------------

    private (double x, double y) ToImage(Point p)
    {
        double z = _canvas?.Zoom ?? 1;
        return (p.X / z, p.Y / z);
    }

    private void OnCanvasPointerPressed(object? s, PointerPressedEventArgs e)
    {
        if (_doc == null) return;
        _pointerDown = true;
        var (ix, iy) = ToImage(e.GetPosition(StageCanvas));
        ToolFactory.Create(_ctx.ActiveTool).OnDown(_ctx, ix, iy);
        RenderPreview();
        DrawSelectionOverlay();
    }

    private void OnCanvasPointerMoved(object? s, PointerEventArgs e)
    {
        if (_doc == null || !_pointerDown) return;
        var (ix, iy) = ToImage(e.GetPosition(StageCanvas));
        ToolFactory.Create(_ctx.ActiveTool).OnMove(_ctx, ix, iy, true);
        RenderPreview();
        DrawSelectionOverlay();
    }

    private void OnCanvasPointerReleased(object? s, PointerReleasedEventArgs e)
    {
        if (_doc == null) { _pointerDown = false; return; }
        _pointerDown = false;
        var (ix, iy) = ToImage(e.GetPosition(StageCanvas));
        ToolFactory.Create(_ctx.ActiveTool).OnUp(_ctx, ix, iy);
        RenderPreview();
        DrawSelectionOverlay();
        RefreshLayers();
        RefreshProps();
    }

    private void OnToolButton(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag)
        {
            _ctx.ActiveTool = tag;
            ApplyBrushFromSliders();
            SetActiveToolText();
        }
    }

    private void SetActiveToolText() =>
        ActiveToolText.Text = $"Tool: {_ctx.ActiveTool}  |  brush {_ctx.Brush.Size:0}px  opacity {_ctx.Brush.Opacity * 100:0}%";

    private void ApplyBrushFromSliders()
    {
        _ctx.Brush.Size = BrushSizeSlider.Value;
        _ctx.Brush.Opacity = BrushOpacitySlider.Value / 100.0;
        _ctx.Brush.Flow = BrushFlowSlider.Value / 100.0;
        _ctx.Brush.Hardness = BrushHardnessSlider.Value / 100.0;
    }

    private void OnBrushParamChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        ApplyBrushFromSliders();
        SetActiveToolText();
    }

    private void OnBrushPresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (BrushPresetCombo.SelectedItem is not string name) return;
        var preset = MirrorStudio.Image.Brushes.BrushPreset.Make(name);
        _ctx.Brush = preset;
        BrushSizeSlider.Value = preset.Size;
        BrushOpacitySlider.Value = preset.Opacity * 100;
        BrushFlowSlider.Value = preset.Flow * 100;
        BrushHardnessSlider.Value = preset.Hardness * 100;
        SetActiveToolText();
    }

    private void OnZoomChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_canvas != null) _canvas.Zoom = ZoomSlider.Value;
        RenderPreview();
    }

    // ---------------- layers ----------------

    private void RefreshAll()
    {
        RefreshLayers();
        RefreshProps();
        RenderPreview();
        DrawSelectionOverlay();
    }

    private void RefreshLayers()
    {
        _layerNames.Clear();
        if (_doc == null) return;
        foreach (var l in _doc.Layers)
            _layerNames.Add($"{(l.IsVisible ? "" : "(hidden) ")}{l.Name}  [{l.Kind}]");
        int idx = _doc.ActiveLayer != null ? _doc.Layers.IndexOf(_doc.ActiveLayer) : -1;
        LayersList.SelectedIndex = idx;
        OnLayerSelected(null, null);
    }

    private void OnLayerSelected(object? sender, SelectionChangedEventArgs? e)
    {
        if (_doc == null || LayersList.SelectedIndex < 0) return;
        var layer = _doc.Layers[LayersList.SelectedIndex];
        _doc.ActiveLayer = layer;
        ChkLayerVisible.IsChecked = layer.IsVisible;
        SldLayerOpacity.Value = layer.Opacity * 100;
        if (CboLayerBlend.ItemsSource is IEnumerable<string> items)
            CboLayerBlend.SelectedItem = items.FirstOrDefault(x => x == layer.BlendMode) ?? "Normal";
        RefreshProps();
    }

    private void OnLayerVisibleChanged(object? sender, RoutedEventArgs e)
    {
        if (_doc?.ActiveLayer == null) return;
        _doc.ActiveLayer.IsVisible = ChkLayerVisible.IsChecked == true;
        RenderPreview();
    }

    private void OnLayerOpacityChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_doc?.ActiveLayer == null) return;
        _doc.ActiveLayer.Opacity = SldLayerOpacity.Value / 100.0;
        RefreshProps();
        RenderPreview();
    }

    private void OnLayerBlendChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_doc?.ActiveLayer == null) return;
        _doc.ActiveLayer.BlendMode = CboLayerBlend.SelectedItem as string ?? "Normal";
        RenderPreview();
    }

    private void OnAddLayer(object? sender, RoutedEventArgs e)
    {
        if (_doc == null) { SetStatus("Open or create an image first."); return; }
        var layer = new ImageLayer
        {
            Name = $"Layer {_doc.Layers.Count + 1}",
            Kind = ImageLayerKind.Raster,
            Bitmap = ImageDocument.MakeBitmap(_doc.Width, _doc.Height),
            PositionX = 0,
            PositionY = 0
        };
        _doc.AddLayer(layer);
        RefreshAll();
    }

    private void OnAddAdjustmentLayer(object? sender, RoutedEventArgs e)
    {
        if (_doc == null) { SetStatus("Open or create an image first."); return; }
        EnsureAdjustLayer();
        _doc.ActiveLayer = _adjustLayer;
        RefreshAll();
        SetStatus("Adjustment layer added — use the Adjustments tab.");
    }

    private void OnDeleteLayer(object? sender, RoutedEventArgs e)
    {
        if (_doc?.ActiveLayer == null) return;
        if (_doc.Layers.Count <= 1) { SetStatus("Cannot delete the last layer."); return; }
        _doc.RemoveLayer(_doc.ActiveLayer.Id);
        _adjustLayer = null;
        RefreshAll();
    }

    private void OnMoveLayerUp(object? sender, RoutedEventArgs e)
    {
        if (_doc?.ActiveLayer == null) return;
        _doc.MoveLayer(_doc.ActiveLayer.Id, -1);
        RefreshLayers();
        RenderPreview();
    }

    private void OnMoveLayerDown(object? sender, RoutedEventArgs e)
    {
        if (_doc?.ActiveLayer == null) return;
        _doc.MoveLayer(_doc.ActiveLayer.Id, +1);
        RefreshLayers();
        RenderPreview();
    }

    // ---------------- adjustments (non-destructive) ----------------

    private void EnsureAdjustLayer()
    {
        if (_doc == null) return;
        if (_adjustLayer != null && _doc.Layers.Contains(_adjustLayer)) return;
        _adjustLayer = new ImageLayer
        {
            Name = "Adjustments",
            Kind = ImageLayerKind.Adjustment,
            PositionX = 0,
            PositionY = 0
        };
        _doc.Layers.Insert(0, _adjustLayer); // top: affects the whole composite
    }

    private static Adjustment A(string type, string param, double value)
    {
        var a = new Adjustment { Type = type };
        a.Set(param, value);
        return a;
    }

    private List<Adjustment> BuildAdjustments()
    {
        var list = new List<Adjustment>();
        if (Math.Abs(SldBrightness.Value) > 0.5) list.Add(A(Adjustment.Brightness, "Amount", SldBrightness.Value / 100));
        if (Math.Abs(SldContrast.Value) > 0.5) list.Add(A(Adjustment.Contrast, "Amount", SldContrast.Value / 100));
        if (Math.Abs(SldExposure.Value) > 1e-6) list.Add(A(Adjustment.Exposure, "Stops", SldExposure.Value));
        if (Math.Abs(SldSaturation.Value) > 0.5) list.Add(A(Adjustment.Saturation, "Amount", SldSaturation.Value / 100));
        if (Math.Abs(SldTemperature.Value) > 0.5) list.Add(A(Adjustment.Temperature, "Amount", SldTemperature.Value / 100));
        if (Math.Abs(SldTint.Value) > 0.5) list.Add(A(Adjustment.Tint, "Amount", SldTint.Value / 100));
        if (Math.Abs(SldHighlights.Value) > 0.5) list.Add(A(Adjustment.Highlights, "Amount", SldHighlights.Value / 100));
        if (Math.Abs(SldShadows.Value) > 0.5) list.Add(A(Adjustment.Shadows, "Amount", SldShadows.Value / 100));
        if (Math.Abs(SldGamma.Value - 100) > 0.5) list.Add(A(Adjustment.Gamma, "Value", SldGamma.Value / 100));
        return list;
    }

    private void OnAdjustChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_doc == null) return;
        var adjs = BuildAdjustments();
        if (adjs.Count == 0)
        {
            if (_adjustLayer != null) _adjustLayer.IsVisible = false;
        }
        else
        {
            EnsureAdjustLayer();
            _adjustLayer!.Adjustments = adjs;
            _adjustLayer.IsVisible = true;
        }
        RenderPreview();
    }

    private void OnAdjustReset(object? sender, RoutedEventArgs e)
    {
        SldBrightness.Value = 0; SldContrast.Value = 0; SldExposure.Value = 0;
        SldSaturation.Value = 0; SldTemperature.Value = 0; SldTint.Value = 0;
        SldHighlights.Value = 0; SldShadows.Value = 0; SldGamma.Value = 100;
        if (_adjustLayer != null) { _adjustLayer.Adjustments.Clear(); _adjustLayer.IsVisible = false; }
        RenderPreview();
    }

    // ---------------- filters ----------------

    private void ApplyFilterToActive(string kind)
    {
        if (_doc?.ActiveLayer == null) { SetStatus("Select a raster layer first."); return; }
        var bmp = _ctx.ActiveBitmap();
        if (bmp == null) { SetStatus("Active layer is not a raster layer."); return; }
        double s = SldFilterStrength.Value / 100.0;
        switch (kind)
        {
            case "blur": FilterEngine.ApplyBlur(bmp, s * 24); break;
            case "sharpen": FilterEngine.ApplySharpen(bmp, s * 2); break;
            case "noise": FilterEngine.ApplyNoiseReduction(bmp, Math.Max(1, (int)(s * 2))); break;
            case "vignette": FilterEngine.ApplyVignette(bmp, s, 1); break;
            case "grayscale": AdjustmentProcessor.Apply(bmp, new Adjustment { Type = Adjustment.Grayscale }); break;
            case "sepia": AdjustmentProcessor.Apply(bmp, new Adjustment { Type = Adjustment.Sepia }); break;
            case "invert": AdjustmentProcessor.Apply(bmp, new Adjustment { Type = Adjustment.Invert }); break;
        }
        RenderPreview();
        SetStatus($"Applied {kind}");
    }

    private void OnFilterBlur(object? s, RoutedEventArgs e) => ApplyFilterToActive("blur");
    private void OnFilterSharpen(object? s, RoutedEventArgs e) => ApplyFilterToActive("sharpen");
    private void OnFilterNoise(object? s, RoutedEventArgs e) => ApplyFilterToActive("noise");
    private void OnFilterVignette(object? s, RoutedEventArgs e) => ApplyFilterToActive("vignette");
    private void OnFilterGrayscale(object? s, RoutedEventArgs e) => ApplyFilterToActive("grayscale");
    private void OnFilterSepia(object? s, RoutedEventArgs e) => ApplyFilterToActive("sepia");
    private void OnFilterInvert(object? s, RoutedEventArgs e) => ApplyFilterToActive("invert");

    // ---------------- transform ----------------

    private void OnRotate90(object? s, RoutedEventArgs e)
    {
        if (_doc == null) return;
        _doc.Rotate(90);
        TxtResizeW.Text = _doc.Width.ToString();
        TxtResizeH.Text = _doc.Height.ToString();
        RefreshAll();
    }

    private void OnFlipH(object? s, RoutedEventArgs e) { _doc?.Flip(true); RefreshAll(); }
    private void OnFlipV(object? s, RoutedEventArgs e) { _doc?.Flip(false); RefreshAll(); }

    private void OnResize(object? s, RoutedEventArgs e)
    {
        if (_doc == null) return;
        if (int.TryParse(TxtResizeW.Text, out int w) && int.TryParse(TxtResizeH.Text, out int h) && w > 0 && h > 0)
        {
            _doc.Resize(w, h);
            RefreshAll();
            SetStatus($"Resized to {w}x{h}");
        }
    }

    // ---------------- export ----------------

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (_doc == null) { SetStatus("Nothing to export."); return; }
        var owner = this.VisualRoot as Window;
        if (owner == null) return;
        var fmt = (ExportFormatCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "PNG";
        var dlg = new SaveFileDialog
        {
            Title = "Export Image",
            InitialFileName = $"image.{fmt.ToLowerInvariant()}",
            Filters = new List<FileDialogFilter>
            {
                new() { Name = fmt, Extensions = { fmt.ToLowerInvariant() } }
            }
        };
        var path = await dlg.ShowAsync(owner);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var bmp = ImageCompositor.Composite(_doc);
            var flatten = !(fmt is "PNG" or "TIFF");
            ImageExportService.ForFormat(fmt).Export(bmp, path,
                new ExportOptions { Quality = (int)ExportQualitySlider.Value, FlattenAlpha = flatten });
            SetStatus($"Exported {fmt} → {IOPath.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            SetStatus("Export failed: " + ex.Message);
        }
    }

    // ---------------- media library ----------------

    private void RefreshProps()
    {
        if (_doc == null) { PropDocText.Text = "No image open"; PropLayerText.Text = "-"; return; }
        PropDocText.Text = $"Size: {_doc.Width} x {_doc.Height}\nDPI: {_doc.Dpi:0}\nLayers: {_doc.Layers.Count}";
        var l = _doc.ActiveLayer;
        PropLayerText.Text = l == null ? "-" :
            $"Name: {l.Name}\nKind: {l.Kind}\nBlend: {l.BlendMode}\nOpacity: {l.Opacity * 100:0}%\nVisible: {l.IsVisible}";
    }

    private void SetStatus(string text) => ActiveToolText.Text = text;

    // ---- media library (drag & drop / double-click) ----

    private MediaEntry? _selectedImageEntry;
    private readonly Dictionary<MediaEntry, Border> _imageCards = new();

    private static IBrush? Res(string key) =>
        Avalonia.Application.Current is { } app && app.TryFindResource(key, out var v) ? v as IBrush : null;

    private void PopulateImageMedia()
    {
        if (ImageMediaHost == null) return;
        ImageMediaHost.Children.Clear();
        _imageCards.Clear();

        var items = (_session.CurrentProject?.Media ?? new List<MediaEntry>())
            .Where(m => m.MediaType is "Images" or "Graphics").ToList();

        if (items.Count == 0)
        {
            ImageMediaHost.Children.Add(new TextBlock
            {
                Text = "No images yet.\nImport media in Video Studio.",
                Foreground = new SolidColorBrush(Color.Parse("#808080")),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 6, 0, 0)
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
            ? null : _session.ResolvePath(entry.ThumbnailRelativePath);

        if (thumbPath != null && File.Exists(thumbPath))
        {
            try
            {
                content = new Avalonia.Controls.Image
                {
                    Source = new Avalonia.Media.Imaging.Bitmap(thumbPath),
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
                };
            }
            catch { content = ToolIcon(); }
        }
        else content = ToolIcon();

        var card = new Border
        {
            Height = 56,
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Background = Res("BInset"),
            BorderThickness = new Thickness(selected ? 1.5 : 0.75),
            BorderBrush = selected ? Res("BAccent") : Res("BBorder"),
            Tag = entry,
            Child = new Grid { RowDefinitions = new RowDefinitions("*,Auto") }
        };
        Grid.SetRow(content, 0);
        ((Grid)card.Child!).Children.Add(content);
        var label = new TextBlock
        {
            Text = IOPath.GetFileNameWithoutExtension(entry.FileName),
            FontSize = 9.5,
            Margin = new Thickness(4, 1, 4, 2),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Res("BTxtHigh")
        };
        Grid.SetRow(label, 1);
        ((Grid)card.Child).Children.Add(label);

        Services.MediaCardFactory.MakeInteractive(card, entry.RelativePath,
            tap: () => { _selectedImageEntry = entry; RestyleImageSelection(); },
            doubleTap: () => { _selectedImageEntry = entry; RestyleImageSelection(); OpenEntry(entry); });
        return card;
    }

    private void RestyleImageSelection()
    {
        foreach (var (item, c) in _imageCards)
        {
            bool selected = ReferenceEquals(item, _selectedImageEntry);
            c.BorderBrush = selected ? Res("BAccent") : Res("BBorder");
            c.BorderThickness = new Thickness(selected ? 1.5 : 0.75);
        }
    }

    private static TextBlock ToolIcon() => new()
    {
        Text = "🖼",
        FontSize = 20,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
    };

    private void OpenEntry(MediaEntry entry)
    {
        var path = _session.ResolvePath(entry.RelativePath) ?? entry.RelativePath;
        if (!File.Exists(path)) return;
        OpenImageFile(path);
    }

    private MediaEntry? EntryFromPayload(DragEventArgs e)
    {
        var rel = Services.MediaCardFactory.PayloadFrom(e);
        if (rel == null) return null;
        return (_session.CurrentProject?.Media ?? new List<MediaEntry>())
            .FirstOrDefault(m => m.RelativePath.Equals(rel, StringComparison.OrdinalIgnoreCase));
    }

    private void OnMediaDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = EntryFromPayload(e) != null ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnMediaDrop(object? sender, DragEventArgs e)
    {
        var entry = EntryFromPayload(e);
        if (entry != null) OpenEntry(entry);
    }
}
