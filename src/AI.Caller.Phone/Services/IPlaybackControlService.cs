using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Services;

/// <summary>
/// 播放控制服务接口
/// </summary>
public interface IPlaybackControlService {
    /// <summary>
    /// 获取通话的播放控制
    /// </summary>
    Task<PlaybackControl?> GetPlaybackControlAsync(string callId);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="playbackControl"></param>
    /// <returns></returns>
    Task AddOrUpdateAsync(PlaybackControl playbackControl);

    /// <summary>
    /// 创建播放控制
    /// </summary>
    Task<PlaybackControl> CreatePlaybackControlAsync(string callId);

    /// <summary>
    /// 更新当前播放片段
    /// </summary>
    Task UpdateCurrentSegmentAsync(string callId, int segmentId);

    /// <summary>
    /// 跳过片段
    /// </summary>
    Task SkipSegmentAsync(string callId, int segmentId);

    /// <summary>
    /// 记录人工干预
    /// </summary>
    Task RecordInterventionAsync(string callId, int segmentId);

    /// <summary>
    /// 获取已跳过的片段列表
    /// </summary>
    Task<List<int>> GetSkippedSegmentsAsync(string callId);
}
