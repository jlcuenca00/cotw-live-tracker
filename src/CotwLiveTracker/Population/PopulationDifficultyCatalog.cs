namespace CotwLiveTracker.Population;

internal sealed record DifficultyBand(
    float MinWeight,
    float MaxWeight,
    int Level,
    string Name)
{
    public string Label => $"{Level}-{Name}";
}

internal static class PopulationDifficultyCatalog
{
    private static readonly IReadOnlyDictionary<string, DifficultyBand[]> Bands =
        new Dictionary<string, DifficultyBand[]>(StringComparer.OrdinalIgnoreCase)
        {
        ["moose"] =
        [
            new(320f, 424.999f, 1, "Trivial"),
            new(425f, 473.999f, 2, "Minor"),
            new(474f, 535.499f, 3, "Very Easy"),
            new(535.5f, 593.099f, 4, "Easy"),
            new(593.1f, 620f, 5, "Medium")
        ],
        ["jackrabbit"] =
        [
            new(1.9f, 4.199f, 1, "Trivial"),
            new(4.2f, 6.299f, 2, "Minor"),
            new(6.3f, 6.83f, 3, "Very Easy")
        ],
        ["mallard"] =
        [
            new(0.72f, 1.359f, 1, "Trivial"),
            new(1.36f, 1.949f, 2, "Minor"),
            new(1.95f, 2.1f, 3, "Very Easy")
        ],
        ["wild_turkey"] =
        [
            new(3.6f, 8.099f, 1, "Trivial"),
            new(8.1f, 9.709f, 2, "Minor"),
            new(9.71f, 11f, 3, "Very Easy")
        ],
        ["black_bear"] =
        [
            new(40f, 66.999f, 1, "Trivial"),
            new(67f, 95.999f, 2, "Minor"),
            new(96f, 122.999f, 3, "Very Easy"),
            new(123f, 149.999f, 4, "Easy"),
            new(150f, 180.199f, 5, "Medium"),
            new(180.2f, 207.749f, 6, "Hard"),
            new(207.75f, 233.999f, 7, "Very Hard"),
            new(234f, 263.999f, 8, "Mythical"),
            new(264f, 290f, 9, "Legendary")
        ],
        ["roosevelt_elk"] =
        [
            new(260f, 319.999f, 1, "Trivial"),
            new(320f, 377.999f, 2, "Minor"),
            new(378f, 429.999f, 3, "Very Easy"),
            new(430f, 474.199f, 4, "Easy"),
            new(474.2f, 500f, 5, "Medium")
        ],
        ["coyote"] =
        [
            new(15f, 16.399f, 1, "Trivial"),
            new(16.4f, 17.699f, 2, "Minor"),
            new(17.7f, 18.999f, 3, "Very Easy"),
            new(19f, 20.299f, 4, "Easy"),
            new(20.3f, 21.599f, 5, "Medium"),
            new(21.6f, 22.999f, 6, "Hard"),
            new(23f, 24.399f, 7, "Very Hard"),
            new(24.4f, 25.499f, 8, "Mythical"),
            new(25.5f, 27f, 9, "Legendary")
        ],
        ["blacktail_deer"] =
        [
            new(40f, 51.999f, 1, "Trivial"),
            new(52f, 64.999f, 2, "Minor"),
            new(65f, 76.999f, 3, "Very Easy"),
            new(77f, 88.999f, 4, "Easy"),
            new(89f, 95f, 5, "Medium")
        ],
        ["whitetail_deer"] =
        [
            new(40f, 73.749f, 1, "Trivial"),
            new(73.75f, 94.939f, 2, "Minor"),
            new(94.94f, 100f, 3, "Very Easy")
        ]
        };

    public static DifficultyBand Get(string species, float weight)
    {
        if (!Bands.TryGetValue(species, out var bands))
        {
            return new DifficultyBand(float.NegativeInfinity, float.PositiveInfinity, 0, "Unknown");
        }

        foreach (var band in bands)
        {
            if (weight >= band.MinWeight && weight <= band.MaxWeight)
            {
                return band;
            }
        }

        if (bands.Length == 0)
        {
            return new DifficultyBand(float.NegativeInfinity, float.PositiveInfinity, 0, "Unknown");
        }

        // Population modifiers can produce weights slightly outside vanilla bands.
        // Clamp to the nearest supported difficulty rather than returning a false gap.
        return weight < bands[0].MinWeight ? bands[0] : bands[^1];
    }
}
