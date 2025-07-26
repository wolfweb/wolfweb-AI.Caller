using AI.Caller.Phone.Models;
using Microsoft.Extensions.Options;

namespace AI.Caller.Phone.Services;

/// <summary>
/// 文件存储服务实现
/// </summary>
public class FileStorageService : IFileStorageService
{
    private readonly ILogger<FileStorageService> _logger;
    private readonly string _basePath;
    private readonly string _tempPath;

    public FileStorageService(ILogger<FileStorageService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _basePath = configuration.GetValue<string>("RecordingSettings:StoragePath") ?? "recordings";
        _tempPath = Path.Combine(_basePath, "temp");
        
        // 确保基础目录存在
        Directory.CreateDirectory(_basePath);
        Directory.CreateDirectory(_tempPath);
    }

    public async Task<string> CreateRecordingFileAsync(string callId, string audioFormat)
    {
        try
        {
            var now = DateTime.Now;
            var dateFolder = Path.Combine(_basePath, now.Year.ToString(), now.Month.ToString("00"));
            
            // 确保日期文件夹存在
            await EnsureDirectoryExistsAsync(dateFolder);
            
            // 生成文件名：YYYYMMDD_HHMMSS_CallId.format
            var fileName = $"{now:yyyyMMdd_HHmmss}_{SanitizeFileName(callId)}.{audioFormat}";
            var filePath = Path.Combine(dateFolder, fileName);
            
            // 创建空文件
            await File.WriteAllBytesAsync(filePath, Array.Empty<byte>());
            
            _logger.LogInformation($"录音文件已创建: {filePath}");
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"创建录音文件失败 - CallId: {callId}");
            return string.Empty;
        }
    }

    public async Task<bool> FinalizeRecordingFileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning($"录音文件不存在: {filePath}");
                return false;
            }

            // 检查文件是否有内容
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                _logger.LogWarning($"录音文件为空: {filePath}");
                // 对于空文件，我们仍然认为是成功的，但会记录警告
            }

            // 创建元数据文件
            var metadataPath = Path.ChangeExtension(filePath, ".json");
            var metadata = new
            {
                FilePath = filePath,
                FileSize = fileInfo.Length,
                CreatedAt = fileInfo.CreationTime,
                FinalizedAt = DateTime.UtcNow,
                Duration = TimeSpan.Zero // 这里应该从实际录音中获取
            };

            var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(metadataPath, metadataJson);
            
            _logger.LogInformation($"录音文件已完成: {filePath}, 大小: {fileInfo.Length} 字节");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"完成录音文件失败: {filePath}");
            return false;
        }
    }

    public async Task<Stream> GetFileStreamAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"录音文件不存在: {filePath}");
            }

            // 使用 FileStream 以支持大文件的流式读取
            return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取文件流失败: {filePath}");
            throw;
        }
    }

    public async Task<bool> DeleteFileAsync(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation($"录音文件已删除: {filePath}");
            }

            // 同时删除元数据文件
            var metadataPath = Path.ChangeExtension(filePath, ".json");
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
                _logger.LogInformation($"录音元数据文件已删除: {metadataPath}");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"删除文件失败: {filePath}");
            return false;
        }
    }

    public async Task<long> GetFileSizeAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return 0;
            }

            var fileInfo = new FileInfo(filePath);
            return fileInfo.Length;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取文件大小失败: {filePath}");
            return 0;
        }
    }

    public async Task<StorageInfo> GetStorageInfoAsync()
    {
        try
        {
            var storageInfo = new StorageInfo();

            // 获取录音文件总数和大小
            if (Directory.Exists(_basePath))
            {
                var files = Directory.GetFiles(_basePath, "*.*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".json")) // 排除元数据文件
                    .ToArray();

                storageInfo.RecordingCount = files.Length;
                storageInfo.UsedSizeMB = files.Sum(f => new FileInfo(f).Length) / (1024 * 1024);
            }

            // 获取磁盘空间信息
            var driveInfo = new DriveInfo(Path.GetPathRoot(_basePath) ?? "C:");
            if (driveInfo.IsReady)
            {
                storageInfo.TotalSizeMB = driveInfo.TotalSize / (1024 * 1024);
                storageInfo.AvailableSizeMB = driveInfo.AvailableFreeSpace / (1024 * 1024);
            }
            else
            {
                // 如果无法获取磁盘信息，使用默认值
                storageInfo.TotalSizeMB = 10240; // 10GB
                storageInfo.AvailableSizeMB = storageInfo.TotalSizeMB - storageInfo.UsedSizeMB;
            }

            return storageInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取存储信息失败");
            return new StorageInfo
            {
                TotalSizeMB = 10240,
                UsedSizeMB = 0,
                AvailableSizeMB = 10240,
                RecordingCount = 0
            };
        }
    }

    public async Task<bool> EnsureDirectoryExistsAsync(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                _logger.LogDebug($"目录已创建: {path}");
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"创建目录失败: {path}");
            return false;
        }
    }

    /// <summary>
    /// 清理文件名中的非法字符
    /// </summary>
    private string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());
        
        // 限制文件名长度
        if (sanitized.Length > 50)
        {
            sanitized = sanitized.Substring(0, 50);
        }
        
        return string.IsNullOrEmpty(sanitized) ? "unknown" : sanitized;
    }

    /// <summary>
    /// 获取录音文件的实际时长（如果可能的话）
    /// </summary>
    private async Task<TimeSpan> GetAudioDurationAsync(string filePath)
    {
        try
        {
            // 这里可以集成音频处理库来获取实际时长
            // 目前返回基于文件大小的估算值
            var fileSize = await GetFileSizeAsync(filePath);
            
            // 假设 44.1kHz, 16-bit, 单声道的 WAV 文件
            // 每秒大约 88.2KB
            var estimatedSeconds = fileSize / (44100 * 2);
            return TimeSpan.FromSeconds(Math.Max(0, estimatedSeconds));
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// 清理临时文件
    /// </summary>
    public async Task<int> CleanupTempFilesAsync()
    {
        try
        {
            if (!Directory.Exists(_tempPath))
            {
                return 0;
            }

            var tempFiles = Directory.GetFiles(_tempPath);
            var cleanupCount = 0;
            var cutoffTime = DateTime.UtcNow.AddHours(-1); // 清理1小时前的临时文件

            foreach (var file in tempFiles)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTime < cutoffTime)
                {
                    try
                    {
                        File.Delete(file);
                        cleanupCount++;
                        _logger.LogDebug($"临时文件已清理: {file}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"清理临时文件失败: {file}");
                    }
                }
            }

            if (cleanupCount > 0)
            {
                _logger.LogInformation($"清理了 {cleanupCount} 个临时文件");
            }

            return cleanupCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理临时文件失败");
            return 0;
        }
    }
}