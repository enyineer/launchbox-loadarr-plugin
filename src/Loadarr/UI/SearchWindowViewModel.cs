using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Loadarr.Services;
using Loadarr.Settings;
using Loadarr.Sources;

namespace Loadarr.UI
{
    internal sealed class SearchWindowViewModel : INotifyPropertyChanged
    {
        // Mutable so we can rebuild the HTTP stack when SearchTimeoutSeconds
        // changes — HttpClient.Timeout is read-only after the first request,
        // so a settings change forces a fresh client. All other settings are
        // read from LoadarrSettings.Current at use time.
        private HttpClient _http;
        private DownloadService _downloads;
        private LaunchBoxImporter _importer;
        private LaunchBoxImageDownloader _imageDownloader;
        private IReadOnlyList<IRomSource> _sources;
        private int _httpBuiltWithTimeout;

        private CancellationTokenSource _searchCts;

        public SearchWindowViewModel()
        {
            Loadarr.Services.Log.Enabled = LoadarrSettings.Current.EnableDebugLogging;
            Loadarr.Services.Log.Info("Loadarr search window opened. Log path: " + (Loadarr.Services.Log.Path ?? "<null>"));

            _importer = new LaunchBoxImporter();
            BuildHttpStack(LoadarrSettings.Current.SearchTimeoutSeconds);

            SearchCommand = new RelayCommand(_ => _ = SearchAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(Query));
            DownloadCommand = new RelayCommand(_ => _ = DownloadAndImportAsync(),
                _ => SelectedResult != null && !IsBusy);
            CancelCommand = new RelayCommand(_ => _searchCts?.Cancel(), _ => IsBusy);
            OpenQueueCommand = new RelayCommand(_ => OpenQueueWindow());
        }

        // Builds (or rebuilds) the HttpClient + everything that holds a
        // reference to it. In-flight jobs already running on the OLD client
        // are unaffected — they captured the local reference; only future
        // searches and queued jobs see the new client.
        private void BuildHttpStack(int timeoutSeconds)
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = true };
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Loadarr/0.1 (LaunchBox plugin)");
            _http = http;
            _httpBuiltWithTimeout = timeoutSeconds;
            _downloads = new DownloadService(_http);
            _imageDownloader = new LaunchBoxImageDownloader(_http);
            _sources = SourceRegistry.Build(_http);
            DownloadQueueService.Instance.Initialize(_downloads, _importer, _imageDownloader);
        }

        private void RebuildIfTimeoutChanged()
        {
            var current = LoadarrSettings.Current.SearchTimeoutSeconds;
            if (current != _httpBuiltWithTimeout) BuildHttpStack(current);
        }

        private static QueueWindow _queueWindow;
        private static void OpenQueueWindow()
        {
            if (_queueWindow == null || !_queueWindow.IsLoaded)
            {
                _queueWindow = new QueueWindow
                {
                    Owner = System.Windows.Application.Current?.Windows
                        .OfType<System.Windows.Window>()
                        .FirstOrDefault(w => w.IsActive),
                };
                _queueWindow.Closed += (_, __) => _queueWindow = null;
                _queueWindow.Show();
            }
            else
            {
                _queueWindow.Activate();
            }
        }

        // ---- bindable properties ----

        private string _query;
        public string Query
        {
            get => _query;
            set { _query = value; OnPropertyChanged(); }
        }

        private string _platformHint;
        public string PlatformHint
        {
            get => _platformHint;
            set { _platformHint = value; OnPropertyChanged(); }
        }

        public ObservableCollection<RomSearchResult> Results { get; } = new ObservableCollection<RomSearchResult>();

        private RomSearchResult _selectedResult;
        public RomSearchResult SelectedResult
        {
            get => _selectedResult;
            set { _selectedResult = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBusy));
                // Re-query Search/Download/Cancel CanExecute so their enabled
                // state reflects the new IsBusy without waiting for the next
                // focus change.
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
        public bool IsNotBusy => !IsBusy;

        private string _statusText = "Ready.";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        private bool _progressIndeterminate;
        public bool ProgressIndeterminate
        {
            get => _progressIndeterminate;
            set { _progressIndeterminate = value; OnPropertyChanged(); }
        }

        public ICommand SearchCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand OpenQueueCommand { get; }

        // Hook for choosing which images to download. Default = open the mouse-
        // driven dialog. The BigBox flow swaps in an auto-pick implementation
        // (same defaults as the dialog) so the user isn't forced to navigate a
        // checkbox grid with a controller.
        // Returns null = user cancelled (abort enqueue); empty list = no images.
        public Func<RomSearchResult, IReadOnlyList<LaunchBoxMetadataLookup.GameImage>,
                    IReadOnlyList<LaunchBoxMetadataLookup.GameImage>> ImagePicker { get; set; }
            = DefaultImagePicker;

        private static IReadOnlyList<LaunchBoxMetadataLookup.GameImage> DefaultImagePicker(
            RomSearchResult result, IReadOnlyList<LaunchBoxMetadataLookup.GameImage> available)
        {
            var dialog = new ImageSelectionWindow(result.Title, result.Region, available)
            {
                Owner = System.Windows.Application.Current?.Windows
                    .OfType<System.Windows.Window>()
                    .FirstOrDefault(w => w.IsActive)
            };
            return dialog.ShowDialog() == true ? dialog.SelectedImages : null;
        }

        // ---- workflow ----

        private async Task SearchAsync()
        {
            RebuildIfTimeoutChanged();

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            IsBusy = true;
            ProgressIndeterminate = true;
            StatusText = $"Searching {_sources.Count} source(s) for \"{Query}\"…";
            Results.Clear();

            try
            {
                var tasks = _sources.Select(s => SafeSearch(s, Query, PlatformHint, ct)).ToArray();
                var allResults = await Task.WhenAll(tasks).ConfigureAwait(true);
                foreach (var batch in allResults)
                    foreach (var r in batch)
                        Results.Add(r);
                StatusText = $"Found {Results.Count} result(s).";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Search cancelled.";
            }
            catch (Exception ex)
            {
                Loadarr.Services.Log.Error("Search failed", ex);
                StatusText = $"Search failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                ProgressIndeterminate = false;
            }
        }

        private static async Task<IReadOnlyList<RomSearchResult>> SafeSearch(
            IRomSource src, string query, string platform, CancellationToken ct)
        {
            try { return await src.SearchAsync(query, platform, ct).ConfigureAwait(false); }
            catch (Exception ex)
            {
                Loadarr.Services.Log.Error("Source \"" + src.Name + "\" SearchAsync threw", ex);
                return Array.Empty<RomSearchResult>();
            }
        }

        // Resolves URL + images on the foreground (so the user can answer the
        // selection dialog and see any URL-resolution errors immediately), then
        // hands the rest off to DownloadQueueService and returns. The search
        // window stays responsive for the next search/queue.
        private async Task DownloadAndImportAsync()
        {
            if (SelectedResult == null) return;
            RebuildIfTimeoutChanged();
            var result = SelectedResult;
            var source = _sources.FirstOrDefault(s => s.Name == result.SourceName);
            if (source == null) { StatusText = "Source not available."; return; }

            // Image selection — only when the LB metadata DB has images. The
            // ImagePicker hook decides whether to prompt (LaunchBox: dialog) or
            // auto-pick defaults (BigBox: no dialog, controller-friendly).
            IReadOnlyList<LaunchBoxMetadataLookup.GameImage> selectedImages = null;
            var preMatch = LaunchBoxImporter.MetadataLookup.Find(result.Title, result.Platform);
            if (preMatch?.DatabaseId != null)
            {
                var available = LaunchBoxImporter.MetadataLookup.FindImages(preMatch.DatabaseId.Value);
                if (available.Count > 0)
                {
                    selectedImages = ImagePicker?.Invoke(result, available);
                    if (selectedImages == null) { StatusText = "Cancelled."; return; }
                }
            }

            IsBusy = true;
            ProgressIndeterminate = true;
            StatusText = "Resolving download URL…";

            try
            {
                var dl = await source.GetDownloadAsync(result, CancellationToken.None).ConfigureAwait(true);
                var platformDir = StringNormalize.SafeFileName(string.IsNullOrEmpty(result.Platform) ? "Unknown" : result.Platform);
                // Read at use time so a Settings-window save between selecting
                // a result and confirming the picker takes effect immediately.
                var targetDir = Path.Combine(LoadarrSettings.Current.EffectiveDownloadDirectory, platformDir);

                DownloadQueueService.Instance.Enqueue(new DownloadJob
                {
                    Title = result.Title,
                    Platform = result.Platform,
                    SourceName = result.SourceName,
                    Region = result.Region,
                    Version = result.Version,
                    DetailsUrl = result.DetailsUrl,
                    Download = dl,
                    TargetDir = targetDir,
                    SelectedImages = selectedImages,
                });

                StatusText = $"Queued \"{result.Title}\" — open Queue to monitor.";
            }
            catch (Exception ex)
            {
                Loadarr.Services.Log.Error("Enqueue failed for \"" + result.Title + "\"", ex);
                StatusText = $"Failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                ProgressIndeterminate = false;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
