using AI.Caller.Phone.Entities;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace AI.Caller.Phone.Services {
    public class AICustomerServiceSettingsProvider : IAICustomerServiceSettingsProvider {
        private readonly AppDbContext _dbContext;

        public AICustomerServiceSettingsProvider(AppDbContext dbContext) {
            _dbContext = dbContext;
        }

        public async Task<AICustomerServiceSettings> GetSettingsAsync() {
            var settings = await _dbContext.AICustomerServiceSettings.FirstOrDefaultAsync();
            if (settings == null) {
                settings = new AICustomerServiceSettings();
                _dbContext.AICustomerServiceSettings.Add(settings);
                await _dbContext.SaveChangesAsync();
            }
            return settings;
        }

        public async Task UpdateSettingsAsync(AICustomerServiceSettings settings) {
            _dbContext.AICustomerServiceSettings.Update(settings);
            await _dbContext.SaveChangesAsync();
        }
    }
}