using AI.Caller.Phone.Models;
using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Services;

public class TtsPlayerService : ITtsPlayerService {
    private readonly ILogger<TtsPlayerService> _logger;
    private readonly ITtsTemplateIntegrationService _templateService;
    private readonly AICustomerServiceManager _aiManager;

    public TtsPlayerService(
        ILogger<TtsPlayerService> logger,
        ITtsTemplateIntegrationService templateService,
        AICustomerServiceManager aiManager) {
        _logger = logger;
        _templateService = templateService;
        _aiManager = aiManager;
    }

    public async Task<(TtsTemplate? template, bool success)> PlayAsync(CallContext callContext, string destination) {
        throw new NotImplementedException();
    }
}