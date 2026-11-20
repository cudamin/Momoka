using System.Buffers.Binary;

namespace Momoka.Infrastructure;

/// <summary>
/// Reads APNG frame controls and wraps each frame's compressed image data in a standalone PNG.
/// Pixel data is decoded by the platform. See https://www.w3.org/TR/png-3/#animation-information.
/// </summary>
internal sealed class AnimatedPng
{
    private const int MaxInputBytes = 32 * 1024 * 1024;
    private const int MaxFrames = 240;
    private const int MaxDimension = 4096;
    private const int MaxCanvasPixels = 4 * 1024 * 1024;
    private const int MaxFramePixels = 64 * 1000 * 1000;
    private const int MaxChunks = 32768;

    private const uint ImageHeader = 0x49484452;       // IHDR
    private const uint ImageData = 0x49444154;         // IDAT
    private const uint ImageEnd = 0x49454E44;          // IEND
    private const uint AnimationControl = 0x6163544C;  // acTL
    private const uint FrameControl = 0x6663544C;       // fcTL
    private const uint FrameData = 0x66644154;          // fdAT
    private const uint Palette = 0x504C5445;            // PLTE
    private const uint Transparency = 0x74524E53;      // tRNS
    private const uint Chromaticity = 0x6348524D;      // cHRM
    private const uint CodePoints = 0x63494350;         // cICP
    private const uint Gamma = 0x67414D41;              // gAMA
    private const uint StandardRgb = 0x73524742;        // sRGB
    private const uint PixelDimensions = 0x70485973;    // pHYs
    private static readonly uint[] CrcTable = CreateCrcTable();

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    private readonly byte[] _source;
    private readonly IReadOnlyList<Range> _colorChunks;

    private AnimatedPng(byte[] source, int width, int height, uint plays,
        List<Range> colorChunks, List<AnimatedPngPart> frames)
    {
        _source = source;
        _colorChunks = colorChunks;
        Width = width;
        Height = height;
        Plays = plays;
        Frames = frames;
    }

    internal int Width { get; }
    internal int Height { get; }
    internal uint Plays { get; }
    internal IReadOnlyList<AnimatedPngPart> Frames { get; }

    internal static bool CanAnimate(byte[] bytes) =>
        bytes.Length <= MaxInputBytes && bytes.AsSpan().StartsWith(Signature) && HasAnimationControl(bytes);

    internal static AnimatedPng? Parse(byte[] bytes)
    {
        if (!CanAnimate(bytes)) return null;
        ReadOnlySpan<byte> source = bytes;
        if (!ReadChunk(source, Signature.Length, out var header)
            || header.Type != ImageHeader || header.Size != 13 || !HasCorrectCrc(source, header)) return null;

        var headerData = source[header.Payload];
        var width = BinaryPrimitives.ReadUInt32BigEndian(headerData);
        var height = BinaryPrimitives.ReadUInt32BigEndian(headerData[4..]);
        if (width is 0 or > MaxDimension || height is 0 or > MaxDimension || width * height > MaxCanvasPixels
            || !ValidHeader(headerData)) return null;

        return new FrameReader(bytes, (int)width, (int)height, headerData[8], headerData[9]).Read(header.End);
    }

    // For ordinary PNGs only scan chunk headers up to IDAT. JPEG, GIF and WebP skip even this work.
    private static bool HasAnimationControl(ReadOnlySpan<byte> source)
    {
        int offset = Signature.Length;
        for (int index = 0; index < MaxChunks && ReadChunk(source, offset, out var chunk); index++)
        {
            if (chunk.Type == AnimationControl) return true;
            if (chunk.Type is ImageData or ImageEnd) return false;
            offset = chunk.End;
        }
        return false;
    }

    private static bool ValidHeader(ReadOnlySpan<byte> header)
    {
        if (header[10] != 0 || header[11] != 0 || header[12] > 1) return false;
        return header[9] switch
        {
            0 => header[8] is 1 or 2 or 4 or 8 or 16,
            2 or 4 or 6 => header[8] is 8 or 16,
            3 => header[8] is 1 or 2 or 4 or 8,
            _ => false
        };
    }

    internal byte[] GetFramePng(AnimatedPngPart frame)
    {
        ReadOnlySpan<byte> source = _source;
        // Signature + IHDR + IEND. The remaining chunks retain their PNG envelope sizes.
        int length = Signature.Length + 25 + 12;
        foreach (var chunk in _colorChunks) length += source[chunk].Length;
        foreach (var segment in frame.Segments) length += 12 + source[segment].Length;

        var image = new byte[length];
        Span<byte> destination = image;
        Signature.CopyTo(destination);
        destination = destination[Signature.Length..];

        Span<byte> header = stackalloc byte[13];
        source.Slice(Signature.Length + 8, header.Length).CopyTo(header);
        BinaryPrimitives.WriteInt32BigEndian(header, frame.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], frame.Height);
        destination = WriteChunk(destination, ImageHeader, header);

        foreach (var chunk in _colorChunks)
        {
            var data = source[chunk];
            data.CopyTo(destination);
            destination = destination[data.Length..];
        }
        foreach (var segment in frame.Segments) destination = WriteChunk(destination, ImageData, source[segment]);
        WriteChunk(destination, ImageEnd, []);
        return image;
    }

    private sealed class FrameReader(byte[] source, int width, int height, byte bitDepth, byte colorType)
    {
        private readonly List<Range> _colorChunks = [];
        private readonly List<AnimatedPngPart> _frames = [];
        private readonly HashSet<uint> _colorTypes = [];
        private uint _plays;
        private uint _expectedFrames;
        private uint _sequence;
        private int _framePixels;
        private int _paletteEntries;
        private int _idatBytes;
        private int _frameBytes;
        private bool _seenIdat;
        private bool _closedIdat;
        private bool _frameUsesIdat;
        private AnimatedPngPart? _frame;
        private List<Range>? _segments;

        internal AnimatedPng? Read(int offset)
        {
            ReadOnlySpan<byte> bytes = source;
            for (int index = 0; index < MaxChunks && ReadChunk(bytes, offset, out var chunk); index++)
            {
                if (!HasCorrectCrc(bytes, chunk)) return null;
                if (_seenIdat && chunk.Type != ImageData) _closedIdat = true;
                var payload = bytes[chunk.Payload];
                switch (chunk.Type)
                {
                    case AnimationControl:
                        if (!ReadAnimationControl(payload)) return null;
                        break;
                    case FrameControl:
                        if (!ReadFrameControl(payload)) return null;
                        break;
                    case FrameData:
                        if (!_seenIdat || _frameUsesIdat || _segments is null || payload.Length < 4
                            || !ReadSequence(payload)) return null;
                        AddFrameData((chunk.Offset + 12)..(chunk.End - 4), payload.Length - 4);
                        break;
                    case ImageData:
                        if (_closedIdat || _expectedFrames == 0 || (colorType == 3 && _paletteEntries == 0)) return null;
                        _seenIdat = true;
                        _idatBytes += payload.Length;
                        if (_segments is not null) AddFrameData(chunk.Payload, payload.Length);
                        break;
                    case ImageEnd:
                        return payload.IsEmpty && chunk.End == source.Length && _idatBytes > 0
                            && FinishFrame() && _frames.Count == _expectedFrames
                            ? new AnimatedPng(source, width, height, _plays, _colorChunks, _frames)
                            : null;
                    case Palette:
                    case Transparency:
                    case Chromaticity:
                    case CodePoints:
                    case Gamma:
                    case StandardRgb:
                    case PixelDimensions:
                        if (!ReadColorChunk(chunk, payload)) return null;
                        break;
                    default:
                        if (!IsAncillary(chunk.Type)) return null;
                        break;
                }
                offset = chunk.End;
            }
            return null;
        }

        private bool ReadAnimationControl(ReadOnlySpan<byte> data)
        {
            if (_seenIdat || _expectedFrames != 0 || data.Length != 8) return false;
            _expectedFrames = BinaryPrimitives.ReadUInt32BigEndian(data);
            _plays = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
            // PNG's four-byte unsigned integer is limited to 2^31 - 1.
            return _expectedFrames is > 0 and <= MaxFrames && _plays <= int.MaxValue;
        }

        private bool ReadFrameControl(ReadOnlySpan<byte> data)
        {
            if (data.Length != 26 || !ReadSequence(data) || !FinishFrame() || _frames.Count >= MaxFrames
                || (_expectedFrames != 0 && _frames.Count >= _expectedFrames)) return false;

            var frameWidth = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
            var frameHeight = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
            var x = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
            var y = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
            if (frameWidth == 0 || frameWidth > width || frameHeight == 0 || frameHeight > height
                || x > width - frameWidth || y > height - frameHeight
                || (!_seenIdat && (x != 0 || y != 0 || frameWidth != width || frameHeight != height))
                || data[24] > 2 || data[25] > 1) return false;

            _framePixels += (int)(frameWidth * frameHeight);
            if (_framePixels > MaxFramePixels) return false;
            var numerator = BinaryPrimitives.ReadUInt16BigEndian(data[20..]);
            var denominator = BinaryPrimitives.ReadUInt16BigEndian(data[22..]);
            var delay = TimeSpan.FromSeconds((double)numerator / (denominator == 0 ? 100 : denominator));
            byte dispose = _frames.Count == 0 && data[24] == 2 ? (byte)1 : data[24];
            _segments = [];
            _frameBytes = 0;
            _frameUsesIdat = !_seenIdat;
            _frame = new AnimatedPngPart((int)x, (int)y, (int)frameWidth, (int)frameHeight,
                delay, dispose, data[25], _segments);
            return true;
        }

        private bool ReadSequence(ReadOnlySpan<byte> data) =>
            BinaryPrimitives.ReadUInt32BigEndian(data) == _sequence++;

        private void AddFrameData(Range segment, int size)
        {
            _segments?.Add(segment);
            _frameBytes += size;
        }

        private bool FinishFrame()
        {
            if (_frame is null) return true;
            if (_frameBytes == 0) return false;
            _frames.Add(_frame);
            _frame = null;
            _segments = null;
            return true;
        }

        private bool ReadColorChunk(PngChunk chunk, ReadOnlySpan<byte> data)
        {
            if (_seenIdat || !_colorTypes.Add(chunk.Type)) return false;

            // Keep the palette, transparency and bounded color/scale metadata used to read the pixels.
            // Compressed color profiles and unrelated metadata are left in the original cover.
            bool valid = chunk.Type switch
            {
                Palette => ReadPalette(data.Length),
                Transparency => colorType switch
                {
                    0 => data.Length == 2,
                    2 => data.Length == 6,
                    3 => data.Length > 0 && data.Length <= _paletteEntries,
                    _ => false
                },
                Chromaticity => _paletteEntries == 0 && data.Length == 32,
                CodePoints => _paletteEntries == 0 && data.Length == 4,
                Gamma => _paletteEntries == 0 && data.Length == 4,
                StandardRgb => _paletteEntries == 0 && data.Length == 1,
                PixelDimensions => data.Length == 9,
                _ => false
            };
            if (valid) _colorChunks.Add(chunk.Offset..chunk.End);
            return valid;
        }

        private bool ReadPalette(int length)
        {
            if (colorType is 0 or 4 || length is 0 or > 768 || length % 3 != 0 || _colorTypes.Contains(Transparency))
                return false;
            _paletteEntries = length / 3;
            return colorType != 3 || _paletteEntries <= 1 << bitDepth;
        }
    }

    private readonly record struct PngChunk(int Offset, int Size, uint Type)
    {
        internal int End => Offset + 12 + Size;
        internal Range Payload => (Offset + 8)..(End - 4);
    }

    private static bool ReadChunk(ReadOnlySpan<byte> source, int offset, out PngChunk chunk)
    {
        chunk = default;
        var tail = source[offset..];
        if (tail.Length < 12) return false;
        var size = BinaryPrimitives.ReadUInt32BigEndian(tail);
        if (size > tail.Length - 12) return false;
        var type = BinaryPrimitives.ReadUInt32BigEndian(tail[4..]);
        chunk = new PngChunk(offset, (int)size, type);
        return true;
    }

    private static bool IsAncillary(uint type) => (type & 0x20000000) != 0;

    private static bool HasCorrectCrc(ReadOnlySpan<byte> source, PngChunk chunk) =>
        ComputeCrc(source[(chunk.Offset + 4)..(chunk.End - 4)]) ==
        BinaryPrimitives.ReadUInt32BigEndian(source[(chunk.End - 4)..]);

    private static Span<byte> WriteChunk(Span<byte> destination, uint type, scoped ReadOnlySpan<byte> data)
    {
        BinaryPrimitives.WriteInt32BigEndian(destination, data.Length);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], type);
        data.CopyTo(destination[8..]);
        BinaryPrimitives.WriteUInt32BigEndian(destination[(8 + data.Length)..],
            ComputeCrc(destination.Slice(4, 4 + data.Length)));
        return destination[(12 + data.Length)..];
    }

    private static uint ComputeCrc(ReadOnlySpan<byte> contents)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in contents) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint crc = index;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            table[index] = crc;
        }
        return table;
    }
}

internal sealed record AnimatedPngPart(int X, int Y, int Width, int Height, TimeSpan Delay, byte Dispose, byte Blend,
    IReadOnlyList<Range> Segments);
