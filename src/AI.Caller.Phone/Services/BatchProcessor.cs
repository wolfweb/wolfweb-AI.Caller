using AI.Caller.Phone.Entities;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace AI.Caller.Phone.Services;

public class BatchProcessor : IBatchProcessor {
    private readonly ILogger<BatchProcessor> _logger;
    private readonly AppDbContext _context;
    private readonly IVariableResolverService _variableResolver;
    private readonly IBackgroundTaskQueue _taskQueue;

    public BatchProcessor(ILogger<BatchProcessor> logger, AppDbContext context, IVariableResolverService variableResolver, IBackgroundTaskQueue taskQueue) {
        _logger = logger;
        _context = context;
        _variableResolver = variableResolver;
        _taskQueue = taskQueue;
    }

    public async Task ProcessBatchJob(int batchJobId) {
        var batchJob = await _context.BatchCallJobs.FindAsync(batchJobId);
        if (batchJob == null) {
            _logger.LogWarning("BatchCallJob with ID {BatchJobId} not found for processing.", batchJobId);
            return;
        }

        try {
            batchJob.Status = Entities.BatchJobStatus.Preprocessing;
            await _context.SaveChangesAsync();

            var template = await _context.TtsTemplates.FindAsync(batchJob.TtsTemplateId);
            if (template == null) {
                throw new InvalidOperationException($"TTS Template with ID {batchJob.TtsTemplateId} not found.");
            }

            var records = new List<Dictionary<string, string>>();
            using (var fs = new FileStream(batchJob.StoredFilePath, FileMode.Open, FileAccess.Read)) {
                IWorkbook workbook = new XSSFWorkbook(fs);
                ISheet sheet = workbook.GetSheetAt(0); // Get the first sheet

                IRow headerRow = sheet.GetRow(0);
                if (headerRow == null) {
                    throw new InvalidOperationException("The Excel file is missing a header row.");
                }
                var headers = new List<string>();
                foreach (ICell headerCell in headerRow) {
                    headers.Add(headerCell.StringCellValue);
                }

                for (int i = (sheet.FirstRowNum + 1); i <= sheet.LastRowNum; i++) {
                    IRow row = sheet.GetRow(i);
                    if (row == null) continue;

                    var record = new Dictionary<string, string>();
                    for (int j = 0; j < headers.Count; j++) {
                        ICell cell = row.GetCell(j);
                        string cellValue = cell?.ToString() ?? "";
                        record[headers[j]] = cellValue;
                    }
                    records.Add(record);
                }
            }

            batchJob.TotalCount = records.Count;
            await _context.SaveChangesAsync();

            foreach (var record in records) {
                if (!record.TryGetValue("PhoneNumber", out var phoneNumber) || string.IsNullOrWhiteSpace(phoneNumber)) {
                    _logger.LogWarning("Skipping record in BatchJobId {BatchJobId} due to missing PhoneNumber.", batchJobId);
                    continue;
                }

                var variables = record.Where(kvp => kvp.Key != "PhoneNumber").ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                string resolvedContent = _variableResolver.Resolve(template.Content, variables);

                var callLog = new CallLog {
                    PhoneNumber = phoneNumber,
                    ResolvedContent = resolvedContent,
                    Status = Entities.CallStatus.Queued,
                    InitiationType = CallInitiationType.Batch,
                    BatchCallJobId = batchJob.Id,
                    CreatedAt = DateTime.UtcNow
                };
                _context.CallLogs.Add(callLog);
            }
            await _context.SaveChangesAsync();

            var enqueuedCallLogs = await _context.CallLogs
                .Where(l => l.BatchCallJobId == batchJobId && l.Status == Entities.CallStatus.Queued)
                .ToListAsync();

            foreach (var callLog in enqueuedCallLogs) {
                _taskQueue.QueueBackgroundWorkItem((token, serviceProvider) => {
                    var callProcessor = serviceProvider.GetRequiredService<ICallProcessor>();
                    return callProcessor.ProcessCallLogJob(callLog.Id);
                });
            }

            batchJob.Status = Entities.BatchJobStatus.Processing;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully enqueued {Count} call tasks for BatchJobId {BatchJobId}.", enqueuedCallLogs.Count, batchJobId);
        } catch (Exception ex) {
            _logger.LogError(ex, "An error occurred while processing BatchJobId {BatchJobId}.", batchJobId);
            batchJob.Status = Entities.BatchJobStatus.Failed;
            await _context.SaveChangesAsync();
        }
    }
}