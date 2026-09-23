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
    public static DifficultyBand Get(
        string species,
        float weight,
        bool isGreatOne = false) =>
        PopulationAnimalMetadataCatalog.GetDifficulty(species, weight, isGreatOne);
}
