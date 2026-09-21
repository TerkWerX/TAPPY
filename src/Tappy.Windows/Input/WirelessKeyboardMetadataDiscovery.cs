namespace Tappy.Windows.Input;

internal sealed record WirelessKeyboardMetadata(bool IsBluetooth, string? BluetoothName, IReadOnlyList<string> PairedKeyboards)
{
    internal static readonly WirelessKeyboardMetadata Empty = new(false, null, []);

    internal string Apply(string fallback)
    {
        var names = PairedKeyboards.Select(KeyboardDeviceNames.CleanReportedName).Where(n => n is not null).ToArray();
        if (names.Length == 1) { return $"{names[0]} — Unifying (paired)"; }
        if (names.Length > 1) { return $"Unifying receiver — paired: {string.Join(" / ", names)}"; }
        return IsBluetooth ? $"{KeyboardDeviceNames.CleanReportedName(BluetoothName) ?? "Keyboard input (model unknown)"} — Bluetooth" : fallback;
    }
}

internal interface IKeyboardMetadataUpdates
{
    bool ConsumeMetadataChange();
    bool IsMetadataPending { get; }
}

/// <summary>Bounded-lifetime background reads; no device I/O on the UI/input dispatch thread.</summary>
internal sealed class WirelessKeyboardMetadataDiscovery
{
    private sealed class Entry
    {
        internal WirelessKeyboardMetadata Value = WirelessKeyboardMetadata.Empty;
        internal bool Pending;
        internal DateTime RetryAfter;
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<RawKeyboardDeviceCandidate, Task<WirelessKeyboardMetadata>> _read;
    private int _changed;

    internal WirelessKeyboardMetadataDiscovery(Func<RawKeyboardDeviceCandidate, Task<WirelessKeyboardMetadata>>? read = null) =>
        _read = read ?? ReadNativeAsync;

    internal bool IsPending { get { lock (_gate) { return _entries.Values.Any(e => e.Pending); } } }
    internal bool ConsumeChange() => Interlocked.Exchange(ref _changed, 0) != 0;

    internal void Retain(IReadOnlyList<RawKeyboardDeviceCandidate> candidates)
    {
        var paths = candidates.Select(c => c.RawDevicePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            foreach (var path in _entries.Keys.Where(p => !paths.Contains(p)).ToArray()) { _entries.Remove(path); }
        }
    }

    internal WirelessKeyboardMetadata Get(RawKeyboardDeviceCandidate candidate)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(candidate.RawDevicePath, out var entry))
            {
                if (_entries.Count >= 128) { return WirelessKeyboardMetadata.Empty; }
                _entries[candidate.RawDevicePath] = entry = new();
            }
            if (!entry.Pending && DateTime.UtcNow >= entry.RetryAfter)
            {
                entry.Pending = true;
                _ = Task.Run(async () =>
                {
                    WirelessKeyboardMetadata value;
                    try { value = await _read(candidate).ConfigureAwait(false); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or OperationCanceledException or ArgumentException or NotSupportedException)
                    { value = WirelessKeyboardMetadata.Empty; }
                    lock (_gate)
                    {
                        if (!_entries.TryGetValue(candidate.RawDevicePath, out var current) || !ReferenceEquals(current, entry)) { return; }
                        var changed = entry.Value.Apply("") != value.Apply("");
                        entry.Value = value;
                        entry.RetryAfter = DateTime.UtcNow.AddSeconds(30);
                        entry.Pending = false;
                        if (changed) { Interlocked.Exchange(ref _changed, 1); }
                    }
                });
            }
            return entry.Value;
        }
    }

    private static async Task<WirelessKeyboardMetadata> ReadNativeAsync(RawKeyboardDeviceCandidate candidate)
    {
        if (candidate.VendorId == 0x046D && candidate.ProductId == 0xC52B &&
            candidate.ContainerId is { } container && container != Guid.Empty)
        {
            return new(false, null, await NativeUnifyingNameReader.ReadAsync(container).ConfigureAwait(false));
        }
        return WindowsKeyboardMetadata.Read(candidate.RawDevicePath, candidate.ReportedProduct);
    }
}
