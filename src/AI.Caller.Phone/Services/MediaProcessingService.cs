using AI.Caller.Core.Recording;
using System.Collections.Concurrent;

namespace AI.Caller.Phone.Services;

/// <summary>
/// 媒体处理服务实现
/// </summary>
public class MediaProcessingService : IMediaProcessingService
{
    private readonly ILogger<MediaProcessingService> _logger;
    private readonly ConcurrentDictionary<string, FileStream> _activeStreams = new();

    public MediaProcessingService(ILogger<MediaProcessingService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ProcessAudioAsync(AudioFrame audioFrame, string outputPath)
    {
        try
        {
            var stream = _activeStreams.GetOrAdd(outputPath, path => 
                new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));

            await stream.WriteAsync(audioFrame.Data);
            await stream.FlushAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"处理音频数据失败: {outputPath}");
            return false;
        }
    }

    public async Task<string?> FinalizeAudioFileAsync(string tempPath, string finalPath, string format)
    {
        try
        {
            // 关闭并移除活动流
            if (_activeStreams.TryRemove(tempPath, out var stream))
            {
                await stream.DisposeAsync();
            }

            if (!File.Exists(tempPath))
                return null;

            // 简单的文件移动（实际项目中可以添加格式转换）
            if (tempPath != finalPath)
            {
                File.Move(tempPath, finalPath);
            }

            _logger.LogInformation($"音频文件处理完成: {finalPath}");
            return finalPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"完成音频文件处理失败: {tempPath} -> {finalPath}");
            return null;
        }
    }

    public async Task<TimeSpan> GetAudioDurationAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return TimeSpan.Zero;

            var fileSize = new FileInfo(filePath).Length;
            // 简单估算：假设44.1kHz, 16-bit, 单声道
            var estimatedSeconds = fileSize / (44100 * 2);
            return TimeSpan.FromSeconds(Math.Max(0, estimatedSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取音频时长失败: {filePath}");
            return TimeSpan.Zero;
        }
    }
}