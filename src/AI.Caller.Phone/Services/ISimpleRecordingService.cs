using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.Services {
    public interface ISimpleRecordingService {
        Task<bool> StartRecordingAsync(int userId);

        Task<bool> StopRecordingAsync(int userId);

        Task<List<Recording>> GetRecordingsAsync(int userId);

        Task<bool> DeleteRecordingAsync(int recordingId, int userId);

        Task<bool> IsAutoRecordingEnabledAsync(int userId);

        Task<bool> SetAutoRecordingAsync(int userId, bool enabled);

        Task<Models.RecordingStatus?> GetRecordingStatusAsync(int userId);
    }
}