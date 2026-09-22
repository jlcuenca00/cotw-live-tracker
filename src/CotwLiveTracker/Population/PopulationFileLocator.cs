namespace CotwLiveTracker.Population;

internal static class PopulationFileLocator
{
    public static string FindReservePopulation(int reserveIndex)
    {
        if (reserveIndex < 0 || reserveIndex > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(reserveIndex));
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            throw new DirectoryNotFoundException("Windows Documents folder could not be resolved.");
        }

        var savesRoot = Path.Combine(documents, "Avalanche Studios", "COTW", "Saves");
        if (!Directory.Exists(savesRoot))
        {
            throw new DirectoryNotFoundException($"COTW save folder was not found at '{savesRoot}'.");
        }

        var filename = $"animal_population_{reserveIndex}";
        var candidates = Directory
            .EnumerateFiles(savesRoot, filename, SearchOption.AllDirectories)
            .Where(path => !IsBackupPath(path))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new FileNotFoundException(
                $"Could not find '{filename}' under '{savesRoot}'.",
                filename);
        }

        return candidates[0].FullName;
    }

    private static bool IsBackupPath(string path)
    {
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var parts = path.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        return parts.Any(part =>
            part.Equals("slots", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("backup", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("backups", StringComparison.OrdinalIgnoreCase));
    }
}
