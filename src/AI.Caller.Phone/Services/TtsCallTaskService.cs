using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace AI.Caller.Phone.Services {
    public class TtsCallTaskService : ITtsCallTaskService {        
        private readonly ILogger<TtsCallTaskService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ITtsTemplateIntegrationService _integrationService;
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _runningTasks = new();

        public TtsCallTaskService(
            ILogger<TtsCallTaskService> logger,
            IServiceScopeFactory serviceScopeFactory,
            ITtsTemplateIntegrationService integrationService
            ) {
            _logger = logger;
            _integrationService = integrationService;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task ExecuteCallTaskAsync(int documentId) {
            var cancellationTokenSource = new CancellationTokenSource();
            _runningTasks.TryAdd(documentId, cancellationTokenSource);

            try {
                await ProcessCallTaskAsync(documentId, cancellationTokenSource.Token);
            } catch (OperationCanceledException) {
                _logger.LogInformation($"TTS外呼任务被取消: DocumentId={documentId}");
            } catch (Exception ex) {
                _logger.LogError(ex, $"TTS外呼任务执行失败: DocumentId={documentId}");
                await UpdateDocumentStatusAsync(documentId, TtsCallTaskStatus.Failed);
            } finally {
                _runningTasks.TryRemove(documentId, out _);
            }
        }

        public async Task PauseTaskAsync(int documentId) {
            if (_runningTasks.TryGetValue(documentId, out var cancellationTokenSource)) {
                cancellationTokenSource.Cancel();
                _runningTasks.TryRemove(documentId, out _);
                _logger.LogInformation($"TTS外呼任务暂停: DocumentId={documentId}");
            }
        }

        public async Task ResumeTaskAsync(int documentId) {
            // 重新启动任务
            _ = Task.Run(() => ExecuteCallTaskAsync(documentId));
            _logger.LogInformation($"TTS外呼任务恢复: DocumentId={documentId}");
        }

        public async Task StopTaskAsync(int documentId) {
            if (_runningTasks.TryGetValue(documentId, out var cancellationTokenSource)) {
                cancellationTokenSource.Cancel();
                _runningTasks.TryRemove(documentId, out _);
            }
            await UpdateDocumentStatusAsync(documentId, TtsCallTaskStatus.Cancelled);
            _logger.LogInformation($"TTS外呼任务停止: DocumentId={documentId}");
        }

        public async Task<bool> IsTaskRunningAsync(int documentId) {
            return _runningTasks.ContainsKey(documentId);
        }

        private async Task ProcessCallTaskAsync(int documentId, CancellationToken cancellationToken) {
            using var scope = _serviceScopeFactory.CreateScope();
            AppDbContext _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var document = await _context.TtsCallDocuments
                .Include(d => d.CallRecords)
                .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

            if (document == null) {
                _logger.LogWarning($"文档不存在: DocumentId={documentId}");
                return;
            }

            _logger.LogInformation($"开始处理TTS外呼任务: DocumentId={documentId}, 总记录数={document.TotalRecords}");

            var pendingRecords = document.CallRecords
                .Where(r => r.CallStatus == TtsCallStatus.Pending)
                .ToList();

            foreach (var record in pendingRecords) {
                cancellationToken.ThrowIfCancellationRequested();

                try {
                    await ProcessSingleCallAsync(record, cancellationToken);
                    await UpdateCallRecordAsync(record.Id, TtsCallStatus.Completed, null);
                    
                    // 更新文档统计
                    document.CompletedCalls++;
                } catch (Exception ex) {
                    _logger.LogError(ex, $"处理单个呼叫失败: RecordId={record.Id}, Phone={record.PhoneNumber}");
                    await UpdateCallRecordAsync(record.Id, TtsCallStatus.Failed, ex.Message);
                    
                    // 更新文档统计
                    document.FailedCalls++;
                }

                // 保存进度
                await _context.SaveChangesAsync(cancellationToken);

                // 呼叫间隔（避免过于频繁）
                await Task.Delay(2000, cancellationToken);
            }

            // 任务完成
            await UpdateDocumentStatusAsync(documentId, TtsCallTaskStatus.Completed);
            _logger.LogInformation($"TTS外呼任务完成: DocumentId={documentId}");
        }

        private async Task ProcessSingleCallAsync(TtsCallRecord record, CancellationToken cancellationToken) {
            _logger.LogInformation($"开始呼叫: {record.PhoneNumber}");

            using var scope = _serviceScopeFactory.CreateScope();
            AppDbContext _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // 更新呼叫状态为正在呼叫
            await UpdateCallRecordAsync(record.Id, TtsCallStatus.Calling, null);

            try {
                // 获取文档所属用户ID
                var document = await _context.TtsCallDocuments
                    .FirstOrDefaultAsync(d => d.Id == record.DocumentId, cancellationToken);
                
                if (document == null) {
                    throw new Exception("找不到关联的文档");
                }

                // 准备外呼脚本
                var script = await _integrationService.PrepareOutboundScriptAsync(record, document.UserId);

                // 执行外呼
                OutboundCallResult? result = null;
                
                // 处理呼叫结果
                await ProcessCallResultAsync(record, result);
                
            } catch (Exception ex) {
                _logger.LogError(ex, $"处理呼叫失败: {record.PhoneNumber}");
                throw;
            }
        }



        private async Task ProcessCallResultAsync(TtsCallRecord record, OutboundCallResult result) {
            try {
                // 更新呼叫记录
                await UpdateCallRecordAsync(record.Id, result.Status, result.FailureReason);
                
                // 记录呼叫时间
                record.CallTime = result.CallTime;
                
                // 记录额外信息
                if (result.Metadata.Any()) {
                    var metadataJson = System.Text.Json.JsonSerializer.Serialize(result.Metadata);
                    _logger.LogDebug($"呼叫元数据: {record.PhoneNumber} -> {metadataJson}");
                }
                
                if (result.Success) {
                    _logger.LogInformation($"外呼成功: {record.PhoneNumber}, 时长: {result.Duration.TotalSeconds:F1}秒");
                } else {
                    _logger.LogWarning($"外呼失败: {record.PhoneNumber}, 原因: {result.FailureReason}");
                    
                    // 如果失败且可以重试，增加重试计数
                    if (ShouldRetry(result.Status, record.RetryCount)) {
                        record.RetryCount++;
                        await UpdateCallRecordAsync(record.Id, TtsCallStatus.Pending, null);
                        _logger.LogInformation($"标记重试: {record.PhoneNumber}, 重试次数: {record.RetryCount}");
                    }
                }
                
            } catch (Exception ex) {
                _logger.LogError(ex, $"处理呼叫结果失败: {record.PhoneNumber}");
            }
        }
        
        private bool ShouldRetry(TtsCallStatus status, int currentRetryCount) {
            // 最多重试2次
            if (currentRetryCount >= 2) return false;
            
            // 只对特定状态进行重试
            return status switch {
                TtsCallStatus.Busy => true,
                TtsCallStatus.NoAnswer => true,
                TtsCallStatus.Failed => true,
                _ => false
            };
        }

        private async Task UpdateCallRecordAsync(int recordId, TtsCallStatus status, string? failureReason) {
            using var scope = _serviceScopeFactory.CreateScope();
            AppDbContext _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await _context.TtsCallRecords.FindAsync(recordId);
            if (record != null) {
                record.CallStatus = status;
                record.CallTime = DateTime.UtcNow;
                record.FailureReason = failureReason;
                
                if (status == TtsCallStatus.Failed) {
                    record.RetryCount++;
                }
            }
        }

        private async Task UpdateDocumentStatusAsync(int documentId, TtsCallTaskStatus status) {
            using var scope = _serviceScopeFactory.CreateScope();
            AppDbContext _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var document = await _context.TtsCallDocuments.FindAsync(documentId);
            if (document != null) {
                document.Status = status;
                if (status == TtsCallTaskStatus.Completed || status == TtsCallTaskStatus.Failed || status == TtsCallTaskStatus.Cancelled) {
                    document.EndTime = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync();
            }
        }
    }
}