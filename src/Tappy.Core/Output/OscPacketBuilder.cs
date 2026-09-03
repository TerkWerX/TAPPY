using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Tappy.Core.Output;

public static class OscPacketBuilder
{
    public static byte[] Build(string address, string? commaSeparatedArguments)
    {
        if (string.IsNullOrWhiteSpace(address) || !address.StartsWith('/'))
        {
            throw new ArgumentException("An OSC address must begin with /.", nameof(address));
        }

        var arguments = string.IsNullOrWhiteSpace(commaSeparatedArguments)
            ? []
            : commaSeparatedArguments.Split(',', StringSplitOptions.TrimEntries);
        var tags = new StringBuilder(",");
        using var stream = new MemoryStream();
        WriteString(stream, address);
        foreach (var argument in arguments)
        {
            tags.Append(int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? 'i' :
                float.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? 'f' : 's');
        }

        WriteString(stream, tags.ToString());
        foreach (var argument in arguments)
        {
            if (int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            {
                WriteInt32(stream, integer);
            }
            else if (float.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                WriteInt32(stream, BitConverter.SingleToInt32Bits(number));
            }
            else
            {
                WriteString(stream, argument);
            }
        }

        return stream.ToArray();
    }

    private static void WriteString(Stream stream, string value)
    {
        stream.Write(Encoding.UTF8.GetBytes(value));
        stream.WriteByte(0);
        while (stream.Length % 4 != 0)
        {
            stream.WriteByte(0);
        }
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
