using System.Windows;
using StarryDisplay.Core;

namespace StarryDisplay.Views;

/// <summary>
/// A way of presenting the now-playing state. Compositions are interchangeable views over the
/// same <see cref="NowPlayingViewModel"/>; add a new one by implementing this on a UserControl
/// and registering it in <c>MainWindow.CreateComposition</c>.
/// </summary>
public interface IComposition
{
    /// <summary>Settings key, e.g. "stage".</summary>
    string Id { get; }

    /// <summary>True if the composition draws its own opaque card; false if it floats on the desktop.</summary>
    bool IsOpaque { get; }

    /// <summary>Default window size (DIPs) the first time this composition is shown.</summary>
    Size DefaultSize { get; }

    FrameworkElement View { get; }

    void Attach(NowPlayingViewModel vm, AppSettings settings);
    void Detach();
    void ApplySettings(AppSettings settings);
}
