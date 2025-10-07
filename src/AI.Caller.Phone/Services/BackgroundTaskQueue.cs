using System.Threading.Channels;

namespace AI.Caller.Phone.Services;

public class BackgroundTaskQueue : IBackgroundTaskQueue {
    private readonly Channel<Func<CancellationToken, IServiceProvider, Task>> _queue;

    public BackgroundTaskQueue(int capacity) {
        var options = new BoundedChannelOptions(capacity) {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<Func<CancellationToken, IServiceProvider, Task>>(options);
    }

    public void QueueBackgroundWorkItem(Func<CancellationToken, IServiceProvider, Task> workItem) {
        if (workItem == null) {
            throw new ArgumentNullException(nameof(workItem));
        }

        if (!_queue.Writer.TryWrite(workItem)) {
            throw new InvalidOperationException("Failed to write to the queue.");
        }
    }

    public async Task<Func<CancellationToken, IServiceProvider, Task>> DequeueAsync(CancellationToken cancellationToken) {
        var workItem = await _queue.Reader.ReadAsync(cancellationToken);
        return workItem;
    }
}