namespace StarryDisplay.Link;

/// <summary>
/// What the link server needs from the app — and nothing more. The server never touches UI
/// types; the app implements this (see App.Link.cs). Getters must be thread-safe (cached
/// snapshots); mutations are marshalled to the UI thread by the implementation.
/// </summary>
public interface IDisplayHost
{
    DisplayState GetDisplay();
    PlaybackSummary GetPlayback();
    ContextState? GetContext();

    /// <param name="actor">Who asked (paired device name), for the log.</param>
    Task<DisplayState> ApplyAsync(DisplayPatch patch, string actor);
    Task SetContextAsync(ContextState? context);

    /// <summary>Raised (any thread) when the display config changes, whatever the source (Mac, tray, toolbar).</summary>
    event Action? DisplayChanged;
    event Action? PlaybackChanged;
    event Action? ContextChanged;
}
