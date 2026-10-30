using System.Buffers.Binary;
using System.Text;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class FontDecodeBudgetTests
{
    private readonly record struct NameRecord(ushort NameId, ushort Platform, ushort Language, byte[] Bytes,
        ushort Encoding = 1);

    private static void RegisterSemantics()
    {
        foreach (var platform in new ushort[] { 0, 3, 1 })
        {
            var value = platform;
            Test($"PERF-16名字边界：platform {platform} 保留128字符，129字符在decode前拒绝", () =>
            {
                var valid = new string('A', 128);
                var invalid = new string('B', 129);
                var names = Read(Font(Name(1, value, 0x0409, invalid), Name(1, value, 0x0409, valid)));
                Assert.Equal(1, names.Count);
                Assert.Equal(valid, names[0].Family);
                Assert.False(names[0].AlsoCalled.Contains(invalid));
            });
        }

        Test("PERF-16Unicode：有效代理对/中日文/组合字符及前后Unicode空白保持原名字语义", () =>
        {
            const string name = "中🌸日𠀀 é";
            var font = Font(Name(1, 3, 0x0804, "\u3000\t" + name + "\r\n\u00A0"),
                Name(1, 3, 0x0409, "English Family"), Name(16, 3, 0x0409, "Typographic Family"));
            var names = Read(font);
            Assert.Equal(1, names.Count);
            Assert.Equal("English Family", names[0].Family);
            Assert.True(names[0].AlsoCalled.Contains(name));
            Assert.True(names[0].AlsoCalled.Contains("Typographic Family"));
            Assert.False(names[0].AlsoCalled.Any(alias => alias.Contains('�')));
        });

        Test("PERF-16Unicode：128 UTF16码元含64个完整代理对合法，超过既有128码元上限仍拒绝", () =>
        {
            var valid = string.Concat(Enumerable.Repeat("🌸", 64));
            var invalid = string.Concat(Enumerable.Repeat("🌸", 65));
            Assert.Equal(128, valid.Length);
            var names = Read(Font(Name(1, 3, 0x0409, invalid), Name(1, 3, 0x0409, valid)));
            Assert.Equal(valid, names.Single().Family);
        });

        Test("PERF-16padding：UTF16/Mac上千空白围住正常短名字，不误拒绝或分配完整padding字符串", () =>
        {
            foreach (var platform in new ushort[] { 3, 1 })
            {
                var padded = new string(' ', 4000) + "Normal Family" + new string(' ', 4000);
                var font = Font(Name(1, platform, platform == 1 ? (ushort)0 : (ushort)0x0409, padded));
                _ = Read(font);
                var before = GC.GetAllocatedBytesForCurrentThread();
                var names = Read(font);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal("Normal Family", names.Single().Family);
                Assert.True(allocated < font.Length + 8192, $"padding不应GetString后Trim：allocated={allocated}");
            }
        });

        Test("PERF-16拒绝：孤立代理/奇数UTF16/替换字符/内部控制码/Mac非ASCII不产生坏名字", () =>
        {
            var high = new byte[] { 0xD8, 0x3C };
            var low = new byte[] { 0xDF, 0x38 };
            var invalid = Font(new NameRecord(1, 3, 0x0409, high), new NameRecord(1, 3, 0x0409, low),
                new NameRecord(1, 3, 0x0409, new byte[] { 0, (byte)'A', 0 }),
                Name(1, 3, 0x0409, "A�B"), Name(1, 3, 0x0409, "A\tB"),
                new NameRecord(1, 1, 0, new byte[] { 0x41, 0x80, 0x42 }, 0),
                Name(1, 3, 0x0409, "Safe Family"));
            var names = Read(invalid);
            Assert.Equal("Safe Family", names.Single().Family);
            Assert.Equal(0, names[0].AlsoCalled.Count);
        });

        Test("PERF-16候选预算：64个本地化名后来的英文族名仍优先，别名数不扩容", () =>
        {
            var records = Enumerable.Range(0, 64).Select(index => Name(1, 3, 0x0804, "族名" + index)).ToList();
            records.Add(Name(1, 3, 0x0409, "Late English"));
            records.AddRange(Enumerable.Range(0, 64).Select(index => Name(1, 3, 0x0409, "Unused English " + index)));
            var names = Read(Font(records.ToArray()));
            Assert.Equal("Late English", names.Single().Family);
            Assert.True(names[0].AlsoCalled.Count <= 63);
            Assert.True(names[0].AlsoCalled.Contains("族名0"));
            Assert.False(names[0].AlsoCalled.Contains("Unused English 0"));
        });

        Test("PERF-16候选预算：family与typographic各64条，fallback英文选择与源名字区分不被合并", () =>
        {
            var records = Enumerable.Range(0, 64).Select(index => Name(16, 3, 0x0804, "排印族" + index)).ToList();
            records.Add(Name(16, 3, 0x0409, "Late Typographic"));
            var fallback = Read(Font(records.ToArray()));
            Assert.Equal("Late Typographic", fallback.Single().Family);
            records.Add(Name(1, 3, 0x0409, "Regular Light"));
            var family = Read(Font(records.ToArray()));
            Assert.Equal("Regular Light", family.Single().Family);
            Assert.True(family[0].AlsoCalled.Contains("Late Typographic"));
        });

        Test("PERF-16文件边界：既有name表1MiB与TTC 512字体上限保持，超限不继续读decode", () =>
        {
            var single = Font(Name(1, 3, 0x0409, "Font Boundaries"));
            var tooLarge = (byte[])single.Clone();
            BinaryPrimitives.WriteUInt32BigEndian(tooLarge.AsSpan(24), (1 << 20) + 1);
            Assert.Equal(0, Read(tooLarge).Count);
            var header = new byte[12];
            BinaryPrimitives.WriteUInt32BigEndian(header, 0x74746366);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 0x00010000);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), 513);
            Assert.Equal(0, Read(header).Count);
            var starts = 12 + 512 * 4;
            var collection = new byte[starts + single.Length];
            BinaryPrimitives.WriteUInt32BigEndian(collection, 0x74746366);
            BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(4), 0x00010000);
            BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(8), 512);
            for (var index = 0; index < 512; index++)
                BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(12 + index * 4), (uint)starts);
            single.CopyTo(collection, starts);
            BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(starts + 20), (uint)(starts + 28));
            Assert.Equal(512, Read(collection).Count);
        });

        Test("PERF-16生产目录：FontCatalogue只读临时字体并保留英文/Unicode别名、搜索和用户名字选择", () =>
        {
            using var directory = new BudgetDirectory();
            var file = System.IO.Path.Combine(directory.Path, "synthetic.ttf");
            File.WriteAllBytes(file, Font(Name(1, 3, 0x0409, "English Family"), Name(1, 3, 0x0804, "本地族🌸")));
            var catalogue = FontCatalogue.FromFiles(new[] { file });
            Assert.Equal(1, catalogue.FileCount);
            Assert.Equal(1, catalogue.Families.Count);
            Assert.Equal("English Family", catalogue.Families[0].Name);
            Assert.True(catalogue.Families[0].AnswersTo("本地族🌸"));
            Assert.Equal(1, catalogue.Search("本地族").Count);
            Assert.Equal(1, catalogue.Including("本地族🌸").Families.Count);
        });
    }

    private static NameRecord Name(ushort id, ushort platform, ushort language, string text) =>
        new(id, platform, language, platform == 1 ? Encoding.ASCII.GetBytes(text) : Encoding.BigEndianUnicode.GetBytes(text),
            platform == 1 ? (ushort)0 : (ushort)1);

    private static byte[] Font(params NameRecord[] records)
    {
        var heap = checked((ushort)(6 + records.Length * 12));
        var table = new byte[heap + records.Sum(record => record.Bytes.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), checked((ushort)records.Length));
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), heap);
        var offset = 0;
        for (var index = 0; index < records.Length; index++)
        {
            var name = records[index];
            var record = table.AsSpan(6 + index * 12, 12);
            BinaryPrimitives.WriteUInt16BigEndian(record, name.Platform);
            BinaryPrimitives.WriteUInt16BigEndian(record[2..], name.Encoding);
            BinaryPrimitives.WriteUInt16BigEndian(record[4..], name.Language);
            BinaryPrimitives.WriteUInt16BigEndian(record[6..], name.NameId);
            BinaryPrimitives.WriteUInt16BigEndian(record[8..], checked((ushort)name.Bytes.Length));
            BinaryPrimitives.WriteUInt16BigEndian(record[10..], checked((ushort)offset));
            name.Bytes.CopyTo(table, heap + offset);
            offset += name.Bytes.Length;
        }
        return WrapTable(table);
    }
}
