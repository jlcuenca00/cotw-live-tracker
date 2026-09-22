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

            TestPopulationRecordRecognition();

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
        var payload = Enumerable.Repeat((byte)0xA5, 96).ToArray();
        const int offset = 32;

        payload[offset] = 1;
        payload[offset + 1] = 0;
        payload[offset + 2] = 0;
        payload[offset + 3] = 0;
        WriteSingle(payload, offset + 4, 72.5f);
        WriteSingle(payload, offset + 8, 248.25f);
        payload[offset + 12] = 0;
        payload[offset + 13] = 0;
        payload[offset + 14] = 0;
        payload[offset + 15] = 0;
        WriteUInt32(payload, offset + 16, 123456789u);
        WriteUInt32(payload, offset + 20, 98765u);
        WriteSingle(payload, offset + 24, 6800f);
        WriteSingle(payload, offset + 28, 5200f);

        var records = PopulationFileReader.EnumerateCandidateRecords(payload);
        if (records.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one population record candidate, got {records.Count}.");
        }

        var record = records[0];
        if (record.Gender != "male" ||
            record.Weight != 72.5f ||
            record.Score != 248.25f ||
            record.VisualVariationSeed != 123456789u ||
            record.Id != 98765u)
        {
            throw new InvalidOperationException("Population record candidate decoded incorrectly.");
        }

        var mapDelta = record.HorizontalDistanceTo(new Float3(6803f, 0f, 5204f));
        if (MathF.Abs(mapDelta - 5f) > 0.001f)
        {
            throw new InvalidOperationException($"Unexpected population map delta {mapDelta}.");
        }
    }

    private static nint Ptr(long value) => (nint)value;

    private static byte[] PointerBytes(nint value)
    {
        var bytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value.ToInt64());
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

    private static void WriteUInt32(byte[] buffer, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, sizeof(uint)), value);

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
