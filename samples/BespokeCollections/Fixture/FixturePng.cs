using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace BespokeCollections.Fixture;

public static class FixturePng
{
    public static byte[] Create(long id, int width, int height)
    {
        if (id <= 0 || width is < 32 or > 1024 || height is < 16 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(width));
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(png, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            var row = new byte[width * 3 + 1];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var checker = (x / Math.Max(1, width / 8) + y / Math.Max(1, height / 8)) % 2;
                    row[x * 3 + 1] = (byte)(40 + (id * 31 + x * 120 / width) % 150);
                    row[x * 3 + 2] = (byte)(60 + (id * 17 + y * 100 / height) % 150);
                    row[x * 3 + 3] = (byte)(checker == 0 ? 180 : 220);
                }
                zlib.Write(row);
            }
        }
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    static void WriteChunk(Stream stream, string name, byte[] data)
    {
        Span<byte> value = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(value, data.Length);
        stream.Write(value);
        var type = Encoding.ASCII.GetBytes(name);
        stream.Write(type);
        stream.Write(data);
        var crc = 0xffffffffu;
        foreach (var part in new[] { type, data })
            foreach (var item in part)
            {
                crc ^= item;
                for (var bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
        BinaryPrimitives.WriteUInt32BigEndian(value, crc ^ 0xffffffffu);
        stream.Write(value);
    }
}
