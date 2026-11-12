using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvImage = Avalonia.Controls.Image;
using MirrorStudio.Graphics;
using MirrorStudio.Graphics.Models;

namespace MirrorStudio.App.Services;

/// <summary>
/// Renders graphics layers (text/shapes/images) over the program monitor as
/// native controls, driven by the playhead. The FFmpeg exporter mirrors this
/// via drawtext/drawbox/overlay filters (see SequenceExporter).
/// </summary>
public sealed class GraphicsOverlayRenderer
{
    private readonly Canvas _host;
    private readonly Dictionary<Guid, Control> _controls = new();
    private readonly Dictionary<string, Bitmap?> _bitmaps = new(StringComparer.OrdinalIgnoreCase);

    public GraphicsOverlayRenderer(Canvas host) => _host = host;

    public void Clear()
    {
        _host.Children.Clear();
        _controls.Clear();
    }

    public void Update(IReadOnlyList<GraphicLayer>? layers, double timelineSeconds, int projW, int projH)
    {
        if (_host.Bounds.Width <= 1 || _host.Bounds.Height <= 1) return;
        double sx = _host.Bounds.Width / Math.Max(1, projW);
        double sy = _host.Bounds.Height / Math.Max(1, projH);

        var active = (layers ?? Array.Empty<GraphicLayer>())
            .Where(l => l.IsActiveAt(timelineSeconds))
            .ToList();
        var activeIds = active.Select(l => l.Id).ToHashSet();

        foreach (var stale in _controls.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            _host.Children.Remove(_controls[stale]);
            _controls.Remove(stale);
        }

        foreach (var layer in active)
        {
            var st = TextAnimator.Evaluate(layer, timelineSeconds);

            Control ctl = EnsureControl(layer);
            UpdateControl(layer, st, sx, sy);

            ctl.Opacity = Math.Clamp(st.Opacity, 0, 1);
            ctl.IsVisible = st.Opacity > 0.005;

            // rotate/scale around the object's own center
            ctl.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            ctl.RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(Math.Max(0.01, st.ScaleX), Math.Max(0.01, st.ScaleY)),
                    new RotateTransform(st.Rotation),
                }
            };

            ctl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = ctl.DesiredSize;
            Canvas.SetLeft(ctl, st.X * sx - size.Width / 2);
            Canvas.SetTop(ctl, st.Y * sy - size.Height / 2);
            ctl.ZIndex = 1;
        }
    }

    private Control EnsureControl(GraphicLayer layer)
    {
        if (_controls.TryGetValue(layer.Id, out var existing)) return existing;

        Control created = layer switch
        {
            TextLayer => new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 900 },
            ShapeLayer sh => CreateShape(sh.ShapeType),
            ImageLayer => new AvImage { Stretch = Stretch.Uniform },
            _ => new TextBlock()
        };
        _controls[layer.Id] = created;
        _host.Children.Add(created);
        return created;
    }

    private static Control CreateShape(string type) => type switch
    {
        "Circle" or "Ellipse" => new Ellipse(),
        "Line" or "Arrow" => new Line(),
        _ => new Rectangle { RadiusX = type == "RoundedRectangle" ? 12 : 0, RadiusY = type == "RoundedRectangle" ? 12 : 0 }
    };

    private void UpdateControl(GraphicLayer layer, GraphicRenderState st, double sx, double sy)
    {
        switch (layer)
        {
            case TextLayer t:
            {
                if (ctl() is not TextBlock tb) break;
                string full = t.Text;
                int chars = Math.Clamp((int)Math.Ceiling(full.Length * st.RevealFraction), 0, full.Length);
                tb.Text = full[..chars];

                tb.FontFamily = new FontFamily(t.Style.FontFamily);
                tb.FontSize = Math.Max(6, t.Style.FontSize * ((sx + sy) / 2));
                tb.FontStyle = t.Style.Italic ? FontStyle.Italic : FontStyle.Normal;
                tb.FontWeight = t.Style.Bold ? FontWeight.Bold : FontWeight.Normal;
                tb.TextAlignment = t.Style.Alignment switch
                {
                    "Left" => TextAlignment.Left,
                    "Right" => TextAlignment.Right,
                    _ => TextAlignment.Center
                };
                tb.Foreground = ParseBrush(t.Style.FillColor);
                if (t.Style.StrokeWidth > 0.5)
                {
                    tb.Effect = null;
                    // stroke approximated with a dark drop shadow for legibility
                    tb.Effect = new DropShadowEffect
                    {
                        Color = ParseColor(t.Style.StrokeColor),
                        BlurRadius = t.Style.StrokeWidth,
                        Opacity = 1
                    };
                }
                else if (t.Style.ShadowOpacity > 0.01)
                {
                    tb.Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = Math.Max(2, t.Style.ShadowBlur),
                        OffsetX = t.Style.ShadowOffsetX,
                        
                        
                        Opacity = Math.Min(1, t.Style.ShadowOpacity)
                    };
                }
                else tb.Effect = null;
                break;
            }

            case ShapeLayer s:
            {
                double w = Math.Max(2, s.Width * st.ScaleX * sx);
                double h = Math.Max(2, s.Height * st.ScaleY * sy);
                switch (ctl())
                {
                    case Rectangle r:
                        r.Width = w; r.Height = h;
                        r.Fill = s.StrokeWidth > 0.5 ? null : ParseBrush(s.FillColor);
                        r.Stroke = s.StrokeWidth > 0.5 ? ParseBrush(s.StrokeColor) : null;
                        r.StrokeThickness = s.StrokeWidth;
                        break;
                    case Ellipse e:
                        e.Width = w; e.Height = h;
                        e.Fill = ParseBrush(s.FillColor);
                        e.Stroke = s.StrokeWidth > 0.5 ? ParseBrush(s.StrokeColor) : null;
                        e.StrokeThickness = s.StrokeWidth;
                        break;
                    case Line ln:
                        ln.StartPoint = new Point(0, h / 2);
                        ln.EndPoint = new Point(w, h / 2);
                        ln.Stroke = ParseBrush(s.StrokeColor == "#000000" && s.StrokeWidth <= 0.5
                            ? s.FillColor : s.StrokeColor);
                        ln.StrokeThickness = Math.Max(2, s.StrokeWidth);
                        break;
                }
                break;
            }

            case ImageLayer img:
            {
                if (ctl() is not AvImage im) break;
                string? path = ResolvePath(img.ImagePath);
                var bmp = LoadBitmap(path);
                if (bmp != null)
                {
                    im.Source = bmp;
                    double baseW = img.SourceWidth > 1 ? img.SourceWidth : bmp.PixelSize.Width;
                    double baseH = img.SourceHeight > 1 ? img.SourceHeight : bmp.PixelSize.Height;
                    im.Width = Math.Max(2, baseW * sx);
                    im.Height = Math.Max(2, baseH * sy);
                }
                else im.Source = null;
                break;
            }
        }

        Control? ctl() => _controls.GetValueOrDefault(layer.Id);
    }

    private Bitmap? LoadBitmap(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (_bitmaps.TryGetValue(path, out var cached)) return cached;
        Bitmap? bmp = null;
        try { bmp = File.Exists(path) ? new Bitmap(path) : null; }
        catch { bmp = null; }
        _bitmaps[path] = bmp;
        return bmp;
    }

    private static IBrush ParseBrush(string hex)
    {
        try { return new SolidColorBrush(ParseColor(hex)); }
        catch { return Brushes.White; }
    }

    private static Avalonia.Media.Color ParseColor(string hex)
    {
        var c = hex.Trim().TrimStart('#');
        if (c.Length == 8)
        {
            var v = uint.Parse(c, System.Globalization.NumberStyles.HexNumber);
            return Color.FromUInt32(v);
        }
        return c.Length == 6 ? Color.Parse("#FF" + c) : Colors.White;
    }

    public Func<string?, string?>? ResolvePathHook { get; set; }

    private string? ResolvePath(string? p)
    {
        if (string.IsNullOrEmpty(p) || File.Exists(p)) return p;
        return ResolvePathHook?.Invoke(p);
    }
}



