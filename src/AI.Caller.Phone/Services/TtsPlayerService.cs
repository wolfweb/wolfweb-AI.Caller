using AI.Caller.Core;
using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Services;

public class TtsPlayerService : ITtsPlayerService {
    private readonly ILogger<TtsPlayerService> _logger;
    private readonly AICustomerServiceManager _aiCustomerServiceManager;

    public TtsPlayerService(
        ILogger<TtsPlayerService> logger,
        AICustomerServiceManager aiCustomerServiceManager) {
        _logger                   = logger;
        _aiCustomerServiceManager = aiCustomerServiceManager;
    }

    public async Task PlayTtsAsync(string text, User user, SIPClient sipClient, float? speed = 1.0f, int speakerId = 0) {
        await _aiCustomerServiceManager.StartAICustomerServiceAsync(user, sipClient, text);
    }
}