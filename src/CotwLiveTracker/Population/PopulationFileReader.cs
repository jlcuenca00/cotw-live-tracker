using System.IO.Compression;

namespace CotwLiveTracker.Population;

internal sealed record PopulationAnimalRecord(
    string Species,
    int SpeciesIndex,
    int GroupIndex,
    int AnimalIndex,
    string Gender,
    float Weight,
    float Score,
    int DifficultyLevel,
    string DifficultyLabel,
    bool IsGreatOne,
    bool IsScripted,
    uint VisualVariationSeed,
    uint Id,
    float MapX,
    float MapY);

internal sealed record PopulationGroupRecord(
    string Species,
    int SpeciesIndex,
    int GroupIndex,
    IReadOnlyList<PopulationAnimalRecord> Animals);

internal sealed record PopulationSpeciesRecord(
    string Species,
    int SpeciesIndex,
    IReadOnlyList<PopulationGroupRecord> Groups)
{
    public IReadOnlyList<PopulationAnimalRecord> Animals =>
        Groups.SelectMany(group => group.Animals).ToArray();
}

internal sealed record PopulationReadResult(
    string FilePath,
    int FileSizeBytes,
    int AdfPayloadSizeBytes,
    uint AdfVersion,
    string ReserveName,
    IReadOnlyList<PopulationSpeciesRecord> Species)
{
    public IReadOnlyList<PopulationAnimalRecord> Animals =>
        Species.SelectMany(species => species.Animals).ToArray();
}

internal static class PopulationFileReader
{
    private const int FileHeaderLength = 32;
    private const int CompressionHeaderLength = 5;

    public static PopulationReadResult Read(
        string path,
        ReservePopulationDefinition reserve)
    {
        ArgumentNullException.ThrowIfNull(reserve);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Population file path is required.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var fileBytes = File.ReadAllBytes(fullPath);
        var payload = ExtractAdfPayload(fileBytes);
        var document = ApexAdfReader.Read(payload);
        var species = ParsePopulations(document, reserve);

        return new PopulationReadResult(
            fullPath,
            fileBytes.Length,
            payload.Length,
            document.Version,
            reserve.DisplayName,
            species);
    }

    internal static byte[] ExtractAdfPayload(byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);

        if (fileBytes.Length <= FileHeaderLength)
        {
            throw new InvalidDataException("Population file is too small to contain the COTW compressed ADF payload.");
        }

        using var compressed = new MemoryStream(
            fileBytes,
            FileHeaderLength,
            fileBytes.Length - FileHeaderLength,
            writable: false);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);

        var decompressed = output.ToArray();
        if (decompressed.Length <= CompressionHeaderLength)
        {
            throw new InvalidDataException("Decompressed population data is missing the ADF payload.");
        }

        return decompressed[CompressionHeaderLength..];
    }

    internal static IReadOnlyList<PopulationSpeciesRecord> ParsePopulations(
        AdfDocument document,
        ReservePopulationDefinition reserve)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(reserve);

        var populationsNode = document.RootValues
            .Select(root => FindField(root, "Populations"))
            .FirstOrDefault(node => node is not null)
            ?? throw new InvalidDataException("ADF population root does not contain a 'Populations' field.");

        var populations = populationsNode.Items;
        var result = new List<PopulationSpeciesRecord>(populations.Count);

        for (var speciesIndex = 0; speciesIndex < populations.Count; speciesIndex++)
        {
            var speciesKey = speciesIndex < reserve.Species.Count
                ? reserve.Species[speciesIndex]
                : $"unknown_species_{speciesIndex}";

            var population = populations[speciesIndex];
            if (!population.TryGetField("Groups", out var groupsNode) || groupsNode is null)
            {
                result.Add(new PopulationSpeciesRecord(speciesKey, speciesIndex, []));
                continue;
            }

            var groups = new List<PopulationGroupRecord>(groupsNode.Items.Count);
            for (var groupIndex = 0; groupIndex < groupsNode.Items.Count; groupIndex++)
            {
                var groupNode = groupsNode.Items[groupIndex];
                if (!groupNode.TryGetField("Animals", out var animalsNode) || animalsNode is null)
                {
                    groups.Add(new PopulationGroupRecord(speciesKey, speciesIndex, groupIndex, []));
                    continue;
                }

                var animals = new List<PopulationAnimalRecord>(animalsNode.Items.Count);
                for (var animalIndex = 0; animalIndex < animalsNode.Items.Count; animalIndex++)
                {
                    animals.Add(ParseAnimal(
                        animalsNode.Items[animalIndex],
                        speciesKey,
                        speciesIndex,
                        groupIndex,
                        animalIndex));
                }

                groups.Add(new PopulationGroupRecord(speciesKey, speciesIndex, groupIndex, animals));
            }

            result.Add(new PopulationSpeciesRecord(speciesKey, speciesIndex, groups));
        }

        return result;
    }

    private static PopulationAnimalRecord ParseAnimal(
        AdfNode node,
        string species,
        int speciesIndex,
        int groupIndex,
        int animalIndex)
    {
        var genderValue = RequiredUInt32(node, "Gender");
        var gender = genderValue switch
        {
            1 => "male",
            2 => "female",
            _ => $"unknown({genderValue})"
        };

        var weight = RequiredSingle(node, "Weight");
        var score = RequiredSingle(node, "Score");
        var seed = RequiredUInt32(node, "VisualVariationSeed");
        var id = RequiredUInt32(node, "Id");

        var greatOne = TryUInt32(node, "IsGreatOne", out var directGreatOne)
            ? directGreatOne == 1
            : TryNestedUInt32(node, "FeatureModifiers", "Flags", out var flags) && flags == 1;

        var scripted = TryUInt32(node, "IsScripted", out var scriptedValue) && scriptedValue == 1;

        var mapX = 0f;
        var mapY = 0f;
        if (node.TryGetField("MapPosition", out var mapPosition) && mapPosition is not null)
        {
            _ = TrySingle(mapPosition, "X", out mapX);
            _ = TrySingle(mapPosition, "Y", out mapY);
        }

        if (!float.IsFinite(weight) || weight < 0f ||
            !float.IsFinite(score) || score < 0f ||
            !float.IsFinite(mapX) ||
            !float.IsFinite(mapY))
        {
            throw new InvalidDataException(
                $"Invalid animal values in {species} group {groupIndex}, animal {animalIndex}.");
        }

        var difficulty = PopulationDifficultyCatalog.Get(species, weight);

        return new PopulationAnimalRecord(
            species,
            speciesIndex,
            groupIndex,
            animalIndex,
            gender,
            weight,
            score,
            difficulty.Level,
            difficulty.Label,
            greatOne,
            scripted,
            seed,
            id,
            mapX,
            mapY);
    }

    private static AdfNode? FindField(AdfNode node, string fieldName)
    {
        if (node.TryGetField(fieldName, out var direct) && direct is not null)
        {
            return direct;
        }

        if (node.Value is IReadOnlyDictionary<string, AdfNode> fields)
        {
            foreach (var child in fields.Values)
            {
                var found = FindField(child, fieldName);
                if (found is not null)
                {
                    return found;
                }
            }
        }
        else if (node.Value is IReadOnlyList<AdfNode> items)
        {
            foreach (var child in items)
            {
                var found = FindField(child, fieldName);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static uint RequiredUInt32(AdfNode node, string fieldName)
    {
        if (!node.TryGetField(fieldName, out var field) || field is null)
        {
            throw new InvalidDataException($"Animal record is missing '{fieldName}'.");
        }

        return field.AsUInt32();
    }

    private static float RequiredSingle(AdfNode node, string fieldName)
    {
        if (!node.TryGetField(fieldName, out var field) || field is null)
        {
            throw new InvalidDataException($"Animal record is missing '{fieldName}'.");
        }

        return field.AsSingle();
    }

    private static bool TryUInt32(AdfNode node, string fieldName, out uint value)
    {
        value = 0;
        if (!node.TryGetField(fieldName, out var field) || field is null)
        {
            return false;
        }

        value = field.AsUInt32();
        return true;
    }

    private static bool TryNestedUInt32(
        AdfNode node,
        string parentField,
        string childField,
        out uint value)
    {
        value = 0;
        return node.TryGetField(parentField, out var parent) &&
               parent is not null &&
               TryUInt32(parent, childField, out value);
    }

    private static bool TrySingle(AdfNode node, string fieldName, out float value)
    {
        value = 0f;
        if (!node.TryGetField(fieldName, out var field) || field is null)
        {
            return false;
        }

        value = field.AsSingle();
        return true;
    }
}
