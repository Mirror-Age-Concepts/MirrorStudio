using MirrorStudio.Core.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MirrorStudio.Video
{
    public static class FilterBuilder
    {
        private static string N(double v) =>
            v.ToString(CultureInfo.InvariantCulture);

        public static string BuildClipFilters(IReadOnlyList<EffectModel> effects)
        {
            if (effects == null || effects.Count == 0) return string.Empty;

            var filters = new List<string>();

            double brightness = 0, contrast = 1, saturation = 1, hueDeg = 0;
            bool grayscale = false, sepia = false;
            double blur = 0, sharpen = 0, vignette = 0;

            foreach (var e in effects)
            {
                switch (e.Type)
                {
                    case "Brightness": brightness = e.Amount; break;
                    case "Contrast": contrast = 1 + e.Amount / 100.0; break;
                    case "Saturation": saturation = 1 + e.Amount / 100.0; break;
                    case "Hue": hueDeg = e.Amount; break;
                    case "Blur": blur = System.Math.Max(blur, e.Amount); break;
                    case "Sharpen": sharpen = System.Math.Max(sharpen, e.Amount); break;
                    case "Opacity": break;
                    case "Grayscale": grayscale = e.Amount > 0; break;
                    case "Sepia": sepia = e.Amount > 0; break;
                    case "Vignette": vignette = e.Amount; break;
                }
            }

            var eqParts = new List<string>();
            if (System.Math.Abs(brightness) > 0.001) eqParts.Add($"brightness={N(brightness)}");
            if (System.Math.Abs(contrast - 1) > 0.001) eqParts.Add($"contrast={N(contrast)}");
            if (System.Math.Abs(saturation - 1) > 0.001) eqParts.Add($"saturation={N(saturation)}");
            if (eqParts.Count > 0) filters.Add("eq=" + string.Join(":", eqParts));

            if (System.Math.Abs(hueDeg) > 0.001) filters.Add($"hue=h={N(hueDeg)}");
            if (grayscale) filters.Add("hue=s=0");
            if (sepia) filters.Add("colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131");
            if (blur > 0.01) filters.Add($"gblur=sigma={N(blur)}");
            if (sharpen > 0.01) filters.Add($"unsharp=5:5:{N(sharpen)}");
            if (vignette > 0.001) filters.Add($"vignette=PI/{N(System.Math.Max(1, 8 - vignette * 0.07))}");

            return string.Join(",", filters);
        }

        public static string BuildAudioArgs(ClipModel clip)
        {
            var parts = new List<string>();
            if (clip.IsMuted)
            {
                parts.Add("volume=0");
            }
            else
            {
                double totalGainDb = clip.GainDb + (clip.Volume / 100.0 - 1.0) * 6.0;
                if (System.Math.Abs(totalGainDb) > 0.05) parts.Add($"volume={N(totalGainDb)}dB");
            }
            if (System.Math.Abs(clip.Pan) > 0.5)
            {
                double p = clip.Pan / 100.0;
                parts.Add($"stereotools=balance_out={N(p)}");
            }
            return parts.Count == 0 ? string.Empty : "-af \"" + string.Join(",", parts) + "\"";
        }
    }
}
