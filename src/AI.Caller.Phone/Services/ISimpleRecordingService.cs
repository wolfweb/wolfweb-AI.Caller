using AI.Caller.Core;
using AI.Caller.Phone.Models;
using System.Linq.Expressions;

namespace AI.Caller.Phone.Services {
    public interface ISimpleRecordingService {
        Task<bool> StartRecordingAsync(int userId, SIPClient sipClient);

        Task<bool> StopRecordingAsync(int userId, SIPClient sipClient);

        Task<PagedResult<Recording>> GetRecordingsAsync(RecordingFilter filter, int? userId = null);
        Task<Recording?> FindRecordingBy(Expression<Func<Recording, bool>> predict);

        Task<bool> DeleteRecordingAsync(int recordingId, int? userId = null);

        Task<bool> IsAutoRecordingEnabledAsync(int userId);

        Task<bool> SetAutoRecordingAsync(int userId, bool enabled);

        Task<RecordingStatus?> GetRecordingStatusAsync(int userId);
        
        Task<bool> PauseRecordingAsync(int userId);

        Task<bool> ResumeRecordingAsync(int userId);
    }
}