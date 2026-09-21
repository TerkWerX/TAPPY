using Tappy.App.Runtime;
using Tappy.Core.Input;
using Tappy.Windows.Input;

namespace Tappy.App.Services;

/// <summary>
/// Bridges the LED animation engine to one specific MIDI controller model's
/// physical pad/button grid, without the animation code needing to know that
/// model's own note/CC addressing scheme. Adding a future controller means
/// writing one new adapter plus one <see cref="LedGridAdapterCatalog"/> entry —
/// nothing else in the animation engine changes.
/// </summary>
public interface ILedGridAdapter
{
    string ModelId { get; }
    int Columns { get; }
    int Rows { get; }

    /// <summary>
    /// Width÷height of one physical pad, matching the same measured geometry
    /// ControllerPhotoCatalog's photo hotspots use for this model — lets the
    /// LED animation canvas render pads shaped like the real grid (e.g. the
    /// Fire's tall/portrait pads vs. the APC MINI's wide/landscape ones)
    /// instead of uniform squares.
    /// </summary>
    double PadAspectRatio { get; }

    /// <summary>The lightable control at this grid cell, or null if that cell has no addressable control on this model.</summary>
    ControlId? ControlIdAt(int column, int row);

    /// <summary>
    /// Downgrades a requested "#RRGGBB" hex or named color key to whatever this
    /// model can actually display, mirroring the downgrade contract
    /// MainViewModel.ResolveSupportedColorKey already applies for one-off color
    /// applies. <paramref name="supportsFreeformColor"/> matches
    /// ControllerLedColorCapability.SupportsFreeformColor for this model.
    /// </summary>
    string ResolveColor(string requestedColorKey, bool supportsFreeformColor);
}

/// <summary>
/// Wraps the Fire's already-verified pad addressing (see
/// DeviceAwareControllerRuntime.TryParseAkaiFirePadIndex, run forward instead of
/// parsed backward — same row*16+column formula, not new protocol work).
/// </summary>
internal sealed class AkaiFireLedGridAdapter : ILedGridAdapter
{
    public string ModelId => MidiControllerModelCatalog.AkaiFireV1Id;
    public int Columns => 16;
    public int Rows => 4;
    // Measured pad hotspot in ControllerPhotoCatalog.CreateAkaiFireV1: 58x81 —
    // tall/portrait, "on end".
    public double PadAspectRatio => 58.0 / 81.0;

    public ControlId? ControlIdAt(int column, int row) =>
        column is < 0 or >= 16 || row is < 0 or >= 4
            ? null
            : ControlId.Create(WinMmMidiInputProvider.ProviderId, $"channel-1:note-{0x36 + (row * 16) + column}");

    public string ResolveColor(string requestedColorKey, bool supportsFreeformColor) =>
        // The Fire supports arbitrary RGB outright; no downgrade needed.
        requestedColorKey;
}

/// <summary>
/// Wraps the APC MINI's already-verified pad addressing (see
/// MidiControllerModelCatalog.CreateAkaiApcMiniV1's noteStart = 56 - row*8
/// formula, run forward). The APC MINI's own hardware is bicolor/limited
/// (off/green/red/amber, selected by Note-On velocity — see
/// DeviceAwareControllerRuntime.ApcMiniVelocity); an arbitrary animation RGB is
/// mapped to whichever of those four the requested color is nearest to, the
/// same approximation MainViewModel.ResolveSupportedColorKey already applies
/// for named colors outside the APC MINI's palette, extended here to cover
/// arbitrary hex.
/// </summary>
internal sealed class AkaiApcMiniLedGridAdapter : ILedGridAdapter
{
    public string ModelId => MidiControllerModelCatalog.AkaiApcMiniV1Id;
    public int Columns => 8;
    public int Rows => 8;
    // Measured pad hotspot in ControllerPhotoCatalog.CreateAkaiApcMiniV1:
    // 104x50 — wide/landscape, "alongside".
    public double PadAspectRatio => 104.0 / 50.0;

    public ControlId? ControlIdAt(int column, int row) =>
        column is < 0 or >= 8 || row is < 0 or >= 8
            ? null
            : ControlId.Create(WinMmMidiInputProvider.ProviderId, $"channel-1:note-{56 - (row * 8) + column}");

    public string ResolveColor(string requestedColorKey, bool supportsFreeformColor)
    {
        var trimmed = string.IsNullOrWhiteSpace(requestedColorKey) ? "Default" : requestedColorKey.Trim();
        if (DeviceAwareControllerRuntime.TryParseHexColor(trimmed, out var red, out var green, out var blue))
        {
            return NearestApcColor(red, green, blue);
        }

        return trimmed.ToLowerInvariant() switch
        {
            "green" or "teal" => "Green",
            "red" or "pink" => "Red",
            "amber" or "orange" or "yellow" or "white" => "Amber",
            "default" => "Default",
            _ => "Default",
        };
    }

    private static string NearestApcColor(byte red, byte green, byte blue)
    {
        if (red < 20 && green < 20 && blue < 20)
        {
            return "Default";
        }

        var candidates = new (string Key, int Red, int Green, int Blue)[]
        {
            ("Green", 0, 255, 0),
            ("Red", 255, 0, 0),
            ("Amber", 255, 170, 0),
        };
        var best = candidates[0];
        var bestDistance = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var distance = Square(red - candidate.Red) + Square(green - candidate.Green) + Square(blue - candidate.Blue);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best.Key;

        static int Square(int value) => value * value;
    }
}

public static class LedGridAdapterCatalog
{
    private static readonly ILedGridAdapter[] Adapters = [new AkaiFireLedGridAdapter(), new AkaiApcMiniLedGridAdapter()];

    public static ILedGridAdapter? Find(string? modelId) =>
        string.IsNullOrEmpty(modelId) ? null : Adapters.FirstOrDefault(a => string.Equals(a.ModelId, modelId, StringComparison.Ordinal));
}
