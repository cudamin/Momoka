using System.Buffers.Binary;
using System.Text;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class FontDecodeBudgetTests
{
    internal static void Register()
    {
        RegisterSemantics();
        foreach (var platform in new ushort[] { 3, 1 })
        {
            var value = platform;
            Test($"PERF-16：platform {platform} 重复引用超长名字在字符串分配前拒绝", () =>
            {
                var text = new string('A', 6000);
                var bytes = value == 1 ? Encoding.ASCII.GetBytes(text) : Encoding.BigEndianUnicode.GetBytes(text);
                var font = RepeatedFont(value, bytes, 512);
                _ = Read(RepeatedFont(value, bytes, 1)); // Warm the actual production path, not a copied model.
                var before = GC.GetAllocatedBytesForCurrentThread();
                var names = Read(font);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(0, names.Count);
                Assert.True(allocated < font.Length + 128 * 1024,
                    $"name表={font.Length}字节，实际解析分配={allocated}字节；拒绝记录不得放大成完整字符串");
                Console.WriteLine($"    分配证据 platform={value} table/file={font.Length} allocated={allocated}");
            });
        }

        Test("PERF-16：64候选耗尽后不继续解码重复的128字符有效名字", () =>
        {
            var bytes = Encoding.BigEndianUnicode.GetBytes(new string('A', 128));
            var font = RepeatedFont(3, bytes, 4096);
            _ = Read(RepeatedFont(3, bytes, 64));
            var before = GC.GetAllocatedBytesForCurrentThread();
            var names = Read(font);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(1, names.Count);
            Assert.Equal(new string('A', 128), names[0].Family);
            Assert.True(allocated < font.Length + 128 * 1024,
                $"候选满后仍在分配：file={font.Length} allocated={allocated}");
            Console.WriteLine($"    候选耗尽分配证据 file={font.Length} allocated={allocated}");
        });
    }

    private static IReadOnlyList<SfntFontNames> Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return SfntNames.Read(stream);
    }

    private static byte[] RepeatedFont(ushort platform, byte[] name, int records)
    {
        var heap = checked((ushort)(6 + records * 12));
        var table = new byte[heap + name.Length];
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), checked((ushort)records));
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), heap);
        for (var index = 0; index < records; index++)
        {
            var record = table.AsSpan(6 + index * 12, 12);
            BinaryPrimitives.WriteUInt16BigEndian(record, platform);
            BinaryPrimitives.WriteUInt16BigEndian(record[2..], platform == 3 ? (ushort)1 : (ushort)0);
            BinaryPrimitives.WriteUInt16BigEndian(record[4..], platform == 3 ? (ushort)0x0409 : (ushort)0);
            BinaryPrimitives.WriteUInt16BigEndian(record[6..], 1);
            BinaryPrimitives.WriteUInt16BigEndian(record[8..], checked((ushort)name.Length));
        }
        name.CopyTo(table, heap);
        return WrapTable(table);
    }

    private static byte[] WrapTable(byte[] table)
    {
        var font = new byte[28 + table.Length];
        BinaryPrimitives.WriteUInt32BigEndian(font, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(12), 0x6E616D65);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(20), 28);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(24), (uint)table.Length);
        table.CopyTo(font, 28);
        return font;
    }
}
