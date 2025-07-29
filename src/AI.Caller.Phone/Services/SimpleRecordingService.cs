using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Services
{
    /// <summary>
    /// 简化录音服务实现
    /// </summary>
    public class SimpleRecordingService : ISimpleRecordingService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<SimpleRecordingService> _logger;
        private readonly string _recordingsPath;
        private readonly ConcurrentDictionary<string, Recording> _activeRecordings;

        public SimpleRecordingService(
            AppDbContext dbContext,
            ILogger<SimpleRecordingService> logger,
            IConfiguration configuration)
        {
            _dbContext = dbContext;
            _logger = logger;
            _recordingsPath = configuration.GetValue<string>("RecordingsPath") ?? "recordings";
            _activeRecordings = new ConcurrentDictionary<string, Recording>();

            // 确保录音目录存在
            Directory.CreateDirectory(_recordingsPath);
        }

        /// <summary>
        /// 开始录音
        /// </summary>
        public async Task<bool> StartRecordingAsync(string sipUsername)
        {
            try
            {
                // 检查是否已经在录音
                if (_activeRecordings.ContainsKey(sipUsername))
                {
                    _logger.LogWarning($"用户 {sipUsername} 已经在录音中");
                    return false;
                }

                // 获取用户信息
                var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.SipUsername == sipUsername);
                if (user == null)
                {
                    _logger.LogError($"未找到用户: {sipUsername}");
                    return false;
                }

                // 生成录音文件名
                var fileName = $"recording_{sipUsername}_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
                var filePath = Path.Combine(_recordingsPath, fileName);

                // 创建录音记录
                var recording = new Recording
                {
                    UserId = user.Id,
                    SipUsername = sipUsername,
                    StartTime = DateTime.UtcNow,
                    FilePath = filePath,
                    Status = RecordingStatus.Recording
                };

                // 保存到数据库
                _dbContext.Recordings.Add(recording);
                await _dbContext.SaveChangesAsync();

                // 添加到活动录音列表
                _activeRecordings.TryAdd(sipUsername, recording);

                // 创建空的录音文件（实际实现中这里应该开始音频录制）
                await CreateEmptyRecordingFileAsync(filePath);

                _logger.LogInformation($"开始录音 - 用户: {sipUsername}, 文件: {fileName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"开始录音失败 - 用户: {sipUsername}");
                return false;
            }
        }

        /// <summary>
        /// 停止录音
        /// </summary>
        public async Task<bool> StopRecordingAsync(string sipUsername)
        {
            try
            {
                // 检查是否在录音中
                if (!_activeRecordings.TryRemove(sipUsername, out var recording))
                {
                    _logger.LogWarning($"用户 {sipUsername} 没有活动的录音");
                    return false;
                }

                // 更新录音记录
                recording.EndTime = DateTime.UtcNow;
                recording.Duration = recording.EndTime.Value - recording.StartTime;
                recording.Status = RecordingStatus.Completed;

                // 完成录音文件（添加一些模拟音频数据）
                await FinalizeRecordingFileAsync(recording.FilePath, recording.Duration);

                // 获取文件大小
                if (File.Exists(recording.FilePath))
                {
                    var fileInfo = new FileInfo(recording.FilePath);
                    recording.FileSize = fileInfo.Length;
                }

                // 更新数据库
                _dbContext.Recordings.Update(recording);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation($"停止录音 - 用户: {sipUsername}, 时长: {recording.Duration}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"停止录音失败 - 用户: {sipUsername}");
                return false;
            }
        }

        /// <summary>
        /// 获取用户的录音列表
        /// </summary>
        public async Task<List<Recording>> GetRecordingsAsync(int userId)
        {
            try
            {
                return await _dbContext.Recordings
                    .Where(r => r.UserId == userId)
                    .OrderByDescending(r => r.StartTime)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取录音列表失败 - 用户ID: {userId}");
                return new List<Recording>();
            }
        }

        /// <summary>
        /// 删除录音
        /// </summary>
        public async Task<bool> DeleteRecordingAsync(int recordingId, int userId)
        {
            try
            {
                var recording = await _dbContext.Recordings
                    .FirstOrDefaultAsync(r => r.Id == recordingId && r.UserId == userId);

                if (recording == null)
                {
                    _logger.LogWarning($"录音不存在或无权限 - ID: {recordingId}, 用户ID: {userId}");
                    return false;
                }

                // 删除文件
                if (File.Exists(recording.FilePath))
                {
                    File.Delete(recording.FilePath);
                }

                // 删除数据库记录
                _dbContext.Recordings.Remove(recording);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation($"删除录音成功 - ID: {recordingId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"删除录音失败 - ID: {recordingId}");
                return false;
            }
        }

        /// <summary>
        /// 检查是否启用自动录音
        /// </summary>
        public async Task<bool> IsAutoRecordingEnabledAsync(int userId)
        {
            try
            {
                var user = await _dbContext.Users.FindAsync(userId);
                return user?.AutoRecording ?? false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"检查自动录音设置失败 - 用户ID: {userId}");
                return false;
            }
        }

        /// <summary>
        /// 设置自动录音
        /// </summary>
        public async Task<bool> SetAutoRecordingAsync(int userId, bool enabled)
        {
            try
            {
                var user = await _dbContext.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogError($"用户不存在 - ID: {userId}");
                    return false;
                }

                user.AutoRecording = enabled;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation($"设置自动录音 - 用户ID: {userId}, 启用: {enabled}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"设置自动录音失败 - 用户ID: {userId}");
                return false;
            }
        }

        /// <summary>
        /// 创建空的录音文件（模拟录音开始）
        /// </summary>
        private async Task CreateEmptyRecordingFileAsync(string filePath)
        {
            try
            {
                // 创建一个空的WAV文件头（44字节的标准WAV头）
                var wavHeader = new byte[]
                {
                    // RIFF header
                    0x52, 0x49, 0x46, 0x46, // "RIFF"
                    0x00, 0x00, 0x00, 0x00, // File size (will be updated later)
                    0x57, 0x41, 0x56, 0x45, // "WAVE"
                    
                    // fmt chunk
                    0x66, 0x6D, 0x74, 0x20, // "fmt "
                    0x10, 0x00, 0x00, 0x00, // Chunk size (16)
                    0x01, 0x00,             // Audio format (PCM)
                    0x01, 0x00,             // Number of channels (mono)
                    0x40, 0x1F, 0x00, 0x00, // Sample rate (8000 Hz)
                    0x80, 0x3E, 0x00, 0x00, // Byte rate
                    0x02, 0x00,             // Block align
                    0x10, 0x00,             // Bits per sample (16)
                    
                    // data chunk
                    0x64, 0x61, 0x74, 0x61, // "data"
                    0x00, 0x00, 0x00, 0x00  // Data size (will be updated later)
                };

                await File.WriteAllBytesAsync(filePath, wavHeader);
                _logger.LogDebug($"创建空录音文件: {filePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"创建录音文件失败: {filePath}");
                throw;
            }
        }

        /// <summary>
        /// 完成录音文件（添加模拟音频数据并更新文件头）
        /// </summary>
        private async Task FinalizeRecordingFileAsync(string filePath, TimeSpan duration)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning($"录音文件不存在: {filePath}");
                    return;
                }

                // 计算需要的音频数据大小（8000 Hz, 16-bit, mono）
                var sampleRate = 8000;
                var bytesPerSample = 2;
                var totalSamples = (int)(duration.TotalSeconds * sampleRate);
                var audioDataSize = totalSamples * bytesPerSample;

                // 生成一些模拟的静音数据（实际实现中这里应该是真实的音频数据）
                var audioData = new byte[audioDataSize];
                // 填充静音数据（全零）

                // 读取现有的WAV头
                var existingData = await File.ReadAllBytesAsync(filePath);
                var newData = new byte[44 + audioDataSize];

                // 复制WAV头
                Array.Copy(existingData, 0, newData, 0, 44);

                // 更新文件大小（总大小 - 8）
                var fileSize = 36 + audioDataSize;
                newData[4] = (byte)(fileSize & 0xFF);
                newData[5] = (byte)((fileSize >> 8) & 0xFF);
                newData[6] = (byte)((fileSize >> 16) & 0xFF);
                newData[7] = (byte)((fileSize >> 24) & 0xFF);

                // 更新数据块大小
                newData[40] = (byte)(audioDataSize & 0xFF);
                newData[41] = (byte)((audioDataSize >> 8) & 0xFF);
                newData[42] = (byte)((audioDataSize >> 16) & 0xFF);
                newData[43] = (byte)((audioDataSize >> 24) & 0xFF);

                // 添加音频数据
                Array.Copy(audioData, 0, newData, 44, audioDataSize);

                // 写入完整的文件
                await File.WriteAllBytesAsync(filePath, newData);

                _logger.LogDebug($"完成录音文件: {filePath}, 时长: {duration}, 大小: {newData.Length} 字节");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"完成录音文件失败: {filePath}");
                throw;
            }
        }

        /// <summary>
        /// 获取当前录音状态
        /// </summary>
        public async Task<RecordingStatus?> GetRecordingStatusAsync(string sipUsername)
        {
            try
            {
                if (_activeRecordings.TryGetValue(sipUsername, out var recording))
                {
                    return recording.Status;
                }

                // 检查数据库中最近的录音状态
                var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.SipUsername == sipUsername);
                if (user != null)
                {
                    var latestRecording = await _dbContext.Recordings
                        .Where(r => r.UserId == user.Id)
                        .OrderByDescending(r => r.StartTime)
                        .FirstOrDefaultAsync();

                    return latestRecording?.Status;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取录音状态失败 - 用户: {sipUsername}");
                return null;
            }
        }
    }
}