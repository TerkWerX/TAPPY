using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Tappy.InputBroker;

internal enum BrokerMessageKind : ushort
{
    Hello = 1,
    HelloAcknowledged = 2,
    StatusRequest = 3,
    StatusResponse = 4,
    Error = 5
}

internal sealed record BrokerFrame(BrokerMessageKind Kind, ulong Sequence, byte[] Payload);

/// <summary>
/// A bounded, versioned, authenticated local frame. Windows pipe ACLs authenticate
/// the user account; this HMAC additionally rejects malformed, stale, or modified
/// frames before any broker command is considered.
/// </summary>
internal static class BrokerPipeProtocol
{
    public const uint Magic = 0x31595054; // "TPY1" in little-endian form.
    public const ushort Version = 1;
    public const int PrefixSize = 24;
    public const int AuthenticationTagSize = 32;
    public const int HeaderSize = PrefixSize + AuthenticationTagSize;
    public const int MinimumKeySize = 32;
    public const int MaximumPayloadSize = 64 * 1024;

    public static byte[] Encode(
        ReadOnlySpan<byte> key,
        BrokerMessageKind kind,
        ulong sequence,
        ReadOnlySpan<byte> payload)
    {
        ValidateKey(key);
        ValidateKind(kind);
        if (sequence == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (payload.Length > MaximumPayloadSize)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }

        var result = new byte[checked(HeaderSize + payload.Length)];
        var prefix = result.AsSpan(0, PrefixSize);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(prefix[4..], Version);
        BinaryPrimitives.WriteUInt16LittleEndian(prefix[6..], (ushort)kind);
        BinaryPrimitives.WriteUInt64LittleEndian(prefix[8..], sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix[16..], checked((uint)payload.Length));
        payload.CopyTo(result.AsSpan(HeaderSize));

        var authenticatedBytes = new byte[checked(PrefixSize + payload.Length)];
        prefix.CopyTo(authenticatedBytes);
        payload.CopyTo(authenticatedBytes.AsSpan(PrefixSize));
        HMACSHA256.HashData(key, authenticatedBytes, result.AsSpan(PrefixSize, AuthenticationTagSize));
        CryptographicOperations.ZeroMemory(authenticatedBytes);
        return result;
    }

    public static BrokerFrame Decode(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> bytes,
        ulong expectedSequence)
    {
        ValidateKey(key);
        if (expectedSequence == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedSequence));
        }

        if (bytes.Length < HeaderSize)
        {
            throw new InvalidDataException("The broker frame is shorter than its fixed header.");
        }

        var prefix = bytes[..PrefixSize];
        if (BinaryPrimitives.ReadUInt32LittleEndian(prefix) != Magic ||
            BinaryPrimitives.ReadUInt16LittleEndian(prefix[4..]) != Version)
        {
            throw new InvalidDataException("The broker frame has an unsupported signature or version.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(prefix[20..]) != 0)
        {
            throw new InvalidDataException("The broker frame reserved field must be zero.");
        }

        var kind = (BrokerMessageKind)BinaryPrimitives.ReadUInt16LittleEndian(prefix[6..]);
        ValidateKind(kind);
        var sequence = BinaryPrimitives.ReadUInt64LittleEndian(prefix[8..]);
        if (sequence != expectedSequence)
        {
            throw new InvalidDataException("The broker frame sequence is stale or out of order.");
        }

        var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(prefix[16..]);
        if (payloadLength > MaximumPayloadSize || bytes.Length != HeaderSize + payloadLength)
        {
            throw new InvalidDataException("The broker frame payload length is invalid.");
        }

        var payload = bytes[HeaderSize..];
        var authenticatedBytes = new byte[checked(PrefixSize + payload.Length)];
        prefix.CopyTo(authenticatedBytes);
        payload.CopyTo(authenticatedBytes.AsSpan(PrefixSize));
        Span<byte> expectedTag = stackalloc byte[AuthenticationTagSize];
        HMACSHA256.HashData(key, authenticatedBytes, expectedTag);
        CryptographicOperations.ZeroMemory(authenticatedBytes);
        if (!CryptographicOperations.FixedTimeEquals(
                expectedTag,
                bytes.Slice(PrefixSize, AuthenticationTagSize)))
        {
            throw new InvalidDataException("The broker frame authentication tag is invalid.");
        }

        CryptographicOperations.ZeroMemory(expectedTag);
        return new BrokerFrame(kind, sequence, payload.ToArray());
    }

    public static async ValueTask<BrokerFrame> ReadAsync(
        Stream stream,
        ReadOnlyMemory<byte> key,
        ulong expectedSequence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateKey(key.Span);

        var header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16));
        if (payloadLength > MaximumPayloadSize)
        {
            throw new InvalidDataException("The broker frame payload exceeds the configured limit.");
        }

        var frame = new byte[checked(HeaderSize + (int)payloadLength)];
        header.CopyTo(frame, 0);
        if (payloadLength > 0)
        {
            await stream.ReadExactlyAsync(frame.AsMemory(HeaderSize), cancellationToken).ConfigureAwait(false);
        }

        return Decode(key.Span, frame, expectedSequence);
    }

    public static async ValueTask WriteAsync(
        Stream stream,
        ReadOnlyMemory<byte> key,
        BrokerMessageKind kind,
        ulong sequence,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var frame = Encode(key.Span, kind, sequence, payload.Span);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length < MinimumKeySize)
        {
            throw new ArgumentException("The broker authentication key must contain at least 256 bits.", nameof(key));
        }
    }

    private static void ValidateKind(BrokerMessageKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new InvalidDataException("The broker frame message kind is invalid.");
        }
    }
}
