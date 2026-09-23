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
}

internal static class ReserveMapCalibrationCatalog
{
    // Layton calibration solved against all 18 published outpost world
    // coordinates and their raw map_reserve_1 normalized positions.
    // RMSE is ~0.00013 normalized map units; cross-axis terms are retained.
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
