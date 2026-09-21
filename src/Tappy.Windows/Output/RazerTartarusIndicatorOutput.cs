namespace Tappy.Windows.Output;

public interface IRazerTartarusIndicatorOutput
{
    /// <summary>Logical color bits: yellow=1, green=2, blue=4; not wire-channel order.</summary>
    Task<int> SynchronizeAsync(string persistentId, int mask, Func<bool> isCurrent);
}

/// <summary>Only the original 1532:0201 bank indicators; no RGB, mode, keymap, or firmware commands.</summary>
public sealed class NativeRazerTartarusIndicatorOutput : IRazerTartarusIndicatorOutput
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly Func<string, ITartarusFeaturePort> _open;
    public NativeRazerTartarusIndicatorOutput() : this(NativeTartarusFeaturePort.OpenConfirmed) { }
    internal NativeRazerTartarusIndicatorOutput(Func<string, ITartarusFeaturePort> open) => _open = open;

    public async Task<int> SynchronizeAsync(string persistentId, int mask, Func<bool> isCurrent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistentId);
        ArgumentNullException.ThrowIfNull(isCurrent);
        if (mask is < 0 or > 7) { throw new ArgumentOutOfRangeException(nameof(mask)); }
        if (!await Gate.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false))
        { throw new IOException("Another Tartarus indicator operation is still running."); }
        try
        {
            return await Task.Run(() =>
            {
                if (!isCurrent()) { throw new OperationCanceledException("The confirmed controller or bank changed."); }
                using var port = _open(persistentId);
                return new TartarusIndicatorProtocol(port, isCurrent).Synchronize(mask);
            }).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }
}

internal interface ITartarusFeaturePort : IDisposable
{
    byte[] Exchange(byte[] request);
}

/// <summary>
/// Independently implemented from documented interoperability facts. Wire format and
/// channel IDs: OpenRazer razercommon/razerchromacommon and original Tartarus profile LEDs.
/// Windows prefixes the 90-byte vendor payload with report ID zero (91 bytes total).
/// </summary>
internal sealed class TartarusIndicatorProtocol(ITartarusFeaturePort port, Func<bool> isCurrent)
{
    internal static byte[] Request(int channel, bool? enabled)
    {
        if (channel is < 0 or > 2) { throw new ArgumentOutOfRangeException(nameof(channel)); }
        var report = new byte[91];
        report[2] = 0xFF; // Transaction; payload status at [1] remains zero.
        report[6] = 3; report[7] = 3;
        report[8] = enabled.HasValue ? (byte)0 : (byte)0x80;
        report[9] = 1; // Model's profile-indicator variable storage selector.
        // Physical colors verified by the operator on original 1532:0201:
        // 0C is green, 0D blue, 0E yellow. Keep the UI/profile's logical
        // yellow/green/blue order and translate both GET and SET here.
        report[10] = channel switch { 0 => (byte)0x0E, 1 => (byte)0x0C, _ => (byte)0x0D };
        report[11] = enabled == true ? (byte)1 : (byte)0;
        report[89] = Checksum(report);
        return report;
    }

    internal static byte Checksum(byte[] report)
    {
        byte checksum = 0;
        for (var i = 3; i < 89; i++) { checksum ^= report[i]; }
        return checksum;
    }

    internal static int Validate(byte[] request, byte[] reply)
    {
        // BUSY, stale transactions, truncated/malformed replies and mismatched
        // channels are failures. Never infer success from the USB write alone.
        if (reply.Length != 91 || reply[0] != 0 || reply[1] != 2 || reply[2] != 0xFF ||
            reply[3] != 0 || reply[4] != 0 || reply[5] != 0 || reply[6] != 3 ||
            reply[7] != 3 || reply[8] != request[8] || reply[9] != 1 ||
            reply[10] != request[10] || reply[11] > 1 || reply[90] != 0 ||
            reply[89] != Checksum(reply))
        { throw new IOException("The Tartarus did not return a valid indicator acknowledgement."); }
        if (request[8] == 0 && reply[11] != request[11])
        { throw new IOException("The Tartarus acknowledged a different indicator state."); }
        return reply[11];
    }

    private int Query(int channel, bool? enabled)
    {
        if (!isCurrent()) { throw new OperationCanceledException("The controller or keymap changed; indicator operation stopped."); }
        var request = Request(channel, enabled);
        return Validate(request, port.Exchange(request));
    }

    internal int Read()
    {
        var mask = 0;
        for (var channel = 0; channel < 3; channel++) { mask |= Query(channel, null) << channel; }
        return mask;
    }

    internal int Synchronize(int desired)
    {
        if (desired is < 0 or > 7) { throw new ArgumentOutOfRangeException(nameof(desired)); }
        var original = Read(); // Prove all three query replies before any state write.
        var writeAttempted = false;
        try
        {
            for (var channel = 0; channel < 3; channel++)
            {
                if (((original ^ desired) & (1 << channel)) == 0) { continue; }
                writeAttempted = true;
                Query(channel, (desired & (1 << channel)) != 0);
            }
            var actual = Read();
            if (actual != desired) { throw new IOException("Tartarus indicator readback did not match the requested bank."); }
            return actual;
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            if (writeAttempted && isCurrent())
            {
                // Same open handle only. Never reopen/re-enumerate to roll back a
                // disconnected device or touch a newly confirmed controller.
                try
                {
                    for (var channel = 0; channel < 3; channel++) { Query(channel, (original & (1 << channel)) != 0); }
                    if (Read() != original) { throw new IOException("Restoration readback failed."); }
                }
                catch (Exception restoreError) when (restoreError is IOException or System.ComponentModel.Win32Exception or OperationCanceledException)
                { throw new IOException("Indicator sync failed and restoration could not be verified. The physical lights may be partially changed.", error); }
                throw new IOException("Indicator sync failed; the original light state was restored and verified.", error);
            }
            throw;
        }
    }
}
