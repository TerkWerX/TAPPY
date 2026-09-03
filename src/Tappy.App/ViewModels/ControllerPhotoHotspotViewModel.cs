using Tappy.App.Services;

namespace Tappy.App.ViewModels;

public sealed class ControllerPhotoHotspotViewModel(
    ControlTileViewModel tile,
    ControllerPhotoHotspotDefinition definition)
{
    public ControlTileViewModel Tile { get; } = tile ?? throw new ArgumentNullException(nameof(tile));
    public double Left { get; } = definition.Left;
    public double Top { get; } = definition.Top;
    public double Width { get; } = definition.Width;
    public double Height { get; } = definition.Height;
    public double CornerRadius { get; } = definition.Shape == ControllerPhotoHotspotShape.Ellipse
        ? Math.Max(definition.Width, definition.Height)
        : 13;
}
