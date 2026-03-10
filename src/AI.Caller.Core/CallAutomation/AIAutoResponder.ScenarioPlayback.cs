using AI.Caller.Core.CallAutomation;
using AI.Caller.Core.Media;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Core;

/// <summary>
/// AIAutoResponder - 场景录音播放功能扩展（依赖管理和播放控制）
/// </summary>
public sealed partial class AIAutoResponder {
    private AudioFilePlayer? _audioFilePlayer;
    private volatile bool _isPaused;
    private readonly object _pauseLock = new();
    
    // 执行上下文
    private ScenarioExecutionContext? _executionContext;
    private readonly object _contextLock = new();

    /// <summary>
    /// 设置音频文件播放器
    /// </summary>
    public void SetAudioFilePlayer(AudioFilePlayer audioFilePlayer) {
        _audioFilePlayer = audioFilePlayer;
        _logger.LogDebug("AudioFilePlayer已设置");
    }

    /// <summary>
    /// 暂停播放
    /// </summary>
    public Task PauseAsync() {
        lock (_pauseLock) {
            if (!_isPaused) {
                _isPaused = true;
                _shouldSendAudio = false;

                // 设置执行上下文为暂停状态
                lock (_contextLock) {
                    if (_executionContext != null) {
                        lock (_executionContext.StateLock) {
                            _executionContext.State = ScenarioPlaybackState.Paused;
                            _executionContext.PauseTime = DateTime.UtcNow;
                        }
                    }
                }

                // 清空音频缓冲
                _playbackCompletionSource?.TrySetResult();
                while (_jitterBuffer.Reader.TryRead(out _)) { }

                _logger.LogInformation("场景播放已暂停");
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 恢复播放
    /// </summary>
    public Task ResumeAsync() {
        lock (_pauseLock) {
            if (_isPaused) {
                _isPaused = false;
                _shouldSendAudio = true;
                
                // 恢复执行上下文
                lock (_contextLock) {
                    if (_executionContext != null) {
                        lock (_executionContext.StateLock) {
                            _executionContext.State = ScenarioPlaybackState.Playing;
                            _executionContext.PauseEvent.Set(); // 发送恢复信号
                        }
                    }
                }
                
                _logger.LogInformation("场景播放已恢复");
            }
        }
        return Task.CompletedTask;
    }

    public Task ResumeScenarioFromSegmentAsync(string callId, int startSegmentId, Dictionary<string, string> variables, CancellationToken ct, int speakerId = 0) {
        lock (_contextLock) {
            if (_executionContext == null) {
                _logger.LogWarning("执行上下文不存在，无法跳转到指定片段");
                return Task.CompletedTask;
            }
            
            lock (_executionContext.StateLock) {
                // 更新变量
                foreach (var kvp in variables) {
                    _executionContext.Variables[kvp.Key] = kvp.Value;
                }
                
                // 设置跳转状态
                _executionContext.State = ScenarioPlaybackState.Jumping;
                _executionContext.JumpToSegmentId = startSegmentId;
                _executionContext.PauseEvent.Set(); // 唤醒主循环
            }
        }
        
        // 清空 jitter buffer，防止旧片段音频在跳转后被发送
        while (_jitterBuffer.Reader.TryRead(out _)) { }
        
        // 恢复音频发送
        _isPaused = false;
        _shouldSendAudio = true;
        
        _logger.LogInformation("设置跳转到片段: {SegmentId}", startSegmentId);
        return Task.CompletedTask;
    }


    /// <summary>
    /// 检查是否暂停
    /// </summary>
    public bool IsPaused => _isPaused;

    /// <summary>
    /// 获取当前正在播放的片段（供 VAD 打断重放使用）
    /// </summary>
    public ScenarioSegment? GetCurrentPlayingSegment() {
        return GetCurrentSegment();
    }

    /// <summary>
    /// 初始化执行上下文
    /// </summary>
    private void InitializeExecutionContext(string callId, List<ScenarioSegment> segments, 
        Dictionary<string, string> variables, int speakerId, CancellationToken ct) {
        
        lock (_contextLock) {
            _executionContext?.Dispose();
            _executionContext = new ScenarioExecutionContext {
                CallId = callId,
                Segments = segments.OrderBy(s => s.Order).ToList(),
                Variables = new Dictionary<string, string>(variables),
                SpeakerId = speakerId,
                CancellationToken = ct,
                CurrentSegmentIndex = 0,
                SkippedSegmentIds = new HashSet<int>(_skippedSegmentIds),
                StartTime = DateTime.UtcNow
            };
        }
    }
    
    /// <summary>
    /// 清理执行上下文
    /// </summary>
    private void CleanupExecutionContext() {
        lock (_contextLock) {
            _executionContext?.Dispose();
            _executionContext = null;
        }
    }
    
    /// <summary>
    /// 等待暂停状态解除
    /// </summary>
    private async Task WaitIfPausedAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            bool isPaused;
            ManualResetEventSlim? pauseEvent;
            
            lock (_contextLock) {
                if (_executionContext == null) return;
                
                lock (_executionContext.StateLock) {
                    isPaused = _executionContext.State == ScenarioPlaybackState.Paused;
                    pauseEvent = _executionContext.PauseEvent;
                    
                    if (isPaused) {
                        pauseEvent.Reset(); // 设置为等待状态
                    }
                }
            }
            
            if (!isPaused) return; // 未暂停，继续执行
            
            // 使用异步等待，避免Task.Run
            try {
                await Task.Run(() => pauseEvent.Wait(ct), ct);
                break; // 收到恢复信号
            } catch (OperationCanceledException) {
                return; // 被取消，退出
            }
        }
    }
    
    /// <summary>
    /// 处理跳转指令
    /// </summary>
    private void HandleJumpInstruction() {
        lock (_contextLock) {
            if (_executionContext == null) return;
            
            lock (_executionContext.StateLock) {
                if (_executionContext.State == ScenarioPlaybackState.Jumping && 
                    _executionContext.JumpToSegmentId.HasValue) {
                    
                    // 找到目标片段索引
                    var targetIndex = _executionContext.Segments.FindIndex(s => s.Id == _executionContext.JumpToSegmentId.Value);
                    if (targetIndex >= 0) {
                        _executionContext.CurrentSegmentIndex = targetIndex;
                        _logger.LogInformation("跳转到片段: SegmentId={SegmentId}, Index={Index}", 
                            _executionContext.JumpToSegmentId.Value, targetIndex);
                    } else {
                        _logger.LogWarning("未找到目标片段: SegmentId={SegmentId}", _executionContext.JumpToSegmentId.Value);
                    }
                    
                    // 重置跳转状态
                    _executionContext.State = ScenarioPlaybackState.Playing;
                    _executionContext.JumpToSegmentId = null;
                    _executionContext.PauseEvent.Set();
                }
            }
        }
    }
    
    /// <summary>
    /// 检查执行是否完成
    /// </summary>
    private bool IsExecutionComplete() {
        lock (_contextLock) {
            return _executionContext == null || 
                   _executionContext.CurrentSegmentIndex >= _executionContext.Segments.Count;
        }
    }
    
    /// <summary>
    /// 获取当前片段
    /// </summary>
    private ScenarioSegment? GetCurrentSegment() {
        lock (_contextLock) {
            if (_executionContext == null || 
                _executionContext.CurrentSegmentIndex >= _executionContext.Segments.Count) {
                return null;
            }
            return _executionContext.Segments[_executionContext.CurrentSegmentIndex];
        }
    }
    
    /// <summary>
    /// 检查是否应该跳过片段
    /// </summary>
    private bool ShouldSkipSegment(ScenarioSegment segment) {
        lock (_contextLock) {
            return _executionContext?.SkippedSegmentIds.Contains(segment.Id) ?? false;
        }
    }
    
    /// <summary>
    /// 移动到下一片段
    /// </summary>
    private void MoveToNextSegment() {
        lock (_contextLock) {
            if (_executionContext != null) {
                _executionContext.CurrentSegmentIndex++;
            }
        }
    }
    
    /// <summary>
    /// 获取当前变量
    /// </summary>
    private Dictionary<string, string> GetCurrentVariables() {
        lock (_contextLock) {
            return _executionContext?.Variables ?? new Dictionary<string, string>();
        }
    }
    
    /// <summary>
    /// 获取当前说话人ID
    /// </summary>
    private int GetCurrentSpeakerId() {
        lock (_contextLock) {
            return _executionContext?.SpeakerId ?? 0;
        }
    }
    
    /// <summary>
    /// 设置变量
    /// </summary>
    private void SetVariable(string name, string value) {
        lock (_contextLock) {
            if (_executionContext != null) {
                _executionContext.Variables[name] = value;
            }
        }
    }
    
    /// <summary>
    /// 跳转到指定片段（用于条件片段）
    /// </summary>
    private void JumpToSegment(int segmentId) {
        lock (_contextLock) {
            if (_executionContext != null) {
                var targetIndex = _executionContext.Segments.FindIndex(s => s.Id == segmentId);
                if (targetIndex >= 0) {
                    _executionContext.CurrentSegmentIndex = targetIndex;
                    _logger.LogInformation("条件跳转到片段: SegmentId={SegmentId}", segmentId);
                }
            }
        }
    }

    /// <summary>
    /// 检查是否正在准备跳转
    /// </summary>
    private bool IsJumpingPending() {
        lock (_contextLock) {
            return _executionContext?.State == ScenarioPlaybackState.Jumping;
        }
    }

    /// <summary>
    /// 检查执行是否已停止
    /// </summary>
    private bool IsExecutionStopped() {
        lock (_contextLock) {
            return _executionContext?.State == ScenarioPlaybackState.Stopped;
        }
    }
}