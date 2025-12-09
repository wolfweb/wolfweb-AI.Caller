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
                _logger.LogInformation("播放已暂停");
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
                _logger.LogInformation("播放已恢复");
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 检查是否暂停
    /// </summary>
    public bool IsPaused => _isPaused;
}
