using System.Text.Json.Serialization;

namespace MirrorStudio.Graphics.Models;

/// <summary>
/// Base for everything placed over video: text, shapes and images.
/// Position/scale are in project pixel coordinates (e.g. 1920x1080).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextLayer), "text")]
[JsonDerivedType(typeof(ShapeLayer), "shape")]
[JsonDerivedType(typeof(ImageLayer), "image")]
public abstract class GraphicLayer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Timeline start in seconds.</summary>
    public double Start { get; set; }

    public double Duration { get; set; } = 5;

    /// <summary>Center of the layer, project pixels.</summary>
    public double PositionX { get; set; }
    public double PositionY { get; set; }

    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    public double Rotation { get; set; }
    public double Opacity { get; set; } = 1;
    public bool Visible { get; set; } = true;

    /// <summary>Entrance preset name — see Animation.AnimationPresets.</summary>
    public string AnimationIn { get; set; } = "";

    /// <summary>Exit preset name.</summary>
    public string AnimationOut { get; set; } = "";

    public double AnimationInDuration { get; set; } = 0.6;
    public double AnimationOutDuration { get; set; } = 0.6;

    /// <summary>Optional per-property keyframes overriding static values.</summary>
    public List<MotionKeyframe> Keyframes { get; set; } = new();

    public bool IsActiveAt(double timelineSeconds) =>
        Visible && timelineSeconds >= Start && timelineSeconds < Start + Duration;

    [JsonIgnore]
    public string StartText => TimeSpan.FromSeconds(Start).ToString(@"hh\:mm\:ss");

    [JsonIgnore]
    public string Preview => this switch
    {
        TextLayer t => t.Text,
        ShapeLayer s => s.ShapeType,
        ImageLayer i => System.IO.Path.GetFileName(i.ImagePath),
        _ => GetType().Name
    };
}
