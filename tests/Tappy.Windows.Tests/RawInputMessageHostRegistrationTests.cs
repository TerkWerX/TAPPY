using Tappy.Windows.Input;
using Tappy.Windows.Interop;

namespace Tappy.Windows.Tests;

public sealed class RawInputMessageHostRegistrationTests
{
    [Fact]
    public void RegistersKeyboardAndVendorPageWithoutSuppressingLegacyInput()
    {
        var window = new nint(1234);

        var registrations = RawInputMessageHost.CreateRawInputRegistrations(window);

        Assert.Collection(
            registrations,
            keyboard =>
            {
                Assert.Equal((ushort)0x0001, keyboard.UsagePage);
                Assert.Equal((ushort)0x0006, keyboard.Usage);
                Assert.Equal(
                    RawInputNativeMethods.RidevInputSink | RawInputNativeMethods.RidevDeviceNotify,
                    keyboard.Flags);
                Assert.Equal(window, keyboard.Target);
            },
            vendorPage =>
            {
                Assert.Equal(LogitechG13Protocol.UsagePage, vendorPage.UsagePage);
                Assert.Equal(LogitechG13Protocol.Usage, vendorPage.Usage);
                Assert.Equal(
                    RawInputNativeMethods.RidevPageOnly |
                    RawInputNativeMethods.RidevInputSink |
                    RawInputNativeMethods.RidevDeviceNotify,
                    vendorPage.Flags);
                Assert.Equal(window, vendorPage.Target);
            });
    }

    [Theory]
    [InlineData(0u, false)]
    [InlineData(32u, true)]
    [InlineData(40u, true)]
    [InlineData(4096u, true)]
    [InlineData(4097u, false)]
    [InlineData(uint.MaxValue, false)]
    public void BoundsOsReportedRawInputSizeBeforeAllocation(uint byteCount, bool expected)
    {
        Assert.Equal(expected, RawInputMessageHost.IsSupportedRawInputByteCount(byteCount));
    }
}
