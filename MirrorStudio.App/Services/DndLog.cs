using System;
using System.IO;

namespace MirrorStudio.App.Services;

/// <summary>
/// Lightweight file logger for drag &amp; drop diagnostics.
/// Writes to %TEMP%\mirrorstudio-dnd.log so drag failures can be traced
/// stage-by-stage without attaching a debugger.
/// </summary>
public static class DndLog
{
    public static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MirrorStudio",
        "dnd.log");

    private static readonly object _gate = new();

    public static void W(string msg)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
            }
        }
        catch
        {
            // never let logging break interaction
        }
    }

    public static void Reset()
    {
        try { File.Delete(LogPath); } catch { /* ignore */ }
    }
}
