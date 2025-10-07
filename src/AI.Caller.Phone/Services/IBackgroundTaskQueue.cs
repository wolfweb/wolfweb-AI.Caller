namespace AI.Caller.Phone.Services;

public interface IBackgroundTaskQueue {
    void QueueBackgroundWorkItem(Func<CancellationToken, IServiceProvider, Task> workItem);

    Task<Func<CancellationToken, IServiceProvider, Task>> DequeueAsync(CancellationToken cancellationToken);
}