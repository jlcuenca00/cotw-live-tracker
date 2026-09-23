using System.Windows;

namespace CotwLiveTracker.Desktop.Maps;

internal sealed record ReserveMapCalibration(
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
    // Layton calibration derived from ten independent outpost correspondences:
    // COTW world X/Z coordinates -> normalized map locations.
    // Cross-axis terms are intentionally retained rather than forcing a zero-rotation fit.
    public static ReserveMapCalibration Layton { get; } = new(
        ReserveIndex: 1,
        ReserveName: "Layton Lake District",
        UX: 0.000114888268968409,
        UZ: -0.0000000459197071221501,
        U0: -0.589623002016760,
        VX: 0.000000306147534157835,
        VZ: 0.000114541294189928,
        V0: -0.412792441489212);

    public static ReserveMapCalibration? Get(int reserveIndex) =>
        reserveIndex == Layton.ReserveIndex
            ? Layton
            : null;
}
