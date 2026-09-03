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

    [Fact]
    public void StopRequestUsesWindowCloseWhenMessageWindowIsReady()
    {
        var window = new nint(1234);
        var windowCalls = 0;
        var threadCalls = 0;

        var requested = RawInputMessageHost.TryRequestMessageLoopStop(
            window,
            42,
            (actualWindow, message, _, _) =>
            {
                windowCalls++;
                Assert.Equal(window, actualWindow);
                Assert.Equal(RawInputNativeMethods.WmClose, message);
                return true;
            },
            (_, _, _, _) =>
            {
                threadCalls++;
                return true;
            });

        Assert.True(requested);
        Assert.Equal(1, windowCalls);
        Assert.Equal(0, threadCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopRequestFallsBackToThreadQuitWhenWindowIsUnavailableOrRejectsClose(bool hasWindow)
    {
        var windowCalls = 0;
        var threadCalls = 0;

        var requested = RawInputMessageHost.TryRequestMessageLoopStop(
            hasWindow ? new nint(1234) : nint.Zero,
            42,
            (_, message, _, _) =>
            {
                windowCalls++;
                Assert.Equal(RawInputNativeMethods.WmClose, message);
                return false;
            },
            (threadId, message, _, _) =>
            {
                threadCalls++;
                Assert.Equal(42u, threadId);
                Assert.Equal(RawInputNativeMethods.WmQuit, message);
                return true;
            });

        Assert.True(requested);
        Assert.Equal(hasWindow ? 1 : 0, windowCalls);
        Assert.Equal(1, threadCalls);
    }

    [Fact]
    public void StopRequestFailsClosedWhenNeitherNativeRouteIsAvailable()
    {
        var requested = RawInputMessageHost.TryRequestMessageLoopStop(
            nint.Zero,
            0,
            (_, _, _, _) => throw new InvalidOperationException("Window route must not run."),
            (_, _, _, _) => throw new InvalidOperationException("Thread route must not run."));

        Assert.False(requested);
    }
}
