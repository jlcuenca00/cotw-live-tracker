using CotwLiveTracker.Configuration;
using CotwLiveTracker.Infrastructure;
using CotwLiveTracker.Memory;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("COTW Live Tracker currently supports Windows only.");
    return 1;
}

var processName = GetOption(args, "--process") ?? "theHunterCotW_F";
var offsetsPath = GetOption(args, "--offsets");

Console.WriteLine("COTW Live Tracker v0.1 bootstrap");
Console.WriteLine($"Looking for process: {processName}.exe");

using var process = GameProcessLocator.Find(processName);
if (process is null)
{
    Console.Error.WriteLine("Game process not found. Start theHunter: Call of the Wild and try again.");
    return 2;
}

Console.WriteLine($"Found PID: {process.Id}");

try
{
    var module = process.MainModule;
    Console.WriteLine($"Executable: {module?.FileName ?? "(unavailable)"}");
    Console.WriteLine($"Module base: 0x{module?.BaseAddress.ToInt64() ?? 0:X}");
    Console.WriteLine($"Module size: {module?.ModuleMemorySize ?? 0:N0} bytes");
    Console.WriteLine($"File version: {module?.FileVersionInfo.FileVersion ?? "(unavailable)"}");
}
catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
{
    Console.WriteLine($"Module metadata unavailable: {ex.Message}");
}

using var memory = ReadOnlyProcessMemory.Open(process);
Console.WriteLine("Read-only process handle opened successfully.");

if (offsetsPath is not null)
{
    var profile = OffsetProfileLoader.Load(offsetsPath);
    Console.WriteLine($"Offset profile: {profile.Name}");
    Console.WriteLine($"Game build: {profile.GameBuild}");
    Console.WriteLine($"Configured offsets: {profile.Offsets.Count}");
}
else
{
    Console.WriteLine("No offset profile supplied. Memory reads beyond process attachment are disabled.");
}

return 0;

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
