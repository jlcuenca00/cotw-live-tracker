using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CotwLiveTracker.Desktop.Maps;

namespace CotwLiveTracker.Desktop.Controls;

public enum RadarFollowMode
{
    None,
    Player,
    Animal
}

public sealed class RadarControl : FrameworkElement
{
    private static readonly Brush BackgroundBrush =
        new SolidColorBrush(Color.FromRgb(8, 10, 7));
    private static readonly Brush GridBrush =
        new SolidColorBrush(Color.FromRgb(52, 59, 47));
    private static readonly Brush TextBrush =
        new SolidColorBrush(Color.FromRgb(218, 214, 199));
    private static readonly Brush PlayerBrush =
        new SolidColorBrush(Color.FromRgb(214, 138, 46));
    private static readonly Brush NormalBrush =
        new SolidColorBrush(Color.FromRgb(144, 150, 138));
    private static readonly Brush SilverBrush =
        new SolidColorBrush(Color.FromRgb(90, 140, 200));
    private static readonly Brush GoldBrush =
        new SolidColorBrush(Color.FromRgb(214, 164, 61));
    private static readonly Brush DiamondBrush =
        new SolidColorBrush(Color.FromRgb(155, 101, 213));
    private static readonly Brush RareBrush =
        new SolidColorBrush(Color.FromRgb(58, 184, 176));
    private static readonly Brush GreatOneBrush =
        new SolidColorBrush(Color.FromRgb(220, 90, 81));
    private static readonly Brush MapShadeBrush =
        new SolidColorBrush(Color.FromArgb(28, 0, 0, 0));
    private static readonly Brush MarkerShadowBrush =
        new SolidColorBrush(Color.FromArgb(96, 0, 0, 0));
    private static readonly Brush PlayerHaloBrush =
        new SolidColorBrush(Color.FromArgb(72, 103, 232, 249));
    private static readonly Brush ReferenceBrush =
        new SolidColorBrush(Color.FromRgb(191, 126, 55));
    private static readonly Brush ReferenceFillBrush =
        new SolidColorBrush(Color.FromArgb(32, 191, 126, 55));

    private const double MinimumMapZoom = 1d;
    private const double MaximumMapZoom = 24d;
    private const double ZoomStep = 1.25d;

    private readonly List<(Point Point, LiveAnimalView Animal)> _markers = [];
    private IReadOnlyList<LiveAnimalView> _animals = [];
    private LiveAnimalView? _selectedAnimal;
    private ImageSource? _mapImage;
    private ReserveMapCalibration? _mapCalibration;
    private double _cameraX;
    private double _cameraZ;

    private double _mapZoom = 1d;
    private Vector _mapPan;
    private Point _dragStart;
    private Vector _panAtDragStart;
    private bool _isDragging;
    private bool _dragMoved;
    private bool _fillViewportOnNextRender;
    private RadarFollowMode _followMode;
    private nint? _followedAnimalAddress;
    private LiveAnimalView? _followedAnimal;

    public Action<LiveAnimalView>? AnimalSelected { get; set; }
    public Action<double>? MapZoomChanged { get; set; }
    public Action? FollowModeChanged { get; set; }
    public Action? FollowTargetLost { get; set; }

    public bool ShowReferences { get; set; } = true;

    public IReadOnlyList<LiveAnimalView> Animals
    {
        get => _animals;
        set
        {
            _animals = value ?? [];
            InvalidateVisual();
        }
    }

    public ImageSource? MapImage
    {
        get => _mapImage;
        set
        {
            _mapImage = value;
            ResetMapView();
            _fillViewportOnNextRender = value is not null;
            InvalidateVisual();
        }
    }

    public ReserveMapCalibration? MapCalibration
    {
        get => _mapCalibration;
        set
        {
            _mapCalibration = value;
            ResetMapView();
            InvalidateVisual();
        }
    }

    public bool IsMapMode =>
        MapImage is not null &&
        MapCalibration is not null;

    public double MapZoom => _mapZoom;
    public RadarFollowMode FollowMode => _followMode;
    public LiveAnimalView? FollowedAnimal => _followedAnimal;

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

    public void ZoomIn() =>
        SetMapZoom(_mapZoom * ZoomStep, ViewCenter());

    public void ZoomOut() =>
        SetMapZoom(_mapZoom / ZoomStep, ViewCenter());

    public void ResetMapView()
    {
        StopFollowing();
        _mapZoom = MinimumMapZoom;
        _mapPan = default;
        MapZoomChanged?.Invoke(_mapZoom);
        InvalidateVisual();
    }

    public void CenterOnPlayer()
    {
        StopFollowing();
        CenterOnWorld(CameraX, CameraZ, ensureTrackingZoom: true);
    }

    public bool StartFollowingPlayer()
    {
        if (!IsMapMode)
        {
            return false;
        }

        _followMode = RadarFollowMode.Player;
        _followedAnimalAddress = null;
        _followedAnimal = null;
        CenterOnWorld(CameraX, CameraZ, ensureTrackingZoom: true);
        FollowModeChanged?.Invoke();
        return true;
    }

    public bool StartFollowingAnimal(LiveAnimalView? animal)
    {
        if (!IsMapMode || animal is null)
        {
            return false;
        }

        _followMode = RadarFollowMode.Animal;
        _followedAnimalAddress = animal.Address;
        _followedAnimal = animal;
        CenterOnWorld(animal.X, animal.Z, ensureTrackingZoom: true);
        FollowModeChanged?.Invoke();
        return true;
    }

    public void StopFollowing()
    {
        if (_followMode == RadarFollowMode.None &&
            _followedAnimalAddress is null &&
            _followedAnimal is null)
        {
            return;
        }

        _followMode = RadarFollowMode.None;
        _followedAnimalAddress = null;
        _followedAnimal = null;
        FollowModeChanged?.Invoke();
    }

    public void RefreshFollowing(IReadOnlyList<LiveAnimalView> loadedAnimals)
    {
        if (!IsMapMode)
        {
            if (_followMode != RadarFollowMode.None)
            {
                StopFollowing();
            }

            return;
        }

        if (_followMode == RadarFollowMode.Player)
        {
            CenterOnWorld(CameraX, CameraZ, ensureTrackingZoom: false);
            return;
        }

        if (_followMode != RadarFollowMode.Animal ||
            _followedAnimalAddress is null)
        {
            return;
        }

        var target = loadedAnimals.FirstOrDefault(
            animal => animal.Address == _followedAnimalAddress.Value);

        if (target is null)
        {
            _followMode = RadarFollowMode.None;
            _followedAnimalAddress = null;
            _followedAnimal = null;
            FollowModeChanged?.Invoke();
            FollowTargetLost?.Invoke();
            InvalidateVisual();
            return;
        }

        _followedAnimal = target;
        CenterOnWorld(target.X, target.Z, ensureTrackingZoom: false);
    }

    private void CenterOnWorld(
        double worldX,
        double worldZ,
        bool ensureTrackingZoom)
    {
        if (!IsMapMode ||
            ActualWidth <= 0d ||
            ActualHeight <= 0d)
        {
            return;
        }

        var calibration = MapCalibration!;
        var normalized = calibration.WorldToNormalized(
            worldX,
            worldZ);

        var fitted = FitImageRect(
            MapImage!.Width,
            MapImage.Height,
            ActualWidth,
            ActualHeight,
            10d);
        var viewCenter = ViewCenter();
        var baseTarget = NormalizedToPoint(
            normalized,
            fitted);
        var baseVector = baseTarget - viewCenter;

        if (ensureTrackingZoom &&
            _mapZoom <= 1.01d)
        {
            _mapZoom = 2.5d;
            MapZoomChanged?.Invoke(_mapZoom);
        }

        _mapPan = -baseVector * _mapZoom;
        ClampMapPan();
        InvalidateVisual();
    }

    protected override void OnRender(
        DrawingContext drawingContext)
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
            ApplyInitialViewportFill(
                width,
                height);

            RenderMap(
                drawingContext,
                width,
                height);
        }
        else
        {
            RenderRelativeRadar(
                drawingContext,
                width,
                height);
        }
    }

    private void ApplyInitialViewportFill(
        double width,
        double height)
    {
        if (!_fillViewportOnNextRender ||
            MapImage is null ||
            width <= 0d ||
            height <= 0d)
        {
            return;
        }

        var fitted = FitImageRect(
            MapImage.Width,
            MapImage.Height,
            width,
            height,
            10d);

        if (fitted.Width <= 0d ||
            fitted.Height <= 0d)
        {
            return;
        }

        var availableWidth = Math.Max(1d, width - 20d);
        var availableHeight = Math.Max(1d, height - 20d);

        var coverZoom = Math.Max(
            availableWidth / fitted.Width,
            availableHeight / fitted.Height);

        _mapZoom = Math.Clamp(
            coverZoom,
            MinimumMapZoom,
            MaximumMapZoom);
        _mapPan = default;
        ClampMapPan();
        _fillViewportOnNextRender = false;

        MapZoomChanged?.Invoke(_mapZoom);
    }

    private void RenderMap(
        DrawingContext drawingContext,
        double width,
        double height)
    {
        var image = MapImage!;
        var calibration = MapCalibration!;

        var fitted = FitImageRect(
            image.Width,
            image.Height,
            width,
            height,
            10d);
        var mapRect = TransformMapRect(
            fitted,
            _mapZoom,
            _mapPan,
            ViewCenter());

        drawingContext.PushClip(
            new RectangleGeometry(
                new Rect(0d, 0d, width, height)));

        drawingContext.DrawImage(
            image,
            mapRect);

        drawingContext.DrawRectangle(
            MapShadeBrush,
            null,
            mapRect);

        DrawMapEdgeFade(
            drawingContext,
            mapRect);

        var references =
            ReserveMapReferenceCatalog.Get(
                calibration.ReserveIndex);
        var nearestReference = references
            .OrderBy(reference =>
                reference.DistanceTo(
                    CameraX,
                    CameraZ))
            .FirstOrDefault();

        if (ShowReferences)
        {
            foreach (var reference in references)
            {
                var referencePoint = NormalizedToPoint(
                    reference.ExpectedNormalized,
                    mapRect);

                drawingContext.DrawEllipse(
                    ReferenceFillBrush,
                    new Pen(ReferenceBrush, 1.5d),
                    referencePoint,
                    4d,
                    4d);
            }
        }

        var playerNormalized =
            calibration.WorldToNormalized(
                CameraX,
                CameraZ);
        var player = NormalizedToPoint(
            playerNormalized,
            mapRect);

        if (ShowReferences && nearestReference is not null)
        {
            var nearestPoint = NormalizedToPoint(
                nearestReference.ExpectedNormalized,
                mapRect);
            var nearestDistance =
                nearestReference.DistanceTo(
                    CameraX,
                    CameraZ);

            drawingContext.DrawEllipse(
                null,
                new Pen(ReferenceBrush, 2d),
                nearestPoint,
                9d,
                9d);

            if (nearestDistance <= 250d)
            {
                drawingContext.DrawLine(
                    new Pen(ReferenceBrush, 1d),
                    player,
                    nearestPoint);
                DrawLabel(
                    drawingContext,
                    $"{nearestReference.Name} · {nearestDistance:F1}m",
                    new Point(
                        nearestPoint.X + 11d,
                        nearestPoint.Y + 6d),
                    ReferenceBrush,
                    11d);
            }
        }

        if (ContainsWithMargin(
                playerNormalized,
                0.05d))
        {
            drawingContext.DrawEllipse(
                PlayerHaloBrush,
                new Pen(PlayerBrush, 1.5d),
                player,
                12d,
                12d);
            DrawFontAwesomeGlyph(
                drawingContext,
                "\uf05b",
                player,
                PlayerBrush,
                13d);
        }

        foreach (var animal in Animals)
        {
            var normalized =
                calibration.WorldToNormalized(
                    animal.X,
                    animal.Z);

            if (!ContainsWithMargin(
                    normalized,
                    0.02d))
            {
                continue;
            }

            var point =
                NormalizedToPoint(
                    normalized,
                    mapRect);

            DrawAnimalMarker(
                drawingContext,
                point,
                animal,
                mapMode: true);
        }

        drawingContext.Pop();

        DrawLabel(
            drawingContext,
            "N",
            new Point(
                width / 2d - 5d,
                12d),
            TextBrush,
            12d);
    }

    private static void DrawMapEdgeFade(
        DrawingContext drawingContext,
        Rect mapRect)
    {
        var fadeSize = Math.Clamp(
            Math.Min(mapRect.Width, mapRect.Height) * 0.075d,
            34d,
            82d);

        var edgeColor = Color.FromArgb(238, 8, 10, 7);
        var transparent = Color.FromArgb(0, 8, 10, 7);

        var leftBrush = new LinearGradientBrush(
            edgeColor,
            transparent,
            new Point(0d, 0.5d),
            new Point(1d, 0.5d));
        var rightBrush = new LinearGradientBrush(
            transparent,
            edgeColor,
            new Point(0d, 0.5d),
            new Point(1d, 0.5d));
        var topBrush = new LinearGradientBrush(
            edgeColor,
            transparent,
            new Point(0.5d, 0d),
            new Point(0.5d, 1d));
        var bottomBrush = new LinearGradientBrush(
            transparent,
            edgeColor,
            new Point(0.5d, 0d),
            new Point(0.5d, 1d));

        drawingContext.DrawRectangle(
            leftBrush,
            null,
            new Rect(
                mapRect.Left,
                mapRect.Top,
                Math.Min(fadeSize, mapRect.Width),
                mapRect.Height));

        drawingContext.DrawRectangle(
            rightBrush,
            null,
            new Rect(
                Math.Max(mapRect.Left, mapRect.Right - fadeSize),
                mapRect.Top,
                Math.Min(fadeSize, mapRect.Width),
                mapRect.Height));

        drawingContext.DrawRectangle(
            topBrush,
            null,
            new Rect(
                mapRect.Left,
                mapRect.Top,
                mapRect.Width,
                Math.Min(fadeSize, mapRect.Height)));

        drawingContext.DrawRectangle(
            bottomBrush,
            null,
            new Rect(
                mapRect.Left,
                Math.Max(mapRect.Top, mapRect.Bottom - fadeSize),
                mapRect.Width,
                Math.Min(fadeSize, mapRect.Height)));
    }

    private void RenderRelativeRadar(
        DrawingContext drawingContext,
        double width,
        double height)
    {
        var center =
            new Point(
                width / 2d,
                height / 2d);
        var radius = Math.Max(
            30d,
            Math.Min(width, height) /
                2d -
            36d);

        var displayRange = Math.Max(
            250d,
            Animals
                .Select(animal => Math.Sqrt(
                    (animal.RelativeX * animal.RelativeX) +
                    (animal.RelativeZ * animal.RelativeZ)))
                .DefaultIfEmpty(0d)
                .Max() *
            1.08d);

        var gridPen =
            new Pen(
                GridBrush,
                1d);

        foreach (var ratio in
                 new[] { 0.25d, 0.5d, 0.75d, 1d })
        {
            var ringRadius =
                radius * ratio;
            drawingContext.DrawEllipse(
                null,
                gridPen,
                center,
                ringRadius,
                ringRadius);

            DrawLabel(
                drawingContext,
                $"{displayRange * ratio:F0}m",
                new Point(
                    center.X + 6d,
                    center.Y -
                    ringRadius +
                    4d),
                TextBrush,
                11d);
        }

        drawingContext.DrawLine(
            gridPen,
            new Point(
                center.X - radius,
                center.Y),
            new Point(
                center.X + radius,
                center.Y));
        drawingContext.DrawLine(
            gridPen,
            new Point(
                center.X,
                center.Y - radius),
            new Point(
                center.X,
                center.Y + radius));

        DrawLabel(
            drawingContext,
            "N",
            new Point(
                center.X - 5d,
                center.Y - radius - 24d),
            TextBrush,
            12d);

        drawingContext.DrawEllipse(
            PlayerBrush,
            null,
            center,
            5d,
            5d);

        foreach (var animal in Animals)
        {
            var point =
                new Point(
                    center.X +
                    (animal.RelativeX /
                     displayRange *
                     radius),
                    center.Y -
                    (animal.RelativeZ /
                     displayRange *
                     radius));

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
        var selected =
            SelectedAnimal is not null &&
            SelectedAnimal.Address == animal.Address;
        var followed =
            FollowMode == RadarFollowMode.Animal &&
            FollowedAnimal?.Address == animal.Address;

        var zoomScale = mapMode
            ? Math.Clamp(0.90d + (Math.Log(Math.Max(1d, _mapZoom), 2d) * 0.08d), 0.9d, 1.28d)
            : 1d;
        var markerRadius =
            (selected || followed ? 9d : 7d) * zoomScale;

        if (animal.IsGreatOne)
        {
            drawingContext.DrawEllipse(
                new SolidColorBrush(Color.FromArgb(30, 220, 90, 81)),
                null,
                point,
                markerRadius + 11d,
                markerRadius + 11d);
            drawingContext.DrawEllipse(
                new SolidColorBrush(Color.FromArgb(56, 220, 90, 81)),
                null,
                point,
                markerRadius + 6d,
                markerRadius + 6d);
        }

        if (animal.IsRare && !animal.IsGreatOne)
        {
            drawingContext.DrawEllipse(
                null,
                new Pen(RareBrush, 2d),
                point,
                markerRadius + 4d,
                markerRadius + 4d);
        }

        if (selected || followed)
        {
            drawingContext.DrawEllipse(
                null,
                new Pen(PlayerBrush, followed ? 2.5d : 1.8d),
                point,
                markerRadius + 5d,
                markerRadius + 5d);
        }

        drawingContext.DrawEllipse(
            MarkerShadowBrush,
            null,
            point,
            markerRadius + 2d,
            markerRadius + 2d);

        drawingContext.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(225, 16, 18, 15)),
            new Pen(brush, 1.8d),
            point,
            markerRadius,
            markerRadius);

        DrawFontAwesomeGlyph(
            drawingContext,
            "\uf1b0",
            point,
            brush,
            Math.Max(8d, markerRadius + 2d));

        if (selected ||
            followed ||
            animal.IsGreatOne ||
            animal.IsRare ||
            string.Equals(
                animal.Trophy,
                "Diamond",
                StringComparison.OrdinalIgnoreCase))
        {
            DrawLabel(
                drawingContext,
                $"{animal.DisplaySpecies} · {animal.Difficulty} · {animal.DistanceMeters:F0}m",
                new Point(
                    point.X + markerRadius + 6d,
                    point.Y - 8d),
                brush,
                10.5d);
        }

        _markers.Add((point, animal));
    }

    private void DrawFontAwesomeGlyph(
        DrawingContext drawingContext,
        string glyph,
        Point center,
        Brush brush,
        double size)
    {
        var typeface = new Typeface(
            new FontFamily("/FontAwesome.Sharp;component/fonts/#Font Awesome 6 Free Solid"),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);

        var text = new FormattedText(
            glyph,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        drawingContext.DrawText(
            text,
            new Point(
                center.X - (text.Width / 2d),
                center.Y - (text.Height / 2d)));
    }

    protected override void OnMouseWheel(
        MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!IsMapMode)
        {
            return;
        }

        var factor =
            e.Delta > 0
                ? ZoomStep
                : 1d / ZoomStep;

        SetMapZoom(
            _mapZoom * factor,
            e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(
        MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        _dragStart =
            e.GetPosition(this);
        _panAtDragStart =
            _mapPan;
        _dragMoved = false;
        _isDragging = IsMapMode;

        if (_isDragging)
        {
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(
        MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_isDragging ||
            e.LeftButton !=
            MouseButtonState.Pressed)
        {
            return;
        }

        var current =
            e.GetPosition(this);
        var delta =
            current - _dragStart;

        if (delta.Length > 3d)
        {
            if (!_dragMoved)
            {
                StopFollowing();
            }

            _dragMoved = true;
        }

        _mapPan =
            _panAtDragStart +
            delta;
        ClampMapPan();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(
        MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        var click =
            e.GetPosition(this);

        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }

        if (!_dragMoved)
        {
            SelectMarkerAt(
                click,
                e.ClickCount >= 2);
        }

        _dragMoved = false;
        e.Handled = true;
    }

    private void SelectMarkerAt(
        Point click,
        bool follow)
    {
        var nearest =
            _markers
                .Select(marker => new
                {
                    Marker = marker,
                    Distance =
                        (marker.Point -
                         click).Length
                })
                .Where(item =>
                    item.Distance <= 14d)
                .OrderBy(item =>
                    item.Distance)
                .FirstOrDefault();

        if (nearest is null)
        {
            return;
        }

        SelectedAnimal =
            nearest.Marker.Animal;
        AnimalSelected?.Invoke(
            nearest.Marker.Animal);

        if (follow)
        {
            StartFollowingAnimal(
                nearest.Marker.Animal);
        }
    }

    private void SetMapZoom(
        double requestedZoom,
        Point anchor)
    {
        if (!IsMapMode)
        {
            return;
        }

        var newZoom =
            Math.Clamp(
                requestedZoom,
                MinimumMapZoom,
                MaximumMapZoom);

        if (Math.Abs(
                newZoom - _mapZoom) <
            0.0001d)
        {
            return;
        }

        var center =
            ViewCenter();
        var anchorVector =
            anchor - center;
        var unscaledVector =
            (anchorVector - _mapPan) /
            _mapZoom;

        _mapPan =
            anchorVector -
            (unscaledVector * newZoom);
        _mapZoom =
            newZoom;

        if (_mapZoom <=
            MinimumMapZoom +
            0.0001d)
        {
            _mapPan = default;
        }

        ClampMapPan();

        MapZoomChanged?.Invoke(
            _mapZoom);

        if (_followMode == RadarFollowMode.Player)
        {
            CenterOnWorld(CameraX, CameraZ, ensureTrackingZoom: false);
        }
        else if (_followMode == RadarFollowMode.Animal &&
                 _followedAnimal is not null)
        {
            CenterOnWorld(
                _followedAnimal.X,
                _followedAnimal.Z,
                ensureTrackingZoom: false);
        }

        InvalidateVisual();
    }

    private void ClampMapPan()
    {
        if (!IsMapMode ||
            MapImage is null ||
            ActualWidth <= 0d ||
            ActualHeight <= 0d)
        {
            return;
        }

        var fitted = FitImageRect(
            MapImage.Width,
            MapImage.Height,
            ActualWidth,
            ActualHeight,
            10d);

        var scaledWidth = fitted.Width * _mapZoom;
        var scaledHeight = fitted.Height * _mapZoom;

        var maxPanX = scaledWidth > ActualWidth
            ? (scaledWidth - ActualWidth) / 2d
            : 0d;
        var maxPanY = scaledHeight > ActualHeight
            ? (scaledHeight - ActualHeight) / 2d
            : 0d;

        _mapPan = new Vector(
            Math.Clamp(_mapPan.X, -maxPanX, maxPanX),
            Math.Clamp(_mapPan.Y, -maxPanY, maxPanY));
    }

    private Point ViewCenter() =>
        new(
            ActualWidth / 2d,
            ActualHeight / 2d);

    private static Rect TransformMapRect(
        Rect fitted,
        double zoom,
        Vector pan,
        Point center)
    {
        var width =
            fitted.Width * zoom;
        var height =
            fitted.Height * zoom;

        return new Rect(
            center.X -
            (width / 2d) +
            pan.X,
            center.Y -
            (height / 2d) +
            pan.Y,
            width,
            height);
    }

    private static Rect FitImageRect(
        double imageWidth,
        double imageHeight,
        double availableWidth,
        double availableHeight,
        double padding)
    {
        var width =
            Math.Max(
                1d,
                availableWidth -
                (padding * 2d));
        var height =
            Math.Max(
                1d,
                availableHeight -
                (padding * 2d));

        if (imageWidth <= 0d ||
            imageHeight <= 0d)
        {
            return new Rect(
                padding,
                padding,
                width,
                height);
        }

        var scale =
            Math.Min(
                width / imageWidth,
                height / imageHeight);
        var renderWidth =
            imageWidth * scale;
        var renderHeight =
            imageHeight * scale;

        return new Rect(
            (availableWidth -
             renderWidth) /
            2d,
            (availableHeight -
             renderHeight) /
            2d,
            renderWidth,
            renderHeight);
    }

    private static Point NormalizedToPoint(
        Point normalized,
        Rect mapRect) =>
        new(
            mapRect.Left +
            (normalized.X *
             mapRect.Width),
            mapRect.Top +
            (normalized.Y *
             mapRect.Height));

    private static bool ContainsWithMargin(
        Point normalized,
        double margin) =>
        normalized.X >= -margin &&
        normalized.X <= 1d + margin &&
        normalized.Y >= -margin &&
        normalized.Y <= 1d + margin;

    private static Brush MarkerBrush(
        LiveAnimalView animal)
    {
        var level = DifficultyLevel(animal.Difficulty);

        if (animal.IsGreatOne || level >= 10)
        {
            return GreatOneBrush;
        }

        if (string.Equals(
                animal.Trophy,
                "Diamond",
                StringComparison.OrdinalIgnoreCase) ||
            level >= 9)
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

        if (string.Equals(
                animal.Trophy,
                "Silver",
                StringComparison.OrdinalIgnoreCase))
        {
            return SilverBrush;
        }

        return NormalBrush;
    }

    private static int DifficultyLevel(string value)
    {
        var separator = value.IndexOf('-');
        var level = separator >= 0
            ? value[..separator]
            : value;

        return int.TryParse(
            level.Trim().TrimStart('~'),
            out var parsed)
            ? parsed
            : 0;
    }

    private static void DrawLabel(
        DrawingContext context,
        string text,
        Point point,
        Brush brush,
        double fontSize)
    {
        var formatted =
            new FormattedText(
                text,
                System.Globalization.CultureInfo
                    .CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                fontSize,
                brush,
                1d);

        context.DrawText(
            formatted,
            point);
    }
}
