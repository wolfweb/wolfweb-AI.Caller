using AI.Caller.Phone.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AI.Caller.Phone.Services;

/// <summary>
/// 播放控制服务实现
/// </summary>
public class PlaybackControlService : IPlaybackControlService {
    private readonly AppDbContext _dbContext;
    private readonly ILogger<PlaybackControlService> _logger;

    public PlaybackControlService(AppDbContext dbContext, ILogger<PlaybackControlService> logger) {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PlaybackControl?> GetPlaybackControlAsync(string callId) {
        try {
            return await _dbContext.PlaybackControls.FirstOrDefaultAsync(p => p.CallId == callId);
        } catch (Exception ex) {
            _logger.LogError(ex, "获取播放控制失败: {CallId}", callId);
            throw;
        }
    }

    public async Task AddOrUpdateAsync(PlaybackControl playbackControl) {
        try {
            if(playbackControl.Id > 0) {
                _dbContext.PlaybackControls.Update(playbackControl);
            } else {
                _dbContext.PlaybackControls.Add(playbackControl);
            }
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("播放控制已创建: {ControlId}, CallId: {CallId}", playbackControl.Id, playbackControl.CallId);
        } catch (Exception ex) {
            _logger.LogError(ex, "创建播放控制失败: {CallId}", playbackControl.CallId);
            throw;
        }
    }

    public async Task<PlaybackControl> CreatePlaybackControlAsync(string callId) {
        try {
            var control = new PlaybackControl {
                CallId = callId,
                PlaybackState = PlaybackState.Playing,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.PlaybackControls.Add(control);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("播放控制已创建: {ControlId}, CallId: {CallId}", control.Id, callId);

            return control;
        } catch (Exception ex) {
            _logger.LogError(ex, "创建播放控制失败: {CallId}", callId);
            throw;
        }
    }

    public async Task UpdateCurrentSegmentAsync(string callId, int segmentId) {
        try {
            var control = await GetOrCreateControlAsync(callId);

            control.CurrentSegmentId = segmentId;
            control.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogDebug("当前片段已更新: CallId {CallId}, SegmentId {SegmentId}", callId, segmentId);
        } catch (Exception ex) {
            _logger.LogError(ex, "更新当前片段失败: {CallId}, {SegmentId}", callId, segmentId);
            throw;
        }
    }

    public async Task SkipSegmentAsync(string callId, int segmentId) {
        try {
            var control = await GetOrCreateControlAsync(callId);

            var skippedSegments = await GetSkippedSegmentsAsync(callId);
            if (!skippedSegments.Contains(segmentId)) {
                skippedSegments.Add(segmentId);
                control.SkippedSegments = JsonSerializer.Serialize(skippedSegments);
                control.UpdatedAt = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("片段已跳过: CallId {CallId}, SegmentId {SegmentId}", callId, segmentId);
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "跳过片段失败: {CallId}, {SegmentId}", callId, segmentId);
            throw;
        }
    }

    public async Task RecordInterventionAsync(string callId, int segmentId) {
        try {
            var control = await GetOrCreateControlAsync(callId);

            control.LastInterventionSegmentId = segmentId;
            control.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("人工干预已记录: CallId {CallId}, SegmentId {SegmentId}", callId, segmentId);
        } catch (Exception ex) {
            _logger.LogError(ex, "记录人工干预失败: {CallId}, {SegmentId}", callId, segmentId);
            throw;
        }
    }

    public async Task<List<int>> GetSkippedSegmentsAsync(string callId) {
        try {
            var control = await GetPlaybackControlAsync(callId);

            if (control == null || string.IsNullOrEmpty(control.SkippedSegments)) {
                return new List<int>();
            }

            return JsonSerializer.Deserialize<List<int>>(control.SkippedSegments) ?? new List<int>();
        } catch (Exception ex) {
            _logger.LogError(ex, "获取跳过片段列表失败: {CallId}", callId);
            throw;
        }
    }

    private async Task<PlaybackControl> GetOrCreateControlAsync(string callId) {
        var control = await GetPlaybackControlAsync(callId);

        if (control == null) {
            control = await CreatePlaybackControlAsync(callId);
        }

        return control;
    }
}
