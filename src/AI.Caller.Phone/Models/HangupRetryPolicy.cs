namespace AI.Caller.Phone.Models
{
    /// <summary>
    /// 挂断重试策略配置
    /// </summary>
    public class HangupRetryPolicy
    {
        /// <summary>
        /// 最大重试次数
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// 重试延迟时间
        /// </summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// 最大重试延迟时间
        /// </summary>
        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 挂断操作超时时间
        /// </summary>
        public TimeSpan HangupTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 通知发送超时时间
        /// </summary>
        public TimeSpan NotificationTimeout { get; set; } = TimeSpan.FromSeconds(5);
    }
}