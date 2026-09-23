using System.IO;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace CotwLiveTracker.Desktop.Maps;

internal sealed class ApexV3ArchiveIndex : IDisposable
{
    private sealed record ArchiveEntry(
        string TabPath,
        string ArcPath,
        uint Hash,
        uint Offset,
        uint Size,
        int Priority,
        DateTime LastWriteTimeUtc);

    private readonly Dictionary<uint, List<ArchiveEntry>> _entries = new();
    private readonly Dictionary<string, FileStream> _openArchives =
        new(StringComparer.OrdinalIgnoreCase);

    public ApexV3ArchiveIndex(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);

        var roots = new[]
        {
            Path.Combine(gameDirectory, "archives_win64"),
            Path.Combine(gameDirectory, "dlc_win64"),
            Path.Combine(gameDirectory, "patch_win64")
        };

        var tabFiles = roots
            .Where(Directory.Exists)
            .SelectMany(root =>
                Directory.EnumerateFiles(
                    root,
                    "*.tab",
                    SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (tabFiles.Length == 0)
        {
            throw new DirectoryNotFoundException(
                $"No COTW .tab archives were found under {gameDirectory}.");
        }

        foreach (var tabPath in tabFiles)
        {
            IndexTab(tabPath);
        }

        TabFileCount = tabFiles.Length;
        EntryCount = _entries.Values.Sum(entries => entries.Count);
    }

    public int TabFileCount { get; }
    public int EntryCount { get; }

    public bool ContainsVirtualPath(string virtualPath) =>
        _entries.ContainsKey(HashVirtualPath(virtualPath));

    public byte[] ReadVirtualFile(
        string virtualPath,
        string? expectedMagic = null)
    {
        var hash = HashVirtualPath(virtualPath);
        if (!_entries.TryGetValue(hash, out var matches) ||
            matches.Count == 0)
        {
            throw new FileNotFoundException(
                $"Asset was not found in COTW archives: {virtualPath}");
        }

        Exception? lastError = null;
        foreach (var entry in matches
                     .OrderByDescending(item => item.Priority)
                     .ThenByDescending(item => item.LastWriteTimeUtc)
                     .ThenByDescending(item => item.TabPath, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var payload = ReadEntry(entry);
                if (IsAaf(payload))
                {
                    payload = ExtractAaf(payload);
                }

                if (expectedMagic is not null &&
                    !StartsWithAscii(payload, expectedMagic))
                {
                    lastError = new InvalidDataException(
                        $"Archive candidate for {virtualPath} did not start with {expectedMagic}.");
                    continue;
                }

                return payload;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidDataException(
            $"COTW asset {virtualPath} was indexed, but no readable matching archive entry was found.",
            lastError);
    }

    public static uint HashVirtualPath(string virtualPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        var normalized = virtualPath
            .Replace('\\', '/')
            .TrimStart('/');
        var data = Encoding.ASCII.GetBytes(normalized);
        return JenkinsHashLittle(data);
    }

    private void IndexTab(string tabPath)
    {
        var arcPath = Path.ChangeExtension(tabPath, ".arc");
        if (!File.Exists(arcPath))
        {
            return;
        }

        using var stream = File.Open(
            tabPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new BinaryReader(stream);

        if (stream.Length < 12)
        {
            return;
        }

        var magic = reader.ReadBytes(4);
        var version0 = reader.ReadUInt16();
        var version1 = reader.ReadUInt16();
        var blockSize = reader.ReadUInt32();

        if (!magic.SequenceEqual(new byte[] { (byte)'T', (byte)'A', (byte)'B', 0 }) ||
            version0 != 2 ||
            version1 != 1 ||
            blockSize is not (2048u or 4096u))
        {
            return;
        }

        var remaining = stream.Length - stream.Position;
        if (remaining < 12)
        {
            return;
        }

        var priority = ArchivePriority(tabPath);
        var lastWrite = File.GetLastWriteTimeUtc(tabPath);

        while (stream.Position + 12 <= stream.Length)
        {
            var hash = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            var size = reader.ReadUInt32();

            if (size == 0)
            {
                continue;
            }

            var entry = new ArchiveEntry(
                tabPath,
                arcPath,
                hash,
                offset,
                size,
                priority,
                lastWrite);

            if (!_entries.TryGetValue(hash, out var list))
            {
                list = [];
                _entries.Add(hash, list);
            }

            list.Add(entry);
        }
    }

    private byte[] ReadEntry(ArchiveEntry entry)
    {
        var stream = GetArchiveStream(entry.ArcPath);

        lock (stream)
        {
            var end = (ulong)entry.Offset + entry.Size;
            if (end > (ulong)stream.Length)
            {
                throw new InvalidDataException(
                    $"Archive entry exceeds {Path.GetFileName(entry.ArcPath)} bounds.");
            }

            stream.Position = entry.Offset;
            var length = checked((int)entry.Size);
            var data = new byte[length];
            stream.ReadExactly(data);
            return data;
        }
    }

    private FileStream GetArchiveStream(string path)
    {
        if (_openArchives.TryGetValue(path, out var stream))
        {
            return stream;
        }

        stream = File.Open(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        _openArchives.Add(path, stream);
        return stream;
    }

    private static bool IsAaf(byte[] data) =>
        data.Length >= 4 &&
        (data[0] == (byte)'A' || data[0] == (byte)'a') &&
        (data[1] == (byte)'A' || data[1] == (byte)'a') &&
        (data[2] == (byte)'F' || data[2] == (byte)'f');

    private static byte[] ExtractAaf(byte[] data)
    {
        using var input = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(input);

        if (data.Length < 48)
        {
            throw new InvalidDataException("AAF payload is shorter than its fixed header.");
        }

        reader.ReadBytes(4);
        reader.ReadUInt32();
        reader.ReadBytes(28);
        var expectedTotal = reader.ReadUInt32();
        reader.ReadUInt32();
        var sectionCount = reader.ReadUInt32();

        using var output = expectedTotal <= int.MaxValue
            ? new MemoryStream((int)expectedTotal)
            : new MemoryStream();

        for (var section = 0u; section < sectionCount; section++)
        {
            var sectionStart = input.Position;
            if (sectionStart + 16 > input.Length)
            {
                throw new InvalidDataException("AAF section header is truncated.");
            }

            var compressedLength = reader.ReadUInt32();
            var uncompressedLength = reader.ReadUInt32();
            var sectionLengthWithHeader = reader.ReadUInt32();
            var marker = reader.ReadBytes(4);

            if (sectionLengthWithHeader < 16 ||
                compressedLength > int.MaxValue ||
                uncompressedLength > int.MaxValue ||
                input.Position + compressedLength > input.Length)
            {
                throw new InvalidDataException("AAF section lengths are invalid.");
            }

            var compressed = reader.ReadBytes((int)compressedLength);
            using var compressedStream = new MemoryStream(compressed, writable: false);
            using var deflate = new DeflateStream(
                compressedStream,
                CompressionMode.Decompress,
                leaveOpen: false);

            var before = output.Length;
            deflate.CopyTo(output);
            var written = output.Length - before;
            if (written != uncompressedLength)
            {
                throw new InvalidDataException(
                    $"AAF section decompressed to {written} bytes; expected {uncompressedLength}.");
            }

            var next = sectionStart + sectionLengthWithHeader;
            if (next > input.Length)
            {
                throw new InvalidDataException("AAF section padding exceeds the payload.");
            }

            input.Position = next;
        }

        if (expectedTotal != 0 &&
            output.Length != expectedTotal)
        {
            throw new InvalidDataException(
                $"AAF decompressed to {output.Length} bytes; expected {expectedTotal}.");
        }

        return output.ToArray();
    }

    private static bool StartsWithAscii(
        byte[] data,
        string expected)
    {
        if (data.Length < expected.Length)
        {
            return false;
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (data[index] != (byte)expected[index])
            {
                return false;
            }
        }

        return true;
    }

    private static int ArchivePriority(string path)
    {
        if (path.Contains(
                $"{Path.DirectorySeparatorChar}patch_win64{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (path.Contains(
                $"{Path.DirectorySeparatorChar}dlc_win64{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1;
    }

    public void Dispose()
    {
        foreach (var stream in _openArchives.Values)
        {
            stream.Dispose();
        }

        _openArchives.Clear();
        GC.SuppressFinalize(this);
    }

    private static uint JenkinsHashLittle(ReadOnlySpan<byte> data)
    {
        unchecked
        {
            var length = data.Length;
            uint a = 0xDEADBEEF + (uint)length;
            uint b = a;
            uint c = a;

            var offset = 0;
            var remaining = length;

            while (remaining > 12)
            {
                a += BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
                b += BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
                c += BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 8, 4));
                Mix(ref a, ref b, ref c);
                offset += 12;
                remaining -= 12;
            }

            switch (remaining)
            {
                case 12:
                    c += (uint)data[offset + 11] << 24;
                    goto case 11;
                case 11:
                    c += (uint)data[offset + 10] << 16;
                    goto case 10;
                case 10:
                    c += (uint)data[offset + 9] << 8;
                    goto case 9;
                case 9:
                    c += data[offset + 8];
                    goto case 8;
                case 8:
                    b += (uint)data[offset + 7] << 24;
                    goto case 7;
                case 7:
                    b += (uint)data[offset + 6] << 16;
                    goto case 6;
                case 6:
                    b += (uint)data[offset + 5] << 8;
                    goto case 5;
                case 5:
                    b += data[offset + 4];
                    goto case 4;
                case 4:
                    a += (uint)data[offset + 3] << 24;
                    goto case 3;
                case 3:
                    a += (uint)data[offset + 2] << 16;
                    goto case 2;
                case 2:
                    a += (uint)data[offset + 1] << 8;
                    goto case 1;
                case 1:
                    a += data[offset];
                    break;
                case 0:
                    return c;
            }

            Final(ref a, ref b, ref c);
            return c;
        }
    }

    private static uint Rotate(uint value, int amount) =>
        (value << amount) | (value >> (32 - amount));

    private static void Mix(
        ref uint a,
        ref uint b,
        ref uint c)
    {
        unchecked
        {
            a -= c; a ^= Rotate(c, 4); c += b;
            b -= a; b ^= Rotate(a, 6); a += c;
            c -= b; c ^= Rotate(b, 8); b += a;
            a -= c; a ^= Rotate(c, 16); c += b;
            b -= a; b ^= Rotate(a, 19); a += c;
            c -= b; c ^= Rotate(b, 4); b += a;
        }
    }

    private static void Final(
        ref uint a,
        ref uint b,
        ref uint c)
    {
        unchecked
        {
            c ^= b; c -= Rotate(b, 14);
            a ^= c; a -= Rotate(c, 11);
            b ^= a; b -= Rotate(a, 25);
            c ^= b; c -= Rotate(b, 16);
            a ^= c; a -= Rotate(c, 4);
            b ^= a; b -= Rotate(a, 14);
            c ^= b; c -= Rotate(b, 24);
        }
    }
}
