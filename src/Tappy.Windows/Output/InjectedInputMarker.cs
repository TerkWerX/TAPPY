using System.Security.Cryptography;

namespace Tappy.Windows.Output;

/// <summary>
/// A non-zero marker generated once per Tappy process. SendInput writes it and the
/// Raw Input boundary rejects matching packets before mapping publication.
/// </summary>
public static class InjectedInputMarker
{
    public static uint Value { get; } = Create();

    public static bool IsSelfInjected(uint extraInformation) =>
        extraInformation != 0 && extraInformation == Value;

    private static uint Create()
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        uint value;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            value = BitConverter.ToUInt32(bytes);
        }
        while (value == 0);

        return value;
    }
}
