using AI.Caller.Core;
using AI.Caller.Phone.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Linq;
using System.Threading.Tasks;

namespace AI.Caller.Phone.Services;

public class TtsTemplateIntegrationService : ITtsTemplateIntegrationService {
    private readonly AppDbContext _dbContext;
    private readonly AICustomerServiceSettings _aiSettings;

    public TtsTemplateIntegrationService(AppDbContext dbContext, IOptions<AICustomerServiceSettings> aiSettings) {
        _dbContext = dbContext;
        _aiSettings = aiSettings.Value;
    }

    public Task<bool> ShouldEnableAIForCall(User user) {
        bool shouldEnable = _aiSettings.Enabled && user.EnableAI;
        return Task.FromResult(shouldEnable);
    }

    public async Task<string> GetTtsScriptForCall(string destination) {
        throw new NotImplementedException();
    }

    public async Task<TtsTemplate?> GetTtsTemplateForCall(string destination) {
        throw new NotImplementedException();
    }
}