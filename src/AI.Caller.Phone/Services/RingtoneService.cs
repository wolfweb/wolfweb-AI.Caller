using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models.Dto;
using Microsoft.EntityFrameworkCore;

namespace AI.Caller.Phone.Services;

public class RingtoneService : IRingtoneService {
    private readonly AppDbContext _dbContext;
    private readonly ILogger<RingtoneService> _logger;
    private readonly IWebHostEnvironment _environment;

    private const long MaxFileSize = 5 * 1024 * 1024; // 5MB
    private const int MaxDuration = 30; // 30 seconds
    private static readonly string[] AllowedExtensions = { ".mp3", ".wav" };
    private static readonly string[] AllowedMimeTypes = { "audio/mpeg", "audio/mp3", "audio/wav", "audio/x-wav" };

    public RingtoneService(
        AppDbContext dbContext,
        ILogger<RingtoneService> logger,
        IWebHostEnvironment environment) {
        _dbContext = dbContext;
        _logger = logger;
        _environment = environment;
    }

    public async Task<List<RingtoneDto>> GetAvailableRingtonesAsync(int userId, string? type = null) {
        var query = _dbContext.Ringtones
            .Include(r => r.Uploader)
            .Where(r => r.IsSystem || r.UploadedBy == userId);

        if (!string.IsNullOrEmpty(type)) {
            query = query.Where(r => r.Type == type || r.Type == "Both");
        }

        var ringtones = await query
            .OrderByDescending(r => r.IsSystem)
            .ThenBy(r => r.Name)
            .ToListAsync();

        return ringtones.Select(r => new RingtoneDto {
            Id = r.Id,
            Name = r.Name,
            FileName = r.FileName,
            FilePath = r.FilePath,
            FileSize = r.FileSize,
            Duration = r.Duration,
            Type = r.Type,
            IsSystem = r.IsSystem,
            UploadedBy = r.UploadedBy,
            UploaderName = r.Uploader?.Username,
            CreatedAt = r.CreatedAt
        }).ToList();
    }

    public async Task<Ringtone> GetRingtoneForUserAsync(int userId, RingtoneType type) {
        _logger.LogDebug("获取用户 {UserId} 的 {Type} 铃音", userId, type);

        var userSettings = await _dbContext.UserRingtoneSettings
            .Include(s => s.IncomingRingtone)
            .Include(s => s.RingbackTone)
            .FirstOrDefaultAsync(s => s.UserId == userId);

        Ringtone? userRingtone = type == RingtoneType.Incoming
            ? userSettings?.IncomingRingtone
            : userSettings?.RingbackTone;

        if (userRingtone != null) {
            _logger.LogInformation("使用用户自定义铃音: {RingtoneName}", userRingtone.Name);
            return userRingtone;
        }

        var systemSettings = await _dbContext.SystemRingtoneSettings
            .Include(s => s.DefaultIncomingRingtone)
            .Include(s => s.DefaultRingbackTone)
            .FirstOrDefaultAsync();

        if (systemSettings != null) {
            Ringtone? systemRingtone = type == RingtoneType.Incoming
                ? systemSettings.DefaultIncomingRingtone
                : systemSettings.DefaultRingbackTone;

            if (systemRingtone != null) {
                _logger.LogInformation("使用系统默认铃音: {RingtoneName}", systemRingtone.Name);
                return systemRingtone;
            }
        }

        _logger.LogWarning("系统默认铃音未配置，使用硬编码默认值");
        return await GetHardcodedDefaultRingtone(type);
    }

    private async Task<Ringtone> GetHardcodedDefaultRingtone(RingtoneType type) {
        var defaultRingtone = await _dbContext.Ringtones
            .FirstOrDefaultAsync(r => r.IsSystem && r.Name == "默认铃音");

        if (defaultRingtone != null) {
            return defaultRingtone;
        }

        var anySystemRingtone = await _dbContext.Ringtones
            .FirstOrDefaultAsync(r => r.IsSystem);

        if (anySystemRingtone != null) {
            return anySystemRingtone;
        }

        _logger.LogError("数据库中没有任何系统铃音，返回临时默认铃音");
        return new Ringtone {
            Id = 0,
            Name = "默认铃音",
            FileName = "default.mp3",
            FilePath = "/ringtones/default.mp3",
            FileSize = 0,
            Duration = 10,
            Type = "Both",
            IsSystem = true
        };
    }

    public async Task<Ringtone> UploadRingtoneAsync(IFormFile file, string name, string type, int userId) {
        if (!ValidateAudioFile(file, out string errorMessage)) {
            throw new InvalidOperationException(errorMessage);
        }

        var customDir = Path.Combine(_environment.WebRootPath, "ringtones", "custom");
        if (!Directory.Exists(customDir)) {
            Directory.CreateDirectory(customDir);
        }

        var extension = Path.GetExtension(file.FileName);
        var fileName = $"user_{userId}_{DateTime.UtcNow.Ticks}{extension}";
        var filePath = Path.Combine(customDir, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create)) {
            await file.CopyToAsync(stream);
        }

        var ringtone = new Ringtone {
            Name = name,
            FileName = fileName,
            FilePath = $"/ringtones/custom/{fileName}",
            FileSize = file.Length,
            Duration = 10, // TODO: 实际应该解析音频文件获取时长
            Type = type,
            IsSystem = false,
            UploadedBy = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Ringtones.Add(ringtone);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("用户 {UserId} 上传铃音: {RingtoneName}", userId, name);

        return ringtone;
    }

    public async Task<bool> DeleteRingtoneAsync(int ringtoneId, int userId) {
        var ringtone = await _dbContext.Ringtones.FindAsync(ringtoneId);

        if (ringtone == null) {
            return false;
        }

        if (ringtone.IsSystem || ringtone.UploadedBy != userId) {
            throw new UnauthorizedAccessException("无权删除此铃音");
        }

        var isInUse = await _dbContext.UserRingtoneSettings
            .AnyAsync(s => s.IncomingRingtoneId == ringtoneId || s.RingbackToneId == ringtoneId);

        if (isInUse) {
            throw new InvalidOperationException("此铃音正在被使用，无法删除");
        }

        var fullPath = Path.Combine(_environment.WebRootPath, ringtone.FilePath.TrimStart('/'));
        if (File.Exists(fullPath)) {
            File.Delete(fullPath);
        }

        _dbContext.Ringtones.Remove(ringtone);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("用户 {UserId} 删除铃音: {RingtoneName}", userId, ringtone.Name);

        return true;
    }

    public async Task<UserRingtoneSettingsDto?> GetUserSettingsAsync(int userId) {
        var settings = await _dbContext.UserRingtoneSettings
            .Include(s => s.IncomingRingtone)
            .Include(s => s.RingbackTone)
            .FirstOrDefaultAsync(s => s.UserId == userId);

        if (settings == null) {
            return null;
        }

        return new UserRingtoneSettingsDto {
            UserId = settings.UserId,
            IncomingRingtone = settings.IncomingRingtone != null ? new RingtoneDto {
                Id = settings.IncomingRingtone.Id,
                Name = settings.IncomingRingtone.Name,
                FilePath = settings.IncomingRingtone.FilePath,
                Type = settings.IncomingRingtone.Type,
                IsSystem = settings.IncomingRingtone.IsSystem
            } : null,
            RingbackTone = settings.RingbackTone != null ? new RingtoneDto {
                Id = settings.RingbackTone.Id,
                Name = settings.RingbackTone.Name,
                FilePath = settings.RingbackTone.FilePath,
                Type = settings.RingbackTone.Type,
                IsSystem = settings.RingbackTone.IsSystem
            } : null,
            UpdatedAt = settings.UpdatedAt
        };
    }

    public async Task<bool> UpdateUserSettingsAsync(int userId, int? incomingRingtoneId, int? ringbackToneId) {
        var settings = await _dbContext.UserRingtoneSettings
            .FirstOrDefaultAsync(s => s.UserId == userId);

        if (settings == null) {
            settings = new UserRingtoneSettings {
                UserId = userId,
                IncomingRingtoneId = incomingRingtoneId,
                RingbackToneId = ringbackToneId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.UserRingtoneSettings.Add(settings);
        } else {
            settings.IncomingRingtoneId = incomingRingtoneId;
            settings.RingbackToneId = ringbackToneId;
            settings.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("用户 {UserId} 更新铃音设置", userId);

        return true;
    }

    public async Task<SystemRingtoneSettings?> GetSystemSettingsAsync() {
        return await _dbContext.SystemRingtoneSettings
            .Include(s => s.DefaultIncomingRingtone)
            .Include(s => s.DefaultRingbackTone)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> UpdateSystemSettingsAsync(int defaultIncomingRingtoneId, int defaultRingbackToneId, int updatedBy) {
        var settings = await _dbContext.SystemRingtoneSettings.FirstOrDefaultAsync();

        if (settings == null) {
            settings = new SystemRingtoneSettings {
                DefaultIncomingRingtoneId = defaultIncomingRingtoneId,
                DefaultRingbackToneId = defaultRingbackToneId,
                UpdatedBy = updatedBy,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.SystemRingtoneSettings.Add(settings);
        } else {
            settings.DefaultIncomingRingtoneId = defaultIncomingRingtoneId;
            settings.DefaultRingbackToneId = defaultRingbackToneId;
            settings.UpdatedBy = updatedBy;
            settings.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("管理员 {UserId} 更新系统默认铃音设置", updatedBy);

        return true;
    }

    public bool ValidateAudioFile(IFormFile file, out string errorMessage) {
        errorMessage = string.Empty;

        if (file == null || file.Length == 0) {
            errorMessage = "文件不能为空";
            return false;
        }

        if (file.Length > MaxFileSize) {
            errorMessage = $"文件大小不能超过 {MaxFileSize / 1024 / 1024}MB";
            return false;
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension)) {
            errorMessage = $"只支持 {string.Join(", ", AllowedExtensions)} 格式";
            return false;
        }

        if (!AllowedMimeTypes.Contains(file.ContentType.ToLowerInvariant())) {
            errorMessage = $"不支持的文件类型: {file.ContentType}";
            return false;
        }

        return true;
    }
}
