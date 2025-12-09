using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Services;

/// <summary>
/// 监听服务接口
/// </summary>
public interface IMonitoringService {
    /// <summary>
    /// 开始监听通话
    /// </summary>
    Task<MonitoringSession> StartMonitoringAsync(string callId, int monitorUserId, string monitorUserName);

    /// <summary>
    /// 停止监听
    /// </summary>
    Task StopMonitoringAsync(int sessionId);

    /// <summary>
    /// 人工接入通话
    /// </summary>
    Task InterventionAsync(int sessionId, string reason);

    /// <summary>
    /// 获取活跃的监听会话
    /// </summary>
    Task<List<MonitoringSession>> GetActiveSessionsAsync();

    /// <summary>
    /// 获取用户的监听会话
    /// </summary>
    Task<List<MonitoringSession>> GetUserSessionsAsync(int userId);

    /// <summary>
    /// 获取通话的监听会话
    /// </summary>
    Task<List<MonitoringSession>> GetCallSessionsAsync(string callId);

    /// <summary>
    /// 检查通话是否正在被监听
    /// </summary>
    Task<bool> IsCallBeingMonitoredAsync(string callId);
}
