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
        var session = _aiCustomerServiceManager.GetActiveSession(user.Id);

        if (session == null) {
            _logger.LogInformation("No active session for user {UserId}, creating a new one.", user.Id);
            
            bool sessionCreated = await _aiCustomerServiceManager.StartAICustomerServiceAsync(user, sipClient, string.Empty);

            if (!sessionCreated) {
                _logger.LogError("Failed to create AI customer service session for user {UserId}.", user.Id);
                return;
            }

            session = _aiCustomerServiceManager.GetActiveSession(user.Id);
            if (session == null) {
                _logger.LogError("Session should have been created but was not found for user {UserId}.", user.Id);
                return;
            }
             _logger.LogInformation("Successfully created and retrieved new session for user {UserId}.", user.Id);
        }

        _ = session.AutoResponder.PlayScriptAsync(text, speakerId, speed ?? 1.0f);

        await session.AutoResponder.WaitForPlaybackToCompleteAsync();
        
        _logger.LogDebug("Finished playing script and waiting for user {UserId}.", user.Id);
    }
}