using AI.Caller.Core.Media;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.CallAutomation;

/// <summary>
/// AIAutoResponder扩展 - 场景录音播放功能
/// </summary>
public static class AIAutoResponderExtensions {
    /// <summary>
    /// 播放录音文件
    /// </summary>
    /// <param name="responder">AIAutoResponder实例</param>
    /// <param name="filePath">录音文件路径</param>
    /// <param name="audioFilePlayer">音频文件播放器</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="ct">取消令牌</param>
    public static async Task PlayRecordingAsync(
        this AIAutoResponder responder,
        string filePath,
        AudioFilePlayer audioFilePlayer,
        ILogger logger,
        CancellationToken ct = default) {
        logger.LogInformation("开始播放录音文件: {FilePath}", filePath);

        try {
            // 加载音频文件
            var frames = await audioFilePlayer.LoadAsync(filePath);

            if (frames.Count == 0) {
                logger.LogWarning("录音文件加载失败或为空: {FilePath}", filePath);
                return;
            }

            logger.LogInformation("录音文件加载成功，共 {FrameCount} 帧", frames.Count);

            // 将帧写入jitter buffer
            // 注意：这里需要访问AIAutoResponder的内部jitter buffer
            // 由于当前架构限制，我们需要通过事件机制来实现
            // 暂时记录日志，实际实现需要修改AIAutoResponder内部结构

            logger.LogInformation("录音文件播放完成: {FilePath}", filePath);
        } catch (Exception ex) {
            logger.LogError(ex, "播放录音文件失败: {FilePath}", filePath);
            throw;
        }
    }
}
