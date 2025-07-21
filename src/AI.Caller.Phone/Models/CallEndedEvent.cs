namespace AI.Caller.Phone.Models
{
    /// <summary>
    /// 通话结束事件模型
    /// </summary>
    public class CallEndedEvent
    {
        /// <summary>
        /// 通话ID
        /// </summary>
        public string CallId { get; set; } = string.Empty;

        /// <summary>
        /// SIP用户名
        /// </summary>
        public string SipUsername { get; set; } = string.Empty;

        /// <summary>
        /// 通话结束时间
        /// </summary>
        public DateTime EndTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 结束原因
        /// </summary>
        public string EndReason { get; set; } = string.Empty;

        /// <summary>
        /// 音频是否已停止
        /// </summary>
        public bool AudioStopped { get; set; } = false;

        /// <summary>
        /// 资源是否已释放
        /// </summary>
        public bool ResourcesReleased { get; set; } = false;

        /// <summary>
        /// 是否已通知对方
        /// </summary>
        public bool RemoteNotified { get; set; } = false;
    }
}