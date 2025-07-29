namespace AI.Caller.Phone.CallRouting.Configuration
{
    /// <summary>
    /// 备用处理选项
    /// </summary>
    public class FallbackOptions
    {
        /// <summary>
        /// 启用语音信箱
        /// </summary>
        public bool EnableVoicemail { get; set; } = false;

        /// <summary>
        /// 启用呼叫转移
        /// </summary>
        public bool EnableCallForwarding { get; set; } = false;

        /// <summary>
        /// 转移号码
        /// </summary>
        public string? ForwardingNumber { get; set; }

        /// <summary>
        /// 自动拒绝
        /// </summary>
        public bool AutoReject { get; set; } = false;

        /// <summary>
        /// 拒绝原因
        /// </summary>
        public string RejectionReason { get; set; } = "User unavailable";

        /// <summary>
        /// 最大等待时间（秒）
        /// </summary>
        public int MaxWaitTimeSeconds { get; set; } = 30;

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; set; } = 3;
    }
}