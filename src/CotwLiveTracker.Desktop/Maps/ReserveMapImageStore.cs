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

        File.WriteAllText(
            SourcePath(map.ReserveIndex),
            map.Source);

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
        File.WriteAllText(
            SourcePath(reserveIndex),
            "manual-image");
        return destination;
    }

    public static string? ReadSource(int reserveIndex)
    {
        var sourcePath = SourcePath(reserveIndex);
        return File.Exists(sourcePath)
            ? File.ReadAllText(sourcePath).Trim()
            : null;
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

        var sourcePath = SourcePath(reserveIndex);
        if (File.Exists(sourcePath))
        {
            File.Delete(sourcePath);
        }
    }

    private static string SourcePath(int reserveIndex) =>
        Path.Combine(
            MapsDirectory,
            $"reserve-{reserveIndex}.source.txt");

    public static ImageSource Load(
        string path,
        ReserveMapCalibration? calibration = null,
        bool applyCalibrationCrop = false)
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

        if (!applyCalibrationCrop ||
            calibration is null ||
            calibration.ImageCropWidth >= 0.999999d &&
            calibration.ImageCropHeight >= 0.999999d)
        {
            return image;
        }

        var x = (int)Math.Round(
            calibration.ImageCropLeft * image.PixelWidth);
        var y = (int)Math.Round(
            calibration.ImageCropTop * image.PixelHeight);
        var width = (int)Math.Round(
            calibration.ImageCropWidth * image.PixelWidth);
        var height = (int)Math.Round(
            calibration.ImageCropHeight * image.PixelHeight);

        x = Math.Clamp(x, 0, Math.Max(0, image.PixelWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, image.PixelHeight - 1));
        width = Math.Clamp(
            width,
            1,
            image.PixelWidth - x);
        height = Math.Clamp(
            height,
            1,
            image.PixelHeight - y);

        var cropped = new CroppedBitmap(
            image,
            new System.Windows.Int32Rect(
                x,
                y,
                width,
                height));
        cropped.Freeze();
        return cropped;
    }
}
