using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Phone.Services;

/// <summary>
/// 场景录音服务实现
/// </summary>
public class ScenarioRecordingService : IScenarioRecordingService {
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ScenarioRecordingService> _logger;

    public ScenarioRecordingService(AppDbContext dbContext, ILogger<ScenarioRecordingService> logger) {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ScenarioRecording?> GetScenarioRecordingAsync(int id) {
        try {
            var scenario = await _dbContext.ScenarioRecordings
                .Include(s => s.Segments.OrderBy(seg => seg.SegmentOrder))
                .ThenInclude(seg => seg.DtmfTemplate)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (scenario == null) {
                _logger.LogWarning("场景录音未找到: {ScenarioId}", id);
            }

            return scenario;
        } catch (Exception ex) {
            _logger.LogError(ex, "获取场景录音失败: {ScenarioId}", id);
            throw;
        }
    }

    public async Task<List<ScenarioRecording>> GetActiveScenarioRecordingsAsync() {
        try {
            return await _dbContext.ScenarioRecordings
                .Where(s => s.IsActive)
                .Include(s => s.Segments.OrderBy(seg => seg.SegmentOrder))
                .OrderBy(s => s.Name)
                .ToListAsync();
        } catch (Exception ex) {
            _logger.LogError(ex, "获取活跃场景录音列表失败");
            throw;
        }
    }

    public async Task<List<ScenarioRecordingSegment>> GetSegmentsAsync(int scenarioRecordingId) {
        try {
            return await _dbContext.ScenarioRecordingSegments
                .Where(s => s.ScenarioRecordingId == scenarioRecordingId)
                .Include(s => s.DtmfTemplate)
                .OrderBy(s => s.SegmentOrder)
                .ToListAsync();
        } catch (Exception ex) {
            _logger.LogError(ex, "获取场景片段失败: {ScenarioId}", scenarioRecordingId);
            throw;
        }
    }

    public async Task<ScenarioRecordingSegment?> GetNextSegmentAsync(int scenarioRecordingId, int currentSegmentId) {
        try {
            var currentSegment = await _dbContext.ScenarioRecordingSegments
                .FirstOrDefaultAsync(s => s.Id == currentSegmentId);

            if (currentSegment == null) {
                _logger.LogWarning("当前片段未找到: {SegmentId}", currentSegmentId);
                return null;
            }

            return await _dbContext.ScenarioRecordingSegments
                .Where(s => s.ScenarioRecordingId == scenarioRecordingId
                    && s.SegmentOrder > currentSegment.SegmentOrder)
                .OrderBy(s => s.SegmentOrder)
                .FirstOrDefaultAsync();
        } catch (Exception ex) {
            _logger.LogError(ex, "获取下一个片段失败: {ScenarioId}, {CurrentSegmentId}",
                scenarioRecordingId, currentSegmentId);
            throw;
        }
    }

    public async Task<ScenarioRecording> CreateScenarioRecordingAsync(ScenarioRecording scenarioRecording) {
        try {
            scenarioRecording.CreatedAt = DateTime.UtcNow;
            scenarioRecording.UpdatedAt = DateTime.UtcNow;

            _dbContext.ScenarioRecordings.Add(scenarioRecording);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("场景录音已创建: {ScenarioId}, {Name}",
                scenarioRecording.Id, scenarioRecording.Name);

            return scenarioRecording;
        } catch (Exception ex) {
            _logger.LogError(ex, "创建场景录音失败: {Name}", scenarioRecording.Name);
            throw;
        }
    }

    public async Task UpdateScenarioRecordingAsync(ScenarioRecording scenarioRecording) {
        try {
            var existing = await _dbContext.ScenarioRecordings
                .FirstOrDefaultAsync(s => s.Id == scenarioRecording.Id);

            if (existing == null) {
                throw new ScenarioRecordingNotFoundException(scenarioRecording.Id);
            }

            existing.Name = scenarioRecording.Name;
            existing.Description = scenarioRecording.Description;
            existing.Category = scenarioRecording.Category;
            existing.IsActive = scenarioRecording.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("场景录音已更新: {ScenarioId}", scenarioRecording.Id);
        } catch (ScenarioRecordingNotFoundException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "更新场景录音失败: {ScenarioId}", scenarioRecording.Id);
            throw;
        }
    }

    public async Task DeleteScenarioRecordingAsync(int id) {
        try {
            var scenario = await _dbContext.ScenarioRecordings
                .Include(s => s.Segments)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (scenario == null) {
                throw new ScenarioRecordingNotFoundException(id);
            }

            _dbContext.ScenarioRecordings.Remove(scenario);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("场景录音已删除: {ScenarioId}", id);
        } catch (ScenarioRecordingNotFoundException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "删除场景录音失败: {ScenarioId}", id);
            throw;
        }
    }

    public async Task<ScenarioRecordingSegment> AddSegmentAsync(ScenarioRecordingSegment segment) {
        try {
            segment.CreatedAt = DateTime.UtcNow;

            _dbContext.ScenarioRecordingSegments.Add(segment);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("片段已添加: {SegmentId}, 场景: {ScenarioId}",
                segment.Id, segment.ScenarioRecordingId);

            return segment;
        } catch (Exception ex) {
            _logger.LogError(ex, "添加片段失败: 场景 {ScenarioId}", segment.ScenarioRecordingId);
            throw;
        }
    }

    public async Task UpdateSegmentAsync(ScenarioRecordingSegment segment) {
        try {
            var existing = await _dbContext.ScenarioRecordingSegments
                .FirstOrDefaultAsync(s => s.Id == segment.Id);

            if (existing == null) {
                throw new SegmentNotFoundException(segment.Id);
            }

            existing.SegmentOrder = segment.SegmentOrder;
            existing.SegmentType = segment.SegmentType;
            existing.FilePath = segment.FilePath;
            existing.TtsText = segment.TtsText;
            existing.TtsVariables = segment.TtsVariables;
            existing.DtmfTemplateId = segment.DtmfTemplateId;
            existing.DtmfVariableName = segment.DtmfVariableName;
            existing.ConditionExpression = segment.ConditionExpression;
            existing.NextSegmentIdOnTrue = segment.NextSegmentIdOnTrue;
            existing.NextSegmentIdOnFalse = segment.NextSegmentIdOnFalse;
            existing.Duration = segment.Duration;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("片段已更新: {SegmentId}", segment.Id);
        } catch (SegmentNotFoundException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "更新片段失败: {SegmentId}", segment.Id);
            throw;
        }
    }

    public async Task DeleteSegmentAsync(int segmentId) {
        try {
            var segment = await _dbContext.ScenarioRecordingSegments
                .FirstOrDefaultAsync(s => s.Id == segmentId);

            if (segment == null) {
                throw new SegmentNotFoundException(segmentId);
            }

            _dbContext.ScenarioRecordingSegments.Remove(segment);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("片段已删除: {SegmentId}", segmentId);
        } catch (SegmentNotFoundException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "删除片段失败: {SegmentId}", segmentId);
            throw;
        }
    }

    public async Task ReorderSegmentsAsync(int scenarioRecordingId, List<int> segmentIds) {
        try {
            var segments = await _dbContext.ScenarioRecordingSegments
                .Where(s => s.ScenarioRecordingId == scenarioRecordingId)
                .ToListAsync();

            for (int i = 0; i < segmentIds.Count; i++) {
                var segment = segments.FirstOrDefault(s => s.Id == segmentIds[i]);
                if (segment != null) {
                    segment.SegmentOrder = i + 1;
                }
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("片段顺序已更新: 场景 {ScenarioId}, {Count} 个片段",
                scenarioRecordingId, segmentIds.Count);
        } catch (Exception ex) {
            _logger.LogError(ex, "重新排序片段失败: 场景 {ScenarioId}", scenarioRecordingId);
            throw;
        }
    }
}
