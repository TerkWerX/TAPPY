using Tappy.Windows.Input;

namespace Tappy.KeypadCapture;

internal static class KeypadCaptureConnectionGuard
{
    // DEVNOTIFY can announce an already-enumerated device after registration.
    // The provider calls that MembershipChanged even if its member set is identical.
    // Compare the exact selected connection; never treat every arrival as unplug.
    internal static string? StopReason(SanitizedDeviceDescriptor? target, KeyboardDeviceChangedEventArgs change)
    {
        if (target is null) { return null; }
        var reported = change.Descriptor;
        var refersToTarget = change.WasCaptureTarget ||
            reported?.PersistentId == target.PersistentId ||
            reported?.PathFingerprintSha256 == target.PathFingerprintSha256;
        if (!refersToTarget) { return null; }
        if (change.Kind == RawInputDeviceChangeKind.Removal) { return "Microsoft pad disconnected"; }
        if (reported is null) { return "Microsoft pad connection could not be verified"; }
        if (reported.PersistentId != target.PersistentId || reported.PathFingerprintSha256 != target.PathFingerprintSha256 ||
            reported.SessionId != target.SessionId || reported.SessionHandle != target.SessionHandle ||
            reported.PresentationModelId != target.PresentationModelId ||
            !reported.MemberSessionHandles.Order().SequenceEqual(target.MemberSessionHandles.Order()))
        { return "Microsoft pad input interfaces changed"; }
        return null;
    }
}
