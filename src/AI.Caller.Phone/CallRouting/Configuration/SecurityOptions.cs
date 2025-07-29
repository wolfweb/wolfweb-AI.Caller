namespace AI.Caller.Phone.CallRouting.Configuration
{
    /// <summary>
    /// 安全选项
    /// </summary>
    public class SecurityOptions
    {
        /// <summary>
        /// IP白名单
        /// </summary>
        public List<string> WhitelistedIPs { get; set; } = new();

        /// <summary>
        /// IP黑名单
        /// </summary>
        public List<string> BlacklistedIPs { get; set; } = new();

        /// <summary>
        /// 需要认证
        /// </summary>
        public bool RequireAuthentication { get; set; } = true;

        /// <summary>
        /// 每分钟最大通话数
        /// </summary>
        public int MaxCallsPerMinute { get; set; } = 10;

        /// <summary>
        /// 启用速率限制
        /// </summary>
        public bool EnableRateLimit { get; set; } = true;

        /// <summary>
        /// 阻止匿名呼叫
        /// </summary>
        public bool BlockAnonymousCalls { get; set; } = false;

        /// <summary>
        /// 记录安全事件
        /// </summary>
        public bool LogSecurityEvents { get; set; } = true;
    }
}