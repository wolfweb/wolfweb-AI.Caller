using AI.Caller.Core.Recording;

namespace AI.Caller.Phone.Services;

/// <summary>
/// 媒体处理服务接口
/// </summary>
public interface IMediaProcessingService
{
    /// <summary>
    /// 处理音频数据并写入文件
    /// </summary>
    Task<bool> ProcessAudioAsync(AudioFrame audioFrame, string outputPath);
    
    /// <summary>
    /// 完成音频文件处理
    /// </summary>
    Task<string?> FinalizeAudioFileAsync(string tempPath, string finalPath, string format);
    
    /// <summary>
    /// 获取音频文件时长
    /// </summary>
    Task<TimeSpan> GetAudioDurationAsync(string filePath);
}