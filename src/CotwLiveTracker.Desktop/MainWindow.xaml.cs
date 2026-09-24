using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CotwLiveTracker.Desktop.Controls;
using CotwLiveTracker.Desktop.Maps;
using Microsoft.Win32;
using CotwLiveTracker.Population;
using FontAwesome.Sharp;

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
    private bool _showGroupInfo = true;
    private bool _showCoordinates = true;
    private bool _showDeveloperDetails;

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
            UpdateSpeciesFilters(initialReserve.Index);
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
        Radar.FollowModeChanged = UpdateFollowUi;
        Radar.FollowTargetLost = () =>
        {
            UpdateFollowUi();
            FollowingStatusText.Text = "Target no longer loaded";
            FollowingStatusText.Foreground =
                (Brush)FindResource("WarnBrush");
        };
        UpdateFollowUi();

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
            UpdateSpeciesFilters(reserve.Index);
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

            Radar.CameraX = snapshot.CameraPosition.X;
            Radar.CameraZ = snapshot.CameraPosition.Z;
            ApplyLiveFilters(animals);
            Radar.RefreshFollowing(animals);

            if (_centerMapOnNextSnapshot &&
                Radar.IsMapMode &&
                Radar.FollowMode == RadarFollowMode.None)
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
                else
                {
                    ClearSelectedAnimal();
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

    private void ReserveBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ReserveBox.SelectedItem is ReserveChoice reserve)
        {
            UpdateSpeciesFilters(reserve.Index);
        }
    }

    private void UpdateSpeciesFilters(int reserveIndex)
    {
        var species = new[] { "All" }
            .Concat(DesktopTrackerSession.SpeciesForReserve(reserveIndex))
            .ToArray();

        SetComboItems(LiveSpeciesFilter, species);
        SetComboItems(PopulationSpeciesFilter, species);
    }

    private static void SetComboItems(
        ComboBox combo,
        IReadOnlyList<string> items)
    {
        var previous = combo.SelectedItem as string;
        combo.ItemsSource = items;

        combo.SelectedItem =
            previous is not null &&
            items.Contains(previous, StringComparer.OrdinalIgnoreCase)
                ? items.First(item =>
                    string.Equals(
                        item,
                        previous,
                        StringComparison.OrdinalIgnoreCase))
                : items.FirstOrDefault();

        if (combo.SelectedIndex < 0 &&
            items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private void ApplyLiveFilters_Click(object sender, RoutedEventArgs e) =>
        ApplyLiveFilters(_latestLiveAnimals);

    private void LiveFilters_Changed(object sender, RoutedEventArgs e) =>
        ApplyLiveFilters(_latestLiveAnimals);

    private void ClearLiveFilters_Click(object sender, RoutedEventArgs e)
    {
        LiveSearchFilter.Text = "";
        LiveSpeciesFilter.SelectedIndex = 0;
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
        var species = LiveSpeciesFilter.SelectedItem as string;
        if (string.Equals(
                species,
                "All",
                StringComparison.OrdinalIgnoreCase))
        {
            species = null;
        }

        var search = LiveSearchFilter.Text.Trim();
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
                string.IsNullOrWhiteSpace(search) ||
                animal.DisplaySpecies.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                animal.Fur.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                animal.Trophy.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                animal.Gender.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                animal.Difficulty.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                animal.IdentityText.Contains(search, StringComparison.OrdinalIgnoreCase))
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

        var activeFilters = new List<(string Key, string Label)>();
        if (!string.IsNullOrWhiteSpace(search)) activeFilters.Add(("search", search));
        if (!string.IsNullOrWhiteSpace(species)) activeFilters.Add(("species", species));
        if (trophy is not null) activeFilters.Add(("trophy", trophy));
        if (difficulty is not null) activeFilters.Add(("difficulty", $"Level {difficulty}"));
        if (!string.IsNullOrWhiteSpace(fur)) activeFilters.Add(("fur", fur));
        if (sex is not null) activeFilters.Add(("sex", sex));
        if (LiveRareOnly.IsChecked == true) activeFilters.Add(("rare", "Rare"));
        if (LiveGreatOneOnly.IsChecked == true) activeFilters.Add(("greatone", "Great One"));

        RenderActiveFilterChips(activeFilters);
    }

    private void RenderActiveFilterChips(
        IReadOnlyList<(string Key, string Label)> filters)
    {
        ActiveFilterChipsPanel.Children.Clear();
        NoActiveFiltersText.Visibility =
            filters.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        foreach (var (key, label) in filters)
        {
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            content.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            });
            content.Children.Add(new IconBlock
            {
                Icon = IconChar.Xmark,
                FontSize = 8,
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            var chip = new Button
            {
                Tag = key,
                Content = content,
                Padding = new Thickness(7, 4, 7, 4),
                Margin = new Thickness(0, 0, 5, 5),
                Background = (Brush)FindResource("PanelRaised"),
                BorderBrush = (Brush)FindResource("BorderBrush")
            };
            chip.Click += ActiveFilterChip_Click;
            ActiveFilterChipsPanel.Children.Add(chip);
        }
    }

    private void ActiveFilterChip_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }

        switch (key)
        {
            case "search":
                LiveSearchFilter.Text = "";
                break;
            case "species":
                LiveSpeciesFilter.SelectedIndex = 0;
                break;
            case "trophy":
                LiveTrophyFilter.SelectedIndex = 0;
                break;
            case "difficulty":
                LiveDifficultyFilter.SelectedIndex = 0;
                break;
            case "fur":
                LiveFurFilter.Text = "";
                break;
            case "sex":
                LiveSexFilter.SelectedIndex = 0;
                break;
            case "rare":
                LiveRareOnly.IsChecked = false;
                break;
            case "greatone":
                LiveGreatOneOnly.IsChecked = false;
                break;
        }

        ApplyLiveFilters(_latestLiveAnimals);
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
            levelText.Trim().TrimStart('~'),
            out var level)
            ? level
            : 0;
    }

    private void ApplyPopulationFilters_Click(object sender, RoutedEventArgs e) =>
        ApplyPopulationFilters();

    private void PopulationFilters_Changed(object sender, RoutedEventArgs e) =>
        ApplyPopulationFilters();

    private void ClearPopulationFilters_Click(object sender, RoutedEventArgs e)
    {
        PopulationSpeciesFilter.SelectedIndex = 0;
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
            PopulationMatchText.Text = "0 / 0";
            PopulationDiamondText.Text = "0";
            PopulationRareText.Text = "0";
            PopulationGreatOneText.Text = "0";
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

        var species = PopulationSpeciesFilter.SelectedItem as string;
        if (string.Equals(
                species,
                "All",
                StringComparison.OrdinalIgnoreCase))
        {
            species = null;
        }

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
            $"{rows.Length:N0} / {_session.Population.Animals.Count:N0}";
        PopulationDiamondText.Text = rows.Count(row =>
            string.Equals(row.Trophy, "Diamond", StringComparison.OrdinalIgnoreCase)).ToString("N0");
        PopulationRareText.Text = rows.Count(row =>
            string.Equals(row.Rarity, "Rare", StringComparison.OrdinalIgnoreCase)).ToString("N0");
        PopulationGreatOneText.Text = rows.Count(row =>
            string.Equals(row.Trophy, "Great One", StringComparison.OrdinalIgnoreCase)).ToString("N0");
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
            SettingsMapStatusText.Text = message;
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
                SettingsMapStatusText.Text =
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
            SettingsMapStatusText.Text =
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
            Radar.MapImage = ReserveMapImageStore.Load(
                stored,
                calibration,
                applyCalibrationCrop: false);
            _centerMapOnNextSnapshot = true;
            MapModeStatusText.Text = reserve.Name;
            SettingsMapStatusText.Text =
                $"{reserve.Name} map loaded from a manual image.";
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
        var mapSource = ReserveMapImageStore.ReadSource(reserveIndex);

        Radar.MapCalibration = calibration;
        Radar.MapImage = null;

        if (calibration is null)
        {
            MapModeStatusText.Text = mapPath is null
                ? "Map not cached · calibration pending · relative radar fallback"
                : "Map cached · calibration pending · relative radar fallback";
            LoadMapImageButton.IsEnabled = false;
            SettingsMapStatusText.Text = mapPath is null
                ? "Map cache not found. Build maps from the installed game."
                : "Map cache exists, but this reserve has no verified calibration yet.";
            return;
        }

        LoadMapImageButton.IsEnabled = true;
        if (mapPath is null)
        {
            MapModeStatusText.Text = calibration.ReserveName;
            SettingsMapStatusText.Text =
                "Verified calibration available. Build maps from the game or load an image.";
            return;
        }

        try
        {
            var applyCalibrationCrop =
                !string.Equals(
                    mapSource,
                    "world_map.ddsc",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    mapSource,
                    "manual-image",
                    StringComparison.OrdinalIgnoreCase) &&
                calibration.ImageCropWidth < 0.999999d;

            Radar.MapImage = ReserveMapImageStore.Load(
                mapPath,
                calibration,
                applyCalibrationCrop);
            _centerMapOnNextSnapshot = true;

            var cropStatus = applyCalibrationCrop
                ? $" · calibrated crop {1d / calibration.ImageCropWidth:F3}x"
                : "";

            MapModeStatusText.Text = calibration.ReserveName;
            SettingsMapStatusText.Text =
                $"{calibration.ReserveName} · map ready" +
                (string.IsNullOrWhiteSpace(mapSource)
                    ? ""
                    : $" · source {mapSource}") +
                cropStatus;
        }
        catch (Exception ex)
        {
            Radar.MapImage = null;
            MapModeStatusText.Text = "Map unavailable";
            SettingsMapStatusText.Text =
                $"Saved map failed to load · {ex.Message}";
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

    private void LiveAnimalsGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (LiveAnimalsGrid.SelectedItem is not LiveAnimalView animal)
        {
            return;
        }

        Radar.SelectedAnimal = animal;

        if (Radar.FollowMode == RadarFollowMode.Animal)
        {
            Radar.StartFollowingAnimal(animal);
        }

        ShowSelectedAnimal(animal);
        UpdateFollowUi();
    }

    private void SelectLiveAnimal(LiveAnimalView animal)
    {
        LiveAnimalsGrid.SelectedItem = animal;
        LiveAnimalsGrid.ScrollIntoView(animal);
        ShowSelectedAnimal(animal);
    }

    private void LiveAnimalsGrid_MouseDoubleClick(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (LiveAnimalsGrid.SelectedItem is LiveAnimalView animal)
        {
            Radar.SelectedAnimal = animal;
            Radar.StartFollowingAnimal(animal);
            ShowSelectedAnimal(animal);
        }
    }

    private void ShowSelectedAnimal(LiveAnimalView animal)
    {
        FollowSelectedAnimalButton.IsEnabled = Radar.IsMapMode;

        SelectedAnimalTitle.Text = animal.DisplaySpecies;
        SelectedDistanceText.Text = $"{animal.DistanceMeters:F0} m from player";
        SelectedGenderText.Text = animal.Gender;
        SelectedDifficultyText.Text = animal.Difficulty;
        SelectedTrophyText.Text = animal.Trophy;
        SelectedScoreText.Text = animal.Score > 0f ? $"{animal.Score:F2}" : "—";
        SelectedWeightText.Text = animal.Weight > 0f ? $"{animal.Weight:F2} kg" : "—";
        SelectedHealthText.Text = $"{animal.Health:F0} / {animal.MaxHealth:F0}";

        SelectedFurText.Text = animal.IsRare && animal.FurProbability > 0f
            ? $"{animal.Fur} · {animal.FurProbabilityText}"
            : animal.Fur;

        SelectedThresholdText.Text = animal.NextTrophyText;

        var trophyBrush = TrophyBrush(animal);
        SelectedTrophyBadge.BorderBrush = trophyBrush;
        SelectedTrophyText.Foreground = trophyBrush;

        var technical = new List<string>();
        if (_showGroupInfo)
        {
            technical.Add($"Identity  {animal.IdentityText}");
        }

        if (_showCoordinates)
        {
            technical.Add($"XYZ       {animal.X:F1}, {animal.Y:F1}, {animal.Z:F1}");
        }

        technical.Add($"Seed      {animal.VisualVariationSeed}");
        technical.Add($"Address   0x{animal.Address.ToInt64():X}");
        SelectedTechnicalText.Text = string.Join(Environment.NewLine, technical);
        SelectedTechnicalPanel.Visibility =
            _showDeveloperDetails
                ? Visibility.Visible
                : Visibility.Collapsed;

        UpdateFollowUi();
    }

    private void ClearSelectedAnimal()
    {
        Radar.SelectedAnimal = null;
        LiveAnimalsGrid.SelectedItem = null;
        FollowSelectedAnimalButton.IsEnabled = false;

        SelectedAnimalTitle.Text = "Select an animal";
        SelectedDistanceText.Text = "Click a marker or loaded animal";
        SelectedGenderText.Text = "—";
        SelectedDifficultyText.Text = "—";
        SelectedTrophyText.Text = "—";
        SelectedScoreText.Text = "—";
        SelectedWeightText.Text = "—";
        SelectedFurText.Text = "—";
        SelectedHealthText.Text = "—";
        SelectedThresholdText.Text = "Select an animal to see trophy threshold context.";
        SelectedTechnicalText.Text = "—";
        SelectedTrophyBadge.BorderBrush = (Brush)FindResource("BorderBrush");

        UpdateFollowUi();
    }

    private Brush TrophyBrush(LiveAnimalView animal)
    {
        if (animal.IsGreatOne ||
            ParseDifficultyLevel(animal.Difficulty) >= 10)
        {
            return (Brush)FindResource("DangerBrush");
        }

        if (string.Equals(animal.Trophy, "Diamond", StringComparison.OrdinalIgnoreCase) ||
            ParseDifficultyLevel(animal.Difficulty) >= 9)
        {
            return (Brush)FindResource("DiamondBrush");
        }

        if (string.Equals(animal.Trophy, "Gold", StringComparison.OrdinalIgnoreCase))
        {
            return (Brush)FindResource("GoldBrush");
        }

        if (string.Equals(animal.Trophy, "Silver", StringComparison.OrdinalIgnoreCase))
        {
            return (Brush)FindResource("SilverBrush");
        }

        return animal.IsRare
            ? (Brush)FindResource("RareBrush")
            : (Brush)FindResource("NormalBrush");
    }

    private void InterfaceSettings_Changed(object sender, RoutedEventArgs e)
    {
        Topmost = AlwaysOnTopCheck.IsChecked == true;
        _showGroupInfo = ShowGroupCheck.IsChecked == true;
        _showCoordinates = ShowCoordinatesCheck.IsChecked == true;
        _showDeveloperDetails = ShowDeveloperCheck.IsChecked == true;
        Radar.ShowReferences = ShowOutpostsCheck.IsChecked == true;

        if (Radar.SelectedAnimal is { } selected)
        {
            ShowSelectedAnimal(selected);
        }
        else
        {
            SelectedTechnicalPanel.Visibility =
                _showDeveloperDetails
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }

    private void FollowPlayerButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (Radar.FollowMode == RadarFollowMode.Player)
        {
            Radar.StopFollowing();
            return;
        }

        if (!Radar.StartFollowingPlayer())
        {
            FollowingStatusText.Text =
                "Following requires a calibrated reserve map";
            FollowingStatusText.Foreground =
                (Brush)FindResource("WarnBrush");
        }
    }

    private void FollowSelectedAnimalButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (Radar.SelectedAnimal is not { } animal)
        {
            return;
        }

        if (Radar.FollowMode == RadarFollowMode.Animal &&
            Radar.FollowedAnimal?.Address == animal.Address)
        {
            Radar.StopFollowing();
            return;
        }

        if (!Radar.StartFollowingAnimal(animal))
        {
            FollowingStatusText.Text =
                "Following requires a calibrated reserve map";
            FollowingStatusText.Foreground =
                (Brush)FindResource("WarnBrush");
        }
    }

    private void StopFollowingButton_Click(
        object sender,
        RoutedEventArgs e) =>
        Radar.StopFollowing();

    private void UpdateFollowUi()
    {
        if (FollowingStatusText is null ||
            FollowPlayerButton is null ||
            FollowSelectedAnimalButton is null ||
            StopFollowingButton is null)
        {
            return;
        }

        var accent = (Brush)FindResource("AccentBrush");
        var secondary = (Brush)FindResource("TextSecondary");
        var raised = (Brush)FindResource("PanelRaised");
        var muted = (Brush)FindResource("AccentMuted");
        var border = (Brush)FindResource("BorderStrong");

        var followingPlayer = Radar.FollowMode == RadarFollowMode.Player;
        FollowPlayerButton.Background = followingPlayer ? muted : raised;
        FollowPlayerButton.BorderBrush = followingPlayer ? accent : border;

        StopFollowingButton.IsEnabled =
            Radar.FollowMode != RadarFollowMode.None;

        var selected = Radar.SelectedAnimal;
        var followingSelected =
            Radar.FollowMode == RadarFollowMode.Animal &&
            selected is not null &&
            Radar.FollowedAnimal?.Address == selected.Address;

        FollowSelectedAnimalButton.IsEnabled =
            Radar.IsMapMode &&
            selected is not null;
        FollowSelectedAnimalButton.Background =
            followingSelected ? muted : (Brush)FindResource("PanelRaised");
        FollowSelectedAnimalButton.BorderBrush =
            followingSelected ? accent : border;

        switch (Radar.FollowMode)
        {
            case RadarFollowMode.Player:
                FollowingStatusText.Text = "FOLLOWING PLAYER";
                FollowingStatusText.Foreground = accent;
                break;

            case RadarFollowMode.Animal when Radar.FollowedAnimal is { } animal:
                FollowingStatusText.Text =
                    $"FOLLOWING · {animal.DisplaySpecies} · {animal.Difficulty}";
                FollowingStatusText.Foreground = accent;
                break;

            default:
                FollowingStatusText.Text = "Free navigation";
                FollowingStatusText.Foreground = secondary;
                break;
        }
    }

    private void ProbeDifficultyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var animal = Radar.SelectedAnimal
            ?? LiveAnimalsGrid.SelectedItem as LiveAnimalView;

        if (animal is null)
        {
            MessageBox.Show(
                this,
                "Select a loaded animal first.",
                "Native difficulty probe",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var report = _session.ProbeDifficulty(animal);
            Clipboard.SetText(report);

            var previewLines = report
                .Split(
                    Environment.NewLine,
                    StringSplitOptions.None)
                .Take(48)
                .ToArray();
            var preview = string.Join(
                Environment.NewLine,
                previewLines);

            if (previewLines.Length <
                report.Split(Environment.NewLine).Length)
            {
                preview +=
                    Environment.NewLine +
                    Environment.NewLine +
                    "(Full report copied to clipboard.)";
            }
            else
            {
                preview +=
                    Environment.NewLine +
                    Environment.NewLine +
                    "Copied to clipboard.";
            }

            MessageBox.Show(
                this,
                preview,
                "Native difficulty probe",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Difficulty probe failed: {ex.Message}",
                "Native difficulty probe",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SetConnected()
    {
        BuildMapsFromGameButton.IsEnabled = true;
        StatusText.Text = "COTW CONNECTED";
        StatusText.Foreground = (Brush)FindResource("GoodBrush");
        StatusPill.Background = new SolidColorBrush(Color.FromRgb(18, 31, 20));
        StatusPill.BorderBrush = new SolidColorBrush(Color.FromRgb(49, 91, 57));
    }

    private void SetOffline(string message)
    {
        if (!_session.IsAttached)
        {
            BuildMapsFromGameButton.IsEnabled = false;
        }

        StatusText.Text = "Offline";
        StatusText.Foreground = (Brush)FindResource("TextSecondary");
        StatusPill.Background = (Brush)FindResource("PanelRaised");
        StatusPill.BorderBrush = (Brush)FindResource("BorderBrush");
        RuntimeStatusText.Text = message;
    }
}
