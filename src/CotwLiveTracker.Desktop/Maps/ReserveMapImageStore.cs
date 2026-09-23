using System.IO;
using System.Windows.Media;
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

    public static string SaveGenerated(ExtractedReserveMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        Directory.CreateDirectory(MapsDirectory);
        DeleteExisting(map.ReserveIndex);

        var destination = Path.Combine(
            MapsDirectory,
            $"reserve-{map.ReserveIndex}.png");

        var bitmap = BitmapSource.Create(
            map.Width,
            map.Height,
            96d,
            96d,
            PixelFormats.Bgra32,
            null,
            map.Bgra32,
            map.Stride);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var output = File.Open(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        encoder.Save(output);

        return destination;
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
        DeleteExisting(reserveIndex);

        var destination = Path.Combine(
            MapsDirectory,
            $"reserve-{reserveIndex}{extension}");
        File.Copy(sourcePath, destination, overwrite: true);
        return destination;
    }

    private static void DeleteExisting(int reserveIndex)
    {
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
