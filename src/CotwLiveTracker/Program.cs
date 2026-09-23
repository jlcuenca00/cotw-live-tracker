using System.ComponentModel;
using System.Diagnostics;
using CotwLiveTracker.Configuration;
using CotwLiveTracker.Diagnostics;
using CotwLiveTracker.Game;
using CotwLiveTracker.Infrastructure;
using CotwLiveTracker.Memory;
using CotwLiveTracker.Population;

if (HasFlag(args, "--selftest"))
{
    return TrackerSelfTest.Run();
}

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("COTW Live Tracker currently supports Windows only.");
    return 1;
}

var processName = GetOption(args, "--process") ?? "theHunterCotW_F";
var offsetsPath = GetOption(args, "--offsets") ?? FindBundledProfile();
var speciesFilter = GetOption(args, "--species");
var watch = HasFlag(args, "--watch");
var intervalMs = GetIntOption(args, "--interval-ms", 1000, 100, 10_000);
var populationFileOption = GetOption(args, "--population-file");
var populationReserveOption = GetOption(args, "--population-reserve");
var populationTop = GetIntOption(args, "--population-top", 3, 1, 20);
var trophyFilter = GetOption(args, "--trophy");
var rareOnly = HasFlag(args, "--rare");
var greatOneOnly = HasFlag(args, "--great-one");
var difficultyFilterRaw = GetOption(args, "--difficulty");
var furFilter = GetOption(args, "--fur");
var sexFilter = GetOption(args, "--sex");
var populationAll = HasFlag(args, "--population-all");
var bridgeProbe = HasFlag(args, "--bridge-probe");
var bridgeLimit = GetIntOption(args, "--bridge-limit", 8, 1, 64);

int? difficultyFilter = null;
if (!string.IsNullOrWhiteSpace(difficultyFilterRaw))
{
    if (!int.TryParse(difficultyFilterRaw, out var parsedDifficulty) ||
        parsedDifficulty < 1 ||
        parsedDifficulty > 10)
    {
        Console.Error.WriteLine("--difficulty must be an integer between 1 and 10.");
        return 10;
    }

    difficultyFilter = parsedDifficulty;
}

if (!string.IsNullOrWhiteSpace(trophyFilter))
{
    var normalizedTrophy = trophyFilter
        .Trim()
        .Replace('_', ' ')
        .Replace('-', ' ')
        .ToLowerInvariant();
    var validTrophies = new[] { "none", "bronze", "silver", "gold", "diamond", "great one" };
    if (!validTrophies.Contains(normalizedTrophy, StringComparer.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            "--trophy must be one of: none, bronze, silver, gold, diamond, great-one.");
        return 10;
    }
}

if (!string.IsNullOrWhiteSpace(sexFilter))
{
    sexFilter = sexFilter.Trim().ToLowerInvariant();
    if (sexFilter is not ("male" or "female"))
    {
        Console.Error.WriteLine("--sex must be either male or female.");
        return 10;
    }
}

var populationFilters = new PopulationFilterOptions(
    Trophy: trophyFilter,
    RareOnly: rareOnly,
    GreatOneOnly: greatOneOnly,
    Difficulty: difficultyFilter,
    Fur: furFilter,
    Sex: sexFilter,
    ShowAll: populationAll);

string? populationPath = null;
ReservePopulationDefinition? populationReserve = null;
try
{
    int? reserveIndex = null;
    if (!string.IsNullOrWhiteSpace(populationReserveOption))
    {
        if (!int.TryParse(populationReserveOption, out var parsedReserveIndex) ||
            parsedReserveIndex < 0 ||
            parsedReserveIndex > 100)
        {
            Console.Error.WriteLine("--population-reserve must be an integer between 0 and 100.");
            return 10;
        }

        reserveIndex = parsedReserveIndex;
        populationReserve = PopulationReserveCatalog.Get(parsedReserveIndex);
    }

    if (!string.IsNullOrWhiteSpace(populationFileOption))
    {
        if (populationReserve is null)
        {
            Console.Error.WriteLine("--population-file requires --population-reserve so species order can be decoded.");
            return 10;
        }

        populationPath = Path.GetFullPath(populationFileOption);
    }
    else if (reserveIndex is not null)
    {
        populationPath = PopulationFileLocator.FindReservePopulation(reserveIndex.Value);
    }
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Could not resolve the population file: {ex.Message}");
    return 10;
}

if ((populationFilters.HasAnimalFilters || populationFilters.ShowAll || bridgeProbe) &&
    populationPath is null)
{
    Console.Error.WriteLine(
        "Population filters/bridge probe require --population-reserve (or --population-file with --population-reserve).");
    return 10;
}

if (watch && populationPath is not null)
{
    Console.Error.WriteLine("Structured population reading is currently one-shot. Remove --watch and run again.");
    return 10;
}

Console.WriteLine("COTW Live Tracker v0.9");
Console.WriteLine($"Looking for process: {processName}.exe");

using var process = GameProcessLocator.Find(processName);
if (process is null)
{
    Console.Error.WriteLine("Game process not found. Start theHunter: Call of the Wild and try again.");
    return 2;
}

ProcessModule module;
try
{
    module = process.MainModule
        ?? throw new InvalidOperationException("The game main module is unavailable.");
}
catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
{
    Console.Error.WriteLine($"Could not inspect the game executable: {ex.Message}");
    return 3;
}

Console.WriteLine($"Found PID: {process.Id}");
Console.WriteLine($"Executable: {module.FileName}");
Console.WriteLine($"Module base: 0x{module.BaseAddress.ToInt64():X}");
Console.WriteLine($"Module size: {module.ModuleMemorySize:N0} bytes");
Console.WriteLine($"File version: {module.FileVersionInfo.FileVersion ?? "(unavailable)"}");

OffsetProfile profile;
try
{
    profile = OffsetProfileLoader.Load(offsetsPath);
}
catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException)
{
    Console.Error.WriteLine($"Could not load offset profile '{offsetsPath}': {ex.Message}");
    return 4;
}

Console.WriteLine($"Offset profile: {profile.Name}");
Console.WriteLine($"Game build: {profile.GameBuild}");
Console.WriteLine($"Game patch: {profile.GamePatch ?? "(not specified)"}");
if (!string.IsNullOrWhiteSpace(profile.Source))
{
    Console.WriteLine($"Profile source: {profile.Source}");
}

string executableSha256;
try
{
    executableSha256 = ExecutableFingerprint.Sha256(module.FileName);
    Console.WriteLine($"Executable SHA-256: {executableSha256}");
}
catch (IOException ex)
{
    Console.Error.WriteLine($"Could not fingerprint the executable: {ex.Message}");
    return 5;
}

if (!string.IsNullOrWhiteSpace(profile.ExecutableSha256) &&
    !string.Equals(profile.ExecutableSha256, executableSha256, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("The executable hash does not match this offset profile. Refusing to read game structures.");
    return 6;
}

if (profile.CommunityDerived && string.IsNullOrWhiteSpace(profile.ExecutableSha256))
{
    Console.WriteLine("Profile status: community-derived candidate; runtime validation is required.");
}

CotwOffsets offsets;
try
{
    offsets = CotwOffsets.FromProfile(profile);
}
catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException)
{
    Console.Error.WriteLine($"Offset profile is invalid: {ex.Message}");
    return 7;
}

try
{
    using var memory = ReadOnlyProcessMemory.Open(process);
    Console.WriteLine("Read-only process handle opened successfully.");

    var tracker = new CotwLiveReader(memory, module.BaseAddress, offsets);
    var probe = tracker.Probe();

    foreach (var message in probe.Messages)
    {
        Console.WriteLine($"Probe: {message}");
    }

    if (!probe.IsValid)
    {
        Console.Error.WriteLine("Runtime validation failed. The profile may not match this game build.");
        return 8;
    }

    if (!watch)
    {
        var snapshot = tracker.ReadSnapshot();
        PopulationReadResult? population = null;

        if (populationPath is not null)
        {
            try
            {
                population = PopulationFileReader.Read(
                    populationPath,
                    populationReserve
                    ?? throw new InvalidOperationException("Population reserve metadata is unavailable."));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Population diagnostic failed: {ex.Message}");
                return 10;
            }
        }

        PrintSnapshot(snapshot, speciesFilter, population);

        if (population is not null)
        {
            PrintPopulationSummary(
                population,
                speciesFilter,
                populationTop,
                populationFilters);

            if (bridgeProbe)
            {
                PrintIdentityBridgeProbe(
                    memory,
                    snapshot,
                    population,
                    speciesFilter,
                    bridgeLimit);
            }
        }

        return 0;
    }

    Console.WriteLine("Live watch started. Press Ctrl+C to stop.");
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    while (!cancellation.IsCancellationRequested && !process.HasExited)
    {
        if (!Console.IsOutputRedirected)
        {
            Console.Clear();
        }

        Console.WriteLine("COTW Live Tracker v0.9 - LIVE");
        Console.WriteLine($"Profile: {profile.Name}");
        PrintSnapshot(tracker.ReadSnapshot(), speciesFilter, null);

        try
        {
            await Task.Delay(intervalMs, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }

    return 0;
}
catch (Win32Exception ex)
{
    Console.Error.WriteLine($"Windows memory read failed: {ex.Message}");
    return 9;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"Tracker read failed: {ex.Message}");
    return 9;
}

static void PrintSnapshot(
    LiveSnapshot snapshot,
    string? speciesFilter,
    PopulationReadResult? population)
{
    Console.WriteLine(
        $"Camera XYZ: {snapshot.CameraPosition.X:F2}, {snapshot.CameraPosition.Y:F2}, {snapshot.CameraPosition.Z:F2}");
    Console.WriteLine(
        $"Loaded animals: {snapshot.Animals.Count} valid, {snapshot.SkippedAnimals} skipped");

    IEnumerable<AnimalSnapshot> animals = snapshot.Animals;
    if (!string.IsNullOrWhiteSpace(speciesFilter))
    {
        animals = animals.Where(animal =>
            animal.Species.Contains(speciesFilter, StringComparison.OrdinalIgnoreCase));
    }

    var displayed = animals.OrderBy(animal => animal.DistanceMeters).ToArray();
    if (displayed.Length == 0)
    {
        Console.WriteLine("No matching loaded animals.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine("DIST   HP           SPECIES                 LVL          TROPHY    WT      SCORE    FUR");
    Console.WriteLine("-----  -----------  ----------------------  -----------  --------  ------  -------  ------------------");

    foreach (var animal in displayed)
    {
        var matched = population is null
            ? null
            : ResolveLivePopulationRecord(animal, population);

        var level = matched?.DifficultyLabel ?? "?";
        var trophy = matched?.Trophy ?? "?";
        var fur = matched?.FurName ?? "?";

        Console.WriteLine(
            $"{animal.DistanceMeters,4:F0}m  {animal.Health,5:F1}/{animal.MaxHealth,-5:F1}  " +
            $"{Truncate(animal.Species, 22),-22}  " +
            $"{Truncate(level, 11),-11}  {Truncate(trophy, 8),-8}  " +
            $"{animal.Weight,6:F2}  {animal.Score,7:F2}  {Truncate(fur, 18),-18}");
    }
}

static PopulationAnimalRecord? ResolveLivePopulationRecord(
    AnimalSnapshot live,
    PopulationReadResult population)
{
    var normalizedLiveSpecies = NormalizeSpecies(live.Species);

    var candidates = population.Animals
        .Where(animal =>
            NormalizeSpecies(animal.Species) == normalizedLiveSpecies ||
            NormalizeSpecies(animal.Species).StartsWith(normalizedLiveSpecies, StringComparison.Ordinal) ||
            normalizedLiveSpecies.StartsWith(NormalizeSpecies(animal.Species), StringComparison.Ordinal))
        .Where(animal =>
            animal.VisualVariationSeed == live.VisualVariationSeed &&
            BitConverter.SingleToInt32Bits(animal.Weight) ==
            BitConverter.SingleToInt32Bits(live.Weight) &&
            BitConverter.SingleToInt32Bits(animal.Score) ==
            BitConverter.SingleToInt32Bits(live.Score))
        .Take(2)
        .ToArray();

    return candidates.Length == 1 ? candidates[0] : null;
}

static string NormalizeSpecies(string value) =>
    new(
        value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

static void PrintPopulationSummary(
    PopulationReadResult population,
    string? speciesFilter,
    int topRecords,
    PopulationFilterOptions filters)
{
    Console.WriteLine();
    Console.WriteLine("FULL RESERVE POPULATION");
    Console.WriteLine($"Reserve: {population.ReserveName}");
    Console.WriteLine($"Population file: {population.FilePath}");
    Console.WriteLine($"Population file size: {population.FileSizeBytes:N0} bytes");
    Console.WriteLine($"Decompressed ADF payload: {population.AdfPayloadSizeBytes:N0} bytes");
    Console.WriteLine($"ADF version: {population.AdfVersion}");
    Console.WriteLine($"Animals decoded: {population.Animals.Count:N0}");

    if (filters.HasAnimalFilters)
    {
        Console.WriteLine($"Filters: {filters.Describe()}");
    }

    IEnumerable<PopulationSpeciesRecord> species = population.Species;
    if (!string.IsNullOrWhiteSpace(speciesFilter))
    {
        species = species.Where(item =>
            item.Species.Contains(speciesFilter.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase) ||
            item.Species.Replace('_', ' ').Contains(speciesFilter, StringComparison.OrdinalIgnoreCase));
    }

    var displayed = species
        .Where(item => item.Animals.Count > 0)
        .Where(item => !filters.HasAnimalFilters || item.Animals.Any(filters.Matches))
        .ToArray();

    if (displayed.Length == 0)
    {
        Console.WriteLine(
            filters.HasAnimalFilters
                ? "No population animals match the selected filters."
                : "No matching population species.");
        return;
    }

    var totalMatches = displayed.Sum(item => item.Animals.Count(filters.Matches));
    Console.WriteLine($"Matching animals: {totalMatches:N0}");

    Console.WriteLine();
    Console.WriteLine("SPECIES                 GROUPS  TOTAL  MATCH   MALE FEMALE  GO  DIA RARE   MAX WT  MAX SCORE");
    Console.WriteLine("----------------------  ------  -----  -----  ----- ------  --  --- ----  -------  ---------");

    foreach (var item in displayed)
    {
        var allAnimals = item.Animals;
        var animals = allAnimals.Where(filters.Matches).ToArray();
        var males = animals.Count(animal => animal.Gender == "male");
        var females = animals.Count(animal => animal.Gender == "female");
        var greatOnes = animals.Count(animal => animal.IsGreatOne);
        var diamonds = animals.Count(animal => animal.Trophy == "Diamond");
        var rareFurs = animals.Count(animal => animal.IsRareFur);
        var maxWeight = animals.Max(animal => animal.Weight);
        var maxScore = animals.Max(animal => animal.Score);

        Console.WriteLine(
            $"{Truncate(item.Species.Replace('_', ' '), 22),-22}  " +
            $"{item.Groups.Count,6}  {allAnimals.Count,5}  {animals.Length,5}  " +
            $"{males,5} {females,6}  {greatOnes,2}  {diamonds,3} {rareFurs,4}  " +
            $"{maxWeight,7:F2}  {maxScore,9:F2}");
    }

    Console.WriteLine();
    Console.WriteLine(
        filters.ShowAll
            ? "All matching population records:"
            : $"Top {topRecords} matching population record(s) per displayed species by saved score:");

    foreach (var item in displayed)
    {
        Console.WriteLine();
        Console.WriteLine(item.Species.Replace('_', ' ').ToUpperInvariant());

        var records = item.Animals
            .Where(filters.Matches)
            .OrderByDescending(animal => animal.IsGreatOne)
            .ThenByDescending(animal => animal.Score);

        if (!filters.ShowAll)
        {
            records = records.Take(topRecords)
                .OrderByDescending(animal => animal.IsGreatOne)
                .ThenByDescending(animal => animal.Score);
        }

        foreach (var animal in records)
        {
            Console.WriteLine(
                $"  G{animal.GroupIndex,-3} #{animal.AnimalIndex,-3} " +
                $"{animal.Gender,-7} {animal.DifficultyLabel,-13} {animal.Trophy,-9} " +
                $"wt {animal.Weight,8:F2}  score {animal.Score,8:F2}  " +
                $"fur {Truncate(animal.FurName, 18),-18} " +
                $"[{animal.FurRarity} {animal.FurProbability * 100f,6:F3}%]  " +
                $"seed {animal.VisualVariationSeed,10}  id {animal.Id,10}");
        }
    }
}

static void PrintIdentityBridgeProbe(
    IMemoryReader memory,
    LiveSnapshot snapshot,
    PopulationReadResult population,
    string? speciesFilter,
    int limit)
{
    IEnumerable<AnimalSnapshot> liveAnimals = snapshot.Animals;
    if (!string.IsNullOrWhiteSpace(speciesFilter))
    {
        liveAnimals = liveAnimals.Where(animal =>
            animal.Species.Contains(speciesFilter, StringComparison.OrdinalIgnoreCase) ||
            speciesFilter.Contains(animal.Species, StringComparison.OrdinalIgnoreCase));
    }

    var selected = liveAnimals
        .OrderBy(animal => animal.DistanceMeters)
        .Take(limit)
        .ToArray();

    Console.WriteLine();
    Console.WriteLine("IDENTITY BRIDGE PROBE");
    Console.WriteLine(
        "Read-only diagnostic: scans each loaded animal object and immediate pointers for saved-record signatures.");
    Console.WriteLine($"Loaded animals selected: {selected.Length}/{snapshot.Animals.Count}");

    if (selected.Length == 0)
    {
        Console.WriteLine("No loaded animals match the bridge probe selection.");
        return;
    }

    var resolvedCount = 0;
    foreach (var liveAnimal in selected)
    {
        var result = PopulationIdentityBridgeProbe.Probe(
            memory,
            liveAnimal,
            population);

        Console.WriteLine();
        Console.WriteLine(
            $"{liveAnimal.Species.ToUpperInvariant()}  " +
            $"entity 0x{liveAnimal.Address.ToInt64():X}  " +
            $"{liveAnimal.DistanceMeters:F0}m");

        if (result.PopulationSpecies is null)
        {
            Console.WriteLine("  Population species mapping: unresolved");
            continue;
        }

        Console.WriteLine($"  Population species: {result.PopulationSpecies}");
        Console.WriteLine($"  Immediate pointer reads: {result.PointerReads}");

        if (result.ResolvedAnimal is { } resolved)
        {
            resolvedCount++;
            Console.WriteLine(
                $"  RESOLVED -> G{resolved.GroupIndex} #{resolved.AnimalIndex} " +
                $"{resolved.Gender} {resolved.DifficultyLabel} {resolved.Trophy} " +
                $"wt {resolved.Weight:F2} score {resolved.Score:F2} " +
                $"fur {resolved.FurName} seed {resolved.VisualVariationSeed}");

            foreach (var match in result.Matches
                         .Where(match =>
                             match.Animal.SpeciesIndex == resolved.SpeciesIndex &&
                             match.Animal.GroupIndex == resolved.GroupIndex &&
                             match.Animal.AnimalIndex == resolved.AnimalIndex)
                         .OrderBy(match => match.Evidence == "stable-record" ? 0 : 1)
                         .Take(8))
            {
                Console.WriteLine(
                    $"    evidence {match.Evidence,-19} {match.Path}");
            }

            continue;
        }

        var candidateGroups = result.Matches
            .GroupBy(match => (
                match.Animal.SpeciesIndex,
                match.Animal.GroupIndex,
                match.Animal.AnimalIndex))
            .OrderByDescending(group => group.Any(match => match.Evidence == "stable-record"))
            .ThenByDescending(group => group.Select(match => match.Evidence).Distinct().Count())
            .Take(5)
            .ToArray();

        if (candidateGroups.Length == 0)
        {
            Console.WriteLine("  No population identity signature found in the probed object graph.");
            continue;
        }

        Console.WriteLine(
            $"  Unresolved: {candidateGroups.Length} candidate record(s) with partial evidence.");
        foreach (var group in candidateGroups)
        {
            var animal = group.First().Animal;
            var evidence = string.Join(
                ", ",
                group.Select(match => match.Evidence).Distinct());
            Console.WriteLine(
                $"    G{animal.GroupIndex} #{animal.AnimalIndex} " +
                $"{animal.Gender} {animal.DifficultyLabel} {animal.Trophy} " +
                $"wt {animal.Weight:F2} score {animal.Score:F2} " +
                $"seed {animal.VisualVariationSeed} [{evidence}]");

            foreach (var match in group.Take(3))
            {
                Console.WriteLine($"      {match.Path}");
            }
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Bridge resolved: {resolvedCount}/{selected.Length}");
}

static string Truncate(string value, int maxLength) =>
    value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

static string FindBundledProfile()
{
    const string profileName = "patch-9.3-2026-09-16-community.json";
    var bundled = Path.Combine(AppContext.BaseDirectory, "offsets", profileName);
    return File.Exists(bundled) ? bundled : Path.Combine("offsets", profileName);
}

static bool HasFlag(string[] args, string name) =>
    args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static int GetIntOption(string[] args, string name, int defaultValue, int minValue, int maxValue)
{
    var raw = GetOption(args, name);
    if (raw is null)
    {
        return defaultValue;
    }

    if (!int.TryParse(raw, out var value) || value < minValue || value > maxValue)
    {
        throw new ArgumentException($"{name} must be an integer between {minValue} and {maxValue}.");
    }

    return value;
}
