using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Services {
    public interface IAICustomerServiceSettingsProvider {
        Task<AICustomerServiceSettings> GetSettingsAsync();
        Task UpdateSettingsAsync(AICustomerServiceSettings settings);
    }
}