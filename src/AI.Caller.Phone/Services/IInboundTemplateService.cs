using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.Services {
    public interface IInboundTemplateService {
        Task<InboundTemplate> CreateTemplateAsync(InboundTemplate template);
        
        Task<InboundTemplate?> UpdateTemplateAsync(int templateId, InboundTemplate template);
        
        Task<bool> DeleteTemplateAsync(int templateId);
        
        Task<List<InboundTemplate>> GetUserTemplatesAsync(int userId);
        
        Task<InboundTemplate?> GetTemplateAsync(int templateId);
        
        Task<bool> SetDefaultTemplateAsync(int templateId, int userId);
        
        Task<InboundTemplate?> GetDefaultTemplateAsync(int userId);
        
        Task<List<InboundTemplate>> GetActiveTemplatesAsync(int userId);
    }
}