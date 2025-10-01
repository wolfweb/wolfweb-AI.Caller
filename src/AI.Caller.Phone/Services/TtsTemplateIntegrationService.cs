using AI.Caller.Core;
using AI.Caller.Phone.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Linq;
using System.Threading.Tasks;

namespace AI.Caller.Phone.Services;

/// <inheritdoc cref="ITtsTemplateIntegrationService" />
public class TtsTemplateIntegrationService : ITtsTemplateIntegrationService
{
    private readonly AppDbContext _dbContext;
    private readonly AICustomerServiceSettings _aiSettings;

    public TtsTemplateIntegrationService(AppDbContext dbContext, IOptions<AICustomerServiceSettings> aiSettings)
    {
        _dbContext = dbContext;
        _aiSettings = aiSettings.Value;
    }

    /// <inheritdoc />
    public Task<bool> ShouldEnableAIForCall(User user)
    {
        // AI is enabled for the call if and only if the global switch AND the user's switch are both on.
        bool shouldEnable = _aiSettings.Enabled && user.EnableAI;
        return Task.FromResult(shouldEnable);
    }

    /// <inheritdoc />
    public async Task<string> GetTtsScriptForCall(string destination)
    {
        // Find the best matching active template.
        // A null or empty TargetPattern is treated as a general-purpose, lower-priority template.
        // A specific match on the destination number is prioritized over a general-purpose one.
        var template = await _dbContext.TtsTemplates
            .Where(t => t.IsActive && (t.TargetPattern == destination || string.IsNullOrEmpty(t.TargetPattern)))
            .OrderByDescending(t => t.TargetPattern == destination) // Prioritize specific match
            .ThenByDescending(t => t.Priority)
            .FirstOrDefaultAsync();

        if (template != null)
        {
            return template.Content;
        }

        // If no template is found in the DB, fallback to the default script from appsettings.json.
        return _aiSettings.DefaultWelcomeScript ?? string.Empty;
    }
}