using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Tappy.InputBroker.Tests;

public sealed class BrokerPipeProtocolTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public void Round_trip_preserves_kind_sequence_and_payload()
    {
        byte[] payload = [1, 3, 5, 7];

        var encoded = BrokerPipeProtocol.Encode(Key, BrokerMessageKind.StatusResponse, 19, payload);
        var decoded = BrokerPipeProtocol.Decode(Key, encoded, 19);

        Assert.Equal(BrokerMessageKind.StatusResponse, decoded.Kind);
        Assert.Equal(19UL, decoded.Sequence);
        Assert.Equal(payload, decoded.Payload);
    }

    [Fact]
    public void Modified_payload_is_rejected()
    {
        var encoded = BrokerPipeProtocol.Encode(Key, BrokerMessageKind.Hello, 1, new byte[16]);
        encoded[^1] ^= 0x80;

        Assert.Throws<InvalidDataException>(() => BrokerPipeProtocol.Decode(Key, encoded, 1));
    }

    [Fact]
    public void Wrong_key_is_rejected()
    {
        var encoded = BrokerPipeProtocol.Encode(Key, BrokerMessageKind.StatusRequest, 1, []);
        var otherKey = Enumerable.Repeat((byte)0xA5, 32).ToArray();

        Assert.Throws<InvalidDataException>(() => BrokerPipeProtocol.Decode(otherKey, encoded, 1));
    }

    [Fact]
    public void Stale_sequence_is_rejected()
    {
        var encoded = BrokerPipeProtocol.Encode(Key, BrokerMessageKind.StatusRequest, 8, []);

        Assert.Throws<InvalidDataException>(() => BrokerPipeProtocol.Decode(Key, encoded, 9));
    }

    [Fact]
    public void Nonzero_reserved_field_is_rejected_even_with_recomputed_tag()
    {
        var encoded = BrokerPipeProtocol.Encode(Key, BrokerMessageKind.StatusRequest, 1, []);
        BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(20), 1);
        HMACSHA256.HashData(
            Key,
            encoded.AsSpan(0, BrokerPipeProtocol.PrefixSize),
            encoded.AsSpan(BrokerPipeProtocol.PrefixSize, BrokerPipeProtocol.AuthenticationTagSize));

        Assert.Throws<InvalidDataException>(() => BrokerPipeProtocol.Decode(Key, encoded, 1));
    }

    [Fact]
    public void Oversized_payload_is_rejected_before_allocation()
    {
        var header = new byte[BrokerPipeProtocol.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, BrokerPipeProtocol.Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), BrokerPipeProtocol.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)BrokerMessageKind.Hello);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(16),
            BrokerPipeProtocol.MaximumPayloadSize + 1U);

        Assert.Throws<InvalidDataException>(() => BrokerPipeProtocol.Decode(Key, header, 1));
    }

    [Fact]
    public async Task Stream_round_trip_reads_one_complete_frame()
    {
        var bytes = BrokerPipeProtocol.Encode(Key, BrokerMessageKind.Hello, 1, new byte[16]);
        await using var stream = new MemoryStream(bytes);

        var frame = await BrokerPipeProtocol.ReadAsync(stream, Key, 1, CancellationToken.None);

        Assert.Equal(BrokerMessageKind.Hello, frame.Kind);
        Assert.Equal(16, frame.Payload.Length);
    }
}
