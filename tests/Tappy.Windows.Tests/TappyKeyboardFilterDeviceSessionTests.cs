using System.Buffers.Binary;
using Tappy.Windows.ExclusiveInput;

namespace Tappy.Windows.Tests;

public sealed class TappyKeyboardFilterDeviceSessionTests
{
    private static readonly KeyboardFilterSessionToken Token = new(
        0x0807060504030201,
        0x100F0E0D0C0B0A09);

    [Fact]
    public void Wire_request_sizes_and_offsets_match_the_native_version_one_contract()
    {
        var session = TappyKeyboardFilterWireCodec.SessionRequest(Token);
        var policy = TappyKeyboardFilterWireCodec.PolicyRequest(
            Token,
            23,
            KeyboardFilterRole.ConfigurableSecondary,
            KeyboardFilterMode.CaptureAndSuppress,
            750);
        var heartbeat = TappyKeyboardFilterWireCodec.HeartbeatRequest(Token, 23);
        var read = TappyKeyboardFilterWireCodec.ReadRequest(Token, 64);

        Assert.Equal(24, session.Length);
        Assert.Equal(48, policy.Length);
        Assert.Equal(32, heartbeat.Length);
        Assert.Equal(32, read.Length);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(policy));
        Assert.Equal(48u, BinaryPrimitives.ReadUInt32LittleEndian(policy.AsSpan(4)));
        Assert.Equal(Token.Low, BinaryPrimitives.ReadUInt64LittleEndian(policy.AsSpan(8)));
        Assert.Equal(Token.High, BinaryPrimitives.ReadUInt64LittleEndian(policy.AsSpan(16)));
        Assert.Equal(23ul, BinaryPrimitives.ReadUInt64LittleEndian(policy.AsSpan(24)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(policy.AsSpan(32)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(policy.AsSpan(36)));
        Assert.Equal(750u, BinaryPrimitives.ReadUInt32LittleEndian(policy.AsSpan(40)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(policy.AsSpan(44)));
    }

    [Fact]
    public void Broker_session_sends_authenticated_policy_heartbeat_read_and_fail_open_requests()
    {
        using var transport = new RecordingTransport();
        transport.Responses.Enqueue(CreateEvent(10, 23, 0x1E, KeyboardFilterEventFlags.None, 0));
        using var session = new TappyKeyboardFilterDeviceSession(transport, Token);

        session.Begin();
        session.SetPolicy(
            23,
            KeyboardFilterRole.ConfigurableSecondary,
            KeyboardFilterMode.CaptureAndSuppress,
            750);
        session.Heartbeat(23);
        var events = session.ReadEvents(16);
        session.ForceFailOpen();

        Assert.Equal(
            [
                TappyKeyboardFilterProtocol.BeginAuthenticatedSession,
                TappyKeyboardFilterProtocol.SetInstancePolicy,
                TappyKeyboardFilterProtocol.Heartbeat,
                TappyKeyboardFilterProtocol.ReadEvents,
                TappyKeyboardFilterProtocol.ForceFailOpen
            ],
            transport.Calls.Select(call => call.ControlCode));
        Assert.Single(events);
        Assert.Equal(10ul, events[0].Sequence);
        Assert.Equal(0x1E, events[0].MakeCode);
        Assert.Equal(16 * TappyKeyboardFilterProtocol.EventSize, transport.Calls[3].OutputCapacity);
    }

    [Fact]
    public void Status_parser_rejects_unknown_enum_values_and_event_parser_rejects_bad_batches()
    {
        var status = new byte[TappyKeyboardFilterProtocol.StatusSize];
        BinaryPrimitives.WriteUInt32LittleEndian(status, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(status.AsSpan(4), 99);
        Assert.Throws<InvalidDataException>(() => TappyKeyboardFilterWireCodec.ParseStatus(status));

        var duplicateEvents = CreateEvent(4, 2, 0x20, KeyboardFilterEventFlags.None, 0)
            .Concat(CreateEvent(4, 2, 0x20, KeyboardFilterEventFlags.Break, 0))
            .ToArray();
        Assert.Throws<InvalidDataException>(() => TappyKeyboardFilterWireCodec.ParseEvents(duplicateEvents));
    }

    [Fact]
    public void Capture_requires_a_secondary_role_and_a_bounded_watchdog()
    {
        using var transport = new RecordingTransport();
        using var session = new TappyKeyboardFilterDeviceSession(transport, Token);
        session.Begin();

        Assert.Throws<ArgumentException>(() => session.SetPolicy(
            1,
            KeyboardFilterRole.ProtectedPrimary,
            KeyboardFilterMode.CaptureAndSuppress,
            750));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetPolicy(
            1,
            KeyboardFilterRole.ConfigurableSecondary,
            KeyboardFilterMode.CaptureAndSuppress,
            249));
    }

    [Fact]
    public void Status_parser_uses_the_native_field_layout()
    {
        var status = new byte[TappyKeyboardFilterProtocol.StatusSize];
        BinaryPrimitives.WriteUInt32LittleEndian(status, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(status.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(status.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(status.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(status.AsSpan(16), 750);
        BinaryPrimitives.WriteUInt64LittleEndian(status.AsSpan(24), 45);
        BinaryPrimitives.WriteUInt64LittleEndian(status.AsSpan(32), 91);
        BinaryPrimitives.WriteUInt64LittleEndian(status.AsSpan(40), 3);

        var parsed = TappyKeyboardFilterWireCodec.ParseStatus(status);

        Assert.Equal(KeyboardFilterMode.CaptureAndSuppress, parsed.EffectiveMode);
        Assert.Equal(KeyboardFilterRole.ConfigurableSecondary, parsed.Role);
        Assert.True(parsed.WatchdogArmed);
        Assert.Equal(750u, parsed.WatchdogTimeoutMilliseconds);
        Assert.Equal(45ul, parsed.PolicyGeneration);
        Assert.Equal(91ul, parsed.LastSequence);
        Assert.Equal(3ul, parsed.RingOverflowCount);
    }

    [Fact]
    public void Endpoint_multistring_parser_stops_at_the_double_null()
    {
        char[] buffer = "path-one\0path-two\0\0ignored".ToCharArray();

        Assert.Equal(
            ["path-one", "path-two"],
            NativeTappyKeyboardFilterEndpointEnumerator.ParseMultiString(buffer));
    }

    private static byte[] CreateEvent(
        ulong sequence,
        ulong generation,
        ushort makeCode,
        KeyboardFilterEventFlags flags,
        ushort unitId)
    {
        var bytes = new byte[TappyKeyboardFilterProtocol.EventSize];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, sequence);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), generation);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), makeCode);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18), (ushort)flags);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), unitId);
        return bytes;
    }

    private sealed class RecordingTransport : ITappyKeyboardFilterTransport
    {
        public List<Call> Calls { get; } = [];
        public Queue<byte[]> Responses { get; } = [];

        public byte[] Invoke(uint controlCode, byte[] input, int outputCapacity)
        {
            Calls.Add(new Call(controlCode, input, outputCapacity));
            return outputCapacity == 0 || Responses.Count == 0 ? [] : Responses.Dequeue();
        }

        public void Dispose()
        {
        }
    }

    private sealed record Call(uint ControlCode, byte[] Input, int OutputCapacity);
}
