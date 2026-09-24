using System.Text.Json;

namespace CotwLiveTracker.Population;

internal sealed record PopulationTrophyResult(string Medal);

internal sealed record PopulationFurResult(
    string Key,
    string Name,
    float Probability,
    bool IsRare,
    string RarityLabel)
{
    public float Percentage => Probability * 100f;
}

internal sealed record PopulationSpeciesMetadata(
    IReadOnlyList<(float Min, float Max)> Levels,
    IReadOnlyDictionary<string, (float Min, float Max)> TrophyBands,
    IReadOnlyDictionary<string, PopulationGenderMetadata> Genders);

internal sealed record PopulationGenderMetadata(
    float FurTotalProbability,
    IReadOnlyList<(string Key, float Weight)> Furs,
    float SeedFurTotalProbability,
    IReadOnlyList<(string Key, float Weight)> SeedFurs);

internal static class PopulationAnimalMetadataCatalog
{
    private static readonly Lazy<CatalogData> Catalog = new(Load, isThreadSafe: true);

    public static DifficultyBand GetDifficulty(string species, float weight, bool isGreatOne = false)
    {
        if (isGreatOne)
        {
            return new DifficultyBand(
                float.NegativeInfinity,
                float.PositiveInfinity,
                10,
                "Fabled");
        }

        if (!Catalog.Value.Species.TryGetValue(species, out var metadata) ||
            metadata.Levels.Count == 0)
        {
            return new DifficultyBand(
                float.NegativeInfinity,
                float.PositiveInfinity,
                0,
                "Unknown");
        }

        for (var i = 0; i < metadata.Levels.Count; i++)
        {
            var (min, max) = metadata.Levels[i];
            if (weight >= min && weight <= max)
            {
                return new DifficultyBand(min, max, i + 1, DifficultyName(i + 1));
            }
        }

        // Population multiplier mods can produce a value just outside the vanilla range.
        // Keep classification useful by clamping to the nearest supported band.
        if (weight < metadata.Levels[0].Min)
        {
            var first = metadata.Levels[0];
            return new DifficultyBand(first.Min, first.Max, 1, DifficultyName(1));
        }

        var last = metadata.Levels[^1];
        return new DifficultyBand(
            last.Min,
            last.Max,
            metadata.Levels.Count,
            DifficultyName(metadata.Levels.Count));
    }

    public static string GetNextTrophyThresholdText(
        string species,
        float score,
        bool isGreatOne)
    {
        if (isGreatOne ||
            !Catalog.Value.Species.TryGetValue(species, out var metadata))
        {
            return "Top trophy tier reached.";
        }

        foreach (var medal in new[] { "bronze", "silver", "gold", "diamond" })
        {
            if (!metadata.TrophyBands.TryGetValue(medal, out var band) ||
                score >= band.Min)
            {
                continue;
            }

            var name = char.ToUpperInvariant(medal[0]) + medal[1..];
            var delta = band.Min - score;
            return $"Next medal · {name} at {band.Min:F2} · {delta:F2} pts away";
        }

        return "Diamond threshold reached.";
    }

    public static PopulationTrophyResult GetTrophy(string species, float score, bool isGreatOne)
    {
        if (isGreatOne)
        {
            return new PopulationTrophyResult("Great One");
        }

        if (!Catalog.Value.Species.TryGetValue(species, out var metadata))
        {
            return new PopulationTrophyResult("Unknown");
        }

        foreach (var medal in new[] { "bronze", "silver", "gold", "diamond" })
        {
            if (metadata.TrophyBands.TryGetValue(medal, out var band) &&
                score >= band.Min &&
                score <= band.Max)
            {
                return new PopulationTrophyResult(
                    char.ToUpperInvariant(medal[0]) + medal[1..]);
            }
        }

        return new PopulationTrophyResult("None");
    }

    public static PopulationFurResult GetFur(
        string species,
        string gender,
        bool isGreatOne,
        uint seed)
    {
        if (!Catalog.Value.Species.TryGetValue(species, out var metadata))
        {
            return UnknownFur();
        }

        var genderKey = isGreatOne ? $"great_one_{gender}" : gender;
        if (!metadata.Genders.TryGetValue(genderKey, out var genderMetadata))
        {
            return UnknownFur();
        }

        var entries = genderMetadata.SeedFurs.Count > 0
            ? genderMetadata.SeedFurs
            : genderMetadata.Furs;
        var total = genderMetadata.SeedFurs.Count > 0
            ? genderMetadata.SeedFurTotalProbability
            : genderMetadata.FurTotalProbability;

        if (entries.Count == 0 || !float.IsFinite(total) || total <= 0f)
        {
            return UnknownFur();
        }

        var probability = SeedToProbability(seed);
        var cumulative = 0f;

        foreach (var entry in entries)
        {
            cumulative += entry.Weight / total;
            if (cumulative >= probability)
            {
                var actualWeight = genderMetadata.Furs
                    .FirstOrDefault(item => string.Equals(
                        item.Key,
                        entry.Key,
                        StringComparison.OrdinalIgnoreCase))
                    .Weight;

                // Use the active fur table for rarity where possible. Some seed tables
                // differ from display probabilities.
                var furProbability =
                    actualWeight > 0f && genderMetadata.FurTotalProbability > 0f
                        ? actualWeight / genderMetadata.FurTotalProbability
                        : entry.Weight / total;

                var name = Catalog.Value.FurNames.TryGetValue(entry.Key, out var furName)
                    ? furName
                    : Humanize(entry.Key);

                var isRare = !isGreatOne && furProbability > 0f && furProbability < 0.01f;
                var rarity = isGreatOne
                    ? "Fabled"
                    : isRare
                        ? "Rare"
                        : "Common";

                return new PopulationFurResult(
                    entry.Key,
                    name,
                    furProbability,
                    isRare,
                    rarity);
            }
        }

        return UnknownFur();
    }

    internal static float SeedToProbability(uint seed)
    {
        var state = unchecked((0x343FDu * seed) + 0x269EC3u);
        var converted = unchecked(((state >> 16) | 0x3F8000u) << 8);
        var probability = MathF.Abs(BitConverter.Int32BitsToSingle(unchecked((int)converted))) - 1f;
        return probability;
    }

    private static PopulationFurResult UnknownFur() =>
        new("unknown", "Unknown", 0f, false, "Unknown");

    private static string DifficultyName(int level) => level switch
    {
        1 => "Trivial",
        2 => "Minor",
        3 => "Very Easy",
        4 => "Easy",
        5 => "Medium",
        6 => "Hard",
        7 => "Very Hard",
        8 => "Mythical",
        9 => "Legendary",
        10 => "Fabled",
        _ => "Unknown"
    };

    private static string Humanize(string value) =>
        string.Join(
            ' ',
            value.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static CatalogData Load()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "population-animal-metadata.json");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Bundled population animal metadata was not found.",
                path);
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;

        var furNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.GetProperty("fur_names").EnumerateObject())
        {
            furNames[property.Name] = property.Value.GetString() ?? Humanize(property.Name);
        }

        var species = new Dictionary<string, PopulationSpeciesMetadata>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var speciesProperty in root.GetProperty("species").EnumerateObject())
        {
            var value = speciesProperty.Value;

            var levels = new List<(float Min, float Max)>();
            foreach (var level in value.GetProperty("level").EnumerateArray())
            {
                levels.Add((
                    level[0].GetSingle(),
                    level[1].GetSingle()));
            }

            var trophies = new Dictionary<string, (float Min, float Max)>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var trophyProperty in value.GetProperty("trophy").EnumerateObject())
            {
                trophies[trophyProperty.Name] = (
                    trophyProperty.Value.GetProperty("score_low").GetSingle(),
                    trophyProperty.Value.GetProperty("score_high").GetSingle());
            }

            var genders = new Dictionary<string, PopulationGenderMetadata>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var genderProperty in value.GetProperty("gender").EnumerateObject())
            {
                var gender = genderProperty.Value;
                var furTotal = gender.GetProperty("fur_total_probability").GetSingle();
                var furs = ReadFurs(gender.GetProperty("furs"));

                var seedFurs = gender.TryGetProperty("seed_furs", out var seedFursElement)
                    ? ReadFurs(seedFursElement)
                    : [];
                var seedTotal = gender.TryGetProperty(
                    "seed_fur_total_probability",
                    out var seedTotalElement)
                    ? seedTotalElement.GetSingle()
                    : furTotal;

                genders[genderProperty.Name] = new PopulationGenderMetadata(
                    furTotal,
                    furs,
                    seedTotal,
                    seedFurs);
            }

            species[speciesProperty.Name] = new PopulationSpeciesMetadata(
                levels,
                trophies,
                genders);
        }

        return new CatalogData(species, furNames);
    }

    private static IReadOnlyList<(string Key, float Weight)> ReadFurs(JsonElement element)
    {
        var furs = new List<(string Key, float Weight)>();
        foreach (var property in element.EnumerateObject())
        {
            furs.Add((property.Name, property.Value.GetSingle()));
        }

        return furs;
    }

    private sealed record CatalogData(
        IReadOnlyDictionary<string, PopulationSpeciesMetadata> Species,
        IReadOnlyDictionary<string, string> FurNames);
}
