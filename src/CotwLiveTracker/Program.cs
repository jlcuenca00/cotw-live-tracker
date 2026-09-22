using System.ComponentModel;
using System.Diagnostics;
using CotwLiveTracker.Configuration;
using CotwLiveTracker.Diagnostics;
using CotwLiveTracker.Game;
using CotwLiveTracker.Infrastructure;
using CotwLiveTracker.Memory;

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

Console.WriteLine("COTW Live Tracker v0.2");
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
        PrintSnapshot(tracker.ReadSnapshot(), speciesFilter);
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

        Console.WriteLine("COTW Live Tracker v0.2 - LIVE");
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
