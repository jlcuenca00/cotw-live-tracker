namespace CotwLiveTracker.Population;

internal sealed record ReservePopulationDefinition(
    string Key,
    string DisplayName,
    IReadOnlyList<string> Species);

internal static class PopulationReserveCatalog
{
    private static readonly IReadOnlyDictionary<int, ReservePopulationDefinition> Reserves =
        new Dictionary<int, ReservePopulationDefinition>
        {
        [0] = new("hirsch", "Hirschfelden Hunting Reserve", ["wild_boar", "eu_rabbit", "fallow_deer", "eu_bison", "roe_deer", "red_fox", "pheasant", "canada_goose", "red_deer"]),
        [1] = new("layton", "Layton Lake District", ["moose", "jackrabbit", "mallard", "wild_turkey", "black_bear", "roosevelt_elk", "coyote", "blacktail_deer", "whitetail_deer"]),
        [2] = new("medved", "Medved-Taiga National Park", ["siberian_musk_deer", "moose", "wild_boar", "reindeer", "eurasian_lynx", "eurasian_brown_bear", "western_capercaillie", "gray_wolf"]),
        [3] = new("vurhonga", "Vurhonga Savanna", ["eurasian_wigeon", "blue_wildebeest", "sidestriped_jackal", "gemsbok", "lesser_kudu", "scrub_hare", "lion", "warthog", "cape_buffalo", "springbok"]),
        [4] = new("parque", "Parque Fernando", ["red_deer", "water_buffalo", "puma", "blackbuck", "cinnamon_teal", "collared_peccary", "mule_deer", "axis_deer"]),
        [6] = new("yukon", "Yukon Valley Nature Reserve", ["harlequin_duck", "moose", "red_fox", "caribou", "canada_goose", "grizzly_bear", "gray_wolf", "plains_bison"]),
        [8] = new("cuatro", "Cuatro Colinas Game Reserve", ["southeastern_ibex", "iberian_wolf", "red_deer", "iberian_mouflon", "wild_boar", "beceite_ibex", "eu_hare", "roe_deer", "ronda_ibex", "pheasant", "gredos_ibex"]),
        [9] = new("silver", "Silver Ridge Peaks", ["prong_horn", "puma", "mountain_goat", "bighorn_sheep", "wild_turkey", "black_bear", "mule_deer", "rockymountain_elk", "blacktail_deer", "plains_bison"]),
        [10] = new("teawaroa", "Te Awaroa National Park", ["moose", "red_deer", "eu_rabbit", "feral_pig", "fallow_deer", "chamois", "mallard", "wild_turkey", "sika_deer", "feral_goat", "tahr"]),
        [11] = new("rancho", "Rancho del Arroyo", ["mexican_bobcat", "rio_grande_turkey", "prong_horn", "desert_bighorn_sheep", "collared_peccary", "antelope_jackrabbit", "mule_deer", "coyote", "pheasant", "whitetail_deer"]),
        [12] = new("mississippi", "Mississippi Acres Preserve", ["feral_pig", "raccoon", "eastern_cottontail_rabbit", "northern_bobwhite_quail", "eastern_wild_turkey", "gray_fox", "black_bear", "american_alligator", "green_wing_teal", "whitetail_deer"]),
        [13] = new("revontuli", "Revontuli Coast", ["mallard", "rock_ptarmigan", "eurasian_wigeon", "moose", "goldeneye", "mountain_hare", "tufted_duck", "black_grouse", "tundra_bean_goose", "willow_ptarmigan", "eurasian_lynx", "hazel_grouse", "eurasian_brown_bear", "eurasian_teal", "western_capercaillie", "canada_goose", "greylag_goose", "whitetail_deer", "raccoon_dog"]),
        [14] = new("newengland", "New England Mountains", ["mallard", "moose", "goldeneye", "raccoon", "eastern_cottontail_rabbit", "northern_bobwhite_quail", "eastern_wild_turkey", "gray_fox", "red_fox", "black_bear", "bobcat", "coyote", "pheasant", "green_wing_teal", "whitetail_deer"]),
        [16] = new("emerald", "Emerald Coast", ["hog_deer", "fallow_deer", "red_deer", "feral_pig", "magpie_goose", "eastern_grey_kangaroo", "red_fox", "sambar", "banteng", "saltwater_crocodile", "feral_goat", "stubble_quail", "axis_deer", "javan_rusa"]),
        [17] = new("sundarpatan", "Sundarpatan Nepal Hunting Reserve", ["blue_sheep", "water_buffalo", "snow_leopard", "blackbuck", "nilgai", "tibetan_fox", "wild_yak", "northern_red_muntjac", "barasingha", "woolly_hare", "bengal_tiger", "greylag_goose", "tahr"]),
        [18] = new("salzwiesen", "Salzwiesen Park", ["mallard", "eurasian_wigeon", "goldeneye", "eu_rabbit", "raccoon", "tufted_duck", "black_grouse", "tundra_bean_goose", "red_fox", "eurasian_teal", "gadwall", "pheasant", "greylag_goose", "ferruginous_duck", "raccoon_dog"]),
        [19] = new("alberta", "Askiy Ridge Hunting Preserve", ["mallard", "manitoban_elk", "moose", "woodland_caribou", "prong_horn", "wood_bison", "wood_duck", "northern_pintail", "mountain_goat", "bighorn_sheep", "black_bear", "mule_deer", "snow_goose", "pheasant", "canada_goose", "gray_wolf", "dusky_grouse", "whitetail_deer", "north_american_beaver"]),
        [20] = new("scotland", "Tórr nan Sìthean Hunting Reserve", ["eurasian_wigeon", "eurasian_woodcock", "fallow_deer", "red_deer", "mountain_hare", "black_grouse", "wild_boar", "wild_haggis", "red_fox", "eurasian_pine_marten", "western_capercaillie", "american_mink", "roe_deer", "european_badger", "sika_deer", "feral_goat", "pheasant", "red_grouse"]),
        [21] = new("peru", "Intisuyu Peru Hunting Reserve", ["taruca", "western_mountain_coati", "puma", "ocelot", "cinnamon_teal", "collared_peccary", "vicuna", "spectacled_bear", "jaguar", "south_american_tapir", "black_caiman", "whitetail_deer", "capybara", "greater_grison"])
        };

    public static IReadOnlyList<(int Index, ReservePopulationDefinition Reserve)> All =>
        Reserves
            .OrderBy(item => item.Key)
            .Select(item => (item.Key, item.Value))
            .ToArray();

    public static ReservePopulationDefinition Get(int reserveIndex) =>
        Reserves.TryGetValue(reserveIndex, out var reserve)
            ? reserve
            : throw new KeyNotFoundException($"No population species mapping is available for reserve index {reserveIndex}.");

    public static bool TryGet(int reserveIndex, out ReservePopulationDefinition? reserve) =>
        Reserves.TryGetValue(reserveIndex, out reserve);
}
