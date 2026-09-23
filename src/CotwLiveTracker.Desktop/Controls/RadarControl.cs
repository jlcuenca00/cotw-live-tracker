using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CotwLiveTracker.Desktop.Controls;

internal sealed class RadarControl : FrameworkElement
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

    public double MaxRangeMeters { get; set; } = 1000d;

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
        if (width <= 0 || height <= 0)
        {
            return;
        }

        drawingContext.DrawRectangle(
            BackgroundBrush,
            null,
            new Rect(0, 0, width, height));

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

        _markers.Clear();
        if (MaxRangeMeters <= 0)
        {
            return;
        }

        foreach (var animal in Animals)
        {
            var planarDistance = Math.Sqrt(
                animal.RelativeX * animal.RelativeX +
                animal.RelativeZ * animal.RelativeZ);
            if (planarDistance > MaxRangeMeters)
            {
                continue;
            }

            var point = new Point(
                center.X + animal.RelativeX / MaxRangeMeters * radius,
                center.Y - animal.RelativeZ / MaxRangeMeters * radius);

            var brush = MarkerBrush(animal);
            var selected = ReferenceEquals(animal, SelectedAnimal);
            var markerRadius = selected ? 8d : 5d;

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
