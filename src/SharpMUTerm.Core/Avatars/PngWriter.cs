using System.Buffers.Binary;
using System.IO.Compression;

namespace SharpMUTerm.Core.Avatars;

/// <summary>Writes an RGBA picture as a PNG: one IDAT, no filtering. Enough for the small squares the avatar copies are.</summary>
public static class PngWriter
{
    private static readonly uint[] Table = MakeTable();

    /// <summary>The PNG bytes of <paramref name="rgba"/>, <paramref name="width"/>×<paramref name="height"/>, four bytes a pixel.</summary>
    public static byte[] Rgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException("the pixels are not width × height × 4 bytes", nameof(rgba));
        }

        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        Chunk(output, "IHDR", header);

        using (var raw = new MemoryStream())
        {
            using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            {
                var stride = width * 4;
                for (var y = 0; y < height; y++)
                {
                    zlib.WriteByte(0); // filter: none
                    zlib.Write(rgba.Slice(y * stride, stride));
                }
            }

            Chunk(output, "IDAT", raw.ToArray());
        }

        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        var name = new[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
        output.Write(name);
        output.Write(data);
        var crc = Crc(Crc(0xFFFFFFFFu, name), data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        output.Write(number);
    }

    private static uint Crc(uint crc, byte[] bytes)
    {
        foreach (var b in bytes)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] MakeTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
