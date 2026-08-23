using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace MirrorStudio.App.Services;

/// <summary>
/// Shared interaction wiring for media-library cards (drag to any drop target,
/// double-click to insert). Used by Video/Audio/Image studio alike.
///
/// Gesture rules that make drag AND double-click work together:
///  - PointerPressed is NOT marked handled — Avalonia's tap recognizer needs it
///    to raise Tapped/DoubleTapped.
///  - Tapping must never rebuild the card list mid-gesture; detaching the
///    pressed card kills the in-flight drag before PointerMoved can start it.
/// </summary>
public static class MediaCardFactory
{
    /// <summary>Drag payload format carrying a project-relative media path.</summary>
    public const string MediaDropFormat = "mirrorstudio/media-path";

    /// <summary>True while a card drag is in flight (views suppress shortcuts).</summary>
    public static bool IsDragging { get; private set; }

    public static void MakeInteractive(
        Border card,
        string dropPayload,
        Action tap,
        Action doubleTap)
    {
        Point? pressPoint = null;

        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            pressPoint = e.GetPosition(card);
            tap();
        };

        card.PointerMoved += async (_, e) =>
        {
            if (pressPoint == null || !e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
                return;

            var d = e.GetPosition(card) - pressPoint.Value;
            if (Math.Sqrt(d.X * d.X + d.Y * d.Y) < 6) return;   // drag threshold

            pressPoint = null;
            var data = new DataObject();
            data.Set(MediaDropFormat, dropPayload);
            IsDragging = true;
            try
            {
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
            }
            finally
            {
                IsDragging = false;
            }
        };

        card.PointerReleased += (_, _) => pressPoint = null;
        card.DoubleTapped += (_, _) => { pressPoint = null; doubleTap(); };
    }

    /// <summary>Reads the dragged media path from a drop/drag-over event.</summary>
    public static string? PayloadFrom(DragEventArgs e) =>
        e.Data?.Get(MediaDropFormat) is string rel && rel.Length > 0 ? rel : null;
}
