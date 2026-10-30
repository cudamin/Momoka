using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Momoka.Tests;

internal static class ImageData
{
    // Generate highly compressible real PNGs without allocating an original-size RGBA frame.
    internal static byte[] Png(int width, int height)
    {
        using var file = new MemoryStream();
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2; // RGB8, no interlace.
        Chunk(file, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[checked(width * 3 + 1)];
            for (var x = 1; x < row.Length; x += 3) { row[x] = 100; row[x + 1] = 80; row[x + 2] = 60; }
            for (var y = 0; y < height; y++) zlib.Write(row);
        }
        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", []);
        return file.ToArray();
    }

    private static void Chunk(Stream target, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        target.Write(number);
        var name = Encoding.ASCII.GetBytes(type);
        target.Write(name);
        target.Write(data);
        var crc = uint.MaxValue;
        foreach (var value in name.Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        target.Write(number);
    }
}
