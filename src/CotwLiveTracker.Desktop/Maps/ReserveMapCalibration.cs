using System.Windows;

namespace CotwLiveTracker.Desktop.Maps;

public sealed record ReserveMapCalibration(
    int ReserveIndex,
    string ReserveName,
    double UX,
    double UZ,
    double U0,
    double VX,
    double VZ,
    double V0)
{
    public Point WorldToNormalized(double x, double z) =>
        new(
            (UX * x) + (UZ * z) + U0,
            (VX * x) + (VZ * z) + V0);

    public bool IsInside(double x, double z, double margin = 0.05)
    {
        var point = WorldToNormalized(x, z);
        return point.X >= -margin &&
               point.X <= 1d + margin &&
               point.Y >= -margin &&
               point.Y <= 1d + margin;
    }

    public static ReserveMapCalibration FromWorldBounds(
        int reserveIndex,
        string reserveName,
        double xA,
        double xB,
        double zA,
        double zB)
    {
        var xMin = Math.Min(xA, xB);
        var xMax = Math.Max(xA, xB);
        var zMin = Math.Min(zA, zB);
        var zMax = Math.Max(zA, zB);

        var xSpan = xMax - xMin;
        var zSpan = zMax - zMin;
        if (xSpan <= 0d || zSpan <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(xA),
                "Reserve world bounds must have positive X and Z spans.");
        }

        // The raw COTW/DECA full map image is the reserve's complete map
        // extent. World positions therefore normalize directly into that
        // full image rather than into a smaller hand-fit landmark region.
        return new ReserveMapCalibration(
            reserveIndex,
            reserveName,
            UX: 1d / xSpan,
            UZ: 0d,
            U0: -xMin / xSpan,
            VX: 0d,
            VZ: 1d / zSpan,
            V0: -zMin / zSpan);
    }
}

internal static class ReserveMapCalibrationCatalog
{
    // Verified against the same full DECA/COTW reserve-1 map geometry used
    // by need-zone mapping tools: Layton occupies world X 0..16400 m and
    // world Z 0..16400 m across the entire stitched map texture.
    //
    // The previous outpost-derived fit compressed the world to ~8.7 km and
    // therefore pushed live runtime positions toward/outside the western map
    // boundary. Runtime X/Z are already in the map's meter coordinate system;
    // they should be normalized against the full 16.4 km reserve extent.
    public static ReserveMapCalibration Layton { get; } =
        ReserveMapCalibration.FromWorldBounds(
            reserveIndex: 1,
            reserveName: "Layton Lake District",
            xA: 0d,
            xB: 16400d,
            zA: 0d,
            zB: 16400d);

    public static ReserveMapCalibration? Get(int reserveIndex) =>
        reserveIndex == Layton.ReserveIndex
            ? Layton
            : null;
}
