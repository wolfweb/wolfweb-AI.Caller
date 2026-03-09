using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Buffers;
using AI.Caller.Core.Media;
using System.Runtime.InteropServices;

namespace AI.Caller.Core;

/// <summary>
/// AudioBridge - 监听功能扩展
/// </summary>
public sealed partial class AudioBridge {
    private volatile int _activeMonitorCount = 0;
    private volatile bool _isInterventionActive = false;
    private readonly ConcurrentDictionary<int, MonitoringListener> _monitoringListeners = new();

    // Jitter Buffer & Pacer State
    private readonly AudioRingBuffer _interventionBuffer = new AudioRingBuffer(8000); 
    private Task? _interventionPacerTask;
    private CancellationTokenSource? _interventionCts;
    private readonly object _pacerLock = new object();

    /// <summary>
    /// 公开人工介入状态，供外部读取
    /// </summary>
    public bool IsInterventionActive => _isInterventionActive;

    /// <summary>
    /// 客户音频准备就绪事件（Incoming - 客户说话）
    /// </summary>
    public event Action<int, byte[]>? IncomingAudioReady;
    
    /// <summary>
    /// 系统音频准备就绪事件（Outgoing - AI/系统播放）
    /// </summary>
    public event Action<int, byte[]>? OutgoingAudioReady;
    
    /// <summary>
    /// 系统播放音频事件（仅系统播放的内容）
    /// </summary>
    public event Action<byte[]>? OutgoingAudioGenerated;

    /// <summary>
    /// 人工接入音频发送事件（监听者说话的音频需要发送到SIP通话）
    /// </summary>
    public event Action<byte[]>? InterventionAudioSend;

    /// <summary>
    /// 设置人工接入状态
    /// </summary>
    /// <param name="active">是否激活人工接入</param>
    public void SetInterventionActive(bool active) {
        _isInterventionActive = active;
        
        if (active) {
            // 清空监听PCM队列中的AI音频
            int clearedCount = 0;
            while (_monitoringPcmQueue.Reader.TryRead(out _)) {
                clearedCount++;
            }
            if (clearedCount > 0) {
                _logger.LogInformation("已清空监听PCM队列: {Count} 帧", clearedCount);
            }
            
            // 清空所有监听会话的AI缓冲区
            foreach (var listener in _monitoringListeners.Values.Where(l => l.IsActive)) {
                listener.Session?.ClearAiBuffer();
            }
            _logger.LogInformation("已清空所有监听会话的AI缓冲区");

            lock (_pacerLock) {
                RestartInterventionPacerUnsafe();
            }
        } else {
            lock (_pacerLock) {
                StopInterventionPacerUnsafe();
            }
        }
        
        _logger.LogInformation("人工接入状态已更新: {Active}", active);
    }

    private void RestartInterventionPacerUnsafe() {
        StopInterventionPacerUnsafe(); 
        
        _interventionBuffer.Clear(); 
        _interventionCts = new CancellationTokenSource();
        _interventionPacerTask = Task.Factory.StartNew(
            InterventionPacerLoop, 
            _interventionCts.Token, 
            TaskCreationOptions.LongRunning, 
            TaskScheduler.Default
        ).Unwrap();
    }

    private void StopInterventionPacerUnsafe() {
        if (_interventionCts != null) {
            _interventionCts.Cancel();
            _interventionCts.Dispose();
            _interventionCts = null;
        }
        _interventionPacerTask = null;
    }

    /// <summary>
    /// 添加监听者
    /// </summary>
    /// <param name="userId">监听用户ID</param>
    /// <param name="userName">监听用户名</param>
    /// <param name="session">WebRTC会话</param>
    public void AddMonitor(int userId, string userName, MonitorMediaSession session) {
        var listener = new MonitoringListener {
            UserId = userId,
            UserName = userName,
            StartTime = DateTime.UtcNow,
            IsActive = true,
            Session = session,
            InterventionAudioHandler = (pcm) => ProcessInterventionAudio(pcm)
        };

        if (_monitoringListeners.TryAdd(userId, listener)) {
            Interlocked.Increment(ref _activeMonitorCount);
            _logger.LogInformation("监听者已添加: UserId {UserId}, UserName {UserName}", userId, userName);
            session.OnInterventionAudioReceived += listener.InterventionAudioHandler;
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
            Interlocked.Decrement(ref _activeMonitorCount);
            listener.IsActive = false;
            listener.EndTime = DateTime.UtcNow;
            
            if (listener.Session != null && listener.InterventionAudioHandler != null) {
                listener.Session.OnInterventionAudioReceived -= listener.InterventionAudioHandler;
            }
            
            if (_activeMonitorCount == 0) {
                _isInterventionActive = false;
                _logger.LogInformation("所有监听者已移除，人工接入状态已重置");
            }
            
            _logger.LogInformation("监听者已移除: UserId {UserId}, 监听时长: {Duration}秒", userId, (listener.EndTime.Value - listener.StartTime).TotalSeconds);
        } else {
            _logger.LogWarning("监听者不存在: UserId {UserId}", userId);
        }
    }

    /// <summary>
    /// 获取活跃的监听者列表
    /// </summary>
    public List<MonitoringListener> GetActiveMonitors() {
        return _monitoringListeners.Values.Where(l => l.IsActive).ToList();
    }

    /// <summary>
    /// 检查是否有活跃的监听者
    /// </summary>
    public bool HasActiveMonitors() {
        return _activeMonitorCount > 0;
    }

    /// <summary>
    /// 处理对方说话的音频（用于监听）
    /// 这个方法会在 ProcessIncomingAudio 中被调用
    /// </summary>
    private void BroadcastIncomingAudioToMonitors(byte[] pcmFrame) {
        if (!HasActiveMonitors())
            return;

        try {            
            foreach (var listener in _monitoringListeners.Values.Where(l => l.IsActive)) {
                IncomingAudioReady?.Invoke(listener.UserId, pcmFrame);
                listener.Session?.SendAudio(pcmFrame, false);
#if DEBUG
                _logger.LogTrace("推送客户音频到监听者: UserId {UserId}, 帧大小 {Size} 字节", listener.UserId, pcmFrame.Length);
#endif
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "广播来电音频到监听者失败");
        }
    }

    /// <summary>
    /// 广播AI播放的PCM音频给所有监听者
    /// 此方法接收原始PCM，如果采样率不是8000Hz则进行重采样
    /// </summary>
    /// <param name="pcmFrame">原始PCM音频帧</param>
    private void BroadcastOutgoingPcmToMonitors(byte[] pcmFrame) {
        if (!HasActiveMonitors())
            return;

        try {
            byte[] finalPcm = pcmFrame;
            int actualLength = pcmFrame.Length;
            bool needsReturn = false;
            
            try {
                if (_profile != null && _profile.SampleRate != 8000) {
                    var resampled = GetOrCreateMonitoringOutResampler(_profile.SampleRate).Resample(pcmFrame);
                    if (resampled.Array != null && resampled.Count > 0) {
                        actualLength = resampled.Count;
                        finalPcm = ArrayPool<byte>.Shared.Rent(actualLength);
                        needsReturn = true;
                        Array.Copy(resampled.Array, resampled.Offset, finalPcm, 0, actualLength);
                    }
                }

                foreach (var listener in _monitoringListeners.Values.Where(l => l.IsActive)) {
                    var segment = new ArraySegment<byte>(finalPcm, 0, actualLength);
                    OutgoingAudioReady?.Invoke(listener.UserId, segment.Array!);
                    listener.Session?.SendAudio(segment.Array!, segment.Offset, actualLength, true);
                }
            } finally {
                if (needsReturn) {
                    ArrayPool<byte>.Shared.Return(finalPcm);
                }
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "广播AI PCM音频到监听者失败");
        }
    }

    /// <summary>
    /// 处理人工接入音频（监听者说话的音频）
    /// 这个方法会将监听者的音频发送给客户
    /// </summary>
    /// <param name="audioData">音频数据（PCM格式）</param>
    public void ProcessInterventionAudio(byte[] audioData) {
        if (!_isStarted || !_isInterventionActive) return;

        try {
            ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(audioData);
            _interventionBuffer.Write(samples);
        } catch (Exception ex) {
            _logger.LogError(ex, "写入介入音频缓冲水池失败");
        }
    }

    private async Task InterventionPacerLoop() {
        var interval = TimeSpan.FromMilliseconds(20);
        using var timer = new PeriodicTimer(interval);
        short[] frameSamples = new short[160];
        
        try {
            for(int i = 0; i < 3; i++) {
                if (!await timer.WaitForNextTickAsync(_interventionCts!.Token)) return;
                
                Array.Clear(frameSamples, 0, frameSamples.Length);
                SendEncodedFrame(frameSamples);
            }

            while (await timer.WaitForNextTickAsync(_interventionCts!.Token)) {
                if (!_isInterventionActive || !_isStarted) continue;
                
                bool hasData = _interventionBuffer.TryReadFrame(frameSamples);
                if (!hasData) {
                    Array.Clear(frameSamples, 0, frameSamples.Length); 
                }
                
                SendEncodedFrame(frameSamples);
            }
        } catch (OperationCanceledException) { } 
          catch (Exception ex) {
            _logger.LogError(ex, "介入音频定时泵异常");
        }
    }

    private void SendEncodedFrame(Span<short> pcmSpan) {
        byte[] pcmData = MemoryMarshal.Cast<short, byte>(pcmSpan).ToArray();
        byte[] encodedAudioData = ConvertPcmToCurrentCodec(pcmData);
        
        if (encodedAudioData != null && encodedAudioData.Length > 0) {
            InterventionAudioSend?.Invoke(encodedAudioData);
        }
    }

    /// <summary>
    /// 将PCM音频转换为当前协商的编码格式
    /// </summary>
    /// <param name="pcmData">PCM音频数据（16位）</param>
    /// <returns>编码后的音频数据</returns>
    private byte[] ConvertPcmToCurrentCodec(byte[] pcmData) {
        try {
            if (pcmData == null || pcmData.Length == 0) {
                _logger.LogWarning("PCM音频数据为空");
                return Array.Empty<byte>();
            }

            _logger.LogTrace("开始转换PCM音频到当前协商编码: 输入大小 {Size} 字节", pcmData.Length);

            var currentCodec = GetCurrentNegotiatedCodec();
            _logger.LogDebug("使用当前协商的编码器: {Codec}", currentCodec);

            var codec = GetCodecForPayloadType((int)currentCodec);

            if (codec == null) {
                _logger.LogWarning("Unsupported payload type: {PayloadType}", currentCodec);
                return Array.Empty<byte>();
            }

            var encodedData = codec.Encode(pcmData);
            
            _logger.LogTrace("音频编码完成: {Codec}, 输出大小 {Size} 字节", currentCodec, encodedData.Length);
            return encodedData;

        } catch (Exception ex) {
            _logger.LogError(ex, "PCM音频编码失败");
            return Array.Empty<byte>();
        }
    }

    /// <summary>
    /// 获取当前协商的编码器类型
    /// 从MediaSessionManager获取当前使用的编码器
    /// </summary>
    private AudioCodec GetCurrentNegotiatedCodec() {
        var currentCodec = _mediaSessionManager?.SelectedCodec ?? AudioCodec.PCMA;
        _logger.LogTrace("获取当前协商的编码器: {Codec}", currentCodec);
        return currentCodec;
    }

    /// <summary>
    /// 获取指定监听者的WebRTC会话
    /// </summary>
    public MonitorMediaSession? GetMonitorSession(int monitorUserId) {
        if (_monitoringListeners.TryGetValue(monitorUserId, out var listener)) {
            return listener.Session;
        }
        return null;
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
    public MonitorMediaSession? Session { get; set; }
    public Action<byte[]>? InterventionAudioHandler { get; set; }
}
