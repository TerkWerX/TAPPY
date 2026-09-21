using Tappy.Core.Input;

namespace Tappy.App.Services;

// Last observed number/navigation report, NOT a readback of the hardware LED.
// Firmware-generated Num Lock reports cannot identify physical button presses.
internal sealed class TargusBankState
{
    internal static readonly ushort[] BankedScans = [0x47, 0x48, 0x49, 0x4B, 0x4D, 0x4F, 0x50, 0x51, 0x52, 0x53];
    internal bool? NumLockOn { get; private set; }
    internal long Revision { get; private set; }
    internal string Label => NumLockOn switch
    {
        true => "Last detected: numeric · both sets remain available",
        false => "Last detected: navigation · both sets remain available",
        null => "Numeric + navigation assignments · both sets always available"
    };

    internal void Reset()
    {
        NumLockOn = null;
        Revision++;
    }

    internal bool Observe(ControlId control, ControlSignalKind kind)
    {
        if (kind != ControlSignalKind.Press) { return false; }
        var revision = Revision;
        var id = control.Value;
        foreach (var scan in BankedScans)
        {
            if (id == TargusKeypadLayout.Key(scan)) { SetMode(true); }
            else if (id == TargusKeypadLayout.Key(scan, true)) { SetMode(false); }
        }
        if (id == TargusKeypadLayout.Key(0x4C)) { SetMode(true); }
        return revision != Revision;
    }

    private void SetMode(bool value)
    {
        if (NumLockOn != value) { NumLockOn = value; Revision++; }
    }

    internal static bool IsNumLock(string id) =>
        id == TargusKeypadLayout.Key(0x45) || id == TargusKeypadLayout.Key(0x45, true);

    internal string? AssignmentReport(string reportId) =>
        reportId == TargusKeypadLayout.TripleZeroReference || IsNumLock(reportId) ? null : reportId;

    internal string UnavailableLabel(string physicalId) => physicalId switch
    {
        TargusKeypadLayout.TripleZeroReference => "Shares 0 input",
        _ when IsNumLock(physicalId) => "Hardware mode · not assignable",
        _ => "Unassigned"
    };
}
