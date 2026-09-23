using System.Buffers.Binary;
using System.ComponentModel;
using CotwLiveTracker.Game;
using CotwLiveTracker.Memory;

namespace CotwLiveTracker.Population;

internal sealed record PopulationBridgeMatch(
    string Evidence,
    string Path,
    PopulationAnimalRecord Animal);

internal sealed record PopulationBridgeProbeResult(
    AnimalSnapshot LiveAnimal,
    string? PopulationSpecies,
    IReadOnlyList<PopulationBridgeMatch> Matches,
    int PointerReads)
{
    public PopulationAnimalRecord? ResolvedAnimal
    {
        get
        {
            var candidates = Matches
                .GroupBy(match => (
                    match.Animal.SpeciesIndex,
                    match.Animal.GroupIndex,
                    match.Animal.AnimalIndex))
                .Where(group =>
                    group.Any(match => match.Evidence == "stable-record") ||
                    group.Select(match => match.Evidence).Distinct().Count() >= 2)
                .Select(group => group.First().Animal)
                .Take(2)
                .ToArray();

            return candidates.Length == 1 ? candidates[0] : null;
        }
    }
}

internal static class PopulationIdentityBridgeProbe
{
    private const int StableRecordLength = 20;
    private const int EntityScanLength = 0x300;
    private const int ChildScanLength = 0x100;
    private const int MaxPointerReads = 64;

    public static PopulationBridgeProbeResult Probe(
        IMemoryReader memory,
        AnimalSnapshot liveAnimal,
        PopulationReadResult population)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(liveAnimal);
        ArgumentNullException.ThrowIfNull(population);

        var species = MatchSpecies(liveAnimal.Species, population.Species);
        if (species is null)
        {
            return new PopulationBridgeProbeResult(
                liveAnimal,
                null,
                [],
                0);
        }

        var index = CandidateIndex.Create(species.Animals);
        var matches = new List<PopulationBridgeMatch>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        byte[] entityBytes;
        try
        {
            entityBytes = memory.ReadBytes(liveAnimal.Address, EntityScanLength);
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            return new PopulationBridgeProbeResult(
                liveAnimal,
                species.Species,
                [],
                0);
        }

        ScanBlock(
            entityBytes,
            "entity",
            index,
            matches,
            seen);

        var pointers = new List<(int Offset, nint Address)>();
        var uniquePointers = new HashSet<nint>();
        for (var offset = 0; offset <= entityBytes.Length - sizeof(long); offset += sizeof(long))
        {
            var value = BinaryPrimitives.ReadInt64LittleEndian(
                entityBytes.AsSpan(offset, sizeof(long)));
            var pointer = (nint)value;
            if (!IsPlausiblePointer(pointer) || !uniquePointers.Add(pointer))
            {
                continue;
            }

            pointers.Add((offset, pointer));
            if (pointers.Count >= MaxPointerReads)
            {
                break;
            }
        }

        var pointerReads = 0;
        foreach (var (entityOffset, pointer) in pointers)
        {
            var child = TryReadWindow(memory, pointer);
            if (child is null)
            {
                continue;
            }

            pointerReads++;
            var path = $"entity+0x{entityOffset:X} -> 0x{pointer.ToInt64():X}";
            ScanBlock(
                child,
                path,
                index,
                matches,
                seen);
        }

        return new PopulationBridgeProbeResult(
            liveAnimal,
            species.Species,
            matches,
            pointerReads);
    }

    internal static byte[] BuildStableSignature(PopulationAnimalRecord animal)
    {
        ArgumentNullException.ThrowIfNull(animal);

        var bytes = new byte[StableRecordLength];
        bytes[0] = animal.Gender switch
        {
            "male" => 1,
            "female" => 2,
            _ => 0
        };

        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(4, sizeof(int)),
            BitConverter.SingleToInt32Bits(animal.Weight));
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(8, sizeof(int)),
            BitConverter.SingleToInt32Bits(animal.Score));
        bytes[12] = animal.IsGreatOne ? (byte)1 : (byte)0;
        bytes[13] = animal.IsScripted ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(16, sizeof(uint)),
            animal.VisualVariationSeed);

        return bytes;
    }

    private static void ScanBlock(
        byte[] block,
        string path,
        CandidateIndex index,
        List<PopulationBridgeMatch> matches,
        HashSet<string> seen)
    {
        for (var offset = 0; offset <= block.Length - StableRecordLength; offset++)
        {
            var signature = Convert.ToHexString(
                block.AsSpan(offset, StableRecordLength));

            if (!index.StableSignatures.TryGetValue(signature, out var candidates))
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                AddMatch(
                    "stable-record",
                    $"{path}+0x{offset:X}",
                    candidate,
                    matches,
                    seen);
            }
        }

        for (var offset = 0; offset <= block.Length - sizeof(uint); offset += sizeof(uint))
        {
            var seed = BinaryPrimitives.ReadUInt32LittleEndian(
                block.AsSpan(offset, sizeof(uint)));

            if (index.UniqueSeeds.TryGetValue(seed, out var candidate))
            {
                AddMatch(
                    "unique-seed",
                    $"{path}+0x{offset:X}",
                    candidate,
                    matches,
                    seen);
            }
        }

        for (var offset = 0; offset <= block.Length - (sizeof(float) * 2); offset += sizeof(float))
        {
            var weightBits = BinaryPrimitives.ReadInt32LittleEndian(
                block.AsSpan(offset, sizeof(float)));
            var scoreBits = BinaryPrimitives.ReadInt32LittleEndian(
                block.AsSpan(offset + sizeof(float), sizeof(float)));

            if (index.UniqueWeightScores.TryGetValue(
                    (weightBits, scoreBits),
                    out var candidate))
            {
                AddMatch(
                    "unique-weight+score",
                    $"{path}+0x{offset:X}",
                    candidate,
                    matches,
                    seen);
            }
        }
    }

    private static void AddMatch(
        string evidence,
        string path,
        PopulationAnimalRecord animal,
        List<PopulationBridgeMatch> matches,
        HashSet<string> seen)
    {
        var key =
            $"{evidence}|{path}|{animal.SpeciesIndex}|{animal.GroupIndex}|{animal.AnimalIndex}";
        if (!seen.Add(key))
        {
            return;
        }

        matches.Add(new PopulationBridgeMatch(
            evidence,
            path,
            animal));
    }

    private static byte[]? TryReadWindow(IMemoryReader memory, nint pointer)
    {
        foreach (var length in new[] { ChildScanLength, 0x80, 0x40, StableRecordLength })
        {
            try
            {
                return memory.ReadBytes(pointer, length);
            }
            catch (Exception ex) when (IsExpectedReadFailure(ex))
            {
                // Try a shorter read. Animal objects often point at small leaf structures.
            }
        }

        return null;
    }

    private static PopulationSpeciesRecord? MatchSpecies(
        string liveSpecies,
        IReadOnlyList<PopulationSpeciesRecord> populationSpecies)
    {
        var live = NormalizeSpecies(liveSpecies);

        var exact = populationSpecies
            .Where(species => NormalizeSpecies(species.Species) == live)
            .ToArray();
        if (exact.Length == 1)
        {
            return exact[0];
        }

        var prefix = populationSpecies
            .Where(species =>
            {
                var candidate = NormalizeSpecies(species.Species);
                return candidate.StartsWith(live, StringComparison.Ordinal) ||
                       live.StartsWith(candidate, StringComparison.Ordinal);
            })
            .ToArray();

        return prefix.Length == 1 ? prefix[0] : null;
    }

    private static string NormalizeSpecies(string value) =>
        new(
            value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

    private static bool IsPlausiblePointer(nint pointer)
    {
        var value = pointer.ToInt64();
        return value >= 0x10000 && value < 0x0000800000000000;
    }

    private static bool IsExpectedReadFailure(Exception ex) =>
        ex is Win32Exception
            or InvalidOperationException
            or ArgumentOutOfRangeException
            or OverflowException;

    private sealed record CandidateIndex(
        IReadOnlyDictionary<string, IReadOnlyList<PopulationAnimalRecord>> StableSignatures,
        IReadOnlyDictionary<uint, PopulationAnimalRecord> UniqueSeeds,
        IReadOnlyDictionary<(int WeightBits, int ScoreBits), PopulationAnimalRecord> UniqueWeightScores)
    {
        public static CandidateIndex Create(
            IReadOnlyList<PopulationAnimalRecord> animals)
        {
            var stable = animals
                .GroupBy(animal => Convert.ToHexString(BuildStableSignature(animal)))
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<PopulationAnimalRecord>)group.ToArray(),
                    StringComparer.Ordinal);

            var seeds = animals
                .GroupBy(animal => animal.VisualVariationSeed)
                .Where(group => group.Count() == 1)
                .ToDictionary(
                    group => group.Key,
                    group => group.Single());

            var weightScores = animals
                .GroupBy(animal => (
                    BitConverter.SingleToInt32Bits(animal.Weight),
                    BitConverter.SingleToInt32Bits(animal.Score)))
                .Where(group => group.Count() == 1)
                .ToDictionary(
                    group => group.Key,
                    group => group.Single());

            return new CandidateIndex(
                stable,
                seeds,
                weightScores);
        }
    }
}
