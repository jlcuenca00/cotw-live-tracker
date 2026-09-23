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
    // Layton is NOT a 0..16400 -> full-image map.
    //
    // The raw zoom-3 tile sheet uses its own map projection.  These
    // coefficients are the least-squares solution from all 18 known Layton
    // outposts: game world X/Z -> the raw map_reserve_1 normalized position.
    //
    // This is independently checkable at Willipeg Southern Outpost:
    // world (6666.631, 6600.873) -> UV approximately (0.1760, 0.3452).
    public static ReserveMapCalibration Layton { get; } = new(
        ReserveIndex: 1,
        ReserveName: "Layton Lake District",
        UX: 0.000114853521,
        UZ: -0.00000000751486842,
        U0: -0.589698550,
        VX: -0.0000000153992234,
        VZ: 0.000114831546,
        V0: -0.412461871);

    public static ReserveMapCalibration? Get(int reserveIndex) =>
        reserveIndex == Layton.ReserveIndex
            ? Layton
            : null;
}
