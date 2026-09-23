using System.Globalization;

namespace CotwLiveTracker.Configuration;

internal sealed record OffsetProfile
{
    public string Name { get; init; } = "";
    public string GameBuild { get; init; } = "";
    public string? GamePatch { get; init; }
    public string? ExecutableSha256 { get; init; }
    public string? Source { get; init; }
    public bool CommunityDerived { get; init; }
    public int MaxAnimals { get; init; } = 512;
    public float MaxDistanceMeters { get; init; } = 1000f;
    public Dictionary<string, string> Offsets { get; init; } = [];
}

internal sealed record CotwOffsets(
    nint ViewProjection,
    nint CameraPosition,
    nint AnimalManager,
    nint AnimalManagerDereference,
    nint AnimalVector,
    nint AnimalSpeciesDefinition,
    nint AnimalPosition,
    nint AnimalHealthMax,
    nint AnimalHealthCurrent,
    nint AnimalWeight,
    nint AnimalScore,
    nint AnimalVisualVariationSeed,
    nint SpeciesRagdollPath,
    int MaxAnimals,
    float MaxDistanceMeters)
{
    public static CotwOffsets FromProfile(OffsetProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new CotwOffsets(
            ParseRequired(profile, "viewProjection"),
            ParseRequired(profile, "cameraPosition"),
            ParseRequired(profile, "animalManager"),
            ParseRequired(profile, "animalManagerDereference"),
            ParseRequired(profile, "animalVector"),
            ParseRequired(profile, "animalSpeciesDefinition"),
            ParseRequired(profile, "animalPosition"),
            ParseRequired(profile, "animalHealthMax"),
            ParseRequired(profile, "animalHealthCurrent"),
            ParseRequired(profile, "animalWeight"),
            ParseRequired(profile, "animalScore"),
            ParseRequired(profile, "animalVisualVariationSeed"),
            ParseRequired(profile, "speciesRagdollPath"),
            profile.MaxAnimals,
            profile.MaxDistanceMeters);
    }

    private static nint ParseRequired(OffsetProfile profile, string key)
    {
        if (!profile.Offsets.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidDataException($"Required offset '{key}' is missing.");
        }

        var value = raw.Trim();
        var number = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

        if (number > long.MaxValue)
        {
            throw new OverflowException($"Offset '{key}' is too large.");
        }

        return (nint)(long)number;
    }
}
