namespace AI.Caller.Phone.CallRouting.Models
{
    /// <summary>
    /// 呼出通话信息
    /// </summary>
    public class OutboundCallInfo
    {
        /// <summary>
        /// SIP Call-ID（主要标识符）
        /// </summary>
        public string CallId { get; set; } = string.Empty;

        /// <summary>
        /// From标签（发起方标签，在整个对话中保持不变）
        /// </summary>
        public string FromTag { get; set; } = string.Empty;

        /// <summary>
        /// To标签（接收方标签，应答后设置）
        /// </summary>
        public string? ToTag { get; set; }

        /// <summary>
        /// SIP用户名
        /// </summary>
        public string SipUsername { get; set; } = string.Empty;

        /// <summary>
        /// 目标号码
        /// </summary>
        public string Destination { get; set; } = string.Empty;

        /// <summary>
        /// 标准化的目标号码（用于匹配）
        /// </summary>
        public string NormalizedDestination { get; set; } = string.Empty;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 通话状态
        /// </summary>
        public CallStatus Status { get; set; } = CallStatus.Initiated;

        /// <summary>
        /// 客户端ID
        /// </summary>
        public string? ClientId { get; set; }

        /// <summary>
        /// 应答时间
        /// </summary>
        public DateTime? AnsweredAt { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime? EndedAt { get; set; }

        /// <summary>
        /// 原始From URI（完整的From头部）
        /// </summary>
        public string? OriginalFromUri { get; set; }

        /// <summary>
        /// 原始To URI（完整的To头部）
        /// </summary>
        public string? OriginalToUri { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 生成对话标识符（Call-ID + From-tag + To-tag的组合）
        /// </summary>
        public string GetDialogueId()
        {
            if (string.IsNullOrEmpty(ToTag))
                return $"{CallId}:{FromTag}";
            return $"{CallId}:{FromTag}:{ToTag}";
        }

        /// <summary>
        /// 检查是否匹配SIP对话
        /// </summary>
        public bool MatchesDialogue(string callId, string? fromTag, string? toTag)
        {
            // Call-ID必须匹配
            if (!CallId.Equals(callId, StringComparison.OrdinalIgnoreCase))
                return false;

            // From-tag必须匹配
            if (!FromTag.Equals(fromTag, StringComparison.OrdinalIgnoreCase))
                return false;

            // 如果已经有To-tag，则必须匹配
            if (!string.IsNullOrEmpty(ToTag) && !string.IsNullOrEmpty(toTag))
            {
                return ToTag.Equals(toTag, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }
    }
}