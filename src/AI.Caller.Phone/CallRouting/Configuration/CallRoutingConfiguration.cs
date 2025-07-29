namespace AI.Caller.Phone.CallRouting.Configuration
{
    /// <summary>
    /// 通话路由配置
    /// </summary>
    public class CallRoutingConfiguration
    {
        /// <summary>
        /// 默认路由策略
        /// </summary>
        public string DefaultStrategy { get; set; } = "Direct";

        /// <summary>
        /// 路由规则
        /// </summary>
        public Dictionary<string, RoutingRule> Rules { get; set; } = new();

        /// <summary>
        /// 备用选项
        /// </summary>
        public FallbackOptions Fallback { get; set; } = new();

        /// <summary>
        /// 安全选项
        /// </summary>
        public SecurityOptions Security { get; set; } = new();

        /// <summary>
        /// 性能选项
        /// </summary>
        public PerformanceOptions Performance { get; set; } = new();
    }
}