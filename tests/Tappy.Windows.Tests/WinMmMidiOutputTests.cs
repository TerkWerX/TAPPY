using Tappy.Core.Output;
using Tappy.Windows.Output;

namespace Tappy.Windows.Tests;

public sealed class WinMmMidiOutputTests
{
    [Fact]
    public void Send_opens_the_named_device_and_forwards_the_packed_message()
    {
        var backend = new FakeMidiOutputBackend(new WinMmMidiOutputDevice(3, "Akai Fire", 1, 2));
        using var output = new WinMmMidiOutput(backend);

        output.Send("Akai Fire", ShortMessage(0x91, 60, 100));

        Assert.Equal(3, backend.OpenedDeviceId);
        Assert.Equal([0x00643C91u], backend.SentShortMessages);
    }

    [Fact]
    public void Send_with_no_preferred_device_opens_the_default_mapper()
    {
        var backend = new FakeMidiOutputBackend();
        using var output = new WinMmMidiOutput(backend);

        output.Send(null, ShortMessage(0x91, 60, 100));

        Assert.Null(backend.OpenedDeviceId);
        Assert.Equal(1, backend.OpenCallCount);
    }

    [Fact]
    public void Send_reuses_the_open_handle_for_the_same_device_but_reopens_for_a_different_one()
    {
        var backend = new FakeMidiOutputBackend(
            new WinMmMidiOutputDevice(1, "Akai Fire", 1, 2),
            new WinMmMidiOutputDevice(2, "APC MINI", 3, 4));
        using var output = new WinMmMidiOutput(backend);

        output.Send("Akai Fire", ShortMessage(0x90, 1, 1));
        output.Send("Akai Fire", ShortMessage(0x90, 2, 1));
        Assert.Equal(1, backend.OpenCallCount);

        output.Send("APC MINI", ShortMessage(0x90, 3, 1));
        Assert.Equal(2, backend.OpenCallCount);
        Assert.Equal(2, backend.OpenedDeviceId);
        Assert.Equal(1, backend.CloseCallCount);
    }

    [Fact]
    public void Send_to_an_unknown_device_name_throws_without_opening_anything()
    {
        var backend = new FakeMidiOutputBackend(new WinMmMidiOutputDevice(1, "Akai Fire", 1, 2));
        using var output = new WinMmMidiOutput(backend);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            output.Send("Not Connected", ShortMessage(0x90, 1, 1)));

        Assert.Contains("Not Connected", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, backend.OpenCallCount);
    }

    [Fact]
    public void A_failed_send_closes_the_connection_so_the_next_send_reopens()
    {
        var backend = new FakeMidiOutputBackend(new WinMmMidiOutputDevice(1, "Akai Fire", 1, 2))
        {
            FailNextShortMessage = true,
        };
        using var output = new WinMmMidiOutput(backend);

        Assert.Throws<InvalidOperationException>(() => output.Send("Akai Fire", ShortMessage(0x90, 1, 1)));
        Assert.Equal(1, backend.CloseCallCount);

        output.Send("Akai Fire", ShortMessage(0x90, 1, 1));
        Assert.Equal(2, backend.OpenCallCount);
        Assert.Equal([0x0101_90u], backend.SentShortMessages);
    }

    [Fact]
    public void SendSysEx_validates_F0_F7_framing_before_touching_the_backend()
    {
        var backend = new FakeMidiOutputBackend(new WinMmMidiOutputDevice(1, "Akai Fire", 1, 2));
        using var output = new WinMmMidiOutput(backend);

        Assert.Throws<ArgumentException>(() => output.SendSysEx("Akai Fire", [0x47, 0x7F]));
        Assert.Throws<ArgumentException>(() => output.SendSysEx("Akai Fire", [0xF0, 0x47]));
        Assert.Equal(0, backend.OpenCallCount);
    }

    [Fact]
    public void SendSysEx_opens_the_named_device_and_forwards_the_exact_bytes()
    {
        var backend = new FakeMidiOutputBackend(new WinMmMidiOutputDevice(5, "Akai Fire", 1, 2));
        using var output = new WinMmMidiOutput(backend);
        byte[] message = [0xF0, 0x47, 0x7F, 0x43, 0x65, 0x00, 0x04, 0x00, 0x00, 0x00, 0x7F, 0xF7];

        output.SendSysEx("Akai Fire", message);

        Assert.Equal(5, backend.OpenedDeviceId);
        Assert.Equal(message, Assert.Single(backend.SentSysExMessages));
    }

    [Fact]
    public void A_failed_SysEx_send_closes_the_connection_and_wraps_the_failure()
    {
        var backend = new FakeMidiOutputBackend(new WinMmMidiOutputDevice(1, "Akai Fire", 1, 2))
        {
            SysExFailure = new TimeoutException("driver never confirmed completion"),
        };
        using var output = new WinMmMidiOutput(backend);
        byte[] message = [0xF0, 0x47, 0xF7];

        var exception = Assert.Throws<InvalidOperationException>(() => output.SendSysEx("Akai Fire", message));

        Assert.Contains("driver never confirmed completion", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, backend.CloseCallCount);
    }

    [Fact]
    public void Reset_is_a_no_op_until_something_has_opened_the_device()
    {
        var backend = new FakeMidiOutputBackend(new WinMmMidiOutputDevice(1, "Akai Fire", 1, 2));
        using var output = new WinMmMidiOutput(backend);

        output.Reset();
        Assert.Equal(0, backend.ResetCallCount);

        output.Send("Akai Fire", ShortMessage(0x90, 1, 1));
        output.Reset();
        Assert.Equal(1, backend.ResetCallCount);
    }

    [Fact]
    public void Dispose_closes_an_open_connection_and_disposes_the_backend()
    {
        var backend = new FakeMidiOutputBackend(new WinMmMidiOutputDevice(1, "Akai Fire", 1, 2));
        var output = new WinMmMidiOutput(backend);
        output.Send("Akai Fire", ShortMessage(0x90, 1, 1));

        output.Dispose();

        Assert.Equal(1, backend.CloseCallCount);
        Assert.True(backend.Disposed);
    }

    private static MidiShortMessage ShortMessage(byte status, byte data1, byte data2) =>
        new(MidiShortMessageKind.NoteOn, (status & 0x0F) + 1, data1, data2,
            status | ((uint)data1 << 8) | ((uint)data2 << 16));

    private sealed class FakeMidiOutputBackend(params WinMmMidiOutputDevice[] devices) : IWinMmMidiOutputBackend
    {
        public int? OpenedDeviceId { get; private set; }
        public int OpenCallCount { get; private set; }
        public int CloseCallCount { get; private set; }
        public int ResetCallCount { get; private set; }
        public bool Disposed { get; private set; }
        public bool FailNextShortMessage { get; set; }
        public Exception? SysExFailure { get; set; }
        public List<uint> SentShortMessages { get; } = [];
        public List<byte[]> SentSysExMessages { get; } = [];

        public IReadOnlyList<WinMmMidiOutputDevice> EnumerateDevices() => devices;

        public void Open(int? deviceId)
        {
            OpenedDeviceId = deviceId;
            OpenCallCount++;
        }

        public void SendShortMessage(uint packedMessage)
        {
            if (FailNextShortMessage)
            {
                FailNextShortMessage = false;
                throw new InvalidOperationException("simulated driver failure");
            }

            SentShortMessages.Add(packedMessage);
        }

        public void SendSysEx(byte[] data)
        {
            if (SysExFailure is { } failure)
            {
                SysExFailure = null;
                throw failure;
            }

            SentSysExMessages.Add(data);
        }

        public void Reset() => ResetCallCount++;

        public void Close() => CloseCallCount++;

        public void Dispose() => Disposed = true;
    }
}
