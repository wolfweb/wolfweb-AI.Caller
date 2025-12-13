using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Services;

public class CallTaskService : ICallTaskService {
    private readonly AppDbContext _context;
    private readonly IVariableResolverService _variableResolverService;
    private readonly IBackgroundTaskQueue _taskQueue;

    public CallTaskService(AppDbContext context, IVariableResolverService variableResolverService, IBackgroundTaskQueue taskQueue) {
        _context = context;
        _variableResolverService = variableResolverService;
        _taskQueue = taskQueue;
    }

    public async Task<BatchCallJob> CreateBatchCallTaskAsync(string jobName, int? templateId, int? scenarioRecordingId, string storedFilePath, string originalFileName, int createdByUserId, int? selectedLineId = null, bool autoSelectLine = true) {
        var batchJob = new BatchCallJob {
            JobName = jobName,
            TtsTemplateId = templateId,
            ScenarioRecordingId = scenarioRecordingId,
            StoredFilePath = storedFilePath,
            OriginalFileName = originalFileName,
            Status = Entities.BatchJobStatus.Queued,
            CreatedAt = System.DateTime.UtcNow,
            CreatedByUserId = createdByUserId,
            SelectedLineId = selectedLineId,
            AutoSelectLine = autoSelectLine
        };

        _context.BatchCallJobs.Add(batchJob);
        await _context.SaveChangesAsync();

        _taskQueue.QueueBackgroundWorkItem((token, serviceProvider) => {
            var batchProcessor = serviceProvider.GetRequiredService<IBatchProcessor>();
            return batchProcessor.ProcessBatchJob(batchJob.Id);
        });

        return batchJob;
    }
}