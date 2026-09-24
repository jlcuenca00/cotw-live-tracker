using CotwLiveTracker.Memory;
using CotwLiveTracker.Population;
using System.Windows.Media;

namespace CotwLiveTracker.Desktop;

internal sealed record ReserveChoice(int Index, string Name)
{
    public override string ToString() => Name;
}

public sealed record LiveAnimalView(
    string Species,
    string Gender,
    string Difficulty,
    string Trophy,
    string Fur,
    string FurRarity,
    float FurProbability,
    string NextTrophyText,
    bool IsRare,
    bool IsGreatOne,
    float DistanceMeters,
    float Health,
    float MaxHealth,
    float Weight,
    float Score,
    uint VisualVariationSeed,
    int? GroupIndex,
    int? AnimalIndex,
    float X,
    float Y,
    float Z,
    float RelativeX,
    float RelativeZ,
    nint Address)
{
    public string DisplaySpecies => Species.Replace('_', ' ');
    public string DistanceText => $"{DistanceMeters:F0} m";
    public string HealthText => $"{Health:F0}/{MaxHealth:F0}";
    public string WeightText => $"{Weight:F2}";
    public string ScoreText => $"{Score:F2}";
    public string FurProbabilityText => FurProbability > 0f ? $"{FurProbability * 100f:F3}%" : "—";
    public Brush AccentBrush
    {
        get
        {
            var level = DifficultyLevel(Difficulty);

            if (IsGreatOne || level >= 10)
            {
                return new SolidColorBrush(Color.FromRgb(220, 90, 81));
            }

            if (string.Equals(Trophy, "Diamond", StringComparison.OrdinalIgnoreCase) ||
                level >= 9)
            {
                return new SolidColorBrush(Color.FromRgb(155, 101, 213));
            }

            if (string.Equals(Trophy, "Gold", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(Color.FromRgb(214, 164, 61));
            }

            if (string.Equals(Trophy, "Silver", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(Color.FromRgb(90, 140, 200));
            }

            if (IsRare)
            {
                return new SolidColorBrush(Color.FromRgb(58, 184, 176));
            }

            return new SolidColorBrush(Color.FromRgb(144, 150, 138));
        }
    }

    public string GroupText => GroupIndex is null ? "—" : $"G{GroupIndex}";
    public string IdentityText => GroupIndex is null || AnimalIndex is null
        ? "Unresolved"
        : $"G{GroupIndex} #{AnimalIndex}";

    private static int DifficultyLevel(string value)
    {
        var separator = value.IndexOf('-');
        var level = separator >= 0 ? value[..separator] : value;

        return int.TryParse(
            level.Trim().TrimStart('~'),
            out var parsed)
            ? parsed
            : 0;
    }
}

internal sealed record DesktopSnapshot(
    Float3 CameraPosition,
    IReadOnlyList<LiveAnimalView> Animals,
    int SkippedAnimals);

internal sealed record PopulationAnimalView(PopulationAnimalRecord Record)
{
    public string Species => Record.Species.Replace('_', ' ');
    public string Group => $"G{Record.GroupIndex}";
    public int Slot => Record.AnimalIndex;
    public string Gender => Record.Gender;
    public string Difficulty => Record.IsGreatOne
        ? Record.DifficultyLabel
        : $"~{Record.DifficultyLabel}";
    public string Trophy => Record.Trophy;
    public string Fur => Record.FurName;
    public string Rarity => Record.FurRarity;
    public float Weight => Record.Weight;
    public float Score => Record.Score;
    public uint Seed => Record.VisualVariationSeed;
}
