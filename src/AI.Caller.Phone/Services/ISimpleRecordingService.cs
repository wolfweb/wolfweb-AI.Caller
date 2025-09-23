using AI.Caller.Core;
using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.Services {
    public interface ISimpleRecordingService {
        Task<bool> StartRecordingAsync(int userId, SIPClient sipClient);

        Task<bool> StopRecordingAsync(int userId, SIPClient sipClient);

        Task<List<Recording>> GetRecordingsAsync(int userId);

        Task<List<Recording>> GetAllRecordingsAsync();

        Task<bool> DeleteRecordingAsync(int recordingId, int? userId = null);

        Task<bool> IsAutoRecordingEnabledAsync(int userId);

        Task<bool> SetAutoRecordingAsync(int userId, bool enabled);

        Task<Models.RecordingStatus?> GetRecordingStatusAsync(int userId);
        
        Task<bool> PauseRecordingAsync(int userId);

        Task<bool> ResumeRecordingAsync(int userId);
    }
}