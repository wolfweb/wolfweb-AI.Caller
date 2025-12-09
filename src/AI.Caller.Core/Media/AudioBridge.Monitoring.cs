using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AI.Caller.Core;

/// <summary>
/// AudioBridge - 监听功能扩展
/// </summary>
public sealed partial class AudioBridge {
    private readonly ConcurrentDictionary<int, MonitoringListener> _monitoringListeners = new();

    /// <summary>
    /// 监听音频准备就绪事件（包含系统播放和对方说话的混合音频）
    /// </summary>
    public event Action<int, byte[]>? MonitoringAudioReady;

    /// <summary>
    /// 系统播放音频事件（仅系统播放的内容）
    /// </summary>
    public event Action<byte[]>? OutgoingAudioGenerated;

    /// <summary>
    /// 添加监听者
    /// </summary>
    /// <param name="userId">监听用户ID</param>
    /// <param name="userName">监听用户名</param>
    public void AddMonitor(int userId, string userName) {
        var listener = new MonitoringListener {
            UserId = userId,
            UserName = userName,
            StartTime = DateTime.UtcNow,
            IsActive = true
        };

        if (_monitoringListeners.TryAdd(userId, listener)) {
            _logger.LogInformation("监听者已添加: UserId {UserId}, UserName {UserName}", userId, userName);
        } else {
            _logger.LogWarning("监听者已存在: UserId {UserId}", userId);
        }
    }

    /// <summary>
    /// 移除监听者
    /// </summary>
    /// <param name="userId">监听用户ID</param>
    public void RemoveMonitor(int userId) {
        if (_monitoringListeners.TryRemove(userId, out var listener)) {
            listener.IsActive = false;
            listener.EndTime = DateTime.UtcNow;
            _logger.LogInformation("监听者已移除: UserId {UserId}, 监听时长: {Duration}秒",
                userId, (listener.EndTime.Value - listener.StartTime).TotalSeconds);
        } else {
            _logger.LogWarning("监听者不存在: UserId {UserId}", userId);
        }
    }

    /// <summary>
    /// 获取活跃的监听者列表
    /// </summary>
    public List<MonitoringListener> GetActiveMonitors() {
        return _monitoringListeners.Values
            .Where(l => l.IsActive)
            .ToList();
    }

    /// <summary>
    /// 检查是否有活跃的监听者
    /// </summary>
    public bool HasActiveMonitors() {
        return _monitoringListeners.Any(kvp => kvp.Value.IsActive);
    }

    /// <summary>
    /// 处理系统播放的音频（用于监听）
    /// 这个方法会在AIAutoResponder的OutgoingAudioGenerated事件中被调用
    /// </summary>
    /// <param name="audioFrame">音频帧（G.711编码）</param>
    public void ProcessOutgoingAudio(byte[] audioFrame) {
        if (!_isStarted || !HasActiveMonitors())
            return;

        try {
            // 触发系统播放音频事件
            OutgoingAudioGenerated?.Invoke(audioFrame);

            // 发送给所有活跃的监听者（系统音频）
            foreach (var listener in _monitoringListeners.Values.Where(l => l.IsActive)) {
                MonitoringAudioReady?.Invoke(listener.UserId, audioFrame);
                _logger.LogTrace("推送系统音频到监听者: UserId {UserId}, 帧大小 {Size} 字节",
                    listener.UserId, audioFrame.Length);
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "处理系统播放音频失败");
        }
    }

    /// <summary>
    /// 处理对方说话的音频（用于监听）
    /// 这个方法会在 ProcessIncomingAudio 中被调用
    /// </summary>
    private void BroadcastIncomingAudioToMonitors(byte[] audioFrame) {
        if (!HasActiveMonitors())
            return;

        try {
            // 发送给所有活跃的监听者（用户音频）
            foreach (var listener in _monitoringListeners.Values.Where(l => l.IsActive)) {
                MonitoringAudioReady?.Invoke(listener.UserId, audioFrame);
                _logger.LogTrace("推送用户音频到监听者: UserId {UserId}, 帧大小 {Size} 字节",
                    listener.UserId, audioFrame.Length);
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "广播来电音频到监听者失败");
        }
    }

    /// <summary>
    /// 清除所有监听者
    /// </summary>
    public void ClearAllMonitors() {
        foreach (var userId in _monitoringListeners.Keys.ToList()) {
            RemoveMonitor(userId);
        }
        _logger.LogInformation("所有监听者已清除");
    }
}

/// <summary>
/// 监听者信息
/// </summary>
public class MonitoringListener {
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public bool IsActive { get; set; }
}
