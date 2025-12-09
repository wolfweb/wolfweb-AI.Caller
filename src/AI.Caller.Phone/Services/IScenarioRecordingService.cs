using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Services;

/// <summary>
/// 场景录音服务接口
/// </summary>
public interface IScenarioRecordingService {
    /// <summary>
    /// 获取场景录音
    /// </summary>
    Task<ScenarioRecording?> GetScenarioRecordingAsync(int id);

    /// <summary>
    /// 获取所有激活的场景录音
    /// </summary>
    Task<List<ScenarioRecording>> GetActiveScenarioRecordingsAsync();

    /// <summary>
    /// 获取场景录音的所有片段（按顺序）
    /// </summary>
    Task<List<ScenarioRecordingSegment>> GetSegmentsAsync(int scenarioRecordingId);

    /// <summary>
    /// 获取下一个要播放的片段
    /// </summary>
    Task<ScenarioRecordingSegment?> GetNextSegmentAsync(int scenarioRecordingId, int currentSegmentId);

    /// <summary>
    /// 创建场景录音
    /// </summary>
    Task<ScenarioRecording> CreateScenarioRecordingAsync(ScenarioRecording scenarioRecording);

    /// <summary>
    /// 更新场景录音
    /// </summary>
    Task UpdateScenarioRecordingAsync(ScenarioRecording scenarioRecording);

    /// <summary>
    /// 删除场景录音
    /// </summary>
    Task DeleteScenarioRecordingAsync(int id);

    /// <summary>
    /// 添加片段到场景录音
    /// </summary>
    Task<ScenarioRecordingSegment> AddSegmentAsync(ScenarioRecordingSegment segment);

    /// <summary>
    /// 更新片段
    /// </summary>
    Task UpdateSegmentAsync(ScenarioRecordingSegment segment);

    /// <summary>
    /// 删除片段
    /// </summary>
    Task DeleteSegmentAsync(int segmentId);

    /// <summary>
    /// 重新排序片段
    /// </summary>
    Task ReorderSegmentsAsync(int scenarioRecordingId, List<int> segmentIds);
}
