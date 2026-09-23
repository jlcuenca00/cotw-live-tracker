using System.Windows;

namespace CotwLiveTracker.Desktop.Maps;

internal sealed record ReserveMapReferencePoint(
    string Name,
    double WorldX,
    double WorldZ,
    double U,
    double V)
{
    public Point ExpectedNormalized => new(U, V);

    public double DistanceTo(double x, double z)
    {
        var dx = WorldX - x;
        var dz = WorldZ - z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }
}

internal static class ReserveMapReferenceCatalog
{
    // Layton outposts: published world coordinates paired with the normalized
    // positions used by the COTW Companion raw Layton tile set.
    //
    // These are deliberately stored independently of ReserveMapCalibration so
    // they can be drawn as ground-truth validation markers.
    private static readonly ReserveMapReferencePoint[] Layton =
    [
        new("Balmont Railroad Outpost", 9557d, 10760d, 0.5079d, 0.8230d),
        new("Roonachee Western Outpost", 6004.883d, 10737.651d, 0.0997d, 0.8206d),
        new("Balmont Outpost", 9919.226d, 10264.726d, 0.5495d, 0.7660d),
        new("Roonachee Outpost", 7416.802d, 10160.904d, 0.2622d, 0.7541d),
        new("Mount Leviathan Outpost", 11228.529d, 10107.859d, 0.6998d, 0.7482d),
        new("Cheelah Southern Outpost", 12400.923d, 9051.162d, 0.8346d, 0.6268d),
        new("Balmont Northern Outpost", 8689.073d, 9039.834d, 0.4082d, 0.6254d),
        new("Cheelah Outpost", 10810.920d, 8337.025d, 0.6520d, 0.5446d),
        new("Mount Kraken Outpost", 7393.195d, 7962.083d, 0.2595d, 0.5017d),
        new("Norden Outpost", 11629.449d, 7719.431d, 0.7459d, 0.4740d),
        new("Highlake Southern Outpost", 9270.795d, 7636.432d, 0.4749d, 0.4644d),
        new("Norden Eastern Outpost", 12815.403d, 7067.663d, 0.8821d, 0.3988d),
        new("Willipeg Southern Outpost", 6666.631d, 6600.873d, 0.1760d, 0.3452d),
        new("High Lake Outpost", 8874.484d, 6168.897d, 0.4296d, 0.2960d),
        new("Calburn Outpost", 10956.351d, 5642.998d, 0.6686d, 0.2351d),
        new("Willipeg Outpost", 6722.713d, 5208.859d, 0.1823d, 0.1857d),
        new("Chopeeka Outpost", 8867.024d, 4421.943d, 0.4288d, 0.0953d),
        new("Norden Northern Outpost", 12650.683d, 4185.815d, 0.8631d, 0.0680d),
    ];

    public static IReadOnlyList<ReserveMapReferencePoint> Get(
        int reserveIndex) =>
        reserveIndex == 1
            ? Layton
            : Array.Empty<ReserveMapReferencePoint>();
}
