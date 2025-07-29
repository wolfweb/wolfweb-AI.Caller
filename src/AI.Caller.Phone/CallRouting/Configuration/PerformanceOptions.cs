namespace AI.Caller.Phone.CallRouting.Configuration
{
    /// <summary>
    /// 性能选项
    /// </summary>
    public class PerformanceOptions
    {
        /// <summary>
        /// 启用连接池
        /// </summary>
        public bool EnableConnectionPool { get; set; } = true;

        /// <summary>
        /// 最大连接池大小
        /// </summary>
        public int MaxPoolSize { get; set; } = 100;

        /// <summary>
        /// 连接超时时间（毫秒）
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 路由决策超时时间（毫秒）
        /// </summary>
        public int RoutingTimeoutMs { get; set; } = 1000;

        /// <summary>
        /// 启用负载均衡
        /// </summary>
        public bool EnableLoadBalancing { get; set; } = false;

        /// <summary>
        /// 负载均衡算法
        /// </summary>
        public string LoadBalancingAlgorithm { get; set; } = "RoundRobin";

        /// <summary>
        /// 启用缓存
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// 缓存过期时间（分钟）
        /// </summary>
        public int CacheExpirationMinutes { get; set; } = 5;
    }
}