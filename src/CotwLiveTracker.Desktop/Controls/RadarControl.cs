using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CotwLiveTracker.Desktop.Maps;

namespace CotwLiveTracker.Desktop.Controls;

public sealed class RadarControl : FrameworkElement
{
    private static readonly Brush BackgroundBrush =
        new SolidColorBrush(Color.FromRgb(9, 13, 18));
    private static readonly Brush GridBrush =
        new SolidColorBrush(Color.FromRgb(51, 65, 85));
    private static readonly Brush TextBrush =
        new SolidColorBrush(Color.FromRgb(148, 163, 184));
    private static readonly Brush PlayerBrush =
        new SolidColorBrush(Color.FromRgb(103, 232, 249));
    private static readonly Brush NormalBrush =
        new SolidColorBrush(Color.FromRgb(226, 232, 240));
    private static readonly Brush GoldBrush =
        new SolidColorBrush(Color.FromRgb(251, 191, 36));
    private static readonly Brush DiamondBrush =
        new SolidColorBrush(Color.FromRgb(96, 165, 250));
    private static readonly Brush RareBrush =
        new SolidColorBrush(Color.FromRgb(34, 211, 238));
    private static readonly Brush GreatOneBrush =
        new SolidColorBrush(Color.FromRgb(244, 114, 182));

    private readonly List<(Point Point, LiveAnimalView Animal)> _markers = [];
    private IReadOnlyList<LiveAnimalView> _animals = [];
    private LiveAnimalView? _selectedAnimal;
    private double _maxRangeMeters = 1000d;
    private ImageSource? _mapImage;
    private ReserveMapCalibration? _mapCalibration;
    private double _cameraX;
    private double _cameraZ;

    public Action<LiveAnimalView>? AnimalSelected { get; set; }

    public IReadOnlyList<LiveAnimalView> Animals
    {
        get => _animals;
        set
        {
            _animals = value ?? [];
            InvalidateVisual();
        }
    }

    public double MaxRangeMeters
    {
        get => _maxRangeMeters;
        set
        {
            _maxRangeMeters = Math.Clamp(value, 100d, 2000d);
            InvalidateVisual();
        }
    }

    public ImageSource? MapImage
    {
        get => _mapImage;
        set
        {
            _mapImage = value;
            InvalidateVisual();
        }
    }

    public ReserveMapCalibration? MapCalibration
    {
        get => _mapCalibration;
        set
        {
            _mapCalibration = value;
            InvalidateVisual();
        }
    }

    public bool IsMapMode => MapImage is not null && MapCalibration is not null;

    public double CameraX
    {
        get => _cameraX;
        set
        {
            _cameraX = value;
            InvalidateVisual();
        }
    }

    public double CameraZ
    {
        get => _cameraZ;
        set
        {
            _cameraZ = value;
            InvalidateVisual();
        }
    }

    public LiveAnimalView? SelectedAnimal
    {
        get => _selectedAnimal;
        set
        {
            _selectedAnimal = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0d || height <= 0d)
        {
            return;
        }

        drawingContext.DrawRectangle(
            BackgroundBrush,
            null,
            new Rect(0d, 0d, width, height));

        _markers.Clear();

        if (IsMapMode)
        {
            RenderMap(drawingContext, width, height);
        }
        else
        {
            RenderRelativeRadar(drawingContext, width, height);
        }
    }

    private void RenderMap(
        DrawingContext drawingContext,
        double width,
        double height)
    {
        var image = MapImage!;
        var calibration = MapCalibration!;
        var mapRect = FitImageRect(
            image.Width,
            image.Height,
            width,
            height,
            10d);

        drawingContext.DrawImage(image, mapRect);

        // Darken the artwork slightly so live markers remain readable.
        drawingContext.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(36, 0, 0, 0)),
            null,
            mapRect);

        var playerNormalized = calibration.WorldToNormalized(
            CameraX,
            CameraZ);
        var player = NormalizedToPoint(playerNormalized, mapRect);

        if (ContainsWithMargin(playerNormalized, 0.05d))
        {
            drawingContext.DrawEllipse(
                new SolidColorBrush(Color.FromArgb(72, 103, 232, 249)),
                new Pen(PlayerBrush, 2d),
                player,
                10d,
                10d);
            drawingContext.DrawEllipse(
                PlayerBrush,
                null,
                player,
                4d,
                4d);
        }

        foreach (var animal in Animals)
        {
            if (animal.DistanceMeters > MaxRangeMeters)
            {
                continue;
            }

            var normalized = calibration.WorldToNormalized(
                animal.X,
                animal.Z);
            if (!ContainsWithMargin(normalized, 0.02d))
            {
                continue;
            }

            var point = NormalizedToPoint(normalized, mapRect);
            DrawAnimalMarker(
                drawingContext,
                point,
                animal,
                mapMode: true);
        }

        DrawLabel(
            drawingContext,
            "N",
            new Point(mapRect.Left + (mapRect.Width / 2d) - 5d, mapRect.Top + 8d),
            TextBrush,
            12d);

        DrawLabel(
            drawingContext,
            "Layton calibrated map · cyan ring = player",
            new Point(mapRect.Left + 10d, mapRect.Bottom - 24d),
            TextBrush,
            11d);
    }

    private void RenderRelativeRadar(
        DrawingContext drawingContext,
        double width,
        double height)
    {
        var center = new Point(width / 2d, height / 2d);
        var radius = Math.Max(
            30d,
            Math.Min(width, height) / 2d - 36d);

        var gridPen = new Pen(GridBrush, 1d);
        foreach (var ratio in new[] { 0.25d, 0.5d, 0.75d, 1d })
        {
            var ringRadius = radius * ratio;
            drawingContext.DrawEllipse(
                null,
                gridPen,
                center,
                ringRadius,
                ringRadius);

            DrawLabel(
                drawingContext,
                $"{MaxRangeMeters * ratio:F0}m",
                new Point(
                    center.X + 6d,
                    center.Y - ringRadius + 4d),
                TextBrush,
                11d);
        }

        drawingContext.DrawLine(
            gridPen,
            new Point(center.X - radius, center.Y),
            new Point(center.X + radius, center.Y));
        drawingContext.DrawLine(
            gridPen,
            new Point(center.X, center.Y - radius),
            new Point(center.X, center.Y + radius));

        DrawLabel(
            drawingContext,
            "N",
            new Point(center.X - 5d, center.Y - radius - 24d),
            TextBrush,
            12d);

        drawingContext.DrawEllipse(
            PlayerBrush,
            null,
            center,
            5d,
            5d);

        if (MaxRangeMeters <= 0d)
        {
            return;
        }

        foreach (var animal in Animals)
        {
            var planarDistance = Math.Sqrt(
                (animal.RelativeX * animal.RelativeX) +
                (animal.RelativeZ * animal.RelativeZ));
            if (planarDistance > MaxRangeMeters)
            {
                continue;
            }

            var point = new Point(
                center.X + (animal.RelativeX / MaxRangeMeters * radius),
                center.Y - (animal.RelativeZ / MaxRangeMeters * radius));

            DrawAnimalMarker(
                drawingContext,
                point,
                animal,
                mapMode: false);
        }
    }

    private void DrawAnimalMarker(
        DrawingContext drawingContext,
        Point point,
        LiveAnimalView animal,
        bool mapMode)
    {
        var brush = MarkerBrush(animal);
        var selected = SelectedAnimal is not null &&
                       SelectedAnimal.Address == animal.Address;
        var markerRadius = selected ? 8d : mapMode ? 6d : 5d;

        if (selected)
        {
            drawingContext.DrawEllipse(
                null,
                new Pen(PlayerBrush, 2d),
                point,
                11d,
                11d);
        }

        drawingContext.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(96, 0, 0, 0)),
            null,
            point,
            markerRadius + 2d,
            markerRadius + 2d);
        drawingContext.DrawEllipse(
            brush,
            null,
            point,
            markerRadius,
            markerRadius);

        if (selected ||
            animal.IsGreatOne ||
            animal.IsRare ||
            string.Equals(
                animal.Trophy,
                "Diamond",
                StringComparison.OrdinalIgnoreCase))
        {
            DrawLabel(
                drawingContext,
                $"{animal.DisplaySpecies} · {animal.DistanceMeters:F0}m",
                new Point(point.X + 10d, point.Y - 8d),
                brush,
                11d);
        }

        _markers.Add((point, animal));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        var click = e.GetPosition(this);
        var nearest = _markers
            .Select(marker => new
            {
                Marker = marker,
                Distance = (marker.Point - click).Length
            })
            .Where(item => item.Distance <= 14d)
            .OrderBy(item => item.Distance)
            .FirstOrDefault();

        if (nearest is null)
        {
            return;
        }

        SelectedAnimal = nearest.Marker.Animal;
        AnimalSelected?.Invoke(nearest.Marker.Animal);
    }

    private static Rect FitImageRect(
        double imageWidth,
        double imageHeight,
        double availableWidth,
        double availableHeight,
        double padding)
    {
        var width = Math.Max(1d, availableWidth - (padding * 2d));
        var height = Math.Max(1d, availableHeight - (padding * 2d));

        if (imageWidth <= 0d || imageHeight <= 0d)
        {
            return new Rect(padding, padding, width, height);
        }

        var scale = Math.Min(
            width / imageWidth,
            height / imageHeight);
        var renderWidth = imageWidth * scale;
        var renderHeight = imageHeight * scale;

        return new Rect(
            (availableWidth - renderWidth) / 2d,
            (availableHeight - renderHeight) / 2d,
            renderWidth,
            renderHeight);
    }

    private static Point NormalizedToPoint(
        Point normalized,
        Rect mapRect) =>
        new(
            mapRect.Left + (normalized.X * mapRect.Width),
            mapRect.Top + (normalized.Y * mapRect.Height));

    private static bool ContainsWithMargin(
        Point normalized,
        double margin) =>
        normalized.X >= -margin &&
        normalized.X <= 1d + margin &&
        normalized.Y >= -margin &&
        normalized.Y <= 1d + margin;

    private static Brush MarkerBrush(LiveAnimalView animal)
    {
        if (animal.IsGreatOne)
        {
            return GreatOneBrush;
        }

        if (animal.IsRare)
        {
            return RareBrush;
        }

        if (string.Equals(
                animal.Trophy,
                "Diamond",
                StringComparison.OrdinalIgnoreCase))
        {
            return DiamondBrush;
        }

        if (string.Equals(
                animal.Trophy,
                "Gold",
                StringComparison.OrdinalIgnoreCase))
        {
            return GoldBrush;
        }

        return NormalBrush;
    }

    private static void DrawLabel(
        DrawingContext context,
        string text,
        Point point,
        Brush brush,
        double fontSize)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            brush,
            1d);

        context.DrawText(formatted, point);
    }
}
