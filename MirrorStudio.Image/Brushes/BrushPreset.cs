using System.Collections.Generic;

namespace MirrorStudio.Image.Brushes;

/// <summary>Built-in brush presets exposed in the UI preset picker.</summary>
public static class BrushPreset
{
    public sealed record Preset(string Name, Brush Brush);

    public static IReadOnlyList<Preset> All { get; } = new List<Preset>
    {
        new("Round Soft", new Brush { Size = 30, Hardness = 0.2, Opacity = 1, Flow = 0.6 }),
        new("Round Hard", new Brush { Size = 20, Hardness = 0.9, Opacity = 1, Flow = 1 }),
        new("Airbrush", new Brush { Size = 40, Hardness = 0.0, Opacity = 0.7, Flow = 0.15, Spacing = 0.08 }),
        new("Pencil", new Brush { Size = 6, Hardness = 1, Opacity = 1, Flow = 1, Shape = "Square" }),
        new("Ink Pen", new Brush { Size = 10, Hardness = 1, Opacity = 1, Flow = 1 }),
        new("Charcoal", new Brush { Size = 36, Hardness = 0.1, Opacity = 0.85, Flow = 0.3, Spacing = 0.05 }),
        new("Watercolor", new Brush { Size = 50, Hardness = 0.0, Opacity = 0.5, Flow = 0.2, Spacing = 0.04 }),
        new("Soft Eraser", new Brush { Size = 28, Hardness = 0.3, Opacity = 1, Flow = 0.8 }),
        new("Hard Eraser", new Brush { Size = 18, Hardness = 1, Opacity = 1, Flow = 1, Shape = "Square" }),
    };

    public static Brush Make(string name)
    {
        var found = All.FirstOrDefault(p => p.Name == name);
        return found?.Brush.Clone() ?? new Brush();
    }
}

public static class BrushExtensions
{
    public static Brush Clone(this Brush b) => new()
    {
        Size = b.Size, Hardness = b.Hardness, Opacity = b.Opacity,
        Flow = b.Flow, Spacing = b.Spacing, Shape = b.Shape,
        Angle = b.Angle, Texture = b.Texture
    };
}
