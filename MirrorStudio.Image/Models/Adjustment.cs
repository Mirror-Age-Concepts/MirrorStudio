using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MirrorStudio.Image.Models;

/// <summary>
/// A single non-destructive tone/colour adjustment. Parameters are stored as
/// a flat set of named doubles so the whole thing round-trips through JSON,
/// while the actual pixel maths live in <see cref="AdjustmentProcessor"/>.
/// </summary>
public sealed class Adjustment
{
    // adjustment type ids
    public const string Brightness = "Brightness";
    public const string Contrast = "Contrast";
    public const string Exposure = "Exposure";
    public const string Saturation = "Saturation";
    public const string Temperature = "Temperature";
    public const string Tint = "Tint";
    public const string Highlights = "Highlights";
    public const string Shadows = "Shadows";
    public const string Gamma = "Gamma";
    public const string Invert = "Invert";
    public const string Levels = "Levels";
    public const string Curves = "Curves";
    public const string HSL = "HSL";
    public const string ColorBalance = "ColorBalance";
    public const string Grayscale = "Grayscale";
    public const string Sepia = "Sepia";
    public const string Vibrance = "Vibrance";
    public const string WhiteBalance = "WhiteBalance";

    public string Type { get; set; } = Brightness;

    [JsonInclude]
    public Dictionary<string, double> Parameters { get; set; } = new();

    public Adjustment Clone() => new() { Type = Type, Parameters = new Dictionary<string, double>(Parameters) };

    // convenient parameter accessors
    public double Get(string key, double fallback = 0) =>
        Parameters.TryGetValue(key, out var v) ? v : fallback;

    public void Set(string key, double value) => Parameters[key] = value;
}
