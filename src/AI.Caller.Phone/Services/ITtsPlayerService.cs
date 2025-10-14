using AI.Caller.Core;
using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Services;

public interface ITtsPlayerService {
    Task<TimeSpan> PlayTtsAsync(string text, User user, SIPClient sipClient, float? speed = 1.0f, int speakerId = 0);
    Task StopPlayoutAsync(User user);
}