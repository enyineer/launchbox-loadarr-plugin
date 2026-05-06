using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Loadarr.Services;

namespace Loadarr.UI
{
    internal sealed class QueueWindowViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<DownloadJob> Jobs { get; }
        public ICommand JobActionCommand { get; }
        public ICommand ClearFinishedCommand { get; }

        public QueueWindowViewModel()
        {
            Jobs = DownloadQueueService.Instance.Jobs;
            Jobs.CollectionChanged += (_, __) => Notify(nameof(JobCountText));

            JobActionCommand = new RelayCommand(OnJobAction, p => p is DownloadJob);
            ClearFinishedCommand = new RelayCommand(_ => DownloadQueueService.Instance.ClearFinished());
        }

        private static void OnJobAction(object parameter)
        {
            if (!(parameter is DownloadJob job)) return;
            // Single button cycles through actions: cancels active jobs, removes
            // finished or already-cancelled queued jobs.
            if (job.IsFinished) DownloadQueueService.Instance.Remove(job);
            else                DownloadQueueService.Instance.Cancel(job);
        }

        public string JobCountText
        {
            get
            {
                var total = Jobs.Count;
                var running = Jobs.Count(j => j.IsRunning);
                var queued = Jobs.Count(j => j.Status == JobStatus.Queued);
                if (total == 0) return "Queue is empty.";
                return $"{total} total — {running} running, {queued} queued";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
