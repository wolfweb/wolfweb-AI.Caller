namespace AI.Caller.Phone.CallRouting.Models
{
    /// <summary>
    /// 呼出通话信息
    /// </summary>
    public class OutboundCallInfo
    {
        /// <summary>
        /// 呼叫ID
        /// </summary>
        public string CallId { get; set; } = string.Empty;

        /// <summary>
        /// SIP用户名
        /// </summary>
        public string SipUsername { get; set; } = string.Empty;

        /// <summary>
        /// 目标号码
        /// </summary>
        public string Destination { get; set; } = string.Empty;

        /// <summary>
        /// From标签
        /// </summary>
        public string? FromTag { get; set; }

        /// <summary>
        /// To标签
        /// </summary>
        public string? ToTag { get; set; }

        /// <summary>
        /// 呼叫状态
        /// </summary>
        public CallStatus Status { get; set; } = CallStatus.Initiated;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}