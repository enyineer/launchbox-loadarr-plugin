using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Loadarr.Settings;
using Loadarr.Sources;

namespace Loadarr.Services
{
    /// <summary>
    /// App-wide singleton that runs queued ROM downloads serially in the
    /// background while the search window stays responsive. Single-worker by
    /// default — multiple parallel HTTP downloads against e.g. Vimm's would
    /// hammer one host and risk being rate-limited or rejected.
    ///
    /// The worker runs on the WPF dispatcher thread; await calls release the
    /// UI between I/O bursts, so ObservableCollection and INotifyPropertyChanged
    /// updates are inherently UI-thread-safe.
    /// </summary>
    internal sealed class DownloadQueueService
    {
        public static DownloadQueueService Instance { get; } = new DownloadQueueService();

        public ObservableCollection<DownloadJob> Jobs { get; } = new ObservableCollection<DownloadJob>();

        private DownloadService _downloads;
        private LaunchBoxImporter _importer;
        private LaunchBoxImageDownloader _imageDownloader;

        private bool _workerRunning;

        private DownloadQueueService() { }

        public void Initialize(
            DownloadService downloads,
            LaunchBoxImporter importer,
            LaunchBoxImageDownloader imageDownloader)
        {
            _downloads = downloads;
            _importer = importer;
            _imageDownloader = imageDownloader;
        }

        public void Enqueue(DownloadJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            Jobs.Add(job);
            Log.Info("Queue: enqueued \"" + job.Title + "\" (" + (job.Platform ?? "Unknown") + ")");
            EnsureWorker();
        }

        public void Cancel(DownloadJob job)
        {
            if (job == null) return;
            if (job.Status == JobStatus.Queued)
            {
                job.Status = JobStatus.Cancelled;
                Log.Info("Queue: cancelled queued job \"" + job.Title + "\"");
            }
            else if (!job.IsFinished)
            {
                try { job.Cts.Cancel(); } catch { }
            }
        }

        public void Remove(DownloadJob job)
        {
            if (job == null) return;
            if (!job.IsFinished) Cancel(job);
            Jobs.Remove(job);
        }

        public void ClearFinished()
        {
            for (int i = Jobs.Count - 1; i >= 0; i--)
                if (Jobs[i].IsFinished) Jobs.RemoveAt(i);
        }

        private void EnsureWorker()
        {
            if (_workerRunning) return;
            _workerRunning = true;
            _ = WorkerLoop();
        }

        private async Task WorkerLoop()
        {
            try
            {
                while (true)
                {
                    var job = Jobs.FirstOrDefault(j => j.Status == JobStatus.Queued);
                    if (job == null) return;

                    try
                    {
                        await ProcessJobAsync(job).ConfigureAwait(true);
                    }
                    catch (OperationCanceledException)
                    {
                        if (job.Status != JobStatus.Cancelled) job.Status = JobStatus.Cancelled;
                        Log.Info("Queue: \"" + job.Title + "\" cancelled.");
                    }
                    catch (Exception ex)
                    {
                        job.Status = JobStatus.Failed;
                        job.ErrorMessage = ex.Message;
                        Log.Error("Queue: \"" + job.Title + "\" failed", ex);
                    }
                }
            }
            finally
            {
                _workerRunning = false;
            }
        }

        private async Task ProcessJobAsync(DownloadJob job)
        {
            var ct = job.Cts.Token;

            // 1. Download ROM
            job.Status = JobStatus.Downloading;
            job.ProgressIndeterminate = false;
            job.ProgressValue = 0;
            job.StatusDetail = "Starting…";

            // Progress<T> dispatches callbacks via SynchronizationContext.Post,
            // which can deliver the FINAL download progress event AFTER our
            // await returns. Without this guard, that late callback overwrites
            // the "Extracting…" status we set below with stale download bytes.
            bool downloadDone = false;
            var progress = new Progress<DownloadProgress>(p =>
            {
                if (downloadDone) return;
                if (p.Percent.HasValue) job.ProgressValue = p.Percent.Value;
                job.StatusDetail =
                    $"{p.BytesDownloaded:n0} bytes" +
                    (p.TotalBytes.HasValue ? $" / {p.TotalBytes:n0}" : "");
            });

            var path = await _downloads.DownloadAsync(
                job.Download.Url, job.Download.FileName, job.TargetDir,
                job.Download.Headers, progress, ct).ConfigureAwait(true);
            downloadDone = true;
            Log.Info("Queue: downloaded to \"" + path + "\"");

            // 2. Extract the primary ROM out of the archive (most ROM archives
            //    bundle a readme alongside the actual ROM, so we always extract
            //    the largest non-junk entry rather than only single-entry zips).
            //    Skipped for arcade platforms where the .zip itself IS the ROM.
            if (LoadarrSettings.Current.ExtractDownloadedArchives && ArchiveExtractor.IsArchive(path))
            {
                if (!ArchiveExtractor.ShouldExtractFor(job.Platform))
                {
                    Log.Info("Queue: keeping archive intact for arcade platform \"" + job.Platform + "\".");
                }
                else
                {
                    job.Status = JobStatus.Extracting;
                    job.ProgressIndeterminate = false;
                    job.ProgressValue = 0;
                    job.StatusDetail = "Extracting archive…";

                    // Marshal progress reports back to the dispatcher so the
                    // UI updates even though extraction runs on a worker.
                    var extractProgress = new Progress<double>(pct =>
                    {
                        job.ProgressValue = pct;
                        job.StatusDetail = $"Extracting… {pct:0}%";
                    });
                    var extractDir = Path.Combine(job.TargetDir, "extracted");
                    var inner = await Task.Run(
                        () => ArchiveExtractor.ExtractPrimary(path, extractDir, extractProgress),
                        ct).ConfigureAwait(true);
                    if (!string.Equals(inner, path, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Info("Queue: extracted ROM at \"" + inner + "\" (was \"" + path + "\")");
                        path = inner;
                    }
                    else
                    {
                        Log.Warn("Queue: extraction returned the original archive path — extraction may have failed.");
                    }
                }
            }

            // 3. Import to LaunchBox (synchronous SDK call)
            ct.ThrowIfCancellationRequested();
            job.Status = JobStatus.Importing;
            job.ProgressIndeterminate = true;
            job.StatusDetail = "Adding to LaunchBox…";
            _importer.Import(new LaunchBoxImporter.ImportRequest
            {
                Title = job.Title,
                PlatformName = job.Platform,
                RomFilePath = path,
                Source = job.SourceName,
                Region = job.Region,
                Version = job.Version,
                Notes = $"Added by Loadarr from {job.SourceName} on {DateTime.Now:yyyy-MM-dd HH:mm}\n{job.DetailsUrl}",
            });

            // 4. Download user-selected images
            if (job.SelectedImages != null && job.SelectedImages.Count > 0)
            {
                job.Status = JobStatus.DownloadingImages;
                job.ProgressIndeterminate = true;
                job.StatusDetail = $"Downloading {job.SelectedImages.Count} image(s)…";
                await _imageDownloader.DownloadSelectedAsync(
                    job.Title, job.Platform, job.SelectedImages, ct).ConfigureAwait(true);
                LaunchBoxImporter.RefreshUi();
            }

            job.Status = JobStatus.Done;
            job.ProgressValue = 100;
            job.ProgressIndeterminate = false;
            job.StatusDetail = "Imported.";
        }
    }
}
