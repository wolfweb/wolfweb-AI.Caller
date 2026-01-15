using AI.Caller.Core.Media;
using DnsClient.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AI.Caller.Core;

/// <summary>
/// AIAutoResponder - 录音播放扩展
/// Phase 1 - 任务1.1: 录音文件播放功能
/// </summary>
public sealed partial class AIAutoResponder {    
    /// <summary>
    /// 播放录音文件
    /// </summary>
    /// <param name="filePath">录音文件路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>播放任务</returns>
    public async Task PlayRecordingAsync(string filePath, CancellationToken ct = default) {
        if (string.IsNullOrEmpty(filePath)) {
            _logger.LogWarning("录音文件路径为空，跳过播放");
            return;
        }

        if (!File.Exists(filePath)) {
            _logger.LogError("录音文件不存在: {FilePath}", filePath);
            return;
        }

        Interlocked.Exchange(ref _totalBytesGenerated, 0);
        Interlocked.Exchange(ref _totalBytesSent, 0);
        _playbackCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _isTtsStreamFinished = false;

        var token = _cts?.Token ?? ct;
        var stopwatch = Stopwatch.StartNew();

        try {
            _logger.LogInformation("开始播放录音文件: {FilePath}", filePath);

            // 创建AudioFilePlayer实例来加载音频文件
            using var audioFilePlayer = new AudioFilePlayer(_loggerFactory, _codecFactory);
            
            // 如果有MediaSessionManager，设置引用以获取当前协商的编码器
            // 注意：这里可能需要从SIPClient或其他地方获取MediaSessionManager引用
            // audioFilePlayer.SetMediaSessionManager(mediaSessionManager);
            
            var frames = await audioFilePlayer.LoadAsync(filePath);

            if (frames == null || frames.Count == 0) {
                _logger.LogWarning("录音文件加载失败或为空: {FilePath}, FrameCount={FrameCount}", filePath, frames?.Count ?? 0);
                _isTtsStreamFinished = true;
                return;
            }

            _logger.LogInformation("录音文件已加载: {FrameCount} 帧", frames.Count);

            // 将所有帧写入jitter buffer
            int frameIndex = 0;
            foreach (var frame in frames) {
                if (token.IsCancellationRequested) {
                    _logger.LogInformation("录音播放被取消");
                    break;
                }

                if (!_jitterBuffer.Writer.TryWrite(frame)) {
                    _logger.LogError("无法写入帧到jitter buffer，帧索引: {FrameIndex}", frameIndex);
                    break;
                }

                Interlocked.Add(ref _totalBytesGenerated, frame.Length);
                frameIndex++;
                
                if (frameIndex % 100 == 0) {
                    _logger.LogDebug("已写入 {FrameIndex}/{TotalFrames} 帧到jitter buffer", frameIndex, frames.Count);
                }
            }

            _logger.LogDebug("录音文件所有帧已写入jitter buffer: {TotalFrames} 帧, {TotalBytes} 字节", frameIndex, Interlocked.Read(ref _totalBytesGenerated));

            _isTtsStreamFinished = true;

            stopwatch.Stop();
            _logger.LogInformation("录音文件播放完成: {FilePath}, 耗时: {ElapsedMs}ms", 
                filePath, stopwatch.ElapsedMilliseconds);

        } catch (Exception ex) {
            _logger.LogError(ex, "播放录音文件失败: {FilePath}", filePath);
            _isTtsStreamFinished = true;
        }
    }
}
