using System.Text.Json;

namespace CotwLiveTracker.Configuration;

internal static class OffsetProfileLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static OffsetProfile Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Offset profile was not found.", path);
        }

        var json = File.ReadAllText(path);
        var profile = JsonSerializer.Deserialize<OffsetProfile>(json, JsonOptions)
            ?? throw new InvalidDataException("Offset profile is empty or invalid.");

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidDataException("Offset profile must include a name.");
        }

        if (string.IsNullOrWhiteSpace(profile.GameBuild))
        {
            throw new InvalidDataException("Offset profile must include a gameBuild value.");
        }

        return profile;
    }
}
