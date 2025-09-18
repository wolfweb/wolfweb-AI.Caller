using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.Services {
    /// <summary>
    /// TTS模板集成服务接口
    /// </summary>
    public interface ITtsTemplateIntegrationService {
        /// <summary>
        /// 根据TTS记录生成个性化脚本
        /// </summary>
        Task<string> GeneratePersonalizedScriptAsync(TtsCallRecord record);
        
        /// <summary>
        /// 获取外呼使用的呼入模板
        /// </summary>
        Task<InboundTemplate?> GetOutboundTemplateAsync(int userId);
        
        /// <summary>
        /// 为外呼任务准备AI客服脚本
        /// </summary>
        Task<OutboundCallScript> PrepareOutboundScriptAsync(TtsCallRecord record, int userId);
        
        /// <summary>
        /// 检测是否应该启用AI客服模式
        /// </summary>
        bool ShouldEnableAICustomerService(TtsCallRecord record);
    }
    
    /// <summary>
    /// 外呼脚本信息
    /// </summary>
    public class OutboundCallScript {
        public string WelcomeScript { get; set; } = string.Empty;
        public string PersonalizedContent { get; set; } = string.Empty;
        public string CombinedScript { get; set; } = string.Empty;
        public InboundTemplate? Template { get; set; }
        public TtsCallRecord Record { get; set; } = null!;
        public Dictionary<string, string> Variables { get; set; } = new();
    }
}