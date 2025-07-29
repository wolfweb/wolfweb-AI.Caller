namespace AI.Caller.Phone.Models
{
    /// <summary>
    /// 录音记录实体
    /// </summary>
    public class Recording
    {
        /// <summary>
        /// 录音ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 用户ID
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// SIP用户名
        /// </summary>
        public string SipUsername { get; set; } = string.Empty;

        /// <summary>
        /// 录音开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 录音结束时间
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 录音文件路径
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// 录音时长
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// 录音状态
        /// </summary>
        public RecordingStatus Status { get; set; }
    }

    /// <summary>
    /// 录音状态枚举
    /// </summary>
    public enum RecordingStatus
    {
        /// <summary>
        /// 录音中
        /// </summary>
        Recording = 0,

        /// <summary>
        /// 录音完成
        /// </summary>
        Completed = 1,

        /// <summary>
        /// 录音失败
        /// </summary>
        Failed = 2
    }
}