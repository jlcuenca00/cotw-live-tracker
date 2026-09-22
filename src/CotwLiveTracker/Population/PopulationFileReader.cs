using System.Buffers.Binary;
using System.IO.Compression;
using CotwLiveTracker.Memory;

namespace CotwLiveTracker.Population;

internal sealed record PopulationRecordCandidate(
    int Offset,
    byte GenderValue,
    float Weight,
    float Score,
    bool IsGreatOne,
    bool IsScripted,
    uint VisualVariationSeed,
    uint Id,
    float MapX,
    float MapY)
{
    public string Gender => GenderValue == 1 ? "male" : "female";

    public float HorizontalDistanceTo(Float3 position)
    {
        var dx = MapX - position.X;
        var dz = MapY - position.Z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }
}

internal sealed record PopulationReadResult(
    string FilePath,
    int FileSizeBytes,
    int AdfPayloadSizeBytes,
    IReadOnlyList<PopulationRecordCandidate> Records);

internal static class PopulationFileReader
{
    private const int FileHeaderLength = 32;
    private const int CompressionHeaderLength = 5;
    private const int AnimalRecordLength = 32;

    public static PopulationReadResult Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Population file path is required.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var fileBytes = File.ReadAllBytes(fullPath);
        var payload = ExtractAdfPayload(fileBytes);
        var records = EnumerateCandidateRecords(payload);

        return new PopulationReadResult(fullPath, fileBytes.Length, payload.Length, records);
    }

    internal static byte[] ExtractAdfPayload(byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);

        if (fileBytes.Length <= FileHeaderLength)
        {
            throw new InvalidDataException("Population file is too small to contain the COTW compressed ADF payload.");
        }

        using var compressed = new MemoryStream(
            fileBytes,
            FileHeaderLength,
            fileBytes.Length - FileHeaderLength,
            writable: false);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);

        var decompressed = output.ToArray();
        if (decompressed.Length <= CompressionHeaderLength)
        {
            throw new InvalidDataException("Decompressed population data is missing the ADF payload.");
        }

        return decompressed[CompressionHeaderLength..];
    }

    internal static IReadOnlyList<PopulationRecordCandidate> EnumerateCandidateRecords(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var records = new List<PopulationRecordCandidate>();
        for (var offset = 0; offset + AnimalRecordLength <= payload.Length; offset += sizeof(int))
        {
            if (TryReadRecord(payload, offset, out var record))
            {
                records.Add(record!);
            }
        }

        return records;
    }

    private static bool TryReadRecord(byte[] payload, int offset, out PopulationRecordCandidate? record)
    {
        record = null;
        var data = payload.AsSpan(offset, AnimalRecordLength);

        var gender = data[0];
        if (gender is not (1 or 2) || data[1] != 0 || data[2] != 0 || data[3] != 0)
        {
            return false;
        }

        var weight = ReadSingle(data, 4);
        var score = ReadSingle(data, 8);
        var greatOne = data[12];
        var scripted = data[13];

        if (greatOne > 1 ||
            scripted > 1 ||
            data[14] != 0 ||
            data[15] != 0 ||
            !float.IsFinite(weight) ||
            !float.IsFinite(score) ||
            weight <= 0.001f ||
            weight > 5_000f ||
            score < 0f ||
            score > 10_000f)
        {
            return false;
        }

        var seed = BinaryPrimitives.ReadUInt32LittleEndian(data[16..20]);
        var id = BinaryPrimitives.ReadUInt32LittleEndian(data[20..24]);
        var mapX = ReadSingle(data, 24);
        var mapY = ReadSingle(data, 28);

        if (!float.IsFinite(mapX) ||
            !float.IsFinite(mapY) ||
            MathF.Abs(mapX) > 100_000f ||
            MathF.Abs(mapY) > 100_000f)
        {
            return false;
        }

        record = new PopulationRecordCandidate(
            offset,
            gender,
            weight,
            score,
            greatOne == 1,
            scripted == 1,
            seed,
            id,
            mapX,
            mapY);
        return true;
    }

    private static float ReadSingle(ReadOnlySpan<byte> buffer, int offset)
    {
        var bits = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset, sizeof(int)));
        return BitConverter.Int32BitsToSingle(bits);
    }
}
