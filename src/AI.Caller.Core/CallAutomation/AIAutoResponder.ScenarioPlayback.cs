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

                //暂停时清空剩余音频
                while (_jitterBuffer.Reader.TryRead(out _)) { }
                _playbackCompletionSource?.TrySetResult();

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

    public async Task ResumeScenarioFromSegmentAsync(string callId, List<ScenarioSegment> segments, int startSegmentId, Dictionary<string, string> variables, CancellationToken ct = default, int speakerId = 0) {
        var orderedSegments = segments.OrderBy(s => s.Order).ToList();
        int startIndex = orderedSegments.FindIndex(s => s.Id == startSegmentId);

        if (startIndex < 0) {
            _logger.LogWarning("未找到指定片段ID: {SegmentId}", startSegmentId);
            return;
        }

        _skippedSegmentIds.Clear();

        var segmentsToPlay = orderedSegments.Skip(startIndex).ToList();
        await PlayScenarioAsync(callId, segmentsToPlay, variables, ct, speakerId);
    }


    /// <summary>
    /// 检查是否暂停
    /// </summary>
    public bool IsPaused => _isPaused;
}
