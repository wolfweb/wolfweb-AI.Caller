using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models.Dto;
using Microsoft.AspNetCore.Http;

namespace AI.Caller.Phone.Services;

public interface IRingtoneService {
    Task<List<RingtoneDto>> GetAvailableRingtonesAsync(int userId, string? type = null);

    Task<Ringtone> GetRingtoneForUserAsync(int userId, RingtoneType type);

    Task<Ringtone> UploadRingtoneAsync(IFormFile file, string name, string type, int userId);

    Task<bool> DeleteRingtoneAsync(int ringtoneId, int userId);

    Task<UserRingtoneSettingsDto?> GetUserSettingsAsync(int userId);

    Task<bool> UpdateUserSettingsAsync(int userId, int? incomingRingtoneId, int? ringbackToneId);

    Task<SystemRingtoneSettings?> GetSystemSettingsAsync();

    Task<bool> UpdateSystemSettingsAsync(int defaultIncomingRingtoneId, int defaultRingbackToneId, int updatedBy);

    bool ValidateAudioFile(IFormFile file, out string errorMessage);
}
