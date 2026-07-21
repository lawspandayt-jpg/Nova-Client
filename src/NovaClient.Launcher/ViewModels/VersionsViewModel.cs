using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using NovaClient.Core.Logging;
using NovaClient.Launcher.Common;

namespace NovaClient.Launcher.ViewModels;

public sealed class VersionTile : ViewModelBase
{
    public required string Id { get; init; }
    public required string Title { get; init; }          // e.g. "NOVA 1.21"
    public required string Subtitle { get; init; }       // e.g. "Minecraft 1.21.11"
    public required Brush Background { get; init; }
    public required bool IsNova { get; init; }
    public required RelayCommand SelectCommand { get; init; }
    public RelayCommand? BadgeCommand { get; init; }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    private string _badgeText = "";
    public string BadgeText { get => _badgeText; set => Set(ref _badgeText, value); }

    private bool _badgeActive;
    public bool BadgeActive { get => _badgeActive; set => Set(ref _badgeActive, value); }
}

/// <summary>The Versions tab: one card per supported major version, Lunar-style grid.</summary>
public sealed class VersionsViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    public ObservableCollection<VersionTile> Tiles { get; } = new();

    // Original per-version gradient hues (top, bottom).
    private static readonly (string Top, string Bottom)[] Palette =
    {
        ("#3B2D6E", "#151226"), ("#1F4B6E", "#0E1826"), ("#1E5E52", "#0C1F1B"),
        ("#5E2D6E", "#1D0F26"), ("#6E4A1F", "#261A0C"), ("#2D4A6E", "#0F1826"),
        ("#6E2D3A", "#260F14"), ("#2D6E3E", "#0F2615"), ("#44506E", "#131826"),
    };

    public VersionsViewModel(MainViewModel main)
    {
        _main = main;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var releases = await _main.Services.Launcher.Installer.GetReleaseVersionsAsync();
            var curated = new List<string>();
            void AddLatestOf(string prefix)
            {
                var match = releases.FirstOrDefault(id => id == prefix || id.StartsWith(prefix + "."));
                if (match is not null && !curated.Contains(match)) curated.Add(match);
            }
            AddLatestOf("1.21");
            AddLatestOf("1.20");
            foreach (var anchor in new[] { "1.19.4", "1.18.2", "1.17.1", "1.16.5", "1.12.2", "1.8.9", "1.7.10" })
                if (releases.Contains(anchor) && !curated.Contains(anchor)) curated.Add(anchor);
            if (curated.Count == 0) curated.Add("1.8.9");

            await Application.Current.Dispatcher.InvokeAsync(() => BuildTiles(curated));
        }
        catch (Exception ex)
        {
            NovaLog.Warn("Versions", $"Version tiles unavailable: {ex.Message}");
            await Application.Current.Dispatcher.InvokeAsync(() => BuildTiles(new List<string> { "1.8.9" }));
        }
    }

    private void BuildTiles(List<string> ids)
    {
        Tiles.Clear();
        var settings = _main.Services.Settings;
        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            var isNova = id == "1.8.9";
            var major = MajorOf(id);
            var (top, bottom) = Palette[i % Palette.Length];
            var brush = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString(top),
                (Color)ColorConverter.ConvertFromString(bottom), 90);
            brush.Freeze();

            var tile = new VersionTile
            {
                Id = id,
                Title = $"NOVA {major}",
                Subtitle = $"Minecraft {id}",
                Background = brush,
                IsNova = isNova,
                SelectCommand = new RelayCommand(() => Select(id)),
                BadgeCommand = new RelayCommand(() =>
                {
                    if (isNova)
                    {
                        _main.ShowOptiFineSetup();
                    }
                    else
                    {
                        settings.Current.UseFabric = !settings.Current.UseFabric;
                        settings.Save();
                        RefreshBadges();
                    }
                }),
            };
            Tiles.Add(tile);
        }
        RefreshBadges();
    }

    private void Select(string id)
    {
        var settings = _main.Services.Settings;
        settings.Current.SelectedVersion = id;
        settings.Save();
        RefreshBadges();
        _main.ShowHome(); // downloads happen when Launch is pressed
    }

    private void RefreshBadges()
    {
        var settings = _main.Services.Settings.Current;
        var optifineInstalled = _main.Services.Launcher.OptiFine.DetectInstalled() is not null;
        foreach (var tile in Tiles)
        {
            tile.IsSelected = tile.Id == settings.SelectedVersion;
            if (tile.IsNova)
            {
                tile.BadgeText = optifineInstalled ? "OPTIFINE ✓" : "OPTIFINE";
                tile.BadgeActive = optifineInstalled;
            }
            else
            {
                tile.BadgeText = "FABRIC";
                tile.BadgeActive = settings.UseFabric;
            }
        }
    }

    private static string MajorOf(string id)
    {
        var parts = id.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : id;
    }
}
