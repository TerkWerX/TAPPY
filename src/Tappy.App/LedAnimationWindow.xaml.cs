using System.Collections.ObjectModel;
using System.Globalization;
using MessageBox = System.Windows.MessageBox;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Tappy.App.ViewModels;
using Tappy.Core.Models;

namespace Tappy.App;

/// <summary>Cell width from AspectRatio at a fixed base height, so a canvas cell reads as portrait or landscape like the real pad instead of a uniform square.</summary>
internal sealed class FrameCellAspectRatioToWidthConverter : IValueConverter
{
    public const double BaseHeight = 24;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double aspectRatio && aspectRatio > 0 ? BaseHeight * aspectRatio : BaseHeight;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Non-modal by design (Show, not ShowDialog) — playing an animation is an
/// ongoing, live operation the user should be able to leave running while doing
/// other things, unlike every other editor in this app. Styling relies entirely
/// on inherited theme brushes, matching FireColorPickerWindow's minimal approach.
/// </summary>
public partial class LedAnimationWindow : Window
{
    private readonly ControllerWorkspace _workspace;
    private readonly ControllerTab _tab;
    private readonly LedAnimationEditorViewModel _viewModel;
    private readonly DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    public LedAnimationWindow(ControllerWorkspace workspace, ControllerTab tab)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _tab = tab ?? throw new ArgumentNullException(nameof(tab));
        InitializeComponent();
        _viewModel = new LedAnimationEditorViewModel(LoadOrCreateLibrary(), workspace);
        _viewModel.Edited += (_, _) => { _autosaveTimer.Stop(); _autosaveTimer.Start(); };
        DataContext = _viewModel;
        _autosaveTimer.Tick += (_, _) => { _autosaveTimer.Stop(); PersistLibrary(); };
        // The window is non-modal and can stay open across a reconnect or a
        // device being confirmed/disconnected in another tab, so the
        // participant checkbox list has to track live workspace membership,
        // not just what was confirmed the moment the window opened.
        _workspace.VisibleTabs.CollectionChanged += (_, _) => _viewModel.RefreshParticipantChoices();
        Closed += (_, _) =>
        {
            _autosaveTimer.Stop();
            PersistLibrary();
            _workspace.AnimationPlayer.Stop();
        };
    }

    private LedAnimationLibrary LoadOrCreateLibrary()
    {
        var snapshot = _workspace.AnimationLibrary?.Load() ?? LedAnimationLibrarySnapshot.Empty;
        var library = snapshot.ToEditableProfile();
        if (library.Animations.Count == 0)
        {
            library.Animations.Add(NewAnimationDefaultingToThisTab());
        }

        library.Normalize();
        return library;
    }

    /// <summary>
    /// A brand-new animation defaults to just the tab the editor was opened
    /// from as its sole participant (offset 0,0) — Play still does something
    /// reasonable without the owner having to touch the participant
    /// checkboxes at all, matching Milestone 1's exact single-device behavior
    /// for anyone who ignores the multi-device feature entirely.
    /// </summary>
    private LedAnimationDefinition NewAnimationDefaultingToThisTab()
    {
        var animation = new LedAnimationDefinition { Name = "New animation" };
        var targets = _workspace.GetAnimationTargets();
        if (targets.FirstOrDefault(target => ReferenceEquals(target.Tab, _tab)) is { } ownTarget)
        {
            animation.Participants.Add(new LedAnimationParticipant
            {
                ProviderId = ownTarget.ProviderId,
                PersistentId = ownTarget.PersistentId,
                DisplayName = ownTarget.DisplayName,
            });
            LedAnimationParticipantLayout.Rearrange(animation, targets);
        }

        return animation;
    }

    private void PersistLibrary()
    {
        if (_workspace.AnimationLibrary is not { } store)
        {
            return;
        }

        foreach (var animation in _viewModel.Library) { animation.Normalize(); }
        var library = new LedAnimationLibrary
        {
            Animations = [.. _viewModel.Library],
            LastOpenAnimationId = _viewModel.Current?.Id,
        };
        library.Normalize();
        store.Save(new LedAnimationLibrarySnapshot(library));
    }

    private void Play_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Current is not { } current)
        {
            return;
        }

        _viewModel.Status = _workspace.AnimationPlayer.Play(current, _workspace)
            ? "Playing."
            : "No checked participant is a confirmed controller with an animatable LED grid.";
    }

    private void Stop_OnClick(object sender, RoutedEventArgs e)
    {
        _workspace.AnimationPlayer.Stop();
        _viewModel.Status = "Stopped.";
    }

    private void NewAnimation_OnClick(object sender, RoutedEventArgs e)
    {
        var animation = NewAnimationDefaultingToThisTab();
        _viewModel.Library.Add(animation);
        _viewModel.SelectedLibraryEntry = animation;
        PersistLibrary();
    }

    private void DuplicateAnimation_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Current is not { } current)
        {
            return;
        }

        var copy = current.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = current.Name + " copy";
        _viewModel.Library.Add(copy);
        _viewModel.SelectedLibraryEntry = copy;
        PersistLibrary();
    }

    private void RenameAnimation_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Current is null)
        {
            return;
        }

        // The Name text box above already renames on every keystroke (debounced
        // autosave); this button exists so renaming is discoverable without
        // requiring the user to notice the text box is editable.
        _viewModel.Status = "Edit the Name field above to rename this animation.";
    }

    private void DeleteAnimation_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Current is not { } current)
        {
            return;
        }

        if (MessageBox.Show(this, $"Delete \"{current.Name}\"? This cannot be undone.", "Delete animation",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _workspace.AnimationPlayer.Stop();
        _viewModel.Library.Remove(current);
        if (_viewModel.Library.Count == 0)
        {
            _viewModel.Library.Add(NewAnimationDefaultingToThisTab());
        }

        _viewModel.SelectedLibraryEntry = _viewModel.Library[0];
        PersistLibrary();
    }

    private void AddFrame_OnClick(object sender, RoutedEventArgs e) => _viewModel.AddFrame();

    private void DuplicateFrame_OnClick(object sender, RoutedEventArgs e) => _viewModel.DuplicateFrame();

    private void DeleteFrame_OnClick(object sender, RoutedEventArgs e) => _viewModel.DeleteFrame();

    private void MoveFrameUp_OnClick(object sender, RoutedEventArgs e) => _viewModel.MoveFrameUp();

    private void MoveFrameDown_OnClick(object sender, RoutedEventArgs e) => _viewModel.MoveFrameDown();

    private void FrameCell_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FrameCellViewModel cell })
        {
            _viewModel.PaintCell(cell);
        }
    }

    private void OpenFramePaintColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        var (initialRed, initialGreen, initialBlue) =
            MainViewModel.TryParseHexColor(_viewModel.SelectedPaintColor.Key, out var red, out var green, out var blue)
                ? (red, green, blue)
                : ((byte)255, (byte)255, (byte)255);
        var picker = new FireColorPickerWindow(initialRed, initialGreen, initialBlue) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            _viewModel.SetCustomPaintColor($"#{picker.SelectedRed:X2}{picker.SelectedGreen:X2}{picker.SelectedBlue:X2}");
        }
    }
}

/// <summary>One row in the Participants checkbox list. Toggling it edits the current animation's Participants directly.</summary>
internal sealed class LedAnimationParticipantChoice(
    LedAnimationTarget target, bool isChecked, Action<LedAnimationTarget, bool> onToggled) : ObservableObject
{
    private bool _isChecked = isChecked;

    public LedAnimationTarget Target { get; } = target;
    public string DisplayName => Target.DisplayName;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!Set(ref _isChecked, value)) { return; }
            onToggled(Target, value);
        }
    }
}

/// <summary>One row in the frame timeline. Wraps one LedAnimationFrame; editing DurationMs writes straight through.</summary>
internal sealed class LedAnimationFrameChoice(LedAnimationFrame frame, int number, Action onEdited) : ObservableObject
{
    public LedAnimationFrame Frame { get; } = frame;

    public int Number { get; } = number;

    public string Label => $"Frame {Number} · {DurationMs} ms";

    public int DurationMs
    {
        get => Frame.DurationMs;
        set
        {
            var clamped = Math.Clamp(value, 16, 60_000);
            if (Frame.DurationMs == clamped)
            {
                return;
            }

            Frame.DurationMs = clamped;
            Raise();
            Raise(nameof(Label));
            onEdited();
        }
    }
}

/// <summary>One cell of the paintable canvas for the currently selected frame.</summary>
internal sealed class FrameCellViewModel(int column, int row) : ObservableObject
{
    private string _colorKey = "Default";
    private string _background = "#202936";
    private double _aspectRatio = 1.0;

    public int Column { get; } = column;
    public int Row { get; } = row;

    public string ColorKey
    {
        get => _colorKey;
        set => Set(ref _colorKey, value);
    }

    public string Background
    {
        get => _background;
        set => Set(ref _background, value);
    }

    /// <summary>Width÷height, from the owning participant's ILedGridAdapter.PadAspectRatio — how this cell should render, not just a fixed square.</summary>
    public double AspectRatio
    {
        get => _aspectRatio;
        set => Set(ref _aspectRatio, value);
    }
}

/// <summary>Bindable wrapper around the in-progress animation library edit session.</summary>
internal sealed class LedAnimationEditorViewModel : ObservableObject
{
    private readonly ControllerWorkspace _workspace;
    private LedAnimationDefinition? _current;
    private string _status = "Ready.";
    private LedAnimationFrameChoice? _selectedFrameChoice;
    private TileColorChoice _selectedPaintColor = MainViewModel.AllTileColorChoices[1]; // Red — visible on the first paint

    public LedAnimationEditorViewModel(LedAnimationLibrary library, ControllerWorkspace workspace)
    {
        _workspace = workspace;
        Library = new ObservableCollection<LedAnimationDefinition>(library.Animations);
        var startAt = library.LastOpenAnimationId is { } id
            ? Library.FirstOrDefault(a => a.Id == id) ?? Library[0]
            : Library[0];
        _current = startAt;
        RefreshParticipantChoices();
        RefreshFrameChoices();
    }

    public event EventHandler? Edited;

    public ObservableCollection<LedAnimationDefinition> Library { get; }
    public IReadOnlyList<LedEffectKind> EffectKinds { get; } = Enum.GetValues<LedEffectKind>();
    public ObservableCollection<LedAnimationParticipantChoice> ParticipantChoices { get; } = [];
    public ObservableCollection<LedAnimationFrameChoice> FrameChoices { get; } = [];
    public ObservableCollection<FrameCellViewModel> CanvasCells { get; } = [];
    public ObservableCollection<TileColorChoice> PaintColorChoices { get; } = new(MainViewModel.AllTileColorChoices);

    public LedAnimationDefinition? Current => _current;

    public LedAnimationDefinition? SelectedLibraryEntry
    {
        get => _current;
        set
        {
            if (value is null || ReferenceEquals(_current, value)) { return; }
            _current = value;
            RaiseAll();
            RefreshParticipantChoices();
            RefreshFrameChoices();
        }
    }

    public string Status { get => _status; set => Set(ref _status, value); }

    /// <summary>
    /// Rebuilds the checkbox list from the workspace's current animation
    /// targets — called on load, whenever the selected library entry
    /// changes, and whenever the workspace's visible tabs change (a device
    /// connecting, disconnecting, or being confirmed while this non-modal
    /// window stays open).
    /// </summary>
    public void RefreshParticipantChoices()
    {
        var targets = _workspace.GetAnimationTargets();
        ParticipantChoices.Clear();
        foreach (var target in targets)
        {
            var isChecked = _current?.Participants.Any(participant =>
                participant.ProviderId == target.ProviderId && participant.PersistentId == target.PersistentId) ?? false;
            ParticipantChoices.Add(new LedAnimationParticipantChoice(target, isChecked, OnParticipantToggled));
        }
    }

    private void OnParticipantToggled(LedAnimationTarget target, bool isChecked)
    {
        if (_current is null)
        {
            return;
        }

        var already = _current.Participants.Any(participant =>
            participant.ProviderId == target.ProviderId && participant.PersistentId == target.PersistentId);
        if (isChecked && !already)
        {
            _current.Participants.Add(new LedAnimationParticipant
            {
                ProviderId = target.ProviderId,
                PersistentId = target.PersistentId,
                DisplayName = target.DisplayName,
            });
        }
        else if (!isChecked && already)
        {
            _current.Participants.RemoveAll(participant =>
                participant.ProviderId == target.ProviderId && participant.PersistentId == target.PersistentId);
        }

        LedAnimationParticipantLayout.Rearrange(_current, _workspace.GetAnimationTargets());
        // The canvas can grow or shrink with the participant set (Rearrange
        // recomputes CanvasColumns/Rows); the paint grid must track that even
        // though the selected frame object itself hasn't changed.
        RefreshCanvasCells();
        RaiseEdited();
    }

    public bool IsEffectMode
    {
        get => _current?.Kind == LedAnimationKind.Effect;
        set { if (_current is null || !value) { return; } _current.Kind = LedAnimationKind.Effect; Raise(); RaiseEdited(); }
    }

    public bool IsFramesMode
    {
        get => _current?.Kind == LedAnimationKind.Frames;
        set
        {
            if (_current is null || !value)
            {
                return;
            }

            _current.Kind = LedAnimationKind.Frames;
            RefreshFrameChoices();
            Raise();
            RaiseEdited();
        }
    }

    public LedAnimationFrameChoice? SelectedFrameChoice
    {
        get => _selectedFrameChoice;
        set
        {
            if (!Set(ref _selectedFrameChoice, value))
            {
                return;
            }

            RefreshCanvasCells();
        }
    }

    public TileColorChoice SelectedPaintColor
    {
        get => _selectedPaintColor;
        set => Set(ref _selectedPaintColor, value ?? MainViewModel.AllTileColorChoices[1]);
    }

    public void SetCustomPaintColor(string hex) => SelectedPaintColor = MainViewModel.BuildCustomColorChoice(hex);

    /// <summary>
    /// Rebuilds the frame timeline from the current animation's Frames list —
    /// called on load, on a library-entry switch, on every structural frame edit,
    /// and on switching to Frames mode. Re-selects whichever frame
    /// <paramref name="preferSelected"/> names (by reference), falling back to
    /// whatever was already selected, then to the first frame. A Frames-kind
    /// animation is never left with zero frames here — covers a brand-new
    /// animation switching into Frames mode for the first time (nothing painted
    /// yet) exactly as much as a Frames-kind animation loaded from disk that
    /// somehow has none, so Play never dead-ends on "no frames" regardless of how
    /// the animation got into that state.
    /// </summary>
    public void RefreshFrameChoices(LedAnimationFrame? preferSelected = null)
    {
        if (_current is { Kind: LedAnimationKind.Frames, Frames.Count: 0 })
        {
            _current.Frames.Add(new LedAnimationFrame());
        }

        var preferred = preferSelected ?? SelectedFrameChoice?.Frame;
        FrameChoices.Clear();
        if (_current is null)
        {
            SelectedFrameChoice = null;
            return;
        }

        for (var index = 0; index < _current.Frames.Count; index++)
        {
            FrameChoices.Add(new LedAnimationFrameChoice(_current.Frames[index], index + 1, RaiseEdited));
        }

        SelectedFrameChoice = FrameChoices.FirstOrDefault(choice => ReferenceEquals(choice.Frame, preferred))
            ?? FrameChoices.FirstOrDefault();
    }

    /// <summary>
    /// Rebuilds the paintable canvas from the selected frame's Cells for the
    /// current animation's CanvasColumns x CanvasRows — called whenever the
    /// selected frame or the canvas size (driven by checked participants) changes.
    /// </summary>
    /// <summary>
    /// Flattened from Current.CanvasColumns for the canvas ItemsPanel's binding —
    /// LedAnimationDefinition is a plain model with no INotifyPropertyChanged, so
    /// a direct "Current.CanvasColumns" binding would never see it change after
    /// participant toggles resize the canvas; this property is explicitly raised
    /// every time RefreshCanvasCells runs instead.
    /// </summary>
    public int CanvasColumns => _current?.CanvasColumns ?? 16;

    private void RefreshCanvasCells()
    {
        CanvasCells.Clear();
        Raise(nameof(CanvasColumns));
        if (_current is null)
        {
            return;
        }

        var cells = SelectedFrameChoice?.Frame.Cells;
        var columnRanges = ParticipantColumnRanges(_current);
        for (var row = 0; row < _current.CanvasRows; row++)
        {
            for (var column = 0; column < _current.CanvasColumns; column++)
            {
                var key = cells?.GetValueOrDefault($"{column},{row}", "Default") ?? "Default";
                CanvasCells.Add(new FrameCellViewModel(column, row)
                {
                    ColorKey = key,
                    Background = ResolvePaintColorChoice(key).Background,
                    AspectRatio = AspectRatioAt(columnRanges, column),
                });
            }
        }
    }

    /// <summary>
    /// Each participant's own adapter shape applies to its own slice of the
    /// shared canvas — a Fire's portrait pads next to an APC MINI's landscape
    /// ones, not one uniform shape across the whole width.
    /// </summary>
    private IReadOnlyList<(int OffsetColumn, int Columns, double AspectRatio)> ParticipantColumnRanges(
        LedAnimationDefinition definition)
    {
        var targets = _workspace.GetAnimationTargets();
        return definition.Participants
            .Select(participant => targets.FirstOrDefault(target =>
                target.ProviderId == participant.ProviderId && target.PersistentId == participant.PersistentId) is { } target
                ? (participant.CanvasOffsetColumn, target.Adapter.Columns, target.Adapter.PadAspectRatio)
                : ((int, int, double)?)null)
            .Where(range => range is not null)
            .Select(range => range!.Value)
            .ToArray();
    }

    private static double AspectRatioAt(IReadOnlyList<(int OffsetColumn, int Columns, double AspectRatio)> ranges, int column)
    {
        foreach (var (offsetColumn, columns, aspectRatio) in ranges)
        {
            if (column >= offsetColumn && column < offsetColumn + columns)
            {
                return aspectRatio;
            }
        }

        return 1.0;
    }

    private static TileColorChoice ResolvePaintColorChoice(string key) =>
        MainViewModel.AllTileColorChoices.FirstOrDefault(choice =>
            string.Equals(choice.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? MainViewModel.BuildCustomColorChoice(MainViewModel.IsHexColor(key) ? key : "#202936");

    public void AddFrame()
    {
        if (_current is null)
        {
            return;
        }

        var frame = new LedAnimationFrame();
        _current.Frames.Add(frame);
        RefreshFrameChoices(frame);
        RaiseEdited();
    }

    public void DuplicateFrame()
    {
        if (_current is null || SelectedFrameChoice is not { } selected)
        {
            return;
        }

        var index = _current.Frames.IndexOf(selected.Frame);
        if (index < 0)
        {
            return;
        }

        var copy = selected.Frame.Clone();
        _current.Frames.Insert(index + 1, copy);
        RefreshFrameChoices(copy);
        RaiseEdited();
    }

    /// <summary>Refuses to leave zero frames — the same rule DeleteAnimation_OnClick already applies to the whole library.</summary>
    public void DeleteFrame()
    {
        if (_current is null || SelectedFrameChoice is not { } selected)
        {
            return;
        }

        var index = _current.Frames.IndexOf(selected.Frame);
        _current.Frames.Remove(selected.Frame);
        if (_current.Frames.Count == 0)
        {
            _current.Frames.Add(new LedAnimationFrame());
        }

        var nextIndex = Math.Clamp(index, 0, _current.Frames.Count - 1);
        RefreshFrameChoices(_current.Frames[nextIndex]);
        RaiseEdited();
    }

    public void MoveFrameUp()
    {
        if (_current is null || SelectedFrameChoice is not { } selected)
        {
            return;
        }

        var index = _current.Frames.IndexOf(selected.Frame);
        if (index <= 0)
        {
            return;
        }

        (_current.Frames[index - 1], _current.Frames[index]) = (_current.Frames[index], _current.Frames[index - 1]);
        RefreshFrameChoices(selected.Frame);
        RaiseEdited();
    }

    public void MoveFrameDown()
    {
        if (_current is null || SelectedFrameChoice is not { } selected)
        {
            return;
        }

        var index = _current.Frames.IndexOf(selected.Frame);
        if (index < 0 || index >= _current.Frames.Count - 1)
        {
            return;
        }

        (_current.Frames[index + 1], _current.Frames[index]) = (_current.Frames[index], _current.Frames[index + 1]);
        RefreshFrameChoices(selected.Frame);
        RaiseEdited();
    }

    /// <summary>Paints with the current SelectedPaintColor, or erases (removes the sparse dict entry) when that color is "Default".</summary>
    public void PaintCell(FrameCellViewModel cell)
    {
        if (_current is null || SelectedFrameChoice is not { } selected)
        {
            return;
        }

        var key = $"{cell.Column},{cell.Row}";
        var isDefault = string.Equals(SelectedPaintColor.Key, "Default", StringComparison.Ordinal);
        if (isDefault)
        {
            selected.Frame.Cells.Remove(key);
        }
        else
        {
            selected.Frame.Cells[key] = SelectedPaintColor.Key;
        }

        cell.ColorKey = isDefault ? "Default" : SelectedPaintColor.Key;
        cell.Background = SelectedPaintColor.Background;
        RaiseEdited();
    }

    public string Name
    {
        get => _current?.Name ?? string.Empty;
        set { if (_current is null) { return; } _current.Name = value; Raise(); RaiseEdited(); }
    }

    public LedEffectKind EffectKind
    {
        get => _current?.Effect.Kind ?? LedEffectKind.RainbowWave;
        set
        {
            if (_current is null) { return; }
            _current.Effect.Kind = value;
            Raise();
            Raise(nameof(ShowsDensity));
            Raise(nameof(ShowsTrailLength));
            Raise(nameof(ShowsWaveDensity));
            RaiseEdited();
        }
    }

    public bool ShowsDensity => EffectKind == LedEffectKind.Sparkle;
    public bool ShowsTrailLength => EffectKind == LedEffectKind.ColorChase;
    public bool ShowsWaveDensity => EffectKind == LedEffectKind.RainbowWave;

    public string ColorsText
    {
        get => _current is null ? string.Empty : string.Join(", ", _current.Effect.Colors);
        set
        {
            if (_current is null) { return; }
            _current.Effect.Colors = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            if (_current.Effect.Colors.Count == 0) { _current.Effect.Colors.Add("Red"); }
            Raise();
            RaiseEdited();
        }
    }

    public double SpeedHz
    {
        get => _current?.Effect.SpeedHz ?? 0.5;
        set { if (_current is null) { return; } _current.Effect.SpeedHz = value; Raise(); RaiseEdited(); }
    }

    public double Density
    {
        get => _current?.Effect.Density ?? 0.15;
        set { if (_current is null) { return; } _current.Effect.Density = value; Raise(); RaiseEdited(); }
    }

    public int TrailLength
    {
        get => _current?.Effect.TrailLength ?? 3;
        set { if (_current is null) { return; } _current.Effect.TrailLength = (int)value; Raise(); RaiseEdited(); }
    }

    public double WaveDensity
    {
        get => _current?.Effect.WaveDensity ?? 1.0;
        set { if (_current is null) { return; } _current.Effect.WaveDensity = value; Raise(); RaiseEdited(); }
    }

    public double SpeedMultiplier
    {
        get => _current?.SpeedMultiplier ?? 1.0;
        set { if (_current is null) { return; } _current.SpeedMultiplier = value; Raise(); RaiseEdited(); }
    }

    public bool Loop
    {
        get => _current?.Loop ?? true;
        set { if (_current is null) { return; } _current.Loop = value; Raise(); RaiseEdited(); }
    }

    private void RaiseAll()
    {
        Raise(nameof(Current));
        Raise(nameof(Name));
        Raise(nameof(IsEffectMode));
        Raise(nameof(IsFramesMode));
        Raise(nameof(EffectKind));
        Raise(nameof(ShowsDensity));
        Raise(nameof(ShowsTrailLength));
        Raise(nameof(ShowsWaveDensity));
        Raise(nameof(ColorsText));
        Raise(nameof(SpeedHz));
        Raise(nameof(Density));
        Raise(nameof(TrailLength));
        Raise(nameof(WaveDensity));
        Raise(nameof(SpeedMultiplier));
        Raise(nameof(Loop));
    }

    private void RaiseEdited() => Edited?.Invoke(this, EventArgs.Empty);
}
