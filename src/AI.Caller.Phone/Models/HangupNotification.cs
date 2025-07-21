namespace AI.Caller.Phone.Models
{
    /// <summary>
    /// 挂断通知模型
    /// </summary>
    public class HangupNotification
    {
        /// <summary>
        /// 通话ID
        /// </summary>
        public string CallId { get; set; } = string.Empty;

        /// <summary>
        /// 发起挂断的用户SIP用户名
        /// </summary>
        public string InitiatorSipUsername { get; set; } = string.Empty;

        /// <summary>
        /// 目标用户SIP用户名
        /// </summary>
        public string TargetSipUsername { get; set; } = string.Empty;

        /// <summary>
        /// 挂断原因
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// 挂断时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 挂断状态
        /// </summary>
        public HangupStatus Status { get; set; } = HangupStatus.Initiated;
    }

    /// <summary>
    /// 挂断状态枚举
    /// </summary>
    public enum HangupStatus
    {
        /// <summary>
        /// 挂断已发起
        /// </summary>
        Initiated,

        /// <summary>
        /// 音频已停止
        /// </summary>
        AudioStopped,

        /// <summary>
        /// 资源已释放
        /// </summary>
        ResourcesReleased,

        /// <summary>
        /// 通知已发送
        /// </summary>
        NotificationSent,

        /// <summary>
        /// 挂断完成
        /// </summary>
        Completed,

        /// <summary>
        /// 挂断失败
        /// </summary>
        Failed
    }
}