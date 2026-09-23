using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CotwLiveTracker.Desktop.Maps;
using Microsoft.Win32;
using CotwLiveTracker.Population;

namespace CotwLiveTracker.Desktop;

public partial class MainWindow : Window
{
    private sealed record MapBuildResult(
        IReadOnlyList<int> Installed,
        IReadOnlyList<int> Succeeded,
        IReadOnlyList<string> Failures);

    private readonly DesktopTrackerSession _session = new();
    private readonly DispatcherTimer _refreshTimer;
    private IReadOnlyList<LiveAnimalView> _latestLiveAnimals =
        Array.Empty<LiveAnimalView>();
    private bool _centerMapOnNextSnapshot;

    public MainWindow()
    {
        InitializeComponent();

        ReserveBox.ItemsSource = DesktopTrackerSession.Reserves;
        ReserveBox.SelectedItem = DesktopTrackerSession.Reserves
            .FirstOrDefault(reserve => reserve.Index == 1)
            ?? DesktopTrackerSession.Reserves.FirstOrDefault();

        if (ReserveBox.SelectedItem is ReserveChoice initialReserve)
        {
            ConfigureReserveMap(initialReserve.Index);
        }

        PopulationTrophyFilter.ItemsSource = new[]
        {
            "All", "None", "Bronze", "Silver", "Gold", "Diamond", "Great One"
        };
        PopulationTrophyFilter.SelectedIndex = 0;

        PopulationDifficultyFilter.ItemsSource =
            new[] { "All" }
                .Concat(Enumerable.Range(1, 10).Select(value => value.ToString()))
                .ToArray();
        PopulationDifficultyFilter.SelectedIndex = 0;

        PopulationSexFilter.ItemsSource = new[] { "All", "Male", "Female" };
        PopulationSexFilter.SelectedIndex = 0;

        LiveTrophyFilter.ItemsSource = new[]
        {
            "All", "None", "Bronze", "Silver", "Gold", "Diamond", "Great One"
        };
        LiveTrophyFilter.SelectedIndex = 0;

        LiveDifficultyFilter.ItemsSource =
            new[] { "All" }
                .Concat(Enumerable.Range(1, 10).Select(value => value.ToString()))
                .ToArray();
        LiveDifficultyFilter.SelectedIndex = 0;

        LiveSexFilter.ItemsSource = new[] { "All", "Male", "Female" };
        LiveSexFilter.SelectedIndex = 0;

        Radar.AnimalSelected = SelectLiveAnimal;
        Radar.MapZoomChanged = zoom =>
        {
            MapZoomText.Text = $"{zoom * 100d:F0}%";
        };

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += (_, _) => RefreshLive();

        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _session.Dispose();
        };
    }

    private void AttachButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReserveBox.SelectedItem is not ReserveChoice reserve)
        {
            SetOffline("Select a reserve first.");
            return;
        }

        _refreshTimer.Stop();
        AttachButton.IsEnabled = false;

        try
        {
            _session.Attach(reserve.Index);
            ConfigureReserveMap(reserve.Index);
            SetConnected();
            UpdateStaticSessionUi();
            ApplyPopulationFilters();
            RefreshLive();
            _refreshTimer.Start();
        }
        catch (Exception ex)
        {
            SetOffline(ex.Message);
            MessageBox.Show(
                this,
                ex.Message,
                "COTW Live Tracker",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            AttachButton.IsEnabled = true;
        }
    }

    private void RefreshLive()
    {
        if (!_session.IsAttached)
        {
            _refreshTimer.Stop();
            return;
        }

        try
        {
            var snapshot = _session.ReadSnapshot();
            var animals = snapshot.Animals;
            _latestLiveAnimals = animals;

            DashboardAnimalsGrid.ItemsSource = animals.Take(10).ToArray();
            ApplyLiveFilters(animals);

            Radar.MaxRangeMeters = RadarRangeSlider.Value;
            Radar.CameraX = snapshot.CameraPosition.X;
            Radar.CameraZ = snapshot.CameraPosition.Z;

            if (_centerMapOnNextSnapshot &&
                Radar.IsMapMode)
            {
                Radar.CenterOnPlayer();
                _centerMapOnNextSnapshot = false;
            }

            LoadedCountText.Text = animals.Count.ToString("N0");
            ResolvedCountText.Text = animals.Count(animal => animal.GroupIndex is not null).ToString("N0");
            DiamondCountText.Text = animals.Count(animal =>
                string.Equals(animal.Trophy, "Diamond", StringComparison.OrdinalIgnoreCase)).ToString("N0");
            RareCountText.Text = animals.Count(animal => animal.IsRare).ToString("N0");
            GreatOneCountText.Text = animals.Count(animal => animal.IsGreatOne).ToString("N0");

            CameraPositionText.Text =
                $"{snapshot.CameraPosition.X:F1}, {snapshot.CameraPosition.Y:F1}, {snapshot.CameraPosition.Z:F1}";
            RuntimeStatusText.Text =
                $"Live refresh OK · {animals.Count:N0} loaded · {snapshot.SkippedAnimals:N0} skipped";

            if (Radar.SelectedAnimal is { } selected)
            {
                var replacement = animals.FirstOrDefault(animal =>
                    animal.Address == selected.Address);
                Radar.SelectedAnimal = replacement;
                if (replacement is not null)
                {
                    ShowSelectedAnimal(replacement);
                }
            }
        }
        catch (Exception ex)
        {
            _refreshTimer.Stop();
            SetOffline($"Live refresh stopped: {ex.Message}");
        }
    }

    private void UpdateStaticSessionUi()
    {
        DashboardReserveText.Text = _session.Reserve?.Name ?? "—";
        PopulationTotalText.Text = _session.Population?.Animals.Count.ToString("N0") ?? "—";

        SettingsPidText.Text = _session.ProcessId?.ToString() ?? "—";
        SettingsReserveText.Text = _session.Reserve?.Name ?? "—";
        SettingsPopulationPathText.Text =
            string.IsNullOrWhiteSpace(_session.PopulationPath)
                ? "—"
                : _session.PopulationPath;
        SettingsProfileText.Text = _session.Profile?.Name ?? "—";
        SettingsPatchText.Text = _session.Profile?.GamePatch ?? "—";
        SettingsHashText.Text =
            string.IsNullOrWhiteSpace(_session.ExecutableSha256)
                ? "—"
                : _session.ExecutableSha256;
    }

    private void ApplyLiveFilters_Click(object sender, RoutedEventArgs e) =>
        ApplyLiveFilters(_latestLiveAnimals);

    private void ClearLiveFilters_Click(object sender, RoutedEventArgs e)
    {
        LiveSpeciesFilter.Text = "";
        LiveTrophyFilter.SelectedIndex = 0;
        LiveDifficultyFilter.SelectedIndex = 0;
        LiveFurFilter.Text = "";
        LiveSexFilter.SelectedIndex = 0;
        LiveRareOnly.IsChecked = false;
        LiveGreatOneOnly.IsChecked = false;
        ApplyLiveFilters(_latestLiveAnimals);
    }

    private void ApplyLiveFilters(
        IReadOnlyList<LiveAnimalView> animals)
    {
        var species = LiveSpeciesFilter.Text.Trim();
        var fur = LiveFurFilter.Text.Trim();

        var trophy = LiveTrophyFilter.SelectedItem as string;
        if (string.Equals(
                trophy,
                "All",
                StringComparison.OrdinalIgnoreCase))
        {
            trophy = null;
        }

        var sex = LiveSexFilter.SelectedItem as string;
        if (string.Equals(
                sex,
                "All",
                StringComparison.OrdinalIgnoreCase))
        {
            sex = null;
        }

        int? difficulty = null;
        var difficultyText =
            LiveDifficultyFilter.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(difficultyText) &&
            !string.Equals(
                difficultyText,
                "All",
                StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(
                difficultyText,
                out var parsedDifficulty))
        {
            difficulty = parsedDifficulty;
        }

        var filtered = animals
            .Where(animal =>
                string.IsNullOrWhiteSpace(species) ||
                animal.DisplaySpecies.Contains(
                    species,
                    StringComparison.OrdinalIgnoreCase))
            .Where(animal =>
                trophy is null ||
                string.Equals(
                    animal.Trophy,
                    trophy,
                    StringComparison.OrdinalIgnoreCase))
            .Where(animal =>
                difficulty is null ||
                ParseDifficultyLevel(animal.Difficulty) ==
                difficulty.Value)
            .Where(animal =>
                string.IsNullOrWhiteSpace(fur) ||
                animal.Fur.Contains(
                    fur,
                    StringComparison.OrdinalIgnoreCase))
            .Where(animal =>
                sex is null ||
                string.Equals(
                    animal.Gender,
                    sex,
                    StringComparison.OrdinalIgnoreCase))
            .Where(animal =>
                LiveRareOnly.IsChecked != true ||
                animal.IsRare)
            .Where(animal =>
                LiveGreatOneOnly.IsChecked != true ||
                animal.IsGreatOne)
            .OrderBy(animal => animal.DistanceMeters)
            .ToArray();

        LiveAnimalsGrid.ItemsSource = filtered;
        Radar.Animals = filtered;
        LiveFilterSummaryText.Text =
            $"{filtered.Length:N0} shown / {animals.Count:N0} loaded";

        if (Radar.SelectedAnimal is { } selected &&
            !filtered.Any(animal =>
                animal.Address == selected.Address))
        {
            Radar.SelectedAnimal = null;
            LiveAnimalsGrid.SelectedItem = null;
            SelectedAnimalTitle.Text =
                "Click a radar marker or live row";
            SelectedAnimalDetails.Text =
                "Distance, identity, score, fur and XYZ will appear here.";
        }
    }

    private static int ParseDifficultyLevel(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var separator = value.IndexOf('-');
        var levelText = separator >= 0
            ? value[..separator]
            : value;

        return int.TryParse(
            levelText.Trim(),
            out var level)
            ? level
            : 0;
    }

    private void ApplyPopulationFilters_Click(object sender, RoutedEventArgs e) =>
        ApplyPopulationFilters();

    private void ClearPopulationFilters_Click(object sender, RoutedEventArgs e)
    {
        PopulationSpeciesFilter.Text = "";
        PopulationTrophyFilter.SelectedIndex = 0;
        PopulationDifficultyFilter.SelectedIndex = 0;
        PopulationFurFilter.Text = "";
        PopulationSexFilter.SelectedIndex = 0;
        PopulationRareOnly.IsChecked = false;
        PopulationGreatOneOnly.IsChecked = false;
        ApplyPopulationFilters();
    }

    private void ApplyPopulationFilters()
    {
        if (_session.Population is null)
        {
            PopulationGrid.ItemsSource = Array.Empty<PopulationAnimalView>();
            PopulationMatchText.Text = "0 matches";
            return;
        }

        var trophy = PopulationTrophyFilter.SelectedItem as string;
        if (string.Equals(trophy, "All", StringComparison.OrdinalIgnoreCase))
        {
            trophy = null;
        }

        int? difficulty = null;
        var difficultyText = PopulationDifficultyFilter.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(difficultyText) &&
            !string.Equals(difficultyText, "All", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(difficultyText, out var parsedDifficulty))
        {
            difficulty = parsedDifficulty;
        }

        var sex = PopulationSexFilter.SelectedItem as string;
        if (string.Equals(sex, "All", StringComparison.OrdinalIgnoreCase))
        {
            sex = null;
        }

        var filters = new PopulationFilterOptions(
            Trophy: trophy,
            RareOnly: PopulationRareOnly.IsChecked == true,
            GreatOneOnly: PopulationGreatOneOnly.IsChecked == true,
            Difficulty: difficulty,
            Fur: string.IsNullOrWhiteSpace(PopulationFurFilter.Text)
                ? null
                : PopulationFurFilter.Text,
            Sex: sex?.ToLowerInvariant(),
            ShowAll: true);

        var species = PopulationSpeciesFilter.Text.Trim();
        var rows = _session.Population.Animals
            .Where(filters.Matches)
            .Where(animal =>
                string.IsNullOrWhiteSpace(species) ||
                animal.Species.Replace('_', ' ')
                    .Contains(species, StringComparison.OrdinalIgnoreCase))
            .OrderBy(animal => animal.Species)
            .ThenByDescending(animal => animal.IsGreatOne)
            .ThenByDescending(animal => animal.Score)
            .Select(animal => new PopulationAnimalView(animal))
            .ToArray();

        PopulationGrid.ItemsSource = rows;
        PopulationMatchText.Text =
            $"{rows.Length:N0} matches / {_session.Population.Animals.Count:N0} total";
    }

    private async void BuildMapsFromGameButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_session.IsAttached ||
            string.IsNullOrWhiteSpace(_session.GameDirectory))
        {
            MessageBox.Show(
                this,
                "Attach to COTW first so the tracker can locate your installed game archives.",
                "COTW Live Tracker",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var gameDirectory = _session.GameDirectory;
        var candidateIndices = DesktopTrackerSession.Reserves
            .Select(reserve => reserve.Index)
            .ToArray();
        IProgress<string> progress = new Progress<string>(message =>
        {
            MapModeStatusText.Text = message;
        });

        BuildMapsFromGameButton.IsEnabled = false;
        LoadMapImageButton.IsEnabled = false;
        AttachButton.IsEnabled = false;

        try
        {
            var result = await Task.Run(() =>
            {
                using var extractor = new ReserveMapExtractor(
                    gameDirectory,
                    progress.Report);
                var installed = extractor
                    .DiscoverInstalledReserves(candidateIndices)
                    .ToArray();

                var succeeded = new List<int>();
                var failures = new List<string>();

                foreach (var reserveIndex in installed)
                {
                    try
                    {
                        var reserveName = DesktopTrackerSession.Reserves
                            .FirstOrDefault(reserve => reserve.Index == reserveIndex)
                            ?.Name
                            ?? $"Reserve {reserveIndex}";

                        progress.Report($"Building {reserveName} map…");
                        var map = extractor.Extract(
                            reserveIndex,
                            progress.Report);
                        ReserveMapImageStore.SaveGenerated(map);
                        succeeded.Add(reserveIndex);
                    }
                    catch (Exception ex)
                    {
                        failures.Add(
                            $"Reserve {reserveIndex}: {ex.Message}");
                    }
                }

                return new MapBuildResult(
                    installed,
                    succeeded,
                    failures);
            });

            if (_session.Reserve is { } activeReserve)
            {
                ConfigureReserveMap(activeReserve.Index);
            }

            if (result.Installed.Count == 0)
            {
                MapModeStatusText.Text =
                    "No known reserve map assets were found in the installed archives.";
                MessageBox.Show(
                    this,
                    "The archive tables were readable, but none of the known reserve map paths were found. " +
                    "Do not install DECA again—send me this result so we can adjust the archive/path handling.",
                    "COTW Live Tracker",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var calibratedCount = result.Succeeded.Count(index =>
                ReserveMapCalibrationCatalog.Get(index) is not null);

            var summary =
                $"Found {result.Installed.Count:N0} installed reserve maps.\n" +
                $"Built {result.Succeeded.Count:N0} local PNG map caches.\n" +
                $"Verified calibration profiles currently active: {calibratedCount:N0}.";
            if (result.Failures.Count > 0)
            {
                summary +=
                    $"\n\n{result.Failures.Count:N0} map(s) could not be built:\n" +
                    string.Join(
                        "\n",
                        result.Failures.Take(5));
                if (result.Failures.Count > 5)
                {
                    summary +=
                        $"\n…plus {result.Failures.Count - 5:N0} more.";
                }
            }

            MessageBox.Show(
                this,
                summary,
                "COTW map cache",
                MessageBoxButton.OK,
                result.Failures.Count == 0
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MapModeStatusText.Text =
                $"Map extraction failed · {ex.Message}";
            MessageBox.Show(
                this,
                $"Could not build maps directly from COTW: {ex.Message}",
                "COTW Live Tracker",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            BuildMapsFromGameButton.IsEnabled = _session.IsAttached;
            AttachButton.IsEnabled = true;

            if (_session.Reserve is { } activeReserve)
            {
                LoadMapImageButton.IsEnabled =
                    ReserveMapCalibrationCatalog.Get(activeReserve.Index) is not null;
            }
        }
    }

    private void LoadMapImageButton_Click(object sender, RoutedEventArgs e)
    {
        var reserve = _session.Reserve
            ?? ReserveBox.SelectedItem as ReserveChoice;

        if (reserve is null)
        {
            MessageBox.Show(
                this,
                "Select a reserve first.",
                "COTW Live Tracker",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var calibration = ReserveMapCalibrationCatalog.Get(reserve.Index);
        if (calibration is null)
        {
            MessageBox.Show(
                this,
                "This reserve does not have a verified world-to-map calibration yet.",
                "COTW Live Tracker",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"Load {reserve.Name} map image",
            Filter = "Map images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var stored = ReserveMapImageStore.Import(
                reserve.Index,
                dialog.FileName);
            Radar.MapCalibration = calibration;
            Radar.MapImage = ReserveMapImageStore.Load(stored);
            _centerMapOnNextSnapshot = true;
            MapModeStatusText.Text =
                $"{reserve.Name} · calibrated actual-map mode";
            LoadMapImageButton.Content = "Replace map";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Could not load the map image: {ex.Message}",
                "COTW Live Tracker",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ConfigureReserveMap(int reserveIndex)
    {
        var calibration = ReserveMapCalibrationCatalog.Get(reserveIndex);
        var mapPath = ReserveMapImageStore.Find(reserveIndex);

        Radar.MapCalibration = calibration;
        Radar.MapImage = null;

        if (calibration is null)
        {
            MapModeStatusText.Text = mapPath is null
                ? "Map not cached · calibration pending · relative radar fallback"
                : "Map cached · calibration pending · relative radar fallback";
            LoadMapImageButton.IsEnabled = false;
            LoadMapImageButton.Content = "Load image";
            return;
        }

        LoadMapImageButton.IsEnabled = true;
        if (mapPath is null)
        {
            MapModeStatusText.Text =
                $"{calibration.ReserveName} calibrated · build maps or load image";
            LoadMapImageButton.Content = "Load image";
            return;
        }

        try
        {
            Radar.MapImage = ReserveMapImageStore.Load(mapPath);
            _centerMapOnNextSnapshot = true;
            MapModeStatusText.Text =
                $"{calibration.ReserveName} · calibrated actual-map mode";
            LoadMapImageButton.Content = "Replace image";
        }
        catch (Exception ex)
        {
            Radar.MapImage = null;
            MapModeStatusText.Text =
                $"Saved map failed to load · {ex.Message}";
            LoadMapImageButton.Content = "Load image";
        }
    }


    private void MapZoomInButton_Click(
        object sender,
        RoutedEventArgs e) =>
        Radar.ZoomIn();

    private void MapZoomOutButton_Click(
        object sender,
        RoutedEventArgs e) =>
        Radar.ZoomOut();

    private void MapFitButton_Click(
        object sender,
        RoutedEventArgs e) =>
        Radar.ResetMapView();

    private void MapCenterPlayerButton_Click(
        object sender,
        RoutedEventArgs e) =>
        Radar.CenterOnPlayer();

    private void RadarRangeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (Radar is null || RadarRangeText is null)
        {
            return;
        }

        Radar.MaxRangeMeters = e.NewValue;
        RadarRangeText.Text = $"{e.NewValue:F0} m";
    }

    private void LiveAnimalsGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (LiveAnimalsGrid.SelectedItem is not LiveAnimalView animal)
        {
            return;
        }

        Radar.SelectedAnimal = animal;
        ShowSelectedAnimal(animal);
    }

    private void SelectLiveAnimal(LiveAnimalView animal)
    {
        LiveAnimalsGrid.SelectedItem = animal;
        LiveAnimalsGrid.ScrollIntoView(animal);
        ShowSelectedAnimal(animal);
    }

    private void ShowSelectedAnimal(LiveAnimalView animal)
    {
        SelectedAnimalTitle.Text =
            $"{animal.DisplaySpecies} · {animal.DistanceMeters:F0} m";

        SelectedAnimalDetails.Text =
            $"Identity : {animal.IdentityText}\n" +
            $"Gender   : {animal.Gender}\n" +
            $"Level    : {animal.Difficulty}\n" +
            $"Trophy   : {animal.Trophy}\n" +
            $"Fur      : {animal.Fur} ({animal.FurRarity})\n" +
            $"Weight   : {animal.Weight:F2}\n" +
            $"Score    : {animal.Score:F2}\n" +
            $"Health   : {animal.Health:F1}/{animal.MaxHealth:F1}\n" +
            $"XYZ      : {animal.X:F1}, {animal.Y:F1}, {animal.Z:F1}\n" +
            $"Seed     : {animal.VisualVariationSeed}";
    }

    private void SetConnected()
    {
        BuildMapsFromGameButton.IsEnabled = true;
        StatusText.Text = $"Connected · PID {_session.ProcessId}";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(187, 247, 208));
        StatusPill.Background = new SolidColorBrush(Color.FromRgb(20, 83, 45));
        StatusPill.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
    }

    private void SetOffline(string message)
    {
        if (!_session.IsAttached)
        {
            BuildMapsFromGameButton.IsEnabled = false;
        }

        StatusText.Text = "Offline";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225));
        StatusPill.Background = new SolidColorBrush(Color.FromRgb(31, 41, 55));
        StatusPill.BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105));
        RuntimeStatusText.Text = message;
    }
}
