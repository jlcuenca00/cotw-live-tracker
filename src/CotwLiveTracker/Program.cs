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

if (watch && populationPath is not null)
{
    Console.Error.WriteLine("Structured population reading is currently one-shot. Remove --watch and run again.");
    return 10;
}

Console.WriteLine("COTW Live Tracker v0.5");
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
        PrintSnapshot(snapshot, speciesFilter);

        if (populationPath is not null)
        {
            try
            {
                var population = PopulationFileReader.Read(
                    populationPath,
                    populationReserve
                    ?? throw new InvalidOperationException("Population reserve metadata is unavailable."));
                PrintPopulationSummary(population, speciesFilter, populationTop);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Population diagnostic failed: {ex.Message}");
                return 10;
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

        Console.WriteLine("COTW Live Tracker v0.5 - LIVE");
        Console.WriteLine($"Profile: {profile.Name}");
        PrintSnapshot(tracker.ReadSnapshot(), speciesFilter);

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

static void PrintSnapshot(LiveSnapshot snapshot, string? speciesFilter)
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
    Console.WriteLine("DIST   HP           SPECIES                 XYZ");
    Console.WriteLine("-----  -----------  ----------------------  --------------------------------");

    foreach (var animal in displayed)
    {
        Console.WriteLine(
            $"{animal.DistanceMeters,4:F0}m  {animal.Health,5:F1}/{animal.MaxHealth,-5:F1}  " +
            $"{Truncate(animal.Species, 22),-22}  " +
            $"{animal.Position.X,8:F1} {animal.Position.Y,8:F1} {animal.Position.Z,8:F1}");
    }
}

static void PrintPopulationSummary(
    PopulationReadResult population,
    string? speciesFilter,
    int topRecords)
{
    Console.WriteLine();
    Console.WriteLine("FULL RESERVE POPULATION");
    Console.WriteLine($"Reserve: {population.ReserveName}");
    Console.WriteLine($"Population file: {population.FilePath}");
    Console.WriteLine($"Population file size: {population.FileSizeBytes:N0} bytes");
    Console.WriteLine($"Decompressed ADF payload: {population.AdfPayloadSizeBytes:N0} bytes");
    Console.WriteLine($"ADF version: {population.AdfVersion}");
    Console.WriteLine($"Animals decoded: {population.Animals.Count:N0}");

    IEnumerable<PopulationSpeciesRecord> species = population.Species;
    if (!string.IsNullOrWhiteSpace(speciesFilter))
    {
        species = species.Where(item =>
            item.Species.Contains(speciesFilter.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase) ||
            item.Species.Replace('_', ' ').Contains(speciesFilter, StringComparison.OrdinalIgnoreCase));
    }

    var displayed = species
        .Where(item => item.Animals.Count > 0)
        .ToArray();

    if (displayed.Length == 0)
    {
        Console.WriteLine("No matching population species.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine("SPECIES                 GROUPS  TOTAL   MALE FEMALE  GO   MAX WT  MAX SCORE");
    Console.WriteLine("----------------------  ------  ------  ----- ------  --  -------  ---------");

    foreach (var item in displayed)
    {
        var animals = item.Animals;
        var males = animals.Count(animal => animal.Gender == "male");
        var females = animals.Count(animal => animal.Gender == "female");
        var greatOnes = animals.Count(animal => animal.IsGreatOne);
        var maxWeight = animals.Max(animal => animal.Weight);
        var maxScore = animals.Max(animal => animal.Score);

        Console.WriteLine(
            $"{Truncate(item.Species.Replace('_', ' '), 22),-22}  " +
            $"{item.Groups.Count,6}  {animals.Count,6}  {males,5} {females,6}  " +
            $"{greatOnes,2}  {maxWeight,7:F2}  {maxScore,9:F2}");
    }

    Console.WriteLine();
    Console.WriteLine($"Top {topRecords} population record(s) per displayed species by saved score:");

    foreach (var item in displayed)
    {
        Console.WriteLine();
        Console.WriteLine(item.Species.Replace('_', ' ').ToUpperInvariant());

        foreach (var animal in item.Animals
                     .OrderByDescending(animal => animal.IsGreatOne)
                     .ThenByDescending(animal => animal.Score)
                     .Take(topRecords))
        {
            Console.WriteLine(
                $"  G{animal.GroupIndex,-3} #{animal.AnimalIndex,-3} " +
                $"{animal.Gender,-7} {animal.DifficultyLabel,-13} " +
                $"wt {animal.Weight,8:F2}  score {animal.Score,8:F2}  " +
                $"GO {(animal.IsGreatOne ? "yes" : "no "),-3}  " +
                $"seed {animal.VisualVariationSeed,10}  id {animal.Id,10}");
        }
    }
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
