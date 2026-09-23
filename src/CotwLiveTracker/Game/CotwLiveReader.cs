using System.Buffers.Binary;
using System.ComponentModel;
using CotwLiveTracker.Configuration;
using CotwLiveTracker.Memory;

namespace CotwLiveTracker.Game;

internal sealed record AnimalSnapshot(
    string Species,
    Float3 Position,
    float Health,
    float MaxHealth,
    float DistanceMeters,
    float Weight,
    float Score,
    uint VisualVariationSeed,
    nint Address);

internal sealed record LiveSnapshot(
    Float3 CameraPosition,
    IReadOnlyList<AnimalSnapshot> Animals,
    int SkippedAnimals);

internal sealed record ProbeResult(bool IsValid, IReadOnlyList<string> Messages);

internal sealed class CotwLiveReader
{
    private const int SpeciesPathReadLength = 160;
    private const int AnimalLiveBlockLength = 0x38;

    private readonly IMemoryReader _memory;
    private readonly nint _moduleBase;
    private readonly CotwOffsets _offsets;
    private readonly Dictionary<nint, string> _speciesCache = [];

    private nint _lastManager;

    public CotwLiveReader(IMemoryReader memory, nint moduleBase, CotwOffsets offsets)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _moduleBase = moduleBase != nint.Zero
            ? moduleBase
            : throw new ArgumentOutOfRangeException(nameof(moduleBase));
        _offsets = offsets ?? throw new ArgumentNullException(nameof(offsets));
    }

    public ProbeResult Probe()
    {
        var messages = new List<string>();

        Float3 camera;
        try
        {
            camera = ReadCameraPosition();
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            return new ProbeResult(false, [$"Camera position read failed: {ex.Message}"]);
        }

        if (!camera.IsFinite)
        {
            return new ProbeResult(false, ["Camera position contains non-finite values."]);
        }

        messages.Add($"Camera position is finite: {camera.X:F1}, {camera.Y:F1}, {camera.Z:F1}");

        AnimalVector vector;
        try
        {
            vector = ReadAnimalVector();
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            return new ProbeResult(false, [.. messages, $"Animal manager/vector validation failed: {ex.Message}"]);
        }

        messages.Add($"Animal manager: 0x{vector.Manager.ToInt64():X}");
        messages.Add($"Spawned animal vector count: {vector.Pointers.Length}");

        if (vector.Pointers.Length == 0)
        {
            messages.Add("No spawned animals are currently loaded; structure checks passed.");
            return new ProbeResult(true, messages);
        }

        var validSamples = 0;
        foreach (var pointer in vector.Pointers.Take(16))
        {
            if (TryReadAnimal(pointer, camera, out _))
            {
                validSamples++;
            }
        }

        messages.Add($"Valid animal samples: {validSamples}/{Math.Min(16, vector.Pointers.Length)}");
        if (validSamples == 0)
        {
            messages.Add("Animal entries could not be decoded. The profile is likely stale.");
            return new ProbeResult(false, messages);
        }

        return new ProbeResult(true, messages);
    }

    public LiveSnapshot ReadSnapshot()
    {
        var camera = ReadCameraPosition();
        if (!camera.IsFinite)
        {
            throw new InvalidOperationException("Camera position contains non-finite values.");
        }

        var vector = ReadAnimalVector();
        var animals = new List<AnimalSnapshot>(vector.Pointers.Length);
        var skipped = 0;

        foreach (var pointer in vector.Pointers)
        {
            if (TryReadAnimal(pointer, camera, out var animal))
            {
                animals.Add(animal!);
            }
            else
            {
                skipped++;
            }
        }

        return new LiveSnapshot(camera, animals, skipped);
    }

    private Float3 ReadCameraPosition() =>
        _memory.ReadFloat3(_moduleBase + _offsets.CameraPosition);

    private AnimalVector ReadAnimalVector()
    {
        var managerRoot = _memory.ReadPointer(_moduleBase + _offsets.AnimalManager);
        EnsurePointer(managerRoot, "animal manager root");

        var manager = _memory.ReadPointer(managerRoot + _offsets.AnimalManagerDereference);
        EnsurePointer(manager, "animal manager");

        if (manager != _lastManager)
        {
            _lastManager = manager;
            _speciesCache.Clear();
        }

        var vectorAddress = manager + _offsets.AnimalVector;
        var begin = _memory.ReadPointer(vectorAddress);
        var end = _memory.ReadPointer(vectorAddress + sizeof(long));

        if (begin == nint.Zero && end == nint.Zero)
        {
            return new AnimalVector(manager, []);
        }

        EnsurePointer(begin, "animal vector begin");
        EnsurePointer(end, "animal vector end");

        var beginValue = begin.ToInt64();
        var endValue = end.ToInt64();
        if (endValue < beginValue)
        {
            throw new InvalidOperationException("Animal vector end precedes begin.");
        }

        var byteLength = endValue - beginValue;
        if (byteLength % sizeof(long) != 0)
        {
            throw new InvalidOperationException("Animal vector length is not pointer-aligned.");
        }

        var count = byteLength / sizeof(long);
        if (count < 0 || count > _offsets.MaxAnimals)
        {
            throw new InvalidOperationException(
                $"Animal vector count {count} is outside the safe range 0-{_offsets.MaxAnimals}.");
        }

        var pointers = _memory
            .ReadPointers(begin, (int)count)
            .Where(pointer => pointer != nint.Zero && IsPlausiblePointer(pointer))
            .ToArray();

        return new AnimalVector(manager, pointers);
    }

    private bool TryReadAnimal(nint animalPointer, Float3 camera, out AnimalSnapshot? animal)
    {
        animal = null;

        if (!IsPlausiblePointer(animalPointer))
        {
            return false;
        }

        try
        {
            var speciesDefinition = _memory.ReadPointer(animalPointer + _offsets.AnimalSpeciesDefinition);
            EnsurePointer(speciesDefinition, "animal species definition");

            var species = ResolveSpecies(speciesDefinition);
            if (species == "?")
            {
                return false;
            }

            var liveBlock = _memory.ReadBytes(animalPointer + _offsets.AnimalPosition, AnimalLiveBlockLength);
            var position = ReadFloat3(liveBlock, 0);
            if (!position.IsFinite)
            {
                return false;
            }

            var distance = position.DistanceTo(camera);
            if (!float.IsFinite(distance) || distance > _offsets.MaxDistanceMeters)
            {
                return false;
            }

            var maxHealthOffset = checked((int)(_offsets.AnimalHealthMax - _offsets.AnimalPosition));
            var healthOffset = checked((int)(_offsets.AnimalHealthCurrent - _offsets.AnimalPosition));

            if (maxHealthOffset < 0 ||
                healthOffset < 0 ||
                maxHealthOffset + sizeof(float) > liveBlock.Length ||
                healthOffset + sizeof(float) > liveBlock.Length)
            {
                throw new InvalidOperationException("Health offsets fall outside the configured live animal block.");
            }

            var maxHealth = ReadSingle(liveBlock, maxHealthOffset);
            var health = ReadSingle(liveBlock, healthOffset);
            if (!float.IsFinite(maxHealth) || !float.IsFinite(health) || maxHealth < 0 || health < 0)
            {
                return false;
            }

            var weight = ReadSingle(
                _memory.ReadBytes(animalPointer + _offsets.AnimalWeight, sizeof(float)),
                0);
            var score = ReadSingle(
                _memory.ReadBytes(animalPointer + _offsets.AnimalScore, sizeof(float)),
                0);
            var seedBytes = _memory.ReadBytes(
                animalPointer + _offsets.AnimalVisualVariationSeed,
                sizeof(uint));
            var seed = BinaryPrimitives.ReadUInt32LittleEndian(seedBytes);

            if (!float.IsFinite(weight) || weight < 0f ||
                !float.IsFinite(score) || score < 0f)
            {
                return false;
            }

            animal = new AnimalSnapshot(
                species,
                position,
                health,
                maxHealth,
                distance,
                weight,
                score,
                seed,
                animalPointer);
            return true;
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            return false;
        }
    }

    private string ResolveSpecies(nint speciesDefinition)
    {
        if (_speciesCache.TryGetValue(speciesDefinition, out var cached))
        {
            return cached;
        }

        var ragdollPathPointer = _memory.ReadPointer(speciesDefinition + _offsets.SpeciesRagdollPath);
        EnsurePointer(ragdollPathPointer, "species ragdoll path");

        var path = _memory.ReadNullTerminatedUtf8(ragdollPathPointer, SpeciesPathReadLength);
        var species = ParseSpeciesName(path);
        _speciesCache[speciesDefinition] = species;
        return species;
    }

    internal static string ParseSpeciesName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "?";
        }

        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        foreach (var marker in new[] { "/animals/", "/birds/" })
        {
            var markerIndex = normalized.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                continue;
            }

            var start = markerIndex + marker.Length;
            var end = normalized.IndexOf('/', start);
            if (end <= start)
            {
                continue;
            }

            return normalized[start..end].Replace('_', ' ');
        }

        return "?";
    }

    private static Float3 ReadFloat3(byte[] buffer, int offset) =>
        new(
            ReadSingle(buffer, offset),
            ReadSingle(buffer, offset + 4),
            ReadSingle(buffer, offset + 8));

    private static float ReadSingle(byte[] buffer, int offset)
    {
        var bits = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, sizeof(int)));
        return BitConverter.Int32BitsToSingle(bits);
    }

    private static void EnsurePointer(nint pointer, string name)
    {
        if (!IsPlausiblePointer(pointer))
        {
            throw new InvalidOperationException($"{name} pointer 0x{pointer.ToInt64():X} is not plausible.");
        }
    }

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

    private sealed record AnimalVector(nint Manager, nint[] Pointers);
}
