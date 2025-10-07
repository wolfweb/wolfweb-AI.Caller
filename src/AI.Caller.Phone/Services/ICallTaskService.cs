using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Services;

public interface ICallTaskService {
    Task<BatchCallJob> CreateBatchCallTaskAsync(string jobName, int templateId, string storedFilePath, string originalFileName, int createdByUserId);
}