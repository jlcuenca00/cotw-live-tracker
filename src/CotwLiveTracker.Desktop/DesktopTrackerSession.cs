using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using CotwLiveTracker.Configuration;
using CotwLiveTracker.Game;
using CotwLiveTracker.Infrastructure;
using CotwLiveTracker.Memory;
using CotwLiveTracker.Population;

namespace CotwLiveTracker.Desktop;

internal sealed class DesktopTrackerSession : IDisposable
{
    private const string ProcessName = "theHunterCotW_F";
    private const string ProfileName = "patch-9.3-2026-09-16-community.json";

    private Process? _process;
    private ReadOnlyProcessMemory? _memory;
    private CotwLiveReader? _reader;

    public static IReadOnlyList<ReserveChoice> Reserves { get; } =
        PopulationReserveCatalog.All
            .Select(item => new ReserveChoice(item.Index, item.Reserve.DisplayName))
            .ToArray();

    public static IReadOnlyList<string> SpeciesForReserve(int reserveIndex) =>
        PopulationReserveCatalog.Get(reserveIndex)
            .Species
            .Select(species => species.Replace('_', ' '))
            .OrderBy(species => species, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool IsAttached =>
        _process is not null &&
        !_process.HasExited &&
        _memory is not null &&
        _reader is not null;

    public int? ProcessId => IsAttached ? _process!.Id : null;
    public OffsetProfile? Profile { get; private set; }
    public string ExecutableSha256 { get; private set; } = "";
    public string GameDirectory { get; private set; } = "";
    public PopulationReadResult? Population { get; private set; }
    public ReserveChoice? Reserve { get; private set; }
    public string PopulationPath => Population?.FilePath ?? "";

    public void Attach(int reserveIndex)
    {
        DisposeSession();

        var reserve = PopulationReserveCatalog.Get(reserveIndex);
        var populationPath = PopulationFileLocator.FindReservePopulation(reserveIndex);
        var profilePath = Path.Combine(
            AppContext.BaseDirectory,
            "offsets",
            ProfileName);

        var profile = OffsetProfileLoader.Load(profilePath);
        var process = GameProcessLocator.Find(ProcessName)
            ?? throw new InvalidOperationException(
                "Game process not found. Start theHunter: Call of the Wild and load into a reserve.");

        try
        {
            var module = process.MainModule
                ?? throw new InvalidOperationException("The game main module is unavailable.");

            var executableHash = ExecutableFingerprint.Sha256(module.FileName);
            var gameDirectory = Path.GetDirectoryName(module.FileName)
                ?? throw new InvalidOperationException(
                    "Could not determine the COTW installation directory.");
            if (!string.IsNullOrWhiteSpace(profile.ExecutableSha256) &&
                !string.Equals(
                    profile.ExecutableSha256,
                    executableHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The running game executable does not match the verified Patch 9.3 profile.");
            }

            var offsets = CotwOffsets.FromProfile(profile);
            var memory = ReadOnlyProcessMemory.Open(process);
            try
            {
                var reader = new CotwLiveReader(
                    memory,
                    module.BaseAddress,
                    offsets);
                var probe = reader.Probe();
                if (!probe.IsValid)
                {
                    throw new InvalidOperationException(
                        $"Runtime profile validation failed: {string.Join("; ", probe.Messages)}");
                }

                var population = PopulationFileReader.Read(
                    populationPath,
                    reserve);

                _process = process;
                _memory = memory;
                _reader = reader;
                Profile = profile;
                ExecutableSha256 = executableHash;
                GameDirectory = gameDirectory;
                Population = population;
                Reserve = new ReserveChoice(reserveIndex, reserve.DisplayName);
            }
            catch
            {
                memory.Dispose();
                throw;
            }
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public DesktopSnapshot ReadSnapshot()
    {
        if (!IsAttached || _reader is null)
        {
            throw new InvalidOperationException("The desktop tracker is not attached to the game.");
        }

        var snapshot = _reader.ReadSnapshot();
        var animals = snapshot.Animals
            .Select(animal => Enrich(animal, snapshot.CameraPosition))
            .OrderBy(animal => animal.DistanceMeters)
            .ToArray();

        return new DesktopSnapshot(
            snapshot.CameraPosition,
            animals,
            snapshot.SkippedAnimals);
    }

    private LiveAnimalView Enrich(
        AnimalSnapshot live,
        Float3 camera)
    {
        var record = ResolvePopulationRecord(live);

        var difficulty = record is null
            ? "Unknown"
            : record.IsGreatOne
                ? record.DifficultyLabel
                : $"~{record.DifficultyLabel}";

        return new LiveAnimalView(
            Species: live.Species,
            Gender: record?.Gender ?? "unknown",
            Difficulty: difficulty,
            Trophy: record?.Trophy ?? "Unknown",
            Fur: record?.FurName ?? "Unknown",
            FurRarity: record?.FurRarity ?? "Unknown",
            FurProbability: record?.FurProbability ?? 0f,
            NextTrophyText: record is null
                ? "Trophy threshold unavailable until population identity resolves."
                : PopulationAnimalMetadataCatalog.GetNextTrophyThresholdText(
                    record.Species,
                    live.Score,
                    record.IsGreatOne),
            IsRare: record?.IsRareFur ?? false,
            IsGreatOne: record?.IsGreatOne ?? false,
            DistanceMeters: live.DistanceMeters,
            Health: live.Health,
            MaxHealth: live.MaxHealth,
            Weight: live.Weight,
            Score: live.Score,
            VisualVariationSeed: live.VisualVariationSeed,
            GroupIndex: record?.GroupIndex,
            AnimalIndex: record?.AnimalIndex,
            X: live.Position.X,
            Y: live.Position.Y,
            Z: live.Position.Z,
            RelativeX: live.Position.X - camera.X,
            RelativeZ: live.Position.Z - camera.Z,
            Address: live.Address);
    }

    public string ProbeDifficulty(
        LiveAnimalView animal)
    {
        ArgumentNullException.ThrowIfNull(animal);

        if (!IsAttached || _memory is null)
        {
            throw new InvalidOperationException(
                "Attach to COTW before probing a live animal.");
        }

        var result = AnimalDifficultyMemoryProbe.Probe(
            _memory,
            animal.Address);

        return result.FormatReport(
            animal.DisplaySpecies,
            animal.Weight,
            animal.Score,
            animal.Difficulty);
    }

    private PopulationAnimalRecord? ResolvePopulationRecord(
        AnimalSnapshot live)
    {
        if (Population is null)
        {
            return null;
        }

        var normalizedLiveSpecies = NormalizeSpecies(live.Species);
        var matches = Population.Animals
            .Where(animal =>
            {
                var populationSpecies = NormalizeSpecies(animal.Species);
                return populationSpecies == normalizedLiveSpecies ||
                       populationSpecies.StartsWith(
                           normalizedLiveSpecies,
                           StringComparison.Ordinal) ||
                       normalizedLiveSpecies.StartsWith(
                           populationSpecies,
                           StringComparison.Ordinal);
            })
            .Where(animal =>
                animal.VisualVariationSeed == live.VisualVariationSeed &&
                BitConverter.SingleToInt32Bits(animal.Weight) ==
                BitConverter.SingleToInt32Bits(live.Weight) &&
                BitConverter.SingleToInt32Bits(animal.Score) ==
                BitConverter.SingleToInt32Bits(live.Score))
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static string NormalizeSpecies(string value) =>
        new(
            value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

    public void Dispose()
    {
        DisposeSession();
        GC.SuppressFinalize(this);
    }

    private void DisposeSession()
    {
        _reader = null;
        _memory?.Dispose();
        _memory = null;
        _process?.Dispose();
        _process = null;

        Profile = null;
        ExecutableSha256 = "";
        GameDirectory = "";
        Population = null;
        Reserve = null;
    }
}
