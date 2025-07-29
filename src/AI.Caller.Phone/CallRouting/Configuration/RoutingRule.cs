namespace AI.Caller.Phone.CallRouting.Configuration
{
    /// <summary>
    /// 路由规则
    /// </summary>
    public class RoutingRule
    {
        /// <summary>
        /// 号码匹配模式
        /// </summary>
        public string Pattern { get; set; } = string.Empty;

        /// <summary>
        /// 路由策略
        /// </summary>
        public string Strategy { get; set; } = "Direct";

        /// <summary>
        /// 策略参数
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new();

        /// <summary>
        /// 允许的来源列表
        /// </summary>
        public List<string> AllowedSources { get; set; } = new();

        /// <summary>
        /// 活跃时间段
        /// </summary>
        public TimeSpan? ActiveHours { get; set; }

        /// <summary>
        /// 优先级（数字越小优先级越高）
        /// </summary>
        public int Priority { get; set; } = 100;

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}