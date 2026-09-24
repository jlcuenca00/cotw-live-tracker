using System.Buffers.Binary;
using System.ComponentModel;
using CotwLiveTracker.Memory;

namespace CotwLiveTracker.Game;

internal sealed record DifficultyMemoryCandidate(
    string Path,
    int Offset,
    string Encoding,
    int Value)
{
    public string Display =>
        $"{Path}+0x{Offset:X3} {Encoding}={Value}";
}

internal sealed record DifficultyMemoryProbeResult(
    nint AnimalAddress,
    IReadOnlyList<DifficultyMemoryCandidate> Candidates,
    int PointerReads,
    int? PreferredCandidateLevel)
{
    public string FormatReport(
        string species,
        float weight,
        float score,
        string estimatedDifficulty)
    {
        var lines = new List<string>
        {
            "COTW native difficulty probe",
            $"Species: {species}",
            $"Entity: 0x{AnimalAddress.ToInt64():X}",
            $"Weight: {weight:F4}",
            $"Score: {score:F4}",
            $"Current weight estimate: {estimatedDifficulty}",
            PreferredCandidateLevel is int preferred
                ? $"Experimental native candidate: {FormatDifficulty(preferred)}"
                : "Experimental native candidate: unavailable",
            "Candidate path: *(entity+0x018)+0x008 int32",
            "Status: strong candidate only; validate on a different in-game level before treating it as authoritative.",
            $"Immediate pointer windows read: {PointerReads}",
            "",
            "Candidate fields whose current value is an integer 1-10:",
        };

        if (Candidates.Count == 0)
        {
            lines.Add("(none)");
        }
        else
        {
            lines.AddRange(Candidates.Select(candidate => candidate.Display));
        }

        lines.Add("");
        lines.Add(
            "This probe is read-only. Compare the same animal's in-game level " +
            "against these candidates; a second animal with a different level " +
            "lets us isolate the native field.");

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatDifficulty(int level) =>
        level switch
        {
            1 => "1-Trivial",
            2 => "2-Minor",
            3 => "3-Very Easy",
            4 => "4-Easy",
            5 => "5-Medium",
            6 => "6-Hard",
            7 => "7-Very Hard",
            8 => "8-Mythical",
            9 => "9-Legendary",
            10 => "10-Fabled",
            _ => level.ToString()
        };
}

internal static class AnimalDifficultyMemoryProbe
{
    private const int EntityScanLength = 0x300;
    private const int ChildScanLength = 0x80;
    private const int MaxPointerReads = 32;
    private const int MaxCandidates = 96;
    private const int PreferredPointerOffset = 0x18;
    private const int PreferredLevelOffset = 0x08;

    public static DifficultyMemoryProbeResult Probe(
        IMemoryReader memory,
        nint animalAddress)
    {
        ArgumentNullException.ThrowIfNull(memory);

        var candidates = new List<DifficultyMemoryCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var preferredCandidateLevel =
            TryReadPreferredCandidate(
                memory,
                animalAddress);

        var entityBytes = memory.ReadBytes(
            animalAddress,
            EntityScanLength);
        ScanAligned(
            entityBytes,
            "entity",
            candidates,
            seen,
            includeBytes: true);

        var pointerReads = 0;
        var uniquePointers = new HashSet<nint>();

        for (var offset = 0;
             offset <= entityBytes.Length - sizeof(long) &&
             pointerReads < MaxPointerReads &&
             candidates.Count < MaxCandidates;
             offset += sizeof(long))
        {
            var pointer = (nint)BinaryPrimitives.ReadInt64LittleEndian(
                entityBytes.AsSpan(offset, sizeof(long)));

            if (!IsPlausiblePointer(pointer) ||
                !uniquePointers.Add(pointer))
            {
                continue;
            }

            byte[] child;
            try
            {
                child = memory.ReadBytes(
                    pointer,
                    ChildScanLength);
            }
            catch (Exception ex) when (IsExpectedReadFailure(ex))
            {
                continue;
            }

            pointerReads++;
            ScanAligned(
                child,
                $"entity+0x{offset:X3}->0x{pointer.ToInt64():X}",
                candidates,
                seen,
                includeBytes: false);
        }

        var ordered = candidates
            .OrderBy(candidate =>
                IsPreferredCandidate(candidate)
                    ? 0
                    : candidate.Path == "entity"
                        ? 1
                        : 2)
            .ThenBy(candidate =>
                candidate.Path == "entity"
                    ? Math.Abs(candidate.Offset - 0x1A0)
                    : candidate.Offset)
            .ThenBy(candidate => EncodingRank(candidate.Encoding))
            .ThenBy(candidate => candidate.Offset)
            .Take(MaxCandidates)
            .ToArray();

        return new DifficultyMemoryProbeResult(
            animalAddress,
            ordered,
            pointerReads,
            preferredCandidateLevel);
    }

    public static int? TryReadPreferredCandidate(
        IMemoryReader memory,
        nint animalAddress)
    {
        ArgumentNullException.ThrowIfNull(memory);

        try
        {
            var pointerBytes = memory.ReadBytes(
                animalAddress + PreferredPointerOffset,
                sizeof(long));
            var child = (nint)BinaryPrimitives.ReadInt64LittleEndian(
                pointerBytes);

            if (!IsPlausiblePointer(child))
            {
                return null;
            }

            var levelBytes = memory.ReadBytes(
                child + PreferredLevelOffset,
                sizeof(int));
            var level = BinaryPrimitives.ReadInt32LittleEndian(
                levelBytes);

            return level is >= 1 and <= 10
                ? level
                : null;
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            return null;
        }
    }

    private static void ScanAligned(
        byte[] block,
        string path,
        List<DifficultyMemoryCandidate> candidates,
        HashSet<string> seen,
        bool includeBytes)
    {
        for (var offset = 0;
             offset <= block.Length - sizeof(int) &&
             candidates.Count < MaxCandidates;
             offset += sizeof(int))
        {
            var bits = BinaryPrimitives.ReadInt32LittleEndian(
                block.AsSpan(offset, sizeof(int)));

            if (bits is >= 1 and <= 10)
            {
                Add(
                    path,
                    offset,
                    "int32",
                    bits,
                    candidates,
                    seen);
            }

            var value = BitConverter.Int32BitsToSingle(bits);
            if (float.IsFinite(value))
            {
                var rounded = (int)MathF.Round(value);
                if (rounded is >= 1 and <= 10 &&
                    MathF.Abs(value - rounded) <= 0.0001f)
                {
                    Add(
                        path,
                        offset,
                        "float32",
                        rounded,
                        candidates,
                        seen);
                }
            }
        }

        if (!includeBytes)
        {
            return;
        }

        for (var offset = 0;
             offset < block.Length &&
             candidates.Count < MaxCandidates;
             offset++)
        {
            var value = block[offset];
            if (value is < 1 or > 10)
            {
                continue;
            }

            Add(
                path,
                offset,
                "byte",
                value,
                candidates,
                seen);
        }
    }

    private static void Add(
        string path,
        int offset,
        string encoding,
        int value,
        List<DifficultyMemoryCandidate> candidates,
        HashSet<string> seen)
    {
        var key = $"{path}|{offset:X}|{encoding}|{value}";
        if (!seen.Add(key))
        {
            return;
        }

        candidates.Add(
            new DifficultyMemoryCandidate(
                path,
                offset,
                encoding,
                value));
    }

    private static bool IsPreferredCandidate(
        DifficultyMemoryCandidate candidate) =>
        candidate.Path.StartsWith(
            "entity+0x018->",
            StringComparison.Ordinal) &&
        candidate.Offset == PreferredLevelOffset &&
        candidate.Encoding == "int32";

    private static int EncodingRank(string encoding) =>
        encoding switch
        {
            "int32" => 0,
            "float32" => 1,
            "byte" => 2,
            _ => 3
        };

    private static bool IsPlausiblePointer(nint pointer)
    {
        var value = pointer.ToInt64();
        return value >= 0x10000 &&
               value < 0x0000800000000000;
    }

    private static bool IsExpectedReadFailure(Exception ex) =>
        ex is Win32Exception
            or InvalidOperationException
            or ArgumentOutOfRangeException
            or OverflowException;
}
