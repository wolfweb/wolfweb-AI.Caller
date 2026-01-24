using AI.Caller.Core.CallAutomation;

namespace AI.Caller.Core;

/// <summary>
/// 场景执行上下文
/// </summary>
public class ScenarioExecutionContext : IDisposable {
    // 基本执行信息
    public string CallId { get; set; } = string.Empty;
    public List<ScenarioSegment> Segments { get; set; } = new();
    public Dictionary<string, string> Variables { get; set; } = new();
    public int SpeakerId { get; set; }
    public CancellationToken CancellationToken { get; set; }
    
    // 执行状态
    public int CurrentSegmentIndex { get; set; }
    public HashSet<int> SkippedSegmentIds { get; set; } = new();
    
    // 控制状态
    public ScenarioPlaybackState State { get; set; } = ScenarioPlaybackState.Playing;
    public ManualResetEventSlim PauseEvent { get; set; } = new(true);
    public object StateLock { get; set; } = new();
    
    // 跳转控制
    public int? JumpToSegmentId { get; set; }
    
    // 时间记录
    public DateTime StartTime { get; set; }
    public DateTime? PauseTime { get; set; }
    
    private bool _disposed = false;
    
    public void Dispose() {
        if (!_disposed) {
            PauseEvent?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// 场景播放状态
/// </summary>
public enum ScenarioPlaybackState {
    Playing,    // 正在播放
    Paused,     // 已暂停
    Jumping,    // 跳转中
    Stopped     // 已停止
}