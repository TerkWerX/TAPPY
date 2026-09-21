using Tappy.Windows.Input;

namespace Tappy.KeypadCapture;

internal enum CaptureStage { ReadyFirst, First, ReadySecond, Second, Completed, Aborted }
internal sealed record ReportCounts(string ScanCode, string Extended, string VirtualKey, int Press, int Release, int Repeat);
internal sealed record PassSummary(int Pass, int AcceptedPackets, int DiscardedPackets, IReadOnlyList<ReportCounts> Reports);
internal sealed record CaptureSummary(string Status, string StopReason, IReadOnlyList<PassSummary> Passes);

/// <summary>Two finite aggregate-only passes. Never stores text, timestamps, or an event sequence.</summary>
internal sealed class KeypadCaptureSession(string persistentId, TimeProvider? timeProvider = null)
{
    internal const int PacketLimit = 512;
    internal static readonly TimeSpan PassLimit = TimeSpan.FromSeconds(120);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly List<PassData> _passes = [];
    private CaptureStage _stage;
    private string _stopReason = "Not started";
    private long _started;

    internal CaptureStage Stage { get { lock (_gate) { return _stage; } } }
    internal bool IsCapturing => Stage is CaptureStage.First or CaptureStage.Second;

    internal void Start()
    {
        lock (_gate)
        {
            if (_stage is not (CaptureStage.ReadyFirst or CaptureStage.ReadySecond)) { throw new InvalidOperationException("Not ready"); }
            _stage = _stage == CaptureStage.ReadyFirst ? CaptureStage.First : CaptureStage.Second;
            _passes.Add(new PassData());
            _started = _clock.GetTimestamp();
            _stopReason = "Capturing";
        }
    }

    internal void Observe(NormalizedKeyboardInput input)
    {
        lock (_gate)
        {
            // Reject other devices before counting or inspecting their key data.
            if (input.PersistentDeviceId != persistentId || !IsCapturing) { return; }
            Tick();
            if (!IsCapturing) { return; }
            var pass = _passes[^1];
            if (pass.Accepted + pass.Discarded >= PacketLimit) { Abort("Packet limit reached"); return; }
            if (!Allowed(input.MakeCode, input.RawVirtualKey, input.ExtendedKey)) { pass.Discarded++; return; }
            var key = (input.MakeCode, input.ExtendedKey, input.RawVirtualKey);
            if (!pass.Rows.TryGetValue(key, out var row)) { row = new int[3]; pass.Rows.Add(key, row); }
            row[input.Transition == KeyTransition.Release ? 1 : input.IsRepeat ? 2 : 0]++;
            pass.Accepted++;
        }
    }

    internal void Tick()
    {
        lock (_gate)
        {
            if (IsCapturing && _clock.GetElapsedTime(_started) >= PassLimit) { Abort("Pass timed out"); }
        }
    }

    internal bool Finish(bool neutral)
    {
        lock (_gate)
        {
            Tick();
            if (!IsCapturing) { return false; }
            if (!neutral) { Abort("Key still held at finish"); return false; }
            if (_passes[^1].Accepted == 0) { return false; }
            _stage = _stage == CaptureStage.First ? CaptureStage.ReadySecond : CaptureStage.Completed;
            _stopReason = _stage == CaptureStage.Completed ? "Both passes collected; not a pass/fail certification" : "Between passes; disarmed";
            return true;
        }
    }

    internal void Abort(string reason)
    {
        lock (_gate)
        {
            if (_stage is CaptureStage.Completed or CaptureStage.Aborted) { return; }
            _stage = CaptureStage.Aborted;
            _stopReason = reason;
        }
    }

    internal CaptureSummary Snapshot()
    {
        lock (_gate)
        {
            return new(_stage.ToString(), _stopReason, _passes.Select((pass, index) => new PassSummary(index + 1,
                pass.Accepted, pass.Discarded, pass.Rows.OrderBy(r => r.Key.Scan).ThenBy(r => r.Key.Extended).ThenBy(r => r.Key.Vk)
                    .Select(r => new ReportCounts($"0x{r.Key.Scan:X4}", r.Key.Extended.ToString(), $"0x{r.Key.Vk:X4}",
                        r.Value[0], r.Value[1], r.Value[2])).ToArray())).ToArray());
        }
    }

    private static bool Allowed(ushort scan, ushort vk, ExtendedKeyKind extended) =>
        extended is ExtendedKeyKind.None or ExtendedKeyKind.E0 &&
        scan is 0x1C or 0x35 or 0x37 or 0x45 or 0x47 or 0x48 or 0x49 or 0x4A or 0x4B or 0x4C or 0x4D or 0x4E or 0x4F or 0x50 or 0x51 or 0x52 or 0x53 &&
        (vk is >= 0x60 and <= 0x69 or 0x0C or 0x0D or 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x6A or 0x6B or 0x6D or 0x6E or 0x6F or 0x90);

    private sealed class PassData
    {
        internal int Accepted;
        internal int Discarded;
        internal Dictionary<(ushort Scan, ExtendedKeyKind Extended, ushort Vk), int[]> Rows { get; } = [];
    }
}
