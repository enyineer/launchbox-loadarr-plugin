using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Loadarr.Services;

namespace Loadarr.UI
{
    internal sealed class ImageSelectionViewModel
    {
        // Types pre-checked when the dialog opens — same defaults the silent
        // downloader used to use, so clicking "Download" with no changes keeps
        // the previous behavior.
        private static readonly string[] DefaultTypes =
        {
            "Box - Front",
            "Clear Logo",
            "Screenshot - Gameplay",
        };

        public string Header { get; }
        public string Subtitle { get; }
        public ObservableCollection<ImageOption> Options { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }

        public ImageSelectionViewModel(string gameTitle,
                                       string preferredRegion,
                                       IReadOnlyList<LaunchBoxMetadataLookup.GameImage> images)
        {
            Header = "Images for \"" + gameTitle + "\"";
            var regionPrefs = RegionPreferenceFor(preferredRegion);
            var prefStr = string.IsNullOrWhiteSpace(preferredRegion) ? "any region" : preferredRegion;
            Subtitle = images.Count + " images available in the LaunchBox database. " +
                       "Default picks: front box, clear logo, gameplay screenshot — biased toward " + prefStr + ".";

            var defaults = ComputeDefaults(images, regionPrefs);
            Options = new ObservableCollection<ImageOption>(
                images.OrderBy(i => i.Type, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(i => i.Region ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                      .Select(i => new ImageOption(i, defaults.Contains(i))));

            SelectAllCommand = new RelayCommand(_ => SetAll(true));
            DeselectAllCommand = new RelayCommand(_ => SetAll(false));
        }

        // Map the Vimm-style source region (e.g. "USA", "Germany", "Japan,Europe")
        // to an ordered list of LaunchBox-DB region strings to try in priority
        // order. The source region(s) themselves come first verbatim so a
        // "Germany" Vimm row picks the LaunchBox "Germany" image when present;
        // the fallback chain then drifts to the broader continent and finally
        // "World" / unset before other regions, so we never accidentally pick a
        // Japan-only box for a Europe dump when better options exist.
        // Same logic the dialog uses to pre-check checkboxes — exposed so
        // headless callers (BigBox flow) can skip the dialog and pick the
        // same set of images automatically.
        public static IReadOnlyList<LaunchBoxMetadataLookup.GameImage> AutoPickDefaults(
            string sourceRegion,
            IReadOnlyList<LaunchBoxMetadataLookup.GameImage> images)
        {
            var prefs = RegionPreferenceFor(sourceRegion);
            return ComputeDefaults(images, prefs).ToArray();
        }

        private static string[] RegionPreferenceFor(string sourceRegion)
        {
            var sourceParts = (sourceRegion ?? string.Empty)
                .Split(new[] { ',', '/', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            var first = (sourceParts.FirstOrDefault() ?? string.Empty).ToUpperInvariant();
            string[] fallback;
            switch (first)
            {
                case "USA":
                case "US":
                case "U":
                case "NORTH AMERICA":
                case "NORTHAMERICA":
                case "NTSC-U":
                case "CANADA":
                case "MEXICO":
                    fallback = new[] { "North America", "United States", "World", null, "Europe", "Japan" };
                    break;

                case "EUROPE":
                case "EU":
                case "EUR":
                case "E":
                case "PAL":
                case "UK":
                case "UNITED KINGDOM":
                case "GERMANY":
                case "FRANCE":
                case "ITALY":
                case "SPAIN":
                case "NETHERLANDS":
                case "SWEDEN":
                case "FINLAND":
                case "NORWAY":
                case "DENMARK":
                case "PORTUGAL":
                case "GREECE":
                case "POLAND":
                case "RUSSIA":
                case "BELGIUM":
                case "AUSTRIA":
                case "SWITZERLAND":
                    fallback = new[] { "Europe", "United Kingdom", "World", null, "North America", "Japan" };
                    break;

                case "JAPAN":
                case "JP":
                case "J":
                case "JPN":
                case "NTSC-J":
                case "KOREA":
                case "SOUTH KOREA":
                case "CHINA":
                case "TAIWAN":
                case "HONG KONG":
                case "ASIA":
                    fallback = new[] { "Japan", "Asia", "World", null, "North America", "Europe" };
                    break;

                case "AUSTRALIA":
                case "AU":
                case "AUS":
                case "NEW ZEALAND":
                    fallback = new[] { "Australia", "Europe", "World", null, "North America" };
                    break;

                case "BRAZIL":
                case "ARGENTINA":
                    fallback = new[] { "Brazil", "South America", "World", null, "North America", "Europe" };
                    break;

                case "WORLD":
                case "WORLDWIDE":
                case "W":
                    fallback = new[] { "World", null, "North America", "United States", "Europe", "Japan" };
                    break;

                default:
                    fallback = new[] { "North America", "United States", "World", null, "Europe", "Japan" };
                    break;
            }

            // Prepend the literal source region(s) so an exact match wins over
            // any broader fallback, then de-dup while preserving order.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>(sourceParts.Count + fallback.Length);
            foreach (var s in sourceParts)
            {
                if (seen.Add(s)) ordered.Add(s);
            }
            foreach (var s in fallback)
            {
                var key = s ?? "\0null";
                if (seen.Add(key)) ordered.Add(s);
            }
            return ordered.ToArray();
        }

        private static HashSet<LaunchBoxMetadataLookup.GameImage> ComputeDefaults(
            IReadOnlyList<LaunchBoxMetadataLookup.GameImage> images,
            string[] regionPrefs)
        {
            var defaults = new HashSet<LaunchBoxMetadataLookup.GameImage>();
            foreach (var t in DefaultTypes)
            {
                var matches = images
                    .Where(i => string.Equals(i.Type, t, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matches.Count == 0) continue;

                LaunchBoxMetadataLookup.GameImage best = null;
                foreach (var r in regionPrefs)
                {
                    best = matches.FirstOrDefault(m =>
                        string.Equals(m.Region, r, StringComparison.OrdinalIgnoreCase));
                    if (best != null) break;
                }
                defaults.Add(best ?? matches[0]);
            }
            return defaults;
        }

        private void SetAll(bool value)
        {
            foreach (var o in Options) o.IsSelected = value;
        }
    }

    internal sealed class ImageOption : INotifyPropertyChanged
    {
        public LaunchBoxMetadataLookup.GameImage Image { get; }

        public string TypeText => Image.Type;
        public string RegionText => string.IsNullOrEmpty(Image.Region) ? "any region" : Image.Region;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public ImageOption(LaunchBoxMetadataLookup.GameImage image, bool isSelected)
        {
            Image = image;
            _isSelected = isSelected;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
