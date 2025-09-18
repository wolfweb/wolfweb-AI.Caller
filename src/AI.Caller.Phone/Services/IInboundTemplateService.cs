using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.Services {
    public interface IInboundTemplateService {
        /// <summary>
        /// 创建呼入模板
        /// </summary>
        Task<InboundTemplate> CreateTemplateAsync(InboundTemplate template);
        
        /// <summary>
        /// 更新呼入模板
        /// </summary>
        Task<InboundTemplate?> UpdateTemplateAsync(int templateId, InboundTemplate template);
        
        /// <summary>
        /// 删除呼入模板
        /// </summary>
        Task<bool> DeleteTemplateAsync(int templateId);
        
        /// <summary>
        /// 获取用户的呼入模板列表
        /// </summary>
        Task<List<InboundTemplate>> GetUserTemplatesAsync(int userId);
        
        /// <summary>
        /// 获取呼入模板详情
        /// </summary>
        Task<InboundTemplate?> GetTemplateAsync(int templateId);
        
        /// <summary>
        /// 设置默认呼入模板
        /// </summary>
        Task<bool> SetDefaultTemplateAsync(int templateId, int userId);
        
        /// <summary>
        /// 获取默认呼入模板
        /// </summary>
        Task<InboundTemplate?> GetDefaultTemplateAsync(int userId);
        
        /// <summary>
        /// 获取活跃的呼入模板
        /// </summary>
        Task<List<InboundTemplate>> GetActiveTemplatesAsync(int userId);
    }
}