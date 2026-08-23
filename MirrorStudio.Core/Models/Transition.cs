using System;

namespace MirrorStudio.Core.Models
{
    public class Transition
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ClipId { get; set; }

        /// <summary>Kind name matching runtime TransitionKind ("Cut", "Fade", "CrossDissolve", ...).</summary>
        public string Kind { get; set; } = "Cut";

        /// <summary>Overlap length in seconds.</summary>
        public double DurationSeconds { get; set; } = 0.5;

        public static string FromRuntimeKind(TransitionKind kind) => kind switch
        {
            TransitionKind.Fade => "Fade",
            TransitionKind.CrossDissolve => "CrossDissolve",
            TransitionKind.DipToBlack => "DipToBlack",
            TransitionKind.DipToWhite => "DipToWhite",
            TransitionKind.Wipe => "Wipe",
            TransitionKind.Slide => "Slide",
            TransitionKind.Zoom => "Zoom",
            _ => "Cut"
        };

        public TransitionKind ToRuntimeKind() => Kind switch
        {
            "Fade" => TransitionKind.Fade,
            "CrossDissolve" => TransitionKind.CrossDissolve,
            "DipToBlack" => TransitionKind.DipToBlack,
            "DipToWhite" => TransitionKind.DipToWhite,
            "Wipe" => TransitionKind.Wipe,
            "Slide" => TransitionKind.Slide,
            "Zoom" => TransitionKind.Zoom,
            _ => TransitionKind.Cut
        };
    }
}
