using System.IO;
using System.Buffers.Binary;

namespace CotwLiveTracker.Desktop.Maps;

internal sealed record DecodedMapTile(
    int Width,
    int Height,
    int Stride,
    int DxgiFormat,
    byte[] Bgra32);

internal static class AvtxMapTileDecoder
{
    private const int DxgiR8G8B8A8Unorm = 28;
    private const int DxgiR8G8B8A8UnormSrgb = 29;
    private const int DxgiBc1Typeless = 70;
    private const int DxgiBc1Unorm = 71;
    private const int DxgiBc1UnormSrgb = 72;
    private const int DxgiBc3Typeless = 76;
    private const int DxgiBc3Unorm = 77;
    private const int DxgiBc3UnormSrgb = 78;
    private const int DxgiB8G8R8A8Unorm = 87;
    private const int DxgiB8G8R8A8Typeless = 90;
    private const int DxgiB8G8R8A8UnormSrgb = 91;

    public static DecodedMapTile Decode(byte[] avtx)
    {
        ArgumentNullException.ThrowIfNull(avtx);

        if (avtx.Length < 40 ||
            avtx[0] != (byte)'A' ||
            avtx[1] != (byte)'V' ||
            avtx[2] != (byte)'T' ||
            avtx[3] != (byte)'X')
        {
            throw new InvalidDataException("Map tile is not an AVTX/DDSC payload.");
        }

        var span = avtx.AsSpan();
        var version = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(4, 2));
        if (version != 1)
        {
            throw new InvalidDataException($"Unsupported AVTX version {version}.");
        }

        var dxgiFormat = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(8, 4));
        var sourceWidth = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(12, 2));
        var sourceHeight = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(14, 2));
        var totalMipCount = span[20];
        var mipCountInFile = span[21];
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(32, 4));

        var total = Math.Max(1, totalMipCount);
        var present = Math.Max(1, mipCountInFile);
        if (present > total)
        {
            throw new InvalidDataException(
                $"AVTX mip counts are invalid ({present}/{total}).");
        }

        var firstMip = total - present;
        var width = Math.Max(1, sourceWidth >> firstMip);
        var height = Math.Max(1, sourceHeight >> firstMip);

        if (headerSize < 40 || headerSize > avtx.Length)
        {
            throw new InvalidDataException(
                $"AVTX header size {headerSize} is outside the tile payload.");
        }

        var body = span.Slice((int)headerSize);
        var pixels = checked(new byte[width * height * 4]);

        switch (dxgiFormat)
        {
            case DxgiBc1Typeless:
            case DxgiBc1Unorm:
            case DxgiBc1UnormSrgb:
                DecodeBc1(body, width, height, pixels);
                break;

            case DxgiBc3Typeless:
            case DxgiBc3Unorm:
            case DxgiBc3UnormSrgb:
                DecodeBc3(body, width, height, pixels);
                break;

            case DxgiR8G8B8A8Unorm:
            case DxgiR8G8B8A8UnormSrgb:
                DecodeRgba32(body, width, height, pixels);
                break;

            case DxgiB8G8R8A8Unorm:
            case DxgiB8G8R8A8Typeless:
            case DxgiB8G8R8A8UnormSrgb:
                DecodeBgra32(body, width, height, pixels);
                break;

            default:
                throw new NotSupportedException(
                    $"COTW map tile DXGI format {dxgiFormat} is not supported yet. " +
                    "BC1, BC3, RGBA8 and BGRA8 are currently supported.");
        }

        return new DecodedMapTile(
            width,
            height,
            checked(width * 4),
            dxgiFormat,
            pixels);
    }

    private static void DecodeRgba32(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        byte[] destination)
    {
        var required = checked(width * height * 4);
        if (source.Length < required)
        {
            throw new InvalidDataException("RGBA AVTX body is truncated.");
        }

        for (var pixel = 0; pixel < width * height; pixel++)
        {
            var sourceOffset = pixel * 4;
            var destinationOffset = sourceOffset;
            destination[destinationOffset] = source[sourceOffset + 2];
            destination[destinationOffset + 1] = source[sourceOffset + 1];
            destination[destinationOffset + 2] = source[sourceOffset];
            destination[destinationOffset + 3] = source[sourceOffset + 3];
        }
    }

    private static void DecodeBgra32(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        byte[] destination)
    {
        var required = checked(width * height * 4);
        if (source.Length < required)
        {
            throw new InvalidDataException("BGRA AVTX body is truncated.");
        }

        source.Slice(0, required).CopyTo(destination);
    }

    private static void DecodeBc1(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        byte[] destination)
    {
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        var required = checked(blocksX * blocksY * 8);
        if (source.Length < required)
        {
            throw new InvalidDataException("BC1 AVTX body is truncated.");
        }

        var sourceOffset = 0;
        Span<Rgba> colors = stackalloc Rgba[4];
        for (var blockY = 0; blockY < blocksY; blockY++)
        {
            for (var blockX = 0; blockX < blocksX; blockX++)
            {
                var color0 = BinaryPrimitives.ReadUInt16LittleEndian(
                    source.Slice(sourceOffset, 2));
                var color1 = BinaryPrimitives.ReadUInt16LittleEndian(
                    source.Slice(sourceOffset + 2, 2));
                var indices = BinaryPrimitives.ReadUInt32LittleEndian(
                    source.Slice(sourceOffset + 4, 4));
                sourceOffset += 8;

                BuildBc1Palette(
                    color0,
                    color1,
                    allowTransparentMode: true,
                    colors);

                WriteColorBlock(
                    destination,
                    width,
                    height,
                    blockX,
                    blockY,
                    indices,
                    colors);
            }
        }
    }

    private static void DecodeBc3(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        byte[] destination)
    {
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        var required = checked(blocksX * blocksY * 16);
        if (source.Length < required)
        {
            throw new InvalidDataException("BC3 AVTX body is truncated.");
        }

        var sourceOffset = 0;
        Span<Rgba> colors = stackalloc Rgba[4];
        Span<byte> alphas = stackalloc byte[8];
        for (var blockY = 0; blockY < blocksY; blockY++)
        {
            for (var blockX = 0; blockX < blocksX; blockX++)
            {
                var alpha0 = source[sourceOffset];
                var alpha1 = source[sourceOffset + 1];

                ulong alphaBits = 0;
                for (var index = 0; index < 6; index++)
                {
                    alphaBits |= (ulong)source[sourceOffset + 2 + index] << (8 * index);
                }

                var color0 = BinaryPrimitives.ReadUInt16LittleEndian(
                    source.Slice(sourceOffset + 8, 2));
                var color1 = BinaryPrimitives.ReadUInt16LittleEndian(
                    source.Slice(sourceOffset + 10, 2));
                var colorBits = BinaryPrimitives.ReadUInt32LittleEndian(
                    source.Slice(sourceOffset + 12, 4));
                sourceOffset += 16;

                BuildBc1Palette(
                    color0,
                    color1,
                    allowTransparentMode: false,
                    colors);

                BuildBc3AlphaPalette(alpha0, alpha1, alphas);

                for (var pixelIndex = 0; pixelIndex < 16; pixelIndex++)
                {
                    var localX = pixelIndex & 3;
                    var localY = pixelIndex >> 2;
                    var x = (blockX * 4) + localX;
                    var y = (blockY * 4) + localY;
                    if (x >= width || y >= height)
                    {
                        continue;
                    }

                    var colorIndex = (int)((colorBits >> (pixelIndex * 2)) & 0x3);
                    var alphaIndex = (int)((alphaBits >> (pixelIndex * 3)) & 0x7);
                    var color = colors[colorIndex];
                    var destinationOffset = ((y * width) + x) * 4;
                    destination[destinationOffset] = color.B;
                    destination[destinationOffset + 1] = color.G;
                    destination[destinationOffset + 2] = color.R;
                    destination[destinationOffset + 3] = alphas[alphaIndex];
                }
            }
        }
    }

    private static void WriteColorBlock(
        byte[] destination,
        int width,
        int height,
        int blockX,
        int blockY,
        uint indices,
        ReadOnlySpan<Rgba> colors)
    {
        for (var pixelIndex = 0; pixelIndex < 16; pixelIndex++)
        {
            var localX = pixelIndex & 3;
            var localY = pixelIndex >> 2;
            var x = (blockX * 4) + localX;
            var y = (blockY * 4) + localY;
            if (x >= width || y >= height)
            {
                continue;
            }

            var colorIndex = (int)((indices >> (pixelIndex * 2)) & 0x3);
            var color = colors[colorIndex];
            var destinationOffset = ((y * width) + x) * 4;
            destination[destinationOffset] = color.B;
            destination[destinationOffset + 1] = color.G;
            destination[destinationOffset + 2] = color.R;
            destination[destinationOffset + 3] = color.A;
        }
    }

    private static void BuildBc1Palette(
        ushort packed0,
        ushort packed1,
        bool allowTransparentMode,
        Span<Rgba> colors)
    {
        colors[0] = DecodeRgb565(packed0);
        colors[1] = DecodeRgb565(packed1);

        if (!allowTransparentMode || packed0 > packed1)
        {
            colors[2] = Blend(colors[0], colors[1], 2, 1, 3);
            colors[3] = Blend(colors[0], colors[1], 1, 2, 3);
        }
        else
        {
            colors[2] = Blend(colors[0], colors[1], 1, 1, 2);
            colors[3] = new Rgba(0, 0, 0, 0);
        }
    }

    private static void BuildBc3AlphaPalette(
        byte alpha0,
        byte alpha1,
        Span<byte> values)
    {
        values[0] = alpha0;
        values[1] = alpha1;

        if (alpha0 > alpha1)
        {
            for (var index = 1; index <= 6; index++)
            {
                values[index + 1] = (byte)(
                    (((7 - index) * alpha0) + (index * alpha1)) / 7);
            }
        }
        else
        {
            for (var index = 1; index <= 4; index++)
            {
                values[index + 1] = (byte)(
                    (((5 - index) * alpha0) + (index * alpha1)) / 5);
            }

            values[6] = 0;
            values[7] = 255;
        }
    }

    private static Rgba DecodeRgb565(ushort packed)
    {
        var r = (packed >> 11) & 0x1F;
        var g = (packed >> 5) & 0x3F;
        var b = packed & 0x1F;

        return new Rgba(
            (byte)((r * 255 + 15) / 31),
            (byte)((g * 255 + 31) / 63),
            (byte)((b * 255 + 15) / 31),
            255);
    }

    private static Rgba Blend(
        Rgba first,
        Rgba second,
        int firstWeight,
        int secondWeight,
        int divisor) =>
        new(
            (byte)(((first.R * firstWeight) + (second.R * secondWeight)) / divisor),
            (byte)(((first.G * firstWeight) + (second.G * secondWeight)) / divisor),
            (byte)(((first.B * firstWeight) + (second.B * secondWeight)) / divisor),
            255);

    private readonly record struct Rgba(
        byte R,
        byte G,
        byte B,
        byte A);
}
