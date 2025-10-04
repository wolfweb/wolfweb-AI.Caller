using AI.Caller.Phone.Entities;
using System.Threading.Tasks;

namespace AI.Caller.Phone.Services;

public interface ITtsTemplateIntegrationService {
    Task<bool> ShouldEnableAIForCall(User user);

    Task<string> GetTtsScriptForCall(string destination);

    Task<TtsTemplate?> GetTtsTemplateForCall(string destination);
}