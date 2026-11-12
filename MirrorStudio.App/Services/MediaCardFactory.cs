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
        bool dragStarted = false;
        bool loggedFirstMove = false;

        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            pressPoint = e.GetPosition(card);
            dragStarted = false;
            loggedFirstMove = false;
            // capture so we keep receiving moves even if the cursor briefly
            // leaves the card; released below before the OLE drag loop takes over
            e.Pointer.Capture(card);
            DndLog.W($"SRC press on '{dropPayload}' at {pressPoint}");
            // NOTE: no side-effects here. Selection runs on PointerReleased —
            // anything heavier than a border restyle (e.g. SelectMedia →
            // timeline rebuild + preview render) stalls the input pump, and
            // Windows coalesces WM_MOUSEMOVE while it is blocked, so the
            // whole drag gesture would be lost before the threshold check.
        };

        card.PointerMoved += async (_, e) =>
        {
            if (pressPoint == null || !e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
                return;

            var d = e.GetPosition(card) - pressPoint.Value;
            if (!loggedFirstMove)
            {
                loggedFirstMove = true;
                DndLog.W($"SRC first move d=({d.X:0},{d.Y:0}) pressed={e.GetCurrentPoint(card).Properties.IsLeftButtonPressed}");
            }
            if (Math.Sqrt(d.X * d.X + d.Y * d.Y) < 6) return;   // drag threshold

            pressPoint = null;
            dragStarted = true;
            e.Pointer.Capture(null);
            var data = new DataObject();
            data.Set(MediaDropFormat, dropPayload);
            IsDragging = true;
            DndLog.W($"SRC DoDragDrop starting payload='{dropPayload}'");
            try
            {
                var result = await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
                DndLog.W($"SRC DoDragDrop finished effect={result}");
            }
            catch (Exception ex)
            {
                DndLog.W($"SRC DoDragDrop EXCEPTION {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                IsDragging = false;
            }
        };

        card.PointerReleased += (_, _) =>
        {
            // true click: gesture stayed within threshold and no drag began
            if (pressPoint != null && !dragStarted)
            {
                DndLog.W("SRC click -> tap");
                tap();
            }
            pressPoint = null;
        };
        card.PointerCaptureLost += (_, _) =>
        {
            if (pressPoint != null) DndLog.W("SRC capture lost before drag started");
            pressPoint = null;
        };
        card.DoubleTapped += (_, _) => { pressPoint = null; doubleTap(); };
    }

    /// <summary>Reads the dragged media path from a drop/drag-over event.</summary>
    public static string? PayloadFrom(DragEventArgs e) =>
        e.Data?.Get(MediaDropFormat) is string rel && rel.Length > 0 ? rel : null;
}
