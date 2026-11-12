using LibVLCSharp.Shared;

namespace MirrorStudio.App.Services;

/// <summary>
/// Lazily initializes the LibVLC native core the first time it is actually
/// needed, guarding the call so a native loader failure (e.g. DLL conflicts
/// under Git Bash / MINGW) degrades gracefully instead of taking down the
/// whole process with a Segmentation fault at startup.
/// </summary>
public static class LibVlcLoader
{
    private static readonly object Lock = new();
    private static bool _tried;
    private static bool _ok;

    public static bool Available
    {
        get
        {
            if (_tried) return _ok;
            lock (Lock)
            {
                if (_tried) return _ok;
                _tried = true;
                try
                {
                    LibVLCSharp.Shared.Core.Initialize();
                    _ok = true;
                }
                catch (System.Exception ex)
                {
                    DndLog.W($"LibVLC init unavailable: {ex.Message}");
                    _ok = false;
                }
                return _ok;
            }
        }
    }

    /// <summary>Call before constructing any LibVLC media player.</summary>
    public static void EnsureInitialized() => _ = Available;
}
