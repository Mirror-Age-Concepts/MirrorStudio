using System;
using MirrorStudio.Core.Enums;

namespace MirrorStudio.Core.Models
{
    public class Effect
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ClipId { get; set; }

        public EffectType Type { get; set; }

        /// <summary>Normalized strength 0..100.</summary>
        public double Amount { get; set; }

        public bool Enabled { get; set; } = true;

        public static EffectType ParseName(string name) => name switch
        {
            "Blur" => EffectType.Blur,
            "Brightness" => EffectType.Brightness,
            "Contrast" => EffectType.Contrast,
            "Saturation" => EffectType.Saturation,
            "Sharpen" => EffectType.Sharpen,
            "Grayscale" or "Black & White" => EffectType.Grayscale,
            "Sepia" => EffectType.Sepia,
            "Invert" => EffectType.Invert,
            "Vignette" => EffectType.Vignette,
            "Noise Reduction" => EffectType.NoiseReduction,
            _ => EffectType.None
        };
    }
}
