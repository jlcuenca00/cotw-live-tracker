namespace CotwLiveTracker.Population;

internal sealed record PopulationFilterOptions(
    string? Trophy = null,
    bool RareOnly = false,
    bool GreatOneOnly = false,
    int? Difficulty = null,
    string? Fur = null,
    string? Sex = null,
    bool ShowAll = false)
{
    public bool HasAnimalFilters =>
        !string.IsNullOrWhiteSpace(Trophy) ||
        RareOnly ||
        GreatOneOnly ||
        Difficulty is not null ||
        !string.IsNullOrWhiteSpace(Fur) ||
        !string.IsNullOrWhiteSpace(Sex);

    public bool Matches(PopulationAnimalRecord animal)
    {
        ArgumentNullException.ThrowIfNull(animal);

        if (!string.IsNullOrWhiteSpace(Trophy) &&
            !string.Equals(
                NormalizeTrophy(animal.Trophy),
                NormalizeTrophy(Trophy),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (RareOnly && !animal.IsRareFur)
        {
            return false;
        }

        if (GreatOneOnly && !animal.IsGreatOne)
        {
            return false;
        }

        if (Difficulty is not null && animal.DifficultyLevel != Difficulty.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Fur))
        {
            var normalizedFilter = NormalizeToken(Fur);
            var matchesKey = NormalizeToken(animal.FurKey)
                .Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase);
            var matchesName = NormalizeToken(animal.FurName)
                .Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase);
            if (!matchesKey && !matchesName)
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(Sex) &&
            !string.Equals(animal.Gender, Sex, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public string Describe()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(Trophy))
        {
            parts.Add($"trophy={Trophy}");
        }

        if (RareOnly)
        {
            parts.Add("rare fur");
        }

        if (GreatOneOnly)
        {
            parts.Add("Great One");
        }

        if (Difficulty is not null)
        {
            parts.Add($"difficulty={Difficulty}");
        }

        if (!string.IsNullOrWhiteSpace(Fur))
        {
            parts.Add($"fur={Fur}");
        }

        if (!string.IsNullOrWhiteSpace(Sex))
        {
            parts.Add($"sex={Sex}");
        }

        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    private static string NormalizeTrophy(string value) =>
        NormalizeToken(value).Replace("great one", "great-one", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeToken(string value) =>
        string.Join(
            ' ',
            value.Trim()
                .Replace('_', ' ')
                .Replace('-', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .ToLowerInvariant();
}
