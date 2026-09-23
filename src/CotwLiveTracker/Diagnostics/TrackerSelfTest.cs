using System.Buffers.Binary;
using System.Text;
using CotwLiveTracker.Configuration;
using CotwLiveTracker.Game;
using CotwLiveTracker.Memory;
using CotwLiveTracker.Population;

namespace CotwLiveTracker.Diagnostics;

internal static class TrackerSelfTest
{
    public static int Run()
    {
        try
        {
            var memory = new SparseMemoryReader();
            var moduleBase = Ptr(0x0000000140000000L);
            var root = Ptr(0x0000010000001000L);
            var manager = Ptr(0x0000010000002000L);
            var vector = Ptr(0x0000010000003000L);
            var animal = Ptr(0x0000010000004000L);
            var speciesDefinition = Ptr(0x0000010000005000L);
            var ragdollPath = Ptr(0x0000010000006000L);

            var offsets = new CotwOffsets(
                ViewProjection: Ptr(0x100),
                CameraPosition: Ptr(0x200),
                AnimalManager: Ptr(0x300),
                AnimalManagerDereference: Ptr(0x8),
                AnimalVector: Ptr(0x2C8),
                AnimalSpeciesDefinition: Ptr(0x28),
                AnimalPosition: Ptr(0x148),
                AnimalHealthMax: Ptr(0x178),
                AnimalHealthCurrent: Ptr(0x17C),
                AnimalWeight: Ptr(0x19C),
                AnimalScore: Ptr(0x1A0),
                AnimalVisualVariationSeed: Ptr(0x1B0),
                SpeciesRagdollPath: Ptr(0xC0),
                MaxAnimals: 512,
                MaxDistanceMeters: 1000f);

            memory.Add(moduleBase + offsets.CameraPosition, Float3Bytes(0f, 0f, 0f));
            memory.Add(moduleBase + offsets.AnimalManager, PointerBytes(root));
            memory.Add(root + offsets.AnimalManagerDereference, PointerBytes(manager));
            memory.Add(
                manager + offsets.AnimalVector,
                Concat(PointerBytes(vector), PointerBytes(vector + sizeof(long))));
            memory.Add(vector, PointerBytes(animal));
            memory.Add(animal + offsets.AnimalSpeciesDefinition, PointerBytes(speciesDefinition));
            memory.Add(speciesDefinition + offsets.SpeciesRagdollPath, PointerBytes(ragdollPath));
            memory.Add(
                ragdollPath,
                Encoding.UTF8.GetBytes(
                    "animations/hp_ragdoll/animals/black_bear/black_bear_internal_ragdoll_100.ragdoll\0"));

            var liveBlock = new byte[0x38];
            WriteFloat3(liveBlock, 0, 1f, 2f, 3f);
            WriteSingle(liveBlock, 0x30, 100f);
            WriteSingle(liveBlock, 0x34, 40f);
            memory.Add(animal + offsets.AnimalPosition, liveBlock);
            memory.Add(animal + offsets.AnimalWeight, SingleBytes(72.5f));
            memory.Add(animal + offsets.AnimalScore, SingleBytes(248.25f));
            memory.Add(animal + offsets.AnimalVisualVariationSeed, UInt32Bytes(123456789u));

            var tracker = new CotwLiveReader(memory, moduleBase, offsets);
            var probe = tracker.Probe();
            if (!probe.IsValid)
            {
                throw new InvalidOperationException(
                    $"Probe failed: {string.Join("; ", probe.Messages)}");
            }

            var snapshot = tracker.ReadSnapshot();
            if (snapshot.Animals.Count != 1)
            {
                throw new InvalidOperationException($"Expected one animal, got {snapshot.Animals.Count}.");
            }

            var result = snapshot.Animals[0];
            if (result.Species != "black bear")
            {
                throw new InvalidOperationException($"Expected black bear, got '{result.Species}'.");
            }

            if (MathF.Abs(result.DistanceMeters - MathF.Sqrt(14f)) > 0.001f)
            {
                throw new InvalidOperationException($"Unexpected distance {result.DistanceMeters}.");
            }

            if (result.Health != 40f || result.MaxHealth != 100f)
            {
                throw new InvalidOperationException(
                    $"Unexpected health {result.Health}/{result.MaxHealth}.");
            }

            if (result.Weight != 72.5f ||
                result.Score != 248.25f ||
                result.VisualVariationSeed != 123456789u)
            {
                throw new InvalidOperationException(
                    "Live weight/score/seed metadata decoded incorrectly.");
            }

            TestPopulationRecordRecognition();
            TestPopulationIdentityBridge();

            Console.WriteLine("Self-test passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Self-test failed: {ex}");
            return 1;
        }
    }

    private static void TestPopulationRecordRecognition()
    {
        var animal = Struct(new Dictionary<string, AdfNode>
        {
            ["Gender"] = Scalar((uint)1),
            ["Weight"] = Scalar(72.5f),
            ["Score"] = Scalar(248.25f),
            ["IsGreatOne"] = Scalar((uint)0),
            ["IsScripted"] = Scalar((uint)0),
            ["VisualVariationSeed"] = Scalar(123456789u),
            ["Id"] = Scalar(98765u),
            ["MapPosition"] = Struct(new Dictionary<string, AdfNode>
            {
                ["X"] = Scalar(6800f),
                ["Y"] = Scalar(5200f)
            })
        });

        var group = Struct(new Dictionary<string, AdfNode>
        {
            ["Animals"] = Array([animal])
        });

        var speciesPopulation = Struct(new Dictionary<string, AdfNode>
        {
            ["Groups"] = Array([group])
        });

        var root = Struct(new Dictionary<string, AdfNode>
        {
            ["Populations"] = Array([speciesPopulation])
        });

        var document = new AdfDocument(
            Version: 3,
            Types: new Dictionary<uint, AdfTypeDefinition>(),
            Instances: [],
            RootValues: [root]);

        var reserve = new ReservePopulationDefinition(
            "synthetic",
            "Synthetic Reserve",
            ["whitetail_deer"]);

        var populations = PopulationFileReader.ParsePopulations(document, reserve);
        if (populations.Count != 1 || populations[0].Animals.Count != 1)
        {
            throw new InvalidOperationException("Structured population parser did not return one synthetic animal.");
        }

        var record = populations[0].Animals[0];
        if (record.Species != "whitetail_deer" ||
            record.Gender != "male" ||
            record.Weight != 72.5f ||
            record.Score != 248.25f ||
            record.DifficultyLevel != 1 ||
            record.DifficultyLabel != "1-Trivial" ||
            record.Trophy != "Gold" ||
            record.FurKey != "brown" ||
            record.FurName != "Brown" ||
            record.FurRarity != "Common" ||
            record.IsRareFur ||
            MathF.Abs(record.FurProbability - (25_000f / 75_226f)) > 0.000001f ||
            record.VisualVariationSeed != 123456789u ||
            record.Id != 98765u ||
            record.MapX != 6800f ||
            record.MapY != 5200f)
        {
            throw new InvalidOperationException("Structured population record decoded incorrectly.");
        }

        var exactFilter = new PopulationFilterOptions(
            Trophy: "gold",
            Difficulty: 1,
            Fur: "brown",
            Sex: "male");
        if (!exactFilter.Matches(record))
        {
            throw new InvalidOperationException("Expected population filter to match synthetic animal.");
        }

        if (new PopulationFilterOptions(Trophy: "diamond").Matches(record))
        {
            throw new InvalidOperationException("Diamond filter incorrectly matched a Gold animal.");
        }

        var rareRecord = record with
        {
            FurKey = "albino",
            FurName = "Albino",
            FurRarity = "Rare",
            FurProbability = 0.0005f,
            IsRareFur = true
        };
        if (!new PopulationFilterOptions(RareOnly: true, Fur: "albino").Matches(rareRecord))
        {
            throw new InvalidOperationException("Rare-fur filter did not match synthetic rare animal.");
        }

        var greatOneRecord = record with
        {
            DifficultyLevel = 10,
            DifficultyLabel = "10-Fabled",
            Trophy = "Great One",
            IsGreatOne = true
        };
        if (!new PopulationFilterOptions(
                Trophy: "great-one",
                GreatOneOnly: true,
                Difficulty: 10)
            .Matches(greatOneRecord))
        {
            throw new InvalidOperationException("Great One filter did not match synthetic Great One.");
        }

        var seedProbability = PopulationAnimalMetadataCatalog.SeedToProbability(123456789u);
        if (MathF.Abs(seedProbability - 0.404632568359375f) > 0.000001f)
        {
            throw new InvalidOperationException(
                $"Unexpected visual-seed probability {seedProbability}.");
        }
    }

    private static void TestPopulationIdentityBridge()
    {
        var animal = Struct(new Dictionary<string, AdfNode>
        {
            ["Gender"] = Scalar((uint)1),
            ["Weight"] = Scalar(93.59f),
            ["Score"] = Scalar(245.17f),
            ["IsGreatOne"] = Scalar((uint)0),
            ["IsScripted"] = Scalar((uint)0),
            ["VisualVariationSeed"] = Scalar(4278008639u),
            ["Id"] = Scalar(0u),
            ["MapPosition"] = Struct(new Dictionary<string, AdfNode>
            {
                ["X"] = Scalar(0f),
                ["Y"] = Scalar(0f)
            })
        });

        var group = Struct(new Dictionary<string, AdfNode>
        {
            ["Animals"] = Array([animal])
        });

        var speciesPopulation = Struct(new Dictionary<string, AdfNode>
        {
            ["Groups"] = Array([group])
        });

        var root = Struct(new Dictionary<string, AdfNode>
        {
            ["Populations"] = Array([speciesPopulation])
        });

        var document = new AdfDocument(
            Version: 4,
            Types: new Dictionary<uint, AdfTypeDefinition>(),
            Instances: [],
            RootValues: [root]);

        var reserve = new ReservePopulationDefinition(
            "synthetic",
            "Synthetic Reserve",
            ["whitetail_deer"]);

        var species = PopulationFileReader.ParsePopulations(document, reserve);
        var population = new PopulationReadResult(
            "synthetic",
            0,
            0,
            4,
            "Synthetic Reserve",
            species);
        var record = population.Animals.Single();

        var memory = new SparseMemoryReader();
        var entityAddress = Ptr(0x0000010000010000L);
        var recordAddress = Ptr(0x0000010000020000L);
        var entityBytes = new byte[0x300];
        BinaryPrimitives.WriteInt64LittleEndian(
            entityBytes.AsSpan(0x40, sizeof(long)),
            recordAddress.ToInt64());

        memory.Add(entityAddress, entityBytes);
        memory.Add(
            recordAddress,
            PopulationIdentityBridgeProbe.BuildStableSignature(record));

        var live = new AnimalSnapshot(
            "whitetail",
            new Float3(10f, 20f, 30f),
            100f,
            100f,
            100f,
            record.Weight,
            record.Score,
            record.VisualVariationSeed,
            entityAddress);

        var result = PopulationIdentityBridgeProbe.Probe(
            memory,
            live,
            population);

        if (result.ResolvedAnimal is null ||
            result.ResolvedAnimal.GroupIndex != 0 ||
            result.ResolvedAnimal.AnimalIndex != 0 ||
            !result.Matches.Any(match => match.Evidence == "stable-record"))
        {
            throw new InvalidOperationException(
                "Population identity bridge failed to resolve synthetic stable record.");
        }
    }

    private static AdfNode Struct(IReadOnlyDictionary<string, AdfNode> fields) =>
        new(0, 0, fields);

    private static AdfNode Array(IReadOnlyList<AdfNode> items) =>
        new(0, 0, items);

    private static AdfNode Scalar(object value) =>
        new(0, 0, value);

    private static nint Ptr(long value) => (nint)value;

    private static byte[] PointerBytes(nint value)
    {
        var bytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value.ToInt64());
        return bytes;
    }

    private static byte[] SingleBytes(float value)
    {
        var bytes = new byte[sizeof(float)];
        WriteSingle(bytes, 0, value);
        return bytes;
    }

    private static byte[] UInt32Bytes(uint value)
    {
        var bytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Float3Bytes(float x, float y, float z)
    {
        var bytes = new byte[12];
        WriteFloat3(bytes, 0, x, y, z);
        return bytes;
    }

    private static void WriteFloat3(byte[] buffer, int offset, float x, float y, float z)
    {
        WriteSingle(buffer, offset, x);
        WriteSingle(buffer, offset + 4, y);
        WriteSingle(buffer, offset + 8, z);
    }

    private static void WriteSingle(byte[] buffer, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(offset, sizeof(int)),
            BitConverter.SingleToInt32Bits(value));

    private static byte[] Concat(params byte[][] arrays)
    {
        var output = new byte[arrays.Sum(array => array.Length)];
        var offset = 0;

        foreach (var array in arrays)
        {
            Buffer.BlockCopy(array, 0, output, offset, array.Length);
            offset += array.Length;
        }

        return output;
    }
}

internal sealed class SparseMemoryReader : IMemoryReader
{
    private readonly Dictionary<nint, byte[]> _chunks = [];

    public void Add(nint address, byte[] bytes) => _chunks[address] = bytes;

    public byte[] ReadBytes(nint address, int length)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var output = new byte[length];
        var requestedStart = address.ToInt64();
        var requestedEnd = checked(requestedStart + length);

        foreach (var (chunkAddress, chunk) in _chunks)
        {
            var chunkStart = chunkAddress.ToInt64();
            var chunkEnd = checked(chunkStart + chunk.Length);
            var copyStart = Math.Max(requestedStart, chunkStart);
            var copyEnd = Math.Min(requestedEnd, chunkEnd);

            if (copyStart >= copyEnd)
            {
                continue;
            }

            Buffer.BlockCopy(
                chunk,
                checked((int)(copyStart - chunkStart)),
                output,
                checked((int)(copyStart - requestedStart)),
                checked((int)(copyEnd - copyStart)));
        }

        return output;
    }
}
