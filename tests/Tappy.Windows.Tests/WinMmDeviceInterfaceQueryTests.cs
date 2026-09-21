using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class WinMmDeviceInterfaceQueryTests
{
    // These exercise the real winmm.dll P/Invoke against a device ID that
    // cannot exist (no test environment can guarantee a real MIDI device is
    // present), which is exactly the "resolution unavailable" path every
    // caller must fail closed on. The success path (a real device ID on a
    // machine with a real MIDI controller attached) can only be verified
    // against physical hardware — see docs/DECISIONS.md D-026.
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void TryGetMidiInDeviceInterfacePath_returns_null_for_an_impossible_device_id(int deviceId) =>
        Assert.Null(WinMmDeviceInterfaceQuery.TryGetMidiInDeviceInterfacePath(deviceId));

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void TryGetMidiOutDeviceInterfacePath_returns_null_for_an_impossible_device_id(int deviceId) =>
        Assert.Null(WinMmDeviceInterfaceQuery.TryGetMidiOutDeviceInterfacePath(deviceId));

    [Fact]
    public void Repeated_calls_do_not_throw_or_leak_native_resources_detectably()
    {
        for (var i = 0; i < 50; i++)
        {
            Assert.Null(WinMmDeviceInterfaceQuery.TryGetMidiInDeviceInterfacePath(-1));
        }
    }
}
