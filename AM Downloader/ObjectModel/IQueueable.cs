using System.Threading.Tasks;

namespace AMDownloader.ObjectModel.Queue
{
    interface IQueueable
    {
        public Task StartAsync();
        public void Pause();
        public bool IsQueued { get; }
        public bool IsCompleted { get; }
    }
}
