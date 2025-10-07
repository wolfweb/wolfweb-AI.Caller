namespace AI.Caller.Phone.Services;
public interface IBatchProcessor {
    Task ProcessBatchJob(int batchJobId);
}