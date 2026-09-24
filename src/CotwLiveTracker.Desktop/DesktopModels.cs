using CotwLiveTracker.Memory;
using CotwLiveTracker.Population;

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
    public string GroupText => GroupIndex is null ? "—" : $"G{GroupIndex}";
    public string IdentityText => GroupIndex is null || AnimalIndex is null
        ? "Unresolved"
        : $"G{GroupIndex} #{AnimalIndex}";
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
