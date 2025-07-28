using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;
using AI.Caller.Core.Recording;
using AI.Caller.Core;
using PhoneRecordingStatus = AI.Caller.Phone.Entities.RecordStatus;
using PhoneStorageInfo = AI.Caller.Phone.Models.StorageInfo;

namespace AI.Caller.Phone.Services;

/// <summary>
/// 录音服务实现
/// </summary>
public class RecordingService : IRecordingService
{
    private readonly AppDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly IMediaProcessingService _mediaProcessingService;
    private readonly ILogger<RecordingService> _logger;
    private readonly ApplicationContext _applicationContext;
    private readonly Dictionary<string, CallRecording> _activeRecordings = new();
    private readonly Dictionary<string, RecordingSession> _recordingSessions = new();

    public RecordingService(
        AppDbContext dbContext,
        IFileStorageService fileStorageService,
        IMediaProcessingService mediaProcessingService,
        ILogger<RecordingService> logger,
        ApplicationContext applicationContext)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _mediaProcessingService = mediaProcessingService;
        _logger = logger;
        _applicationContext = applicationContext;
    }

    public async Task<RecordingResult> StartRecordingAsync(string callId, int userId, string callerNumber, string calleeNumber)
    {
        try
        {
            _logger.LogInformation($"开始录音 - CallId: {callId}");

            // 基础验证
            if (_activeRecordings.ContainsKey(callId))
                return RecordingResult.CreateFailure(callId, "该通话已经在录音中");

            var storageInfo = await GetStorageInfoAsync();
            if (storageInfo.UsagePercentage > 95)
                return RecordingResult.CreateFailure(callId, "存储空间不足");

            // 获取SipClient
            var sipClient = await GetSipClientAsync(userId);
            if (sipClient == null)
                return RecordingResult.CreateFailure(callId, "SIP客户端不可用");

            // 创建录音会话
            var settings = await GetUserRecordingSettingsAsync(userId);
            var filePath = await _fileStorageService.CreateRecordingFileAsync(callId, settings.AudioFormat);
            if (string.IsNullOrEmpty(filePath))
                return RecordingResult.CreateFailure(callId, "无法创建录音文件");

            var recording = await CreateRecordingAsync(callId, userId, callerNumber, calleeNumber, filePath, settings);
            var session = CreateRecordingSession(sipClient, filePath);

            // 启动录音
            if (!await session.StartAsync())
            {
                await CleanupFailedRecording(recording, session);
                return RecordingResult.CreateFailure(callId, "启动录音失败");
            }

            _activeRecordings[callId] = recording;
            _recordingSessions[callId] = session;

            _logger.LogInformation($"录音开始成功 - CallId: {callId}");
            return RecordingResult.CreateSuccess(callId, recording.Id, "录音已开始");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"开始录音失败 - CallId: {callId}");
            return RecordingResult.CreateFailure(callId, $"开始录音失败: {ex.Message}");
        }
    }

    public async Task<RecordingResult> StopRecordingAsync(string callId)
    {
        try
        {
            _logger.LogInformation($"停止录音 - CallId: {callId}");

            var recording = _activeRecordings.GetValueOrDefault(callId) ?? 
                await _dbContext.CallRecordings.FirstOrDefaultAsync(r => r.CallId == callId && r.Status == PhoneRecordingStatus.Recording);

            if (recording == null)
                return RecordingResult.CreateFailure(callId, "未找到正在录音的记录");

            // 停止录音会话
            if (_recordingSessions.TryGetValue(callId, out var session))
            {
                await session.StopAsync();
                session.Dispose();
                _recordingSessions.Remove(callId);
            }

            // 完成录音文件处理
            var finalPath = await _mediaProcessingService.FinalizeAudioFileAsync(recording.FilePath, recording.FilePath, recording.AudioFormat);
            var fileFinalized = !string.IsNullOrEmpty(finalPath);

            // 更新录音记录
            recording.EndTime = DateTime.UtcNow;
            recording.Duration = recording.EndTime.Value - recording.StartTime;
            recording.Status = fileFinalized ? PhoneRecordingStatus.Completed : PhoneRecordingStatus.Failed;
            recording.FileSize = fileFinalized ? await _fileStorageService.GetFileSizeAsync(recording.FilePath) : 0;

            await _dbContext.SaveChangesAsync();
            _activeRecordings.Remove(callId);

            _logger.LogInformation($"录音停止成功 - CallId: {callId}");
            return RecordingResult.CreateSuccess(callId, recording.Id, "录音已停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"停止录音失败 - CallId: {callId}");
            return RecordingResult.CreateFailure(callId, $"停止录音失败: {ex.Message}");
        }
    }

    public async Task<CallRecording?> GetRecordingAsync(int recordingId)
    {
        return await _dbContext.CallRecordings
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == recordingId && r.DeletedAt == null);
    }

    public async Task<PagedResult<CallRecording>> GetRecordingsAsync(int userId, RecordingFilter filter)
    {
        var query = _dbContext.CallRecordings
            .Where(r => r.UserId == userId && r.DeletedAt == null);

        // 应用过滤器
        if (filter.StartDate.HasValue)
        {
            query = query.Where(r => r.StartTime >= filter.StartDate.Value);
        }

        if (filter.EndDate.HasValue)
        {
            query = query.Where(r => r.StartTime <= filter.EndDate.Value);
        }

        if (!string.IsNullOrEmpty(filter.CallerNumber))
        {
            query = query.Where(r => r.CallerNumber.Contains(filter.CallerNumber));
        }

        if (!string.IsNullOrEmpty(filter.CalleeNumber))
        {
            query = query.Where(r => r.CalleeNumber.Contains(filter.CalleeNumber));
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(r => r.Status == filter.Status.Value);
        }

        // 获取总数
        var totalCount = await query.CountAsync();

        // 应用分页和排序
        var items = await query
            .OrderByDescending(r => r.StartTime)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Include(r => r.User)
            .ToListAsync();

        return new PagedResult<CallRecording>
        {
            Items = items,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<Stream?> GetRecordingStreamAsync(int recordingId)
    {
        var recording = await GetRecordingAsync(recordingId);
        if (recording == null || recording.Status != PhoneRecordingStatus.Completed)
        {
            return null;
        }

        return await _fileStorageService.GetFileStreamAsync(recording.FilePath);
    }

    public async Task<bool> DeleteRecordingAsync(int recordingId, int userId)
    {
        try
        {
            var recording = await _dbContext.CallRecordings
                .FirstOrDefaultAsync(r => r.Id == recordingId && r.UserId == userId && r.DeletedAt == null);

            if (recording == null)
            {
                return false;
            }

            // 软删除
            recording.DeletedAt = DateTime.UtcNow;
            recording.Status = PhoneRecordingStatus.Deleted;

            await _dbContext.SaveChangesAsync();

            // 异步删除物理文件
            _ = Task.Run(async () =>
            {
                try
                {
                    await _fileStorageService.DeleteFileAsync(recording.FilePath);
                    _logger.LogInformation($"录音文件已删除 - RecordingId: {recordingId}, FilePath: {recording.FilePath}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"删除录音文件失败 - RecordingId: {recordingId}, FilePath: {recording.FilePath}");
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"删除录音记录失败 - RecordingId: {recordingId}");
            return false;
        }
    }

    public async Task<PhoneStorageInfo> GetStorageInfoAsync()
    {
        return await _fileStorageService.GetStorageInfoAsync();
    }

    public async Task<int> CleanupExpiredRecordingsAsync()
    {
        try
        {
            var cleanupCount = 0;
            var users = await _dbContext.Users.ToListAsync();

            foreach (var user in users)
            {
                var settings = await GetUserRecordingSettingsAsync(user.Id);
                var expiredDate = DateTime.UtcNow.AddDays(-settings.MaxRetentionDays);

                var expiredRecordings = await _dbContext.CallRecordings
                    .Where(r => r.UserId == user.Id && 
                               r.CreatedAt < expiredDate && 
                               r.DeletedAt == null)
                    .ToListAsync();

                foreach (var recording in expiredRecordings)
                {
                    recording.DeletedAt = DateTime.UtcNow;
                    recording.Status = PhoneRecordingStatus.Deleted;

                    // 异步删除物理文件
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _fileStorageService.DeleteFileAsync(recording.FilePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"清理过期录音文件失败 - FilePath: {recording.FilePath}");
                        }
                    });

                    cleanupCount++;
                }
            }

            if (cleanupCount > 0)
            {
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation($"清理了 {cleanupCount} 个过期录音记录");
            }

            return cleanupCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理过期录音记录失败");
            return 0;
        }
    }

    public async Task<PhoneRecordingStatus?> GetRecordingStatusAsync(string callId)
    {
        // 先检查活动录音
        if (_activeRecordings.TryGetValue(callId, out var activeRecording))
        {
            return activeRecording.Status;
        }

        // 从数据库查询
        var recording = await _dbContext.CallRecordings
            .Where(r => r.CallId == callId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        return recording?.Status;
    }

    public async Task<bool> HasRecordingPermissionAsync(int userId, int recordingId)
    {
        return await _dbContext.CallRecordings
            .AnyAsync(r => r.Id == recordingId && r.UserId == userId && r.DeletedAt == null);
    }

    /// <summary>
    /// 获取用户录音设置
    /// </summary>
    private async Task<RecordingSetting> GetUserRecordingSettingsAsync(int userId)
    {
        var settings = await _dbContext.RecordingSettings
            .FirstOrDefaultAsync(s => s.UserId == userId);

        if (settings == null)
        {
            // 创建默认设置
            settings = new RecordingSetting
            {
                UserId = userId,
                AutoRecording = true, // 默认启用自动录音
                StoragePath = "recordings",
                MaxRetentionDays = 30,
                MaxStorageSizeMB = 1024,
                AudioFormat = "wav",
                AudioQuality = 44100,
                EnableCompression = true
            };

            _dbContext.RecordingSettings.Add(settings);
            await _dbContext.SaveChangesAsync();
        }

        return settings;
    }

    /// <summary>
    /// 获取SipClient
    /// </summary>
    private async Task<SIPClient?> GetSipClientAsync(int userId)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.SipUsername == null) return null;
        
        return _applicationContext.SipClients.GetValueOrDefault(user.SipUsername);
    }

    /// <summary>
    /// 创建录音记录
    /// </summary>
    private async Task<CallRecording> CreateRecordingAsync(string callId, int userId, string callerNumber, string calleeNumber, string filePath, RecordingSetting settings)
    {
        var recording = new CallRecording
        {
            CallId = callId,
            UserId = userId,
            CallerNumber = callerNumber,
            CalleeNumber = calleeNumber,
            StartTime = DateTime.UtcNow,
            FilePath = filePath,
            AudioFormat = settings.AudioFormat,
            Status = PhoneRecordingStatus.Recording,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.CallRecordings.Add(recording);
        await _dbContext.SaveChangesAsync();
        return recording;
    }

    /// <summary>
    /// 创建录音会话
    /// </summary>
    private RecordingSession CreateRecordingSession(SIPClient sipClient, string outputPath)
    {
        var audioRecorder = new AudioRecorder(_logger);
        var audioMixer = new AudioMixer(_logger);
        
        sipClient.EnableRecording(audioRecorder, _logger);
        
        var session = new RecordingSession(sipClient, audioRecorder, audioMixer, _logger);
        session.AudioFrameReady += async (sender, frame) => 
            await _mediaProcessingService.ProcessAudioAsync(frame, outputPath);
        
        return session;
    }

    /// <summary>
    /// 清理失败的录音
    /// </summary>
    private async Task CleanupFailedRecording(CallRecording recording, RecordingSession session)
    {
        try
        {
            session?.Dispose();
            _dbContext.CallRecordings.Remove(recording);
            await _dbContext.SaveChangesAsync();
            await _fileStorageService.DeleteFileAsync(recording.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理失败录音时出错");
        }
    }
}