using System.IO;
using System.Windows.Media.Imaging;

namespace CotwLiveTracker.Desktop.Maps;

internal static class ReserveMapImageStore
{
    private static readonly string MapsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CotwLiveTracker",
        "maps");

    private static readonly string[] Extensions =
        [".png", ".jpg", ".jpeg", ".bmp"];

    public static string? Find(int reserveIndex)
    {
        foreach (var extension in Extensions)
        {
            var path = Path.Combine(MapsDirectory, $"reserve-{reserveIndex}{extension}");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public static string Import(int reserveIndex, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Map image must be PNG, JPG, JPEG, or BMP.");
        }

        Directory.CreateDirectory(MapsDirectory);

        foreach (var oldExtension in Extensions)
        {
            var oldPath = Path.Combine(
                MapsDirectory,
                $"reserve-{reserveIndex}{oldExtension}");
            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }
        }

        var destination = Path.Combine(
            MapsDirectory,
            $"reserve-{reserveIndex}{extension}");
        File.Copy(sourcePath, destination, overwrite: true);
        return destination;
    }

    public static BitmapImage Load(string path)
    {
        using var stream = File.Open(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
