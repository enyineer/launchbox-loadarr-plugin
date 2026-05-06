using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using Loadarr.Sources;

namespace Loadarr.Services
{
    internal enum JobStatus
    {
        Queued,
        Downloading,
        Extracting,
        Importing,
        DownloadingImages,
        Done,
        Failed,
        Cancelled,
    }

    /// <summary>
    /// One entry in the download queue. All "pre-resolution" — URL lookup,
    /// image selection — has already happened on the UI thread before this job
    /// is enqueued, so the worker only does background-friendly work
    /// (HTTP download, archive extract, LaunchBox import, image download).
    /// </summary>
    internal sealed class DownloadJob : INotifyPropertyChanged
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime EnqueuedAt { get; } = DateTime.Now;

        public string Title { get; set; }
        public string Platform { get; set; }
        public string SourceName { get; set; }
        public string Region { get; set; }
        public string Version { get; set; }
        public string DetailsUrl { get; set; }

        public ResolvedDownload Download { get; set; }
        public string TargetDir { get; set; }
        public IReadOnlyList<LaunchBoxMetadataLookup.GameImage> SelectedImages { get; set; }

        public CancellationTokenSource Cts { get; } = new CancellationTokenSource();

        private JobStatus _status = JobStatus.Queued;
        public JobStatus Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value;
                Notify();
                Notify(nameof(StatusText));
                Notify(nameof(IsRunning));
                Notify(nameof(IsFinished));
                Notify(nameof(ActionLabel));
            }
        }

        public string StatusText
        {
            get
            {
                switch (_status)
                {
                    case JobStatus.Queued:            return "Queued";
                    case JobStatus.Downloading:       return "Downloading";
                    case JobStatus.Extracting:        return "Extracting";
                    case JobStatus.Importing:         return "Importing";
                    case JobStatus.DownloadingImages: return "Downloading images";
                    case JobStatus.Done:              return "Done";
                    case JobStatus.Failed:            return "Failed";
                    case JobStatus.Cancelled:         return "Cancelled";
                    default:                          return _status.ToString();
                }
            }
        }

        public bool IsRunning =>
            _status == JobStatus.Downloading ||
            _status == JobStatus.Extracting ||
            _status == JobStatus.Importing ||
            _status == JobStatus.DownloadingImages;

        public bool IsFinished =>
            _status == JobStatus.Done ||
            _status == JobStatus.Failed ||
            _status == JobStatus.Cancelled;

        public string ActionLabel => IsFinished ? "Remove" : "Cancel";

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set { if (Math.Abs(_progressValue - value) < 0.0001) return; _progressValue = value; Notify(); }
        }

        private bool _progressIndeterminate;
        public bool ProgressIndeterminate
        {
            get => _progressIndeterminate;
            set { if (_progressIndeterminate == value) return; _progressIndeterminate = value; Notify(); }
        }

        private string _statusDetail = string.Empty;
        public string StatusDetail
        {
            get => _statusDetail;
            set { if (_statusDetail == value) return; _statusDetail = value; Notify(); }
        }

        private string _errorMessage;
        public string ErrorMessage
        {
            get => _errorMessage;
            set { if (_errorMessage == value) return; _errorMessage = value; Notify(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
