using System.ComponentModel;
using Tappy.App.Services;

namespace Tappy.App.ViewModels;

public sealed class ControllerPhotoHotspotViewModel : ObservableObject, IDisposable
{
    private readonly List<ControlTileViewModel> _tiles = [];
    private readonly ControllerPhotoHotspotDefinition _definition;

    public ControllerPhotoHotspotViewModel(
        ControlTileViewModel tile,
        ControllerPhotoHotspotDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Tile = tile ?? throw new ArgumentNullException(nameof(tile));
        AddTile(tile);
    }

    public ControlTileViewModel Tile { get; }
    public string VisualId => _definition.EffectiveVisualId;
    public double Left => _definition.Left;
    public double Top => _definition.Top;
    public double Width => _definition.Width;
    public double Height => _definition.Height;
    public double CornerRadius => _definition.Shape == ControllerPhotoHotspotShape.Ellipse
        ? Math.Max(_definition.Width, _definition.Height)
        : 13;
    public bool IsButton => _definition.Kind == ControllerPhotoControlKind.Button;
    public bool IsLinear => _definition.Kind is ControllerPhotoControlKind.Fader;
    public bool IsRotary => _definition.Kind is
        ControllerPhotoControlKind.Potentiometer or ControllerPhotoControlKind.RotaryEncoder;
    public bool IsVertical => _definition.Orientation == ControllerPhotoControlOrientation.Vertical;
    public bool IsHorizontal => !IsVertical;
    public bool IsSelected => _tiles.Any(item => item.IsSelected);
    public bool IsIlluminated => _tiles.Any(item => item.IsIlluminated);
    public bool HasCustomColor => _tiles.Any(item => item.HasCustomColor);
    public string TileBackground => _tiles.FirstOrDefault(item => item.HasCustomColor)?.TileBackground ??
                                    Tile.TileBackground;
    public double IndicatorWidth => _definition.IndicatorWidth;
    public double IndicatorHeight => _definition.IndicatorHeight;
    public double IndicatorLeft => Interpolate(
        _definition.MinimumIndicatorLeft,
        _definition.MaximumIndicatorLeft,
        CurrentAnalogPosition);
    public double IndicatorTop => Interpolate(
        _definition.MinimumIndicatorTop,
        _definition.MaximumIndicatorTop,
        CurrentAnalogPosition);
    public double IndicatorAngle => _definition.Kind == ControllerPhotoControlKind.RotaryEncoder
        ? CurrentAnalogTile.EncoderAngle
        : _definition.RotaryStartAngle + (_definition.RotarySweepAngle * CurrentAnalogPosition);
    public double RotationCenterX => Width / 2;
    public double RotationCenterY => Height / 2;
    public double RotaryLineEndY => Math.Max(5, Height * 0.16);
    public string PositionText => _definition.Kind == ControllerPhotoControlKind.RotaryEncoder
        ? $"{IndicatorAngle:0}°"
        : $"{CurrentAnalogPosition:P0}";

    private ControlTileViewModel CurrentAnalogTile =>
        _tiles.LastOrDefault(item => item.HasAnalogValue) ?? Tile;

    private double CurrentAnalogPosition => CurrentAnalogTile.AnalogPosition;

    public void AddTile(ControlTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (_tiles.Contains(tile))
        {
            return;
        }

        _tiles.Add(tile);
        tile.PropertyChanged += Tile_OnPropertyChanged;
        RaiseAllVisualProperties();
    }

    public void Dispose()
    {
        foreach (var tile in _tiles)
        {
            tile.PropertyChanged -= Tile_OnPropertyChanged;
        }

        _tiles.Clear();
    }

    private void Tile_OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(ControlTileViewModel.IsSelected) or
            nameof(ControlTileViewModel.IsIlluminated) or
            nameof(ControlTileViewModel.HasCustomColor) or
            nameof(ControlTileViewModel.TileBackground) or
            nameof(ControlTileViewModel.AnalogPosition) or
            nameof(ControlTileViewModel.EncoderAngle) or
            nameof(ControlTileViewModel.HasAnalogValue))
        {
            RaiseAllVisualProperties();
        }
    }

    private void RaiseAllVisualProperties()
    {
        Raise(nameof(IsSelected));
        Raise(nameof(IsIlluminated));
        Raise(nameof(HasCustomColor));
        Raise(nameof(TileBackground));
        Raise(nameof(IndicatorLeft));
        Raise(nameof(IndicatorTop));
        Raise(nameof(IndicatorAngle));
        Raise(nameof(PositionText));
    }

    private static double Interpolate(double start, double end, double position) =>
        start + ((end - start) * Math.Clamp(position, 0, 1));
}
