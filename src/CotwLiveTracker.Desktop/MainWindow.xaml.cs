using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CotwLiveTracker.Population;

namespace CotwLiveTracker.Desktop;

public partial class MainWindow : Window
{
    private readonly DesktopTrackerSession _session = new();
    private readonly DispatcherTimer _refreshTimer;
    private DesktopSnapshot? _lastSnapshot;

    public MainWindow()
    {
        InitializeComponent();

        ReserveBox.ItemsSource = DesktopTrackerSession.Reserves;
        ReserveBox.SelectedItem = DesktopTrackerSession.Reserves
            .FirstOrDefault(reserve => reserve.Index == 1)
            ?? DesktopTrackerSession.Reserves.FirstOrDefault();

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

        Radar.AnimalSelected = SelectLiveAnimal;

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
            _lastSnapshot = snapshot;
            var animals = snapshot.Animals;

            LiveAnimalsGrid.ItemsSource = animals;
            DashboardAnimalsGrid.ItemsSource = animals.Take(10).ToArray();

            Radar.Animals = animals;
            Radar.MaxRangeMeters = RadarRangeSlider.Value;

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
        StatusText.Text = $"Connected · PID {_session.ProcessId}";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(187, 247, 208));
        StatusPill.Background = new SolidColorBrush(Color.FromRgb(20, 83, 45));
        StatusPill.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
    }

    private void SetOffline(string message)
    {
        StatusText.Text = "Offline";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225));
        StatusPill.Background = new SolidColorBrush(Color.FromRgb(31, 41, 55));
        StatusPill.BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105));
        RuntimeStatusText.Text = message;
    }
}
